using Cathode.Dsp.Wdf;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The oscillator of Fender's brownface harmonic vibrato (6G4 Super, 6G12 Concert, and the 6G5 / 6G7 / 6G9 of the same
///     family): a triode phase-shift oscillator whose three-section CR ladder sets the rate, and the network that carries
///     its output to the mixer pair's grid lines. The defaults are the 6G12's and 6G4's drawings.
/// </summary>
/// <remarks>
///     <para>
///         <b>Rate.</b> The oscillator runs where the ladder from its plate to its grid turns the phase by 180°: the plate
///         (as a source behind <paramref name="PlateResistance" />, with <paramref name="PlateShunt" /> to ground), then
///         <paramref name="Ladder1" /> to a node grounded through the Speed rheostat and <paramref name="SpeedSeries" />,
///         <paramref name="Ladder2" /> to a node with <paramref name="LadderResistor2" /> to the (bypassed) cathode, and
///         <paramref name="Ladder3" /> to the grid with <paramref name="LadderResistor3" />. This is solved here, so the
///         rate
///         follows from the drawing's values (2.6 to 9.4 Hz on the 6G12).
///     </para>
///     <para>
///         <b>Depth.</b> The oscillator's plate drives <paramref name="CouplingResistor" /> and
///         <paramref name="CouplingCap" /> into the Intensity pot's top, which <paramref name="PotCap" /> filters to
///         ground; the wiper (filtered by <paramref name="WiperCap" />) feeds one grid line through
///         <paramref name="GridFeed" />, which a second <paramref name="GridFeed" /> ties to the paraphase splitter's grid
///         (an AC ground). The splitter drives the other grid line in antiphase through its own feed. This network is
///         solved
///         at the rate for the wiper's amplitude, with the wiper loaded by the two feeds. What is not solved is the
///         oscillator's own amplitude: it oscillates until its triode clips, and <paramref name="PlateSwing" /> (the
///         fundamental of that clipped wave at the plate) is an estimate. The splitter is taken as exact (the two lines
///         swing equal and opposite), and the waveform as the fundamental alone (the network's caps filter the clipped
///         wave's harmonics by 20 dB and more).
///     </para>
/// </remarks>
/// <param name="PlateResistance">The oscillator's plate as a source: its plate resistor in parallel with r_p.</param>
/// <param name="PlateShunt">From the oscillator's plate to ground.</param>
/// <param name="Ladder1">The first ladder cap, from the plate.</param>
/// <param name="Ladder2">The second ladder cap.</param>
/// <param name="Ladder3">The third ladder cap, into the grid.</param>
/// <param name="Speed">The Speed rheostat (turned up, it takes resistance out).</param>
/// <param name="SpeedSeries">In series with the Speed rheostat.</param>
/// <param name="LadderResistor2">The second section's resistor.</param>
/// <param name="LadderResistor3">The third section's resistor (the grid leak).</param>
/// <param name="PlateSwing">The oscillator's plate swing at the fundamental, peak volts (an estimate; see the remarks).</param>
/// <param name="CouplingResistor">From the plate towards the Intensity pot.</param>
/// <param name="CouplingCap">In series with <paramref name="CouplingResistor" />.</param>
/// <param name="PotCap">From the Intensity pot's top to ground.</param>
/// <param name="Intensity">The Intensity pot, wiper to the grid line.</param>
/// <param name="WiperCap">From the wiper to ground.</param>
/// <param name="GridFeed">From the wiper to the grid line, and from the grid line to the splitter's grid.</param>
public sealed record VibratoOscillatorDesign(
    double PlateResistance = 68e3,
    double PlateShunt = 0.03e-6,
    double Ladder1 = 0.02e-6,
    double Ladder2 = 0.01e-6,
    double Ladder3 = 0.01e-6,
    Pot? Speed = null,
    double SpeedSeries = 100e3,
    double LadderResistor2 = 1e6,
    double LadderResistor3 = 1e6,
    double PlateSwing = 100,
    double CouplingResistor = 4.7e6,
    double CouplingCap = 0.005e-6,
    double PotCap = 0.1e-6,
    Pot? Intensity = null,
    double WiperCap = 0.05e-6,
    double GridFeed = 1e6
) {
    /// <summary>The Speed rheostat: 4M reverse audio ("4M-RA") on the 6G drawings.</summary>
    public Pot SpeedPot => Speed ?? new(4e6, PotTaper.ReverseAudio);

    /// <summary>The Intensity pot: 10M linear ("10M-L").</summary>
    public Pot IntensityPot => Intensity ?? new(10e6, PotTaper.Linear);

    /// <summary>The oscillation frequency, in hertz, at Speed rotation <paramref name="speed" /> (0..1).</summary>
    public double Rate(double speed) {
        var rheostat = (1 - SpeedPot.Fraction(speed)) * SpeedPot.Ohms + SpeedSeries;

        // The imaginary part of the plate voltage needed for a unit grid voltage changes sign at the oscillation
        // frequency (where the ladder inverts): bracket it on a log scale, then bisect.
        var low = 0.05;
        var high = 0.05;
        var previous = Plate(low, rheostat).Imaginary;
        while (high < 200) {
            high *= 1.25;
            var value = Plate(high, rheostat).Imaginary;
            if (Math.Sign(value) != Math.Sign(previous)) {
                break;
            }

            low = high;
            previous = value;
        }

        for (var i = 0; i < 60; i++) {
            var middle = Math.Sqrt(low * high);
            if (Math.Sign(Plate(middle, rheostat).Imaginary) == Math.Sign(previous)) {
                low = middle;
            } else {
                high = middle;
            }
        }

        return Math.Sqrt(low * high);
    }

    /// <summary>
    ///     The oscillation's peak voltage at the Intensity pot's wiper, at <paramref name="frequency" /> hertz and Intensity
    ///     rotation <paramref name="intensity" /> (0..1): the source that drives one grid line through
    ///     <see cref="GridFeed" />.
    /// </summary>
    public double Depth(double frequency, double intensity) {
        var fraction = IntensityPot.Fraction(intensity);
        if (fraction <= 0) {
            return 0;
        }

        var omega = 2 * Math.PI * frequency;
        var coupling = new Complex(CouplingResistor, -1 / (omega * CouplingCap));
        var upper = Math.Max((1 - fraction) * IntensityPot.Ohms, 1);
        var lower = Math.Max(fraction * IntensityPot.Ohms, 1);

        // Nodal equations for the pot's top (q) and the wiper (w); the wiper sees its cap and the two grid feeds in
        // series to the splitter's grid (an AC ground).
        var yqq = 1 / coupling + new Complex(0, omega * PotCap) + 1 / upper;
        var yqw = -1 / upper;
        var yww = 1 / upper + 1 / lower + new Complex(0, omega * WiperCap) + 1 / (2 * GridFeed);
        var source = PlateSwing / coupling;
        var w = -yqw * source / (yqq * yww - yqw * yqw);
        return w.Magnitude;
    }

    /// <summary>The plate voltage that gives 1 V at the grid, at <paramref name="frequency" /> hertz.</summary>
    Complex Plate(double frequency, double rheostat) {
        var omega = 2 * Math.PI * frequency;
        Complex Reactance(double farads) => new(0, -1 / (omega * farads));
        Complex grid = 1;
        var current = grid / LadderResistor3;
        var second = grid + current * Reactance(Ladder3);
        current += second / LadderResistor2;
        var first = second + current * Reactance(Ladder2);
        current += first / rheostat;
        var plate = first + current * Reactance(Ladder1);
        current += plate * new Complex(0, omega * PlateShunt);
        return plate + current * PlateResistance;
    }
}

/// <summary>
///     Fender's brownface harmonic vibrato as a preamp stage: the crossover that splits the channel into a bass and a
///     treble band, the mixer pair (one 7025: a triode per band on a shared, bypassed cathode) whose bias the oscillator
///     moves in antiphase on the two grids, and the plates mixed through two resistors. With the vibrato stopped the dry
///     sound still runs through this circuit; with it running, the bands swell in turn (a phase-shifting, lower-depth
///     tremolo whose two halves cross over). The defaults are the 6G12 Concert's and 6G4 Super's values.
/// </summary>
/// <remarks>
///     <para>
///         The netlist, from the input (the vibrato channel's second stage, through <paramref name="InputCap" />): the
///         bass
///         band through <paramref name="LowResistor" /> into <paramref name="LowCap" /> to ground, then
///         <paramref name="BassCoupling" /> to the bass grid line; the treble band from the divider
///         <paramref name="DividerTop" /> / <paramref name="DividerBottom" /> through <paramref name="TrebleCoupling" />
///         to
///         the treble grid line. Each grid line returns through one <see cref="VibratoOscillatorDesign.GridFeed" /> to its
///         LFO source (the Intensity wiper, the splitter's plate: here ideal sources moving in antiphase, at rest 0 V) and
///         through another to the splitter's grid node, which is AC-grounded by <paramref name="SplitterCap" /> and held
///         by
///         a third feed to ground. The triodes' plates have <paramref name="PlateResistor" /> each from
///         <paramref name="Supply" />, the shared cathode <paramref name="CathodeResistor" /> ‖
///         <paramref name="CathodeBypass" />; each plate feeds the output through <paramref name="MixResistor" />, and the
///         idle channel loads the output through <paramref name="IdleCap" /> and <paramref name="IdleLoad" />. The output
///         is DC-blocked (<see cref="StageNetlist.DcBlock" />) for the inverter's input network.
///     </para>
///     <para>
///         The oscillator runs while <paramref name="Pedal" /> is on (the footswitch; off stops it, as the pedal does by
///         grounding the ladder), with the depth the Intensity knob sets (<see cref="VibratoOscillatorDesign" />). The
///         Speed and Intensity knobs are the Circuit Amp's tremolo controls, as on every model with a tremolo of its own
///         (<see cref="AmpControls.TremoloRate" /> in hertz, held to the range the Speed rheostat gives the ladder;
///         <see cref="AmpControls.TremoloDepth" /> the Intensity's rotation), so the stage counts as the amp's tremolo
///         (<see cref="HasTremolo" />) and no generic bias tremolo is added. The depth follows the knobs
///         and the pedal through a 30 ms smoother (as the oscillator starts and stops over a few cycles).
///     </para>
/// </remarks>
public sealed record HarmonicVibratoStageDesign(
    string Name,
    double Supply,
    VibratoOscillatorDesign? Oscillator = null,
    SwitchRole Pedal = SwitchRole.Switch2,
    double InputCap = 0.05e-6,
    double LowResistor = 470e3,
    double LowCap = 0.005e-6,
    double BassCoupling = 0.01e-6,
    double DividerTop = 220e3,
    double DividerBottom = 220e3,
    double TrebleCoupling = 250e-12,
    double SplitterCap = 0.05e-6,
    double PlateResistor = 100e3,
    double CathodeResistor = 4.7e3,
    double CathodeBypass = 25e-6,
    double MixResistor = 470e3,
    double IdleCap = 0.05e-6,
    double IdleLoad = 1.05e6
) : AmpStageDesign(Name) {
    /// <summary>The LFO source's element index in a band's netlist (<see cref="Band" />).</summary>
    internal const int BandSource = 0;

    /// <summary>The oscillator (the 6G12's by default).</summary>
    public VibratoOscillatorDesign Lfo => Oscillator ?? new();

    /// <summary>The amp's one tremolo: the Circuit Amp's tremolo controls drive it (see the remarks).</summary>
    public override bool HasTremolo => true;

    /// <summary>
    ///     The whole circuit as one netlist, with the LFO sources at rest (0 V): the reference for the stage, which runs
    ///     it as two bands (see <see cref="HarmonicVibratoStage" />).
    /// </summary>
    public NetworkStageDesign Network => Build();

    public override AmpStage Create(double sampleRate) =>
        new HarmonicVibratoStage(this, Band(true).Create(sampleRate), Band(false).Create(sampleRate), sampleRate);

    /// <summary>
    ///     One band as a stage of its own: the input cap, the band's filter (the other band's branch, as its resistance,
    ///     loading the split node), the grid line with its feed to the LFO source (the netlist's first element,
    ///     <see cref="BandSource" />)
    ///     and its feed to the splitter's grid (an AC ground: the LFO moves the two lines in antiphase, and the splitter's
    ///     own cap holds it at audio), and the triode with its plate resistor; its cathode is twice the shared resistor
    ///     (each half draws its operating point through it) with a bypass that also holds it still at the LFO's rate (the
    ///     pair's currents move in antiphase, so the shared cathode does). The output is the plate.
    /// </summary>
    NetworkStageDesign Band(bool bass) {
        var lfo = Lfo;
        var net = new StageNetlist();
        var (source, split, line) = (net.Node(), net.Node(), net.Node());
        net.Supply(source, 0);
        net.Capacitor(net.Input, split, InputCap);
        var filter = net.Node();
        if (bass) {
            net.Resistor(split, filter, LowResistor);
            net.Capacitor(filter, StageNetlist.Ground, LowCap);
            net.Capacitor(filter, line, BassCoupling);
            net.Resistor(split, StageNetlist.Ground, DividerTop + DividerBottom);
        } else {
            net.Resistor(split, filter, DividerTop);
            net.Resistor(filter, StageNetlist.Ground, DividerBottom);
            net.Capacitor(filter, line, TrebleCoupling);
            net.Resistor(split, StageNetlist.Ground, LowResistor); // the bass branch, its 5 nF a short at audio
        }

        net.Resistor(line, source, lfo.GridFeed);
        net.Resistor(line, StageNetlist.Ground, lfo.GridFeed);
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Supply);
        net.Resistor(rail, plate, PlateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * CathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-3);
        net.Triode(plate, line, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return new(bass ? Name + " bass" : Name + " treble", net);
    }

    NetworkStageDesign Build() {
        var lfo = Lfo;
        var net = new StageNetlist();
        var split = net.Node();
        net.Capacitor(net.Input, split, InputCap);

        // Bass band: a low-pass into the bass grid line.
        var (low, bassLine) = (net.Node(), net.Node());
        net.Resistor(split, low, LowResistor);
        net.Capacitor(low, StageNetlist.Ground, LowCap);
        net.Capacitor(low, bassLine, BassCoupling);

        // Treble band: half the signal through a small cap into the treble grid line.
        var (divider, trebleLine) = (net.Node(), net.Node());
        net.Resistor(split, divider, DividerTop);
        net.Resistor(divider, StageNetlist.Ground, DividerBottom);
        net.Capacitor(divider, trebleLine, TrebleCoupling);

        // The grid lines' returns: each to its LFO source (at rest here) and to the splitter's grid.
        var (bassSource, trebleSource, splitter) = (net.Node(), net.Node(), net.Node());
        net.Supply(bassSource, 0);
        net.Supply(trebleSource, 0);
        net.Resistor(bassLine, bassSource, lfo.GridFeed);
        net.Resistor(trebleLine, trebleSource, lfo.GridFeed);
        net.Resistor(bassLine, splitter, lfo.GridFeed);
        net.Resistor(trebleLine, splitter, lfo.GridFeed);
        net.Resistor(splitter, StageNetlist.Ground, lfo.GridFeed);
        net.Capacitor(splitter, StageNetlist.Ground, SplitterCap);

        // The mixer pair on its shared cathode, plates mixed into the output.
        var (rail, bassPlate, treblePlate, cathode, output, idle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Supply);
        net.Resistor(rail, bassPlate, PlateResistor);
        net.Resistor(rail, treblePlate, PlateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, CathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, CathodeBypass);
        net.Triode(bassPlate, bassLine, cathode, TriodeModel.Ecc83);
        net.Triode(treblePlate, trebleLine, cathode, TriodeModel.Ecc83);
        net.Resistor(bassPlate, output, MixResistor);
        net.Resistor(treblePlate, output, MixResistor);
        net.Capacitor(output, idle, IdleCap);
        net.Resistor(idle, StageNetlist.Ground, IdleLoad);
        net.Output = net.DcBlock(output);
        return new(Name, net);
    }
}

/// <summary>
///     A <see cref="HarmonicVibratoStageDesign" /> as two single-triode stages, one per band (each a Newton solve of two
///     ports instead of one of four), whose plates are mixed through the mixing resistors as Thévenin sources into the
///     idle channel's load; the output rests at 0 V. A sine oscillator moves the bands' LFO sources in antiphase each
///     sample; its phase restarts at a reset, so a reset amp renders the same every time.
/// </summary>
/// <remarks>
///     What the split leaves out, against <see cref="HarmonicVibratoStageDesign.Network" /> (the tests compare the two):
///     the shared cathode (each half on its own, held still), the splitter's grid as a node (an AC ground here, so the
///     two grid lines do not load each other through it), and each plate's load beyond its Thévenin cut.
/// </remarks>
public sealed class HarmonicVibratoStage : AmpStage {
    const double SmoothingTime = 0.03;

    readonly VibratoOscillatorDesign lfo;
    readonly AmpStage bass;
    readonly AmpStage treble;
    readonly IRailModulation bassRails;
    readonly IRailModulation trebleRails;
    double bassWeight; // each plate's share of the output node: its conductance through the mixing resistor
    double trebleWeight;
    double restMix;
    double outputResistance;
    double sampleRate;
    double target; // the depth the knobs and pedal ask for, peak volts at the wiper
    double smoothing;
    double sine;
    double cosine = 1;
    double stepSine;
    double stepCosine = 1;
    readonly double slowest; // the ladder's rate at Speed 0 and 10, in hertz
    readonly double fastest;
    double speed = -1;
    double intensity = -1;
    bool running;
    bool modulated; // the sources are off their rest

    public HarmonicVibratoStageDesign Design { get; }

    /// <summary>The oscillator's rate for the current Speed, in hertz (within the ladder's range).</summary>
    public double Frequency { get; private set; }

    /// <summary>The LFO's depth, the smoothed peak volts at the Intensity wiper (each grid line swings about half of it).</summary>
    public double Depth { get; private set; }

    /// <summary>The treble band's Newton statistics (<see cref="Newton" /> is the bass band's).</summary>
    public NewtonSolver? TrebleNewton => treble.Newton;

    public override double OutputResistance => outputResistance;

    public override double RestOutput => 0;

    public override NewtonSolver? Newton => bass.Newton;

    public override bool IsGainStage => true;

    internal HarmonicVibratoStage(
        HarmonicVibratoStageDesign design,
        AmpStage bass,
        AmpStage treble,
        double sampleRate
    ) {
        Design = design;
        lfo = design.Lfo;
        this.bass = bass;
        this.treble = treble;
        bassRails = (IRailModulation)bass;
        trebleRails = (IRailModulation)treble;
        this.sampleRate = sampleRate;
        (slowest, fastest) = (lfo.Rate(0), lfo.Rate(1));
        Configure(lfo.Rate(0.5), 0.5, false);
        Mix();
    }

    public override void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        bass.SetSampleRate(sampleRate);
        treble.SetSampleRate(sampleRate);
        Step();
    }

    public override void SetSourceResistance(double ohms) {
        bass.SetSourceResistance(ohms);
        treble.SetSourceResistance(ohms);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) {
        bass.SetSupply(scale);
        treble.SetSupply(scale);
    }

    public override void Update(in AmpControls controls) {
        bass.Update(controls);
        treble.Update(controls);
        Configure(controls.TremoloRate, controls.TremoloDepth, controls.IsOn(Design.Pedal));
    }

    public override void Apply(in PreampSettings settings) {
        bass.Apply(settings);
        treble.Apply(settings);
    }

    public override void Retune(double restInput) {
        bass.Retune(restInput);
        treble.Retune(restInput);
        Mix();
    }

    public override void Reset(double restInput) {
        bass.Reset(restInput);
        treble.Reset(restInput);
        Mix();
        sine = 0;
        cosine = 1;
        Depth = target;
        modulated = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        Depth += smoothing * (target - Depth);
        if (Depth > 1e-6) {
            // A rotating phasor, renormalised each sample (to first order: cheap and drift-free).
            var s = sine * stepCosine + cosine * stepSine;
            var c = cosine * stepCosine - sine * stepSine;
            var norm = 1.5 - 0.5 * (s * s + c * c);
            sine = s * norm;
            cosine = c * norm;
            var volts = Depth * sine;
            bassRails.OffsetRail(HarmonicVibratoStageDesign.BandSource, volts);
            trebleRails.OffsetRail(HarmonicVibratoStageDesign.BandSource, -volts);
            modulated = true;
        } else if (modulated) {
            bassRails.OffsetRail(HarmonicVibratoStageDesign.BandSource, 0);
            trebleRails.OffsetRail(HarmonicVibratoStageDesign.BandSource, 0);
            modulated = false;
        }

        return bassWeight * bass.Tick(input) + trebleWeight * treble.Tick(input) - restMix;
    }

    public override void FlushDenormals() {
        bass.FlushDenormals();
        treble.FlushDenormals();
    }

    /// <summary>The output node from the two plates through the mixing resistors, loaded by the idle channel.</summary>
    void Mix() {
        var gb = 1 / (bass.OutputResistance + Design.MixResistor);
        var gt = 1 / (treble.OutputResistance + Design.MixResistor);
        var total = gb + gt + 1 / Design.IdleLoad;
        (bassWeight, trebleWeight) = (gb / total, gt / total);
        outputResistance = 1 / total;
        restMix = bassWeight * bass.RestOutput + trebleWeight * treble.RestOutput;
    }

    /// <summary>
    ///     The rate (<paramref name="speed" /> hertz, held to the ladder's range) and the depth (the Intensity's
    ///     rotation) for the knobs, recomputed only when one moved.
    /// </summary>
    void Configure(double speed, double intensity, bool running) {
        if (speed == this.speed && intensity == this.intensity && running == this.running) {
            return;
        }

        if (speed != this.speed) {
            Frequency = Math.Clamp(speed, slowest, fastest);
        }

        (this.speed, this.intensity, this.running) = (speed, intensity, running);
        target = running ? lfo.Depth(Frequency, intensity) : 0;
        Step();
    }

    void Step() {
        var omega = 2 * Math.PI * Frequency / sampleRate;
        (stepSine, stepCosine) = Math.SinCos(omega);
        smoothing = 1 - Math.Exp(-1 / (SmoothingTime * sampleRate));
    }
}
