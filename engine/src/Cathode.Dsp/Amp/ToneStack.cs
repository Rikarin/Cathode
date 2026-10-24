using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The component values of a passive treble/bass/middle tone stack in the Fender/Marshall topology (Yeh and Smith,
///     "Discretization of the '59 Fender Bassman tone stack", DAFx-06): the treble cap from the input to the top of the
///     treble pot, whose wiper is the output; the slope resistor from the input to a node feeding the bass cap (to the
///     bottom of the treble pot, the top of the bass pot) and the middle cap (to the bottom of the bass pot, the top of
///     the middle pot); bass and middle pots wired as variable resistors, the middle pot to ground.
/// </summary>
public sealed record ToneStackDesign(
    string Name,
    double TrebleCap,
    double SlopeResistor,
    double BassCap,
    double MiddleCap,
    Pot Treble,
    Pot Bass,
    Pot Middle
) {
    /// <summary>
    ///     Fender blackface (AB763 Twin/Super Reverb): 250 pF, 100k, 0.1 µF, 47 nF, 250k A treble and bass, 10k mid.
    /// </summary>
    public static ToneStackDesign Fender { get; } = new(
        "Fender",
        250e-12,
        100e3,
        100e-9,
        47e-9,
        new(250e3, PotTaper.Audio),
        new(250e3, PotTaper.Audio),
        new(10e3, PotTaper.Linear)
    );

    /// <summary>
    ///     Marshall 1959 Super Lead: 470 pF, 33k, 22 nF, 22 nF, 220k linear treble, 1M A bass, 25k linear mid.
    /// </summary>
    public static ToneStackDesign Marshall { get; } = new(
        "Marshall",
        470e-12,
        33e3,
        22e-9,
        22e-9,
        new(220e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    /// <summary>
    ///     A Vox AC30 top-boost-style stack in the same topology: 50 pF, 100k, 22 nF, 22 nF, 1M linear treble, 1M A bass.
    ///     The original has a fixed 10k in place of the middle pot; here it is a 20k pot, so that middle at noon is
    ///     stock.
    /// </summary>
    public static ToneStackDesign Vox { get; } = new(
        "Vox",
        50e-12,
        100e3,
        22e-9,
        22e-9,
        new(1e6, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(20e3, PotTaper.Linear)
    );

    /// <summary>The stacks, in the order of the Tone Stack block's type choice.</summary>
    public static IReadOnlyList<ToneStackDesign> All { get; } = [Fender, Marshall, Vox];
}

/// <summary>
///     A tone stack's elements inside a <see cref="WdfNetlist" />: three adapted capacitor ports (treble, bass, middle
///     cap, declared in that order) and the pots as internal resistors, so that turning a knob only recomputes the
///     scattering matrix.
/// </summary>
public readonly record struct ToneStackNetwork(
    ToneStackDesign Design,
    int Output,
    int TrebleTop,
    int TrebleBottom,
    int BassResistor,
    int MiddleResistor
) {
    /// <summary>The slope resistor's index among the netlist's resistors.</summary>
    public int SlopeResistor { get; init; }

    /// <summary>
    ///     Adds the stack fed from node <paramref name="input" /> and declares its three capacitor ports (call it where
    ///     those ports belong in the port list). The output is the treble wiper, unloaded.
    /// </summary>
    public static ToneStackNetwork Add(WdfNetlist net, int input, ToneStackDesign design) {
        var top = net.Node();
        var bottom = net.Node();
        var slope = net.Node();
        var middle = net.Node();
        net.Port(input, top);
        net.Port(slope, bottom);
        net.Port(slope, middle);
        var slopeResistor = net.Resistor(input, slope, design.SlopeResistor);
        var output = net.Node();
        var trebleTop = net.Resistor(top, output, design.Treble.Ohms / 2);
        var trebleBottom = net.Resistor(output, bottom, design.Treble.Ohms / 2);
        var bass = net.Resistor(bottom, middle, design.Bass.Ohms / 2);
        var mid = net.Resistor(middle, WdfNetlist.Ground, design.Middle.Ohms / 2);
        return new(design, output, trebleTop, trebleBottom, bass, mid) { SlopeResistor = slopeResistor };
    }

    /// <summary>
    ///     The pot resistances (treble top, treble bottom, bass, middle) for knob rotations 0..1, in the order of
    ///     <see cref="TrebleTop" />, <see cref="TrebleBottom" />, <see cref="BassResistor" />, <see cref="MiddleResistor" />.
    /// </summary>
    public (double TrebleTop, double TrebleBottom, double Bass, double Middle) Resistances(
        double bass,
        double middle,
        double treble
    ) {
        var t = Design.Treble.Fraction(treble);
        return ((1 - t) * Design.Treble.Ohms, t * Design.Treble.Ohms, Design.Bass.Fraction(bass) * Design.Bass.Ohms,
            Design.Middle.Fraction(middle) * Design.Middle.Ohms);
    }
}

/// <summary>
///     A passive tone stack on its own: a linear R-type root (Werner et al., DAFx-15) driven from a resistive source
///     (<see cref="SourceResistance" />, a cathode follower's or a plate's output impedance) into a resistive load
///     (<see cref="LoadResistance" />, the next grid). Exactly the bilinear transform of the analog circuit at any
///     setting; the pots live inside the adaptor, and moving one recomputes the matrix (a few microseconds,
///     allocation-free).
/// </summary>
public sealed class ToneStackCircuit {
    readonly ToneStackNetwork network;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor>, NoDevice> root;
    double bass = double.NaN;
    double middle = double.NaN;
    double treble = double.NaN;

    public ToneStackDesign Design { get; }

    public double SourceResistance { get; }

    public double LoadResistance { get; }

    public ToneStackCircuit(
        ToneStackDesign design,
        double sampleRate,
        double sourceResistance = 1e3,
        double loadResistance = 1e6
    ) {
        Design = design;
        SourceResistance = sourceResistance;
        LoadResistance = loadResistance;
        var net = new WdfNetlist();
        var input = net.Node();
        net.Port(input, WdfNetlist.Ground);
        network = ToneStackNetwork.Add(net, input, design);
        net.Resistor(network.Output, WdfNetlist.Ground, loadResistance);
        net.Probe(network.Output);
        root = new(
            net,
            new(
                new(sourceResistance),
                new(design.TrebleCap, sampleRate),
                new(design.BassCap, sampleRate),
                new(design.MiddleCap, sampleRate)
            ),
            default
        );
        Set(0.5, 0.5, 0.5);
    }

    /// <summary>Changes the sample rate, keeping the knobs. Control thread or between blocks.</summary>
    public void SetSampleRate(double sampleRate) {
        root.Ports.B.Configure(Design.TrebleCap, sampleRate);
        root.Ports.C.Configure(Design.BassCap, sampleRate);
        root.Ports.D.Configure(Design.MiddleCap, sampleRate);
        root.Propagate();
    }

    /// <summary>Knob rotations 0..1. Recomputes the scattering matrix only when one of them changed. Allocation-free.</summary>
    public void Set(double bass, double middle, double treble) {
        if (bass == this.bass && middle == this.middle && treble == this.treble) {
            return;
        }

        this.bass = bass;
        this.middle = middle;
        this.treble = treble;
        var (top, bottom, b, m) = network.Resistances(bass, middle, treble);
        root.SetResistance(network.TrebleTop, top);
        root.SetResistance(network.TrebleBottom, bottom);
        root.SetResistance(network.BassResistor, b);
        root.SetResistance(network.MiddleResistor, m);
        root.Propagate();
    }

    /// <summary>One sample: the source voltage in, the load voltage out.</summary>
    public double Tick(double input) {
        root.Ports.A.Volts = input;
        return root.Tick();
    }

    public void Reset() => root.Reset();

    /// <summary>Clears capacitor charge that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        Flush.Capacitor(ref root.Ports.B);
        Flush.Capacitor(ref root.Ports.C);
        Flush.Capacitor(ref root.Ports.D);
    }
}

/// <summary>Clears wave digital filter state that has decayed into the subnormal range.</summary>
static class Flush {
    const double Tiny = 1e-30;

    public static void Capacitor(ref Capacitor capacitor) {
        if (Math.Abs(capacitor.Reflected) < Tiny) {
            capacitor.Reset();
        }
    }

    public static void Inductor(ref Inductor inductor) {
        if (Math.Abs(inductor.Reflected) < Tiny) {
            inductor.Reset();
        }
    }

    public static double Value(double x) => Math.Abs(x) < Tiny ? 0 : x;
}
