using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>What an expert setting does to a capacitor of a <see cref="StageNetlist" />.</summary>
public enum CapacitorRole {
    /// <summary>Nothing.</summary>
    Plain,

    /// <summary>A bright cap: scaled by <see cref="PreampSettings.BrightCap" />.</summary>
    Bright,

    /// <summary>A tone-stack cap: divided by <see cref="PreampSettings.StackFrequency" />.</summary>
    Stack,

    /// <summary>A cap across a plate resistor: divided by <see cref="PreampSettings.PlateFrequency" />.</summary>
    Plate
}

/// <summary>Which part of a pot a resistor of a <see cref="StageNetlist" /> is.</summary>
public enum PotPart {
    /// <summary>Not a pot.</summary>
    None,

    /// <summary>Top end to wiper: (1 - fraction) of the track.</summary>
    Upper,

    /// <summary>Wiper to bottom end, or a pot wired as a variable resistor: the fraction of the track.</summary>
    Lower
}

/// <summary>What a <see cref="StageElement" /> is.</summary>
public enum ElementKind {
    Resistor,
    Capacitor,
    Inductor,
    Supply,
    Triode,
    Pentode,
    Diodes,
    OpAmp,

    /// <summary>An op-amp that never reaches its rails, as a linear voltage source inside the adaptor.</summary>
    Amplifier
}

/// <summary>
///     One element of a <see cref="StageNetlist" />; the optional properties apply to the kinds that use them. Nodes: a
///     two-terminal element or a supply (to ground) is A-B; a triode is plate A, grid B, cathode C; a pentode plate A,
///     screen B, grid C, cathode D; a diode clipper anode A, cathode B; an op-amp non-inverting input A, inverting input
///     B, output C (its rails ± <see cref="Value" /> volts, about ground); a linear amplifier likewise, its open-loop gain
///     <see cref="Value" />.
/// </summary>
public sealed record StageElement(ElementKind Kind, int A, int B, double Value) {
    public int C { get; init; }

    public int D { get; init; }

    /// <summary>A switched element's value while its switch is off (0: absent, an open circuit).</summary>
    public double OffValue { get; init; }

    public SwitchRole? Switch { get; init; }

    public Pot Pot { get; init; }

    public PotPart Part { get; init; }

    public PotRole Role { get; init; }

    public CapacitorRole CapacitorRole { get; init; }

    /// <summary>A cathode resistor, which <see cref="PreampSettings.CathodeResistor" /> scales.</summary>
    public bool Cathode { get; init; }

    public TriodeModel Triode { get; init; }

    public PentodeModel Pentode { get; init; }

    public DiodeModel Diode { get; init; }

    public int Forward { get; init; }

    public int Reverse { get; init; }

    public OpAmpModel OpAmp { get; init; }

    /// <summary>
    ///     A rotary selector's values (<see cref="StageNetlist.SelectedCapacitor" />), the first at the knob's zero and
    ///     the last at ten; null for none.
    /// </summary>
    public double[]? Steps { get; init; }

    /// <summary>
    ///     The value at <paramref name="controls" />, before pots and expert settings: a selector's step, a switched
    ///     element's on or off value (0: absent), or the value.
    /// </summary>
    public double ValueAt(in AmpControls controls) {
        if (Steps is { Length: > 0 } steps) {
            return steps[(int)Math.Round(Math.Clamp(controls.Rotation(Role), 0, 1) * (steps.Length - 1))];
        }

        return Switch is not { } role || controls.IsOn(role) ? Value : OffValue;
    }
}

/// <summary>
///     The circuit of a <see cref="NetworkStageDesign" />, written like a SPICE netlist: nodes, passive elements
///     (resistors, capacitors, inductors), pots turned by front-panel knobs, elements switched by front-panel switches,
///     supply rails that follow the preamp's sag, and up to five triodes, or a pentode, or one or two diode clippers (a
///     triode with one or two clippers, and two triodes with two, also work), or one or two op-amps (one op-amp with a
///     diode clipper also works), besides any number of linear amplifiers. The source drives <see cref="Input" />
///     through the previous stage's output resistance; the stage's output is the voltage at <see cref="Output" />.
/// </summary>
/// <remarks>
///     Build it on the control thread when the model is defined; the stage reads it once. Values are in ohms, farads,
///     henries and volts.
/// </remarks>
public sealed class StageNetlist {
    /// <summary>The reference node.</summary>
    public const int Ground = 0;

    readonly List<StageElement> elements = [];

    /// <summary>The node the source drives (through the previous stage's output resistance).</summary>
    public int Input { get; }

    /// <summary>The node whose voltage is the stage's output (default: the input).</summary>
    public int Output { get; set; }

    /// <summary>Number of nodes besides ground.</summary>
    public int Nodes { get; private set; }

    /// <summary>The elements, in the order they were added.</summary>
    public IReadOnlyList<StageElement> Elements => elements;

    public StageNetlist() {
        Input = Node();
        Output = Input;
    }

    /// <summary>Adds a node and returns it.</summary>
    public int Node() => ++Nodes;

    public void Resistor(int a, int b, double ohms) => Add(new(ElementKind.Resistor, a, b, ohms));

    /// <summary>A cathode resistor: the one <see cref="PreampSettings.CathodeResistor" /> scales (Preamp Bias).</summary>
    public void CathodeResistor(int a, int b, double ohms) =>
        Add(new(ElementKind.Resistor, a, b, ohms) { Cathode = true });

    /// <summary>
    ///     A resistor of <paramref name="on" /> ohms while <paramref name="role" /> is on, else <paramref name="off" />;
    ///     either 0 is an open contact (a closed one is a small resistance, e.g. 1 Ω).
    /// </summary>
    public void SwitchedResistor(int a, int b, double off, double on, SwitchRole role) =>
        Add(new(ElementKind.Resistor, a, b, on) { OffValue = off, Switch = role });

    public void Capacitor(int a, int b, double farads, CapacitorRole role = CapacitorRole.Plain) =>
        Add(new(ElementKind.Capacitor, a, b, farads) { CapacitorRole = role });

    /// <summary>
    ///     A capacitor of <paramref name="farads" /> while <paramref name="role" /> is on, else <paramref name="off" />
    ///     (0: absent); <paramref name="capacitorRole" /> says which expert setting scales it.
    /// </summary>
    public void SwitchedCapacitor(
        int a,
        int b,
        double farads,
        SwitchRole role,
        CapacitorRole capacitorRole = CapacitorRole.Plain,
        double off = 0
    ) =>
        Add(new(ElementKind.Capacitor, a, b, farads) { OffValue = off, Switch = role, CapacitorRole = capacitorRole });

    /// <summary>
    ///     A capacitor chosen by a rotary switch turned by <paramref name="role" />'s knob: <paramref name="steps" />
    ///     spread over its rotation (the Matchless DC-30's six-way tone switch of coupling caps).
    /// </summary>
    public void SelectedCapacitor(
        int a,
        int b,
        double[] steps,
        PotRole role,
        CapacitorRole capacitorRole = CapacitorRole.Plain
    ) =>
        Add(new(ElementKind.Capacitor, a, b, steps[0]) { Steps = steps, Role = role, CapacitorRole = capacitorRole });

    public void Inductor(int a, int b, double henries) => Add(new(ElementKind.Inductor, a, b, henries));

    /// <summary>
    ///     A pot between <paramref name="top" /> and <paramref name="bottom" /> turned by <paramref name="role" />;
    ///     returns the wiper.
    /// </summary>
    public int Pot(int top, int bottom, Pot pot, PotRole role) {
        var wiper = Node();
        Add(new(ElementKind.Resistor, top, wiper, pot.Ohms) { Pot = pot, Part = PotPart.Upper, Role = role });
        Add(new(ElementKind.Resistor, wiper, bottom, pot.Ohms) { Pot = pot, Part = PotPart.Lower, Role = role });
        return wiper;
    }

    /// <summary>
    ///     A pot wired as a variable resistor: the track's fraction at the knob's rotation (a TMB stack's bass pot), or
    ///     with <paramref name="falling" /> the rest of the track, so that turning the knob up takes resistance out (the
    ///     Matchless Chieftain's bass pot, in series with its bass path).
    /// </summary>
    public void Rheostat(int a, int b, Pot pot, PotRole role, bool falling = false) =>
        Add(
            new(ElementKind.Resistor, a, b, pot.Ohms) {
                Pot = pot, Part = falling ? PotPart.Upper : PotPart.Lower, Role = role
            }
        );

    /// <summary>
    ///     A DC block after <paramref name="node" />: a 1 µF series cap and 100M to ground (a corner of 1.6 mHz); returns
    ///     the blocked node. For a stage whose output (a plate, a mix of plates) rests at a high voltage and feeds the power
    ///     amp, whose input network (<see cref="InverterInputDesign" />) holds the real coupling cap and grid leak and takes
    ///     an input that rests at 0 V.
    /// </summary>
    public int DcBlock(int node) {
        var blocked = Node();
        Capacitor(node, blocked, 1e-6);
        Resistor(blocked, Ground, 100e6);
        return blocked;
    }

    /// <summary>A supply rail at <paramref name="volts" /> (nominal; it follows the preamp's sag).</summary>
    public void Supply(int node, double volts) => Add(new(ElementKind.Supply, node, Ground, volts));

    public void Triode(int plate, int grid, int cathode, TriodeModel tube) =>
        Add(new(ElementKind.Triode, plate, grid, 0) { C = cathode, Triode = tube });

    public void Pentode(int plate, int screen, int grid, int cathode, PentodeModel tube) =>
        Add(new(ElementKind.Pentode, plate, screen, 0) { C = grid, D = cathode, Pentode = tube });

    /// <summary>
    ///     Antiparallel diode stacks from <paramref name="anode" /> to <paramref name="cathode" />:
    ///     <paramref name="forward" /> diodes conducting from anode to cathode, <paramref name="reverse" /> the other way.
    /// </summary>
    public void Diodes(int anode, int cathode, DiodeModel model, int forward, int reverse) =>
        Add(new(ElementKind.Diodes, anode, cathode, 0) { Diode = model, Forward = forward, Reverse = reverse });

    /// <summary>
    ///     An op-amp (<see cref="Wdf.OpAmp" />) on symmetric rails of ± <paramref name="rails" /> volts, its output's
    ///     reference ground. Its rails are regulated: they do not follow the preamp's sag.
    /// </summary>
    public void OpAmp(int nonInverting, int inverting, int output, OpAmpModel model, double rails) =>
        Add(new(ElementKind.OpAmp, nonInverting, inverting, rails) { C = output, OpAmp = model });

    /// <summary>
    ///     An op-amp whose signal never reaches its rails (a return buffer, a summing stage, a stage's make-up gain), as
    ///     an ideal amplifier of open-loop gain <paramref name="gain" /> with its output referred to ground: a
    ///     voltage-controlled voltage source inside the adaptor, so the stage has no device to solve for it. Use
    ///     <see cref="OpAmp" /> where the stage can clip.
    /// </summary>
    public void Amplifier(int nonInverting, int inverting, int output, double gain = 1e5) =>
        Add(new(ElementKind.Amplifier, nonInverting, inverting, gain) { C = output });

    void Add(StageElement element) {
        if (element.A > Nodes || element.B > Nodes || element.C > Nodes || element.D > Nodes) {
            throw new ArgumentException("Unknown node.", nameof(element));
        }

        elements.Add(element);
    }
}

/// <summary>
///     A preamp stage described by a <see cref="StageNetlist" />: any passive network with pots and switches (non-TMB
///     tone stacks, voicing networks, the Mark's lead network, a switched coupling-cap tone control), with or without a
///     nonlinear device (a cathode follower not driving a stack, an EF86 pentode stage, a diode clipper, a cold clipper
///     with its plate bypass cap, a pair of triodes such as jumpered channels). Built as a
///     <see cref="NetworkStage{TDevice}" />.
/// </summary>
/// <param name="Name">A label.</param>
/// <param name="Netlist">The circuit.</param>
/// <param name="InputResistance">In series with the source (a grid stopper, an input-jack resistor).</param>
public sealed record NetworkStageDesign(string Name, StageNetlist Netlist, double InputResistance = 0)
    : AmpStageDesign(Name) {
    /// <summary>
    ///     The Newton iterations a sample may take before it counts as unconverged (the solver's default, 32, unless a
    ///     stage needs more: three triodes in a feedback loop driven into hard clipping).
    /// </summary>
    public int MaxIterations { get; init; } = 32;

    /// <summary>Chord steps on a recent Jacobian before full ones (<see cref="NewtonSolver.ReuseFactors" />).</summary>
    public bool ReuseFactors { get; init; }

    /// <summary>
    ///     Newton's absolute (volts) and relative step tolerance (the solver's default, 1e-6, unless a stage that
    ///     converges slowly can take a looser one: the error a converged step leaves is about its square).
    /// </summary>
    public double Tolerance { get; init; } = 1e-6;

    /// <summary>A tremolo of the model's own on this stage (the stage built is wrapped in it); null for none.</summary>
    public StageTremoloDesign? Tremolo { get; init; }

    public override bool HasTremolo => Tremolo is not null;

    public override AmpStage Create(double sampleRate) =>
        Tremolo is { } tremolo ? tremolo.Wrap(this, CreateNetwork(sampleRate), sampleRate) : CreateNetwork(sampleRate);

    /// <summary>The stage itself, without <see cref="Tremolo" />.</summary>
    AmpStage CreateNetwork(double sampleRate) {
        var devices = Netlist.Elements
            .Where(static e => e.Kind is ElementKind.Triode
                    or ElementKind.Pentode
                    or ElementKind.Diodes
                    or ElementKind.OpAmp
            )
            .OrderBy(static e => e.Kind)
            .ToArray();
        var kinds = string.Concat(
            devices.Select(static e => e.Kind switch {
                    ElementKind.Triode => 'T',
                    ElementKind.Pentode => 'P',
                    ElementKind.Diodes => 'D',
                    _ => 'O'
                }
            )
        );
        return kinds switch {
            "" => new NetworkStage<NoDevice>(this, devices, static (_, _) => default, sampleRate),
            "T" => new NetworkStage<FastTriode>(this, devices, (s, _) => s.Triode(devices[0].Triode), sampleRate),
            "TT" => new NetworkStage<Devices<FastTriode, FastTriode>>(
                this,
                devices,
                (s, _) => new(s.Triode(devices[0].Triode), s.Triode(devices[1].Triode)),
                sampleRate
            ),
            "TTT" => new NetworkStage<Devices<FastTriode, Devices<FastTriode, FastTriode>>>(
                this,
                devices,
                (s, _) => new(
                    s.Triode(devices[0].Triode),
                    new(s.Triode(devices[1].Triode), s.Triode(devices[2].Triode))
                ),
                sampleRate
            ),
            "TTTT" => new NetworkStage<Devices<Devices<FastTriode, FastTriode>, Devices<FastTriode, FastTriode>>>(
                this,
                devices,
                (s, _) => new(
                    new(s.Triode(devices[0].Triode), s.Triode(devices[1].Triode)),
                    new(s.Triode(devices[2].Triode), s.Triode(devices[3].Triode))
                ),
                sampleRate
            ),
            "TTTTT" => new NetworkStage<
                Devices<Devices<FastTriode, FastTriode>, Devices<FastTriode, Devices<FastTriode, FastTriode>>>>(
                    this,
                    devices,
                    (s, _) => new(
                        new(s.Triode(devices[0].Triode), s.Triode(devices[1].Triode)),
                        new(s.Triode(devices[2].Triode), new(s.Triode(devices[3].Triode), s.Triode(devices[4].Triode)))
                    ),
                    sampleRate
                ),
            "P" => new NetworkStage<Pentode>(this, devices, (_, _) => new(devices[0].Pentode), sampleRate),
            "D" => new NetworkStage<DiodeClipper>(this, devices, (_, _) => Clipper(devices[0]), sampleRate),
            "TD" => new NetworkStage<Devices<FastTriode, DiodeClipper>>(
                this,
                devices,
                (s, _) => new(s.Triode(devices[0].Triode), Clipper(devices[1])),
                sampleRate
            ),
            "O" => new NetworkStage<OpAmp>(this, devices, (_, rate) => OpAmp(devices[0], rate), sampleRate),
            "OO" => new NetworkStage<Devices<OpAmp, OpAmp>>(
                this,
                devices,
                (_, rate) => new(OpAmp(devices[0], rate), OpAmp(devices[1], rate)),
                sampleRate
            ),
            "DO" => new NetworkStage<Devices<DiodeClipper, OpAmp>>(
                this,
                devices,
                (_, rate) => new(Clipper(devices[0]), OpAmp(devices[1], rate)),
                sampleRate
            ),
            "DD" => new NetworkStage<Devices<DiodeClipper, DiodeClipper>>(
                this,
                devices,
                (_, _) => new(Clipper(devices[0]), Clipper(devices[1])),
                sampleRate
            ),
            "TDD" => new NetworkStage<Devices<FastTriode, Devices<DiodeClipper, DiodeClipper>>>(
                this,
                devices,
                (s, _) => new(s.Triode(devices[0].Triode), new(Clipper(devices[1]), Clipper(devices[2]))),
                sampleRate
            ),
            "TTDD" => new NetworkStage<Devices<Devices<FastTriode, FastTriode>, Devices<DiodeClipper, DiodeClipper>>>(
                this,
                devices,
                (s, _) => new(
                    new(s.Triode(devices[0].Triode), s.Triode(devices[1].Triode)),
                    new(Clipper(devices[2]), Clipper(devices[3]))
                ),
                sampleRate
            ),
            _ => throw new NotSupportedException(
                $"{Name}: a network stage takes T, TT, TTT, TTTT, P, D, DD, TD, TDD, TTDD, O, OO or DO "
                + "(triodes, a pentode, diode clippers, op-amps)."
            )
        };
    }

    static DiodeClipper Clipper(StageElement e) => new(e.Diode, e.Forward, e.Diode, e.Reverse);

    static OpAmp OpAmp(StageElement e, double sampleRate) => new(e.OpAmp, -e.Value, e.Value, sampleRate);
}

/// <summary>A stage whose supply rails another part of the amp moves per sample (an oscillator on a bias line).</summary>
public interface IRailModulation {
    /// <summary>
    ///     Sets supply <paramref name="element" /> (an index into the netlist's elements) to its nominal voltage, with the
    ///     sag, plus <paramref name="volts" />, for the coming samples. A supply change (<see cref="AmpStage.SetSupply" />)
    ///     puts it back at nominal, so set it after that, before each sample.
    /// </summary>
    void OffsetRail(int element, double volts);
}

/// <summary>
///     A <see cref="NetworkStageDesign" /> as one R-type root: capacitors and inductors as adapted ports, resistors, pots
///     and supply rails inside the adaptor (a knob or switch only recomputes the matrix), the devices at the root,
///     solved by Newton's method per sample. Its operating point comes from a DC solve of the same netlist
///     (capacitors open, inductors shorted, Newton with source stepping, allocation-free), and its output resistance
///     from the circuit linearised there at 1 kHz magnitudes (each capacitor and inductor as the resistance of its
///     reactance), for the next stage's Thévenin cut; turning a knob or a switch recomputes it there.
/// </summary>
public sealed class NetworkStage<TDevice> : AmpStage, IRailModulation where TDevice : struct, INonlinearDevice {
    const double Off = 1e-15; // a switched-off capacitor
    const double Open = 1e12; // a switched-off resistor
    const double Gmin = 1e-12;
    const double Short = 1e3; // siemens: an inductor at DC
    const double EndStop = RTypeScattering.EndStop; // ohms: a pot's half at an end stop, as the scattering's own floor

    readonly StageElement[] elements;

    readonly int[]
        index; // per element: its port (reactive), resistor or supply index in the WDF netlist; -1 for devices

    readonly double[] values; // per element: its value as the knobs, switches and settings have it
    readonly bool[] acrossMaster; // per element: a cap across a master pot's upper half
    readonly (int Plus, int Minus)[] devicePorts;
    readonly Func<PreampSettings, double, TDevice> make; // the devices for the settings and the sample rate
    readonly int size; // DC unknowns: nodes and supply currents
    readonly double[] matrix;
    readonly double[] vector;
    readonly double[] solution;
    readonly double[] portVoltages;
    readonly double[] portCurrents;
    readonly double[] jacobian;
    readonly int[] pivots;
    readonly int[] supplies; // the supply elements' indices: the preamp's sag moves them every sample
    readonly int amplifiers;
    readonly bool hasMemory; // a device with state of its own (an op-amp's pole)
    RTypeRoot<NetworkPorts, TDevice> root;
    AmpControls controls;
    PreampSettings settings = PreampSettings.Stock;
    double sourceResistance;
    double sampleRate;
    double supplyScale = 1;
    double outputResistance;
    double restOutput;
    bool solved;

    public NetworkStageDesign Design { get; }

    public override double OutputResistance => outputResistance;

    public override double RestOutput => restOutput;

    public override NewtonSolver? Newton => root.Device.Ports > 0 ? root.Newton : null;

    public override bool IsGainStage { get; }

    internal NetworkStage(
        NetworkStageDesign design,
        StageElement[] devices,
        Func<PreampSettings, double, TDevice> make,
        double sampleRate
    ) {
        Design = design;
        this.make = make;
        this.sampleRate = sampleRate;
        var netlist = design.Netlist;
        elements = [..netlist.Elements];
        index = new int[elements.Length];
        values = new double[elements.Length];
        // A cap across a master pot's upper half (its bright cap) follows Master Cap.
        acrossMaster = [..elements.Select(e => e.Kind == ElementKind.Capacitor && AcrossMaster(e, elements))];
        IsGainStage = elements.Any(static e => e.Cathode);
        hasMemory = devices.Any(static e => e.Kind == ElementKind.OpAmp);
        controls = new(0.5, 0.5, 0.5, 0.5, 1, false);

        var net = new WdfNetlist();
        for (var n = 0; n < netlist.Nodes; n++) {
            net.Node();
        }

        net.Port(netlist.Input, WdfNetlist.Ground);
        var inductors = new List<bool>();
        for (var i = 0; i < elements.Length; i++) {
            var e = elements[i];
            values[i] = Value(i);
            index[i] = e.Kind switch {
                ElementKind.Capacitor or ElementKind.Inductor => net.Port(e.A, e.B) - 1,
                ElementKind.Resistor => net.Resistor(e.A, e.B, Math.Max(values[i], EndStop)), // a pot at an end stop
                ElementKind.Supply => net.VoltageSource(e.A, WdfNetlist.Ground),
                ElementKind.Amplifier => net.Vcvs(e.C, WdfNetlist.Ground, e.A, e.B, e.Value),
                _ => -1
            };
            if (e.Kind is ElementKind.Capacitor or ElementKind.Inductor) {
                inductors.Add(e.Kind == ElementKind.Inductor);
            }

            if (e.Kind == ElementKind.Amplifier) {
                amplifiers++;
            }
        }

        supplies = [..Enumerable.Range(0, elements.Length).Where(i => elements[i].Kind == ElementKind.Supply)];

        var ports = new List<(int, int)>();
        foreach (var d in devices) {
            switch (d.Kind) {
                case ElementKind.Triode:
                    net.Triode(d.A, d.B, d.C);
                    ports.Add((d.B, d.C));
                    ports.Add((d.A, d.C));
                    break;
                case ElementKind.Pentode:
                    net.Pentode(d.A, d.B, d.C, d.D);
                    ports.Add((d.C, d.D));
                    ports.Add((d.B, d.D));
                    ports.Add((d.A, d.D));
                    break;
                case ElementKind.Diodes:
                    net.Diode(d.A, d.B);
                    ports.Add((d.A, d.B));
                    break;
                default:
                    net.OpAmp(d.A, d.B, d.C);
                    ports.Add((d.A, d.B));
                    ports.Add((d.C, StageNetlist.Ground));
                    break;
            }
        }

        devicePorts = [..ports];
        net.Probe(netlist.Output);
        for (var n = 1; n <= netlist.Nodes; n++) {
            net.Probe(n);
        }

        sourceResistance = Math.Max(design.InputResistance, 1);
        root = new(net, new(sourceResistance, [..inductors]), make(settings, sampleRate));
        for (var i = 0; i < elements.Length; i++) {
            if (elements[i].Kind is ElementKind.Capacitor or ElementKind.Inductor) {
                root.Ports.Configure(index[i], values[i], sampleRate);
            }
        }

        root.Propagate();
        ApplySupplies();
        root.Newton.Linearise = true;
        root.Newton.MaxIterations = design.MaxIterations;
        root.Newton.ReuseFactors = design.ReuseFactors;
        root.Newton.AbsoluteTolerance = design.Tolerance;
        root.Newton.RelativeTolerance = design.Tolerance;

        size = netlist.Nodes + supplies.Length + amplifiers;
        matrix = new double[size * size];
        vector = new double[size];
        solution = new double[size];
        var k = devicePorts.Length;
        portVoltages = new double[Math.Max(k, 1)];
        portCurrents = new double[Math.Max(k, 1)];
        jacobian = new double[Math.Max(k * k, 1)];
        pivots = new int[size];
        Reset(0);
    }

    /// <summary>The voltage at node <paramref name="node" /> of the netlist at the last sample.</summary>
    public double Voltage(int node) => node == StageNetlist.Ground ? 0 : root.Probe(node);

    /// <summary>The voltage at node <paramref name="node" /> at the operating point (after <see cref="Reset" />).</summary>
    public double RestVoltage(int node) => node == StageNetlist.Ground ? 0 : solution[node - 1];

    public override void SetSampleRate(double sampleRate) {
        this.sampleRate = sampleRate;
        if (hasMemory) {
            // An op-amp's pole and slew rate are per sample: a new device for the rate (the channel resets after).
            root.Device = make(settings, sampleRate);
        }

        for (var i = 0; i < elements.Length; i++) {
            if (elements[i].Kind is ElementKind.Capacitor or ElementKind.Inductor) {
                root.Ports.Configure(index[i], values[i], sampleRate);
            }
        }

        root.Propagate();
    }

    public override void SetSourceResistance(double ohms) {
        var total = Math.Max(ohms + Design.InputResistance, 1);
        if (total == sourceResistance) {
            return;
        }

        sourceResistance = total;
        root.Ports.Source.Value = total;
        root.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetSupply(double scale) {
        if (scale == supplyScale) {
            return;
        }

        supplyScale = scale;
        ApplySupplies();
    }

    public override void Update(in AmpControls controls) {
        this.controls = controls;
        Refresh();
    }

    public override void Apply(in PreampSettings settings) {
        if (settings.ChangesDevices(this.settings)) {
            root.Device = make(settings, sampleRate);
        }

        this.settings = settings;
        Refresh();
    }

    public override void Retune(double restInput) {
        SolveOperatingPoint(restInput);
        restOutput = Rest(Design.Netlist.Output);
        outputResistance = SmallSignalOutputResistance();
        solved = true;
    }

    public override void Reset(double restInput) {
        SolveOperatingPoint(restInput);
        root.Reset();
        ApplySupplies();
        for (var i = 0; i < elements.Length; i++) {
            var e = elements[i];
            var across = Rest(e.A) - Rest(e.B);
            if (e.Kind == ElementKind.Capacitor) {
                root.Ports.Precharge(index[i], across);
            } else if (e.Kind == ElementKind.Inductor) {
                root.Ports.Precharge(index[i], Short * across);
            }
        }

        var k = devicePorts.Length;
        for (var p = 0; p < k; p++) {
            portVoltages[p] = Rest(devicePorts[p].Plus) - Rest(devicePorts[p].Minus);
        }

        if (k > 0) {
            root.SetInitialVoltages(portVoltages.AsSpan(0, k));
        }

        restOutput = Rest(Design.Netlist.Output);
        outputResistance = SmallSignalOutputResistance();
        solved = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double Tick(double input) {
        root.Ports.Source.Volts = input;
        return root.Tick();
    }

    public override void FlushDenormals() => root.Ports.FlushDenormals();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OffsetRail(int element, double volts) =>
        root.SetVoltage(index[element], elements[element].Value * supplyScale + volts);

    /// <summary>Every element's value for the current knobs, switches and settings; recomputes the matrix if one moved.</summary>
    void Refresh() {
        var changed = false;
        for (var i = 0; i < elements.Length; i++) {
            var e = elements[i];
            if (e.Kind is not (ElementKind.Resistor or ElementKind.Capacitor or ElementKind.Inductor)) {
                continue;
            }

            var value = Value(i);
            if (value == values[i]) {
                continue;
            }

            values[i] = value;
            changed = true;
            if (e.Kind == ElementKind.Resistor) {
                root.SetResistance(index[i], value);
            } else {
                root.Ports.Configure(index[i], value, sampleRate);
            }
        }

        if (!changed) {
            return;
        }

        root.Propagate();
        if (solved) {
            // A pot or switch in the output's path moves its resistance (a volume's wiper, a switched input): the next
            // stage reads it for its Thévenin cut. The operating point stays (pots and switches sit behind coupling caps).
            outputResistance = SmallSignalOutputResistance();
        }
    }

    /// <summary>Element <paramref name="i" />'s value for the current knobs, switches and settings.</summary>
    double Value(int i) {
        var e = elements[i];
        var on = e.Switch is not { } role || controls.IsOn(role);
        switch (e.Kind) {
            case ElementKind.Resistor: {
                // A switched resistor's 0 is absent (an open contact) in either position, as documented.
                var ohms = on ? e.Switch is null || e.Value > 0 ? e.Value : Open : e.OffValue > 0 ? e.OffValue : Open;
                if (e.Part != PotPart.None) {
                    var fraction = e.Pot.Fraction(controls.Rotation(e.Role));
                    ohms = (e.Part == PotPart.Upper ? 1 - fraction : fraction) * e.Pot.Ohms;
                }

                return e.Cathode ? ohms * settings.CathodeResistor : ohms;
            }
            case ElementKind.Capacitor: {
                var farads = e.ValueAt(controls) is var f && f > 0 ? f : Off;
                if (acrossMaster[i]) {
                    return farads; // a cap across the master is not the Bright Cap control's
                }

                return e.CapacitorRole switch {
                    CapacitorRole.Bright => farads * settings.BrightCap,
                    CapacitorRole.Stack => farads / settings.StackFrequency,
                    CapacitorRole.Plate => farads / settings.PlateFrequency,
                    _ => farads
                };
            }
            default:
                return e.Value;
        }
    }

    /// <summary>Whether <paramref name="cap" /> sits across the upper half of a master pot among <paramref name="all" />.</summary>
    static bool AcrossMaster(StageElement cap, StageElement[] all) {
        foreach (var pot in all) {
            if (pot is { Role: PotRole.Master, Part: PotPart.Upper }
                && ((pot.A == cap.A && pot.B == cap.B) || (pot.A == cap.B && pot.B == cap.A))) {
                return true;
            }
        }

        return false;
    }

    void ApplySupplies() {
        foreach (var i in supplies) {
            root.SetVoltage(index[i], elements[i].Value * supplyScale);
        }
    }

    double Rest(int node) => node == StageNetlist.Ground ? 0 : solution[node - 1];

    /// <summary>
    ///     The DC operating point for a source resting at <paramref name="restInput" /> volts: Newton on the nodal
    ///     equations with the devices, the supplies (and the source) stepped up from a tenth in ten steps.
    /// </summary>
    void SolveOperatingPoint(double restInput) {
        Array.Clear(solution);
        const int Steps = 10;
        for (var s = 1; s <= Steps; s++) {
            var scale = (double)s / Steps;
            for (var iteration = 0; iteration < 100; iteration++) {
                Stamp(restInput * scale, scale, true);
                if (!DenseLu.SolveInPlace(matrix.AsSpan(0, size * size), size, pivots, vector.AsSpan(0, size))) {
                    return;
                }

                // The step: vector holds the new solution; damp large moves.
                var largest = 0.0;
                for (var j = 0; j < size; j++) {
                    largest = Math.Max(largest, Math.Abs(vector[j] - solution[j]));
                }

                var damping = largest > 25 ? 25 / largest : 1;
                var converged = true;
                for (var j = 0; j < size; j++) {
                    var next = solution[j] + damping * (vector[j] - solution[j]);
                    converged &= Math.Abs(next - solution[j]) <= 1e-9 + 1e-9 * Math.Abs(next);
                    solution[j] = next;
                }

                if (converged) {
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     The linearised nodal system at the current solution, solved for the next one (Newton in the form
    ///     (G + J) x = b - i(v0) + J v0). With <paramref name="dc" /> capacitors are open and inductors shorted;
    ///     otherwise both are the resistances of their reactances at 1 kHz and the sources are zero.
    /// </summary>
    void Stamp(double input, double scale, bool dc) {
        var n = Design.Netlist.Nodes;
        var a = matrix.AsSpan(0, size * size);
        var b = vector.AsSpan(0, size);
        a.Clear();
        b.Clear();
        for (var j = 0; j < n; j++) {
            a[j * size + j] += Gmin;
        }

        var input0 = Design.Netlist.Input;
        Conductance(a, input0, StageNetlist.Ground, 1 / sourceResistance);
        if (dc) {
            b[input0 - 1] += input / sourceResistance;
        }

        var supply = 0;
        var amplifier = 0;
        const double Omega = 2 * Math.PI * 1_000;
        for (var i = 0; i < elements.Length; i++) {
            var e = elements[i];
            switch (e.Kind) {
                case ElementKind.Resistor:
                    Conductance(a, e.A, e.B, 1 / Math.Max(values[i], EndStop));
                    break;
                case ElementKind.Capacitor:
                    if (!dc) {
                        Conductance(a, e.A, e.B, Omega * values[i]);
                    }

                    break;
                case ElementKind.Inductor:
                    Conductance(a, e.A, e.B, dc ? Short : 1 / (Omega * values[i]));
                    break;
                case ElementKind.Amplifier: {
                    // v(C) = A (v(A) - v(B)), its output current the row's unknown.
                    var row = n + supplies.Length + amplifier++;
                    if (e.C > 0) {
                        a[row * size + e.C - 1] = 1;
                        a[(e.C - 1) * size + row] = 1;
                    }

                    if (e.A > 0) {
                        a[row * size + e.A - 1] -= e.Value;
                    }

                    if (e.B > 0) {
                        a[row * size + e.B - 1] += e.Value;
                    }

                    break;
                }
                case ElementKind.Supply: {
                    var row = n + supply++;
                    if (e.A > 0) {
                        a[row * size + e.A - 1] = 1;
                        a[(e.A - 1) * size + row] = 1;
                    }

                    b[row] = dc ? e.Value * supplyScale * scale : 0;
                    break;
                }
                case ElementKind.Triode or ElementKind.Pentode or ElementKind.Diodes or ElementKind.OpAmp:
                    // The devices: linearised through their ports below.
                    break;
            }
        }

        var k = devicePorts.Length;
        if (k == 0) {
            return;
        }

        for (var p = 0; p < k; p++) {
            portVoltages[p] = Rest(devicePorts[p].Plus) - Rest(devicePorts[p].Minus);
        }

        jacobian.AsSpan(0, k * k).Clear();
        root.Device.Evaluate(portVoltages.AsSpan(0, k), portCurrents.AsSpan(0, k), jacobian.AsSpan(0, k * k), k);
        for (var p = 0; p < k; p++) {
            var (plus, minus) = devicePorts[p];
            // i_p(v) ≈ i_p(v0) + Σ J_pl (v_l - v0_l): the constant part to the right-hand side.
            var constant = portCurrents[p];
            for (var l = 0; l < k; l++) {
                var g = jacobian[p * k + l];
                if (g == 0) {
                    continue;
                }

                constant -= g * portVoltages[l];
                var (lp, lm) = devicePorts[l];
                Stamp(a, plus, lp, g);
                Stamp(a, plus, lm, -g);
                Stamp(a, minus, lp, -g);
                Stamp(a, minus, lm, g);
            }

            if (dc) {
                if (plus > 0) {
                    b[plus - 1] -= constant;
                }

                if (minus > 0) {
                    b[minus - 1] += constant;
                }
            }
        }
    }

    /// <summary>The output's resistance to ground with the sources off, linearised at the operating point.</summary>
    double SmallSignalOutputResistance() {
        Stamp(0, 0, false);
        var output = Design.Netlist.Output;
        if (output == StageNetlist.Ground) {
            return 0;
        }

        var b = vector.AsSpan(0, size);
        b.Clear();
        b[output - 1] = 1;
        if (!DenseLu.SolveInPlace(matrix.AsSpan(0, size * size), size, pivots, b)) {
            return 0;
        }

        return Math.Max(b[output - 1], 1);
    }

    void Conductance(Span<double> a, int p, int m, double g) {
        if (p > 0) {
            a[(p - 1) * size + p - 1] += g;
        }

        if (m > 0) {
            a[(m - 1) * size + m - 1] += g;
        }

        if (p > 0 && m > 0) {
            a[(p - 1) * size + m - 1] -= g;
            a[(m - 1) * size + p - 1] -= g;
        }
    }

    void Stamp(Span<double> a, int row, int column, double g) {
        if (row > 0 && column > 0) {
            a[(row - 1) * size + column - 1] += g;
        }
    }
}
