using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;
using LinearOpAmpCircuit = Cathode.Dsp.Wdf.WdfCircuit<
    Cathode.Dsp.Wdf.RTypeAdaptor<
        Cathode.Dsp.Wdf.Ports<
            Cathode.Dsp.Wdf.ResistiveVoltageSource,
            Cathode.Dsp.Wdf.Resistor,
            Cathode.Dsp.Wdf.Capacitor,
            Cathode.Dsp.Wdf.Capacitor,
            Cathode.Dsp.Wdf.Capacitor,
            Cathode.Dsp.Wdf.Capacitor
        >
    >,
    Cathode.Dsp.Wdf.DiodePair
>;

namespace Cathode.Dsp.Blocks;

/// <summary>How an <see cref="OpAmpStage" /> models its op-amp.</summary>
public enum OpAmpSolve {
    /// <summary>
    ///     Diodes across the feedback: a linear single-pole op-amp inside the adaptor, the diodes in closed form at its
    ///     parent port.
    /// </summary>
    FeedbackDiodes,

    /// <summary>
    ///     No feedback diodes: a linear single-pole op-amp whose output saturates on its rails, an exponential clamp from
    ///     its pole node to ground in closed form at the adaptor's parent port. No slew limit.
    /// </summary>
    Rails,

    /// <summary>The full <see cref="OpAmp" /> macro-model (pole, slew rate, smooth rails) at a Newton root.</summary>
    Device
}

/// <summary>
///     A pedal's op-amp gain stage (<see cref="StageKind.OpAmp" />) for one channel: a non-inverting amplifier whose
///     feedback holds a resistor, the Drive pot, a capacitor and optional clipping diodes, with up to two series RC legs
///     from the inverting input to ground.
/// </summary>
/// <remarks>
///     <para>
///         With diodes across the feedback (the Tube Screamer family) the output never gets further than a diode drop
///         from the input: far from the rails, and far below the slew rate (1 V at 20 kHz is 0.13 V/µs against the
///         JRC4558's 1 V/µs). There the op-amp is its linear single-pole model from its <see cref="OpAmpModel" />, exact in
///         gain-bandwidth: a VCVS of the open-loop gain into an RC at the dominant pole, buffered through the output
///         resistance, all inside an R-type adaptor. The diodes are then the only nonlinearity, and the adaptor is adapted
///         towards them: a <see cref="DiodePair" /> root solves them in closed form, with antiderivative antialiasing
///         (<see cref="OpAmpSolve.FeedbackDiodes" />).
///     </para>
///     <para>
///         Without feedback diodes the op-amp clips on its rails. The same linear op-amp then saturates where a real one
///         does, at its internal (pole) node: an exponential clamp to ground there, at the rails less the model's
///         headroom, is again a closed-form root (<see cref="OpAmpSolve.Rails" />). Where the slew rate is the character
///         (the RAT's LM308, <see cref="DriveStage.SlewLimited" />), the full <see cref="OpAmp" /> device sits at a
///         <see cref="CircuitRoot{TSource,TDevice}" /> instead (K = 2, <see cref="OpAmpSolve.Device" />), at the cost of a
///         Newton solve.
///     </para>
///     <para>
///         Nodes: non-inverting input, inverting input, output, first leg, second leg (then, for the linear model, the
///         open-loop output, the pole and the buffer). Ports: the input source (the guitar or the previous stage, behind
///         its resistance); for the device root the feedback capacitor and the two leg capacitors; for the linear model the
///         load (a resistor port, whose voltage is the output), the feedback capacitor, the two leg capacitors and the
///         pole capacitor, with the diodes or the rail clamp at the parent port.
///     </para>
/// </remarks>
public sealed class OpAmpStage {
    const double Absent = 1e12; // an unused second leg: 1 TΩ in series with 1 pF
    const double PoleResistance = 1e3;
    const double ClampEmission = 0.03; // n Vt of the rail clamp: a firm knee, 0.35 V past the rail at 100 A
    const int InputBias = 0;
    const int Feedback = 1;
    const int Leg = 2;
    const int SecondLeg = 3;
    const int Load = 4; // the device netlist's load resistor
    const int Output = 5; // the linear netlists' output resistance

    static readonly WdfNetlist DeviceNetlist = Netlist(OpAmpSolve.Device);
    static readonly WdfNetlist DiodesNetlist = Netlist(OpAmpSolve.FeedbackDiodes);
    static readonly WdfNetlist RailsNetlist = Netlist(OpAmpSolve.Rails);

    readonly CircuitRoot<ResistiveVoltageSource, OpAmp> device;
    LinearOpAmpCircuit diodes;
    LinearOpAmpCircuit rails;
    DriveStage? stage;
    double rate;
    double drive = double.NaN;
    double source = double.NaN;
    OpAmpSolve solve;

    /// <summary>The model the configured stage uses.</summary>
    public OpAmpSolve Solve => solve;

    /// <summary>The Newton root of <see cref="OpAmpSolve.Device" />.</summary>
    public CircuitRoot<ResistiveVoltageSource, OpAmp> Device => device;

    public OpAmpStage() {
        var amplifier = new OpAmp(OpAmpModel.JRC4558, -4.5, 4.5, 48_000);
        device = new(DeviceNetlist, new(1e3), Capacitors(3), amplifier) {
            // 100 µV steps: the op-amp is linear inside its window, so only its knees are approximate.
            AbsoluteTolerance = 1e-4,
            RelativeTolerance = 1e-4
        };
        var c = new Capacitor(1e-9, 48_000);
        diodes = new(
            new(DiodesNetlist, new(new(1e3), new(10e3), c, c, c, c)),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        diodes.Propagate();
        rails = new(new(RailsNetlist, new(new(1e3), new(10e3), c, c, c, c)), DiodePair.Symmetric(Clamp(3.5)));
        rails.Propagate();
    }

    /// <summary>The netlist for one way of modelling the op-amp (see <see cref="OpAmpSolve" />).</summary>
    public static WdfNetlist Netlist(OpAmpSolve solve) {
        var net = new WdfNetlist();
        var (plus, minus, output, leg, secondLeg) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var linear = solve != OpAmpSolve.Device;
        net.Port(plus, WdfNetlist.Ground);
        if (linear) {
            net.Port(output, WdfNetlist.Ground); // the load
        }

        net.Port(output, minus);
        net.Port(leg, WdfNetlist.Ground);
        net.Port(secondLeg, WdfNetlist.Ground);
        net.Resistor(plus, WdfNetlist.Ground, 1e6);
        net.Resistor(output, minus, 10e3);
        net.Resistor(minus, leg, 4.7e3);
        net.Resistor(minus, secondLeg, Absent);
        if (linear) {
            var (openLoop, pole, buffer) = (net.Node(), net.Node(), net.Node());
            net.Port(pole, WdfNetlist.Ground);
            net.Resistor(openLoop, pole, PoleResistance);
            net.Resistor(buffer, output, 75);
            net.Vcvs(openLoop, WdfNetlist.Ground, plus, minus, 1e5);
            net.Vcvs(buffer, WdfNetlist.Ground, pole, WdfNetlist.Ground, 1);
            if (solve == OpAmpSolve.FeedbackDiodes) {
                net.Up(output, minus);
            } else {
                net.Up(pole, WdfNetlist.Ground);
            }
        } else {
            net.Resistor(output, WdfNetlist.Ground, 10e3);
            net.OpAmp(plus, minus, output);
            net.Probe(output);
        }

        return net;
    }

    /// <summary>
    ///     The rail clamp as a diode model: <see cref="ClampEmission" /> per e-fold of current, 1 mA at
    ///     <paramref name="rail" /> volts.
    /// </summary>
    public static DiodeModel Clamp(double rail) =>
        new(1e-3 * Math.Exp(-rail / ClampEmission), ClampEmission / DiodeModel.ThermalVoltage);

    /// <summary>
    ///     Sets the component values for Drive at <paramref name="drive" /> (0..1) and a source of
    ///     <paramref name="sourceResistance" />. Recomputes only what changed; a new stage or rate starts discharged.
    /// </summary>
    public void Configure(DriveStage stage, double drive, double sourceResistance, double rate, bool antialias) {
        var fresh = !ReferenceEquals(stage, this.stage) || rate != this.rate;
        diodes.Root.Antialiased = antialias;
        rails.Root.Antialiased = antialias;
        if (!fresh && drive == this.drive && sourceResistance == source) {
            return;
        }

        var model = stage.Amplifier;
        if (fresh) {
            this.stage = stage;
            this.rate = rate;
            var d = stage.Diodes;
            solve = d.ForwardCount + d.ReverseCount > 0 ? OpAmpSolve.FeedbackDiodes
                : stage.SlewLimited ? OpAmpSolve.Device
                : OpAmpSolve.Rails;
            var rail = stage.Supply / 2;
            switch (solve) {
                case OpAmpSolve.FeedbackDiodes:
                    diodes.Root.Configure(d.Forward, d.ForwardCount, d.Reverse, d.ReverseCount);
                    break;
                case OpAmpSolve.Rails: {
                    var clamp = Clamp(rail - model.Headroom);
                    rails.Root.Configure(clamp, 1, clamp, 1);
                    break;
                }

                default:
                    device.Device = new(model, -rail, rail, rate);
                    break;
            }
        }

        this.drive = drive;
        source = sourceResistance;
        var second = stage.GroundResistance2 > 0;
        var feedback = stage.Feedback + stage.FeedbackPot * DriveCircuits.AudioTaper(drive);
        var leg = stage.GroundResistance + stage.GroundPot * DriveCircuits.AudioTaper(1 - drive);
        if (solve == OpAmpSolve.Device) {
            device.Source.Value = sourceResistance;
            device.SetResistance(InputBias, stage.InputBias);
            device.SetResistance(Feedback, feedback);
            device.SetResistance(Leg, leg);
            device.SetResistance(SecondLeg, second ? stage.GroundResistance2 : Absent);
            device.SetResistance(Load, stage.Load);
            device.Capacitor(0).Configure(Math.Max(stage.FeedbackCapacitance, 1e-15), rate);
            device.Capacitor(1).Configure(stage.GroundCapacitance, rate);
            device.Capacitor(2).Configure(second ? stage.GroundCapacitance2 : 1e-12, rate);
            device.Propagate();
        } else {
            ref var circuit = ref (solve == OpAmpSolve.FeedbackDiodes ? ref diodes : ref rails);
            ref var tree = ref circuit.Tree;
            tree.Ports.A.Value = sourceResistance;
            tree.Ports.B.Value = stage.Load;
            tree.SetResistance(InputBias, stage.InputBias);
            tree.SetResistance(Feedback, feedback);
            tree.SetResistance(Leg, leg);
            tree.SetResistance(SecondLeg, second ? stage.GroundResistance2 : Absent);
            tree.SetGain(0, model.OpenLoopGain);
            tree.SetResistance(Output, model.OutputResistance);
            tree.Ports.C.Configure(Math.Max(stage.FeedbackCapacitance, 1e-15), rate);
            tree.Ports.D.Configure(stage.GroundCapacitance, rate);
            tree.Ports.E.Configure(second ? stage.GroundCapacitance2 : 1e-12, rate);
            var pole = 2 * Math.PI * model.GainBandwidth / model.OpenLoopGain;
            tree.Ports.F.Configure(1 / (pole * PoleResistance), rate);
            circuit.Propagate();
        }

        if (fresh) {
            Reset();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        switch (solve) {
            case OpAmpSolve.FeedbackDiodes:
                diodes.Tree.Ports.A.Volts = x;
                diodes.Tick();
                return diodes.Tree.Ports.B.Voltage;
            case OpAmpSolve.Rails:
                rails.Tree.Ports.A.Volts = x;
                rails.Tick();
                return rails.Tree.Ports.B.Voltage;
            default:
                device.Source.Volts = x;
                return device.Tick();
        }
    }

    public void Reset() {
        device.Reset();
        diodes.Reset();
        rails.Reset();
    }

    public void FlushDenormals() {
        switch (solve) {
            case OpAmpSolve.FeedbackDiodes:
                Flush(ref diodes);
                break;
            case OpAmpSolve.Rails:
                Flush(ref rails);
                break;
            default:
                device.FlushDenormals();
                break;
        }
    }

    static void Flush(ref LinearOpAmpCircuit circuit) {
        ref var ports = ref circuit.Tree.Ports;
        if (Math.Abs(ports.C.Reflected) < 1e-30
            && Math.Abs(ports.D.Reflected) < 1e-30
            && Math.Abs(ports.E.Reflected) < 1e-30
            && Math.Abs(ports.F.Reflected) < 1e-30) {
            circuit.Reset();
        }
    }

    static Capacitor[] Capacitors(int count) => [..Enumerable.Repeat(new Capacitor(1e-9, 48_000), count)];
}

/// <summary>
///     A single bipolar transistor in common emitter (<see cref="StageKind.Transistor" />) for one channel: the
///     Rangemaster treble booster, or the first stage of a Tone Bender. Base bias from a divider and/or from the collector,
///     emitter resistor with its bypass capacitor, collector load, coupling capacitors in and out. The source is a
///     <see cref="StageSource" />: the guitar (pickup resistance and inductance, volume pot) for a first stage. It starts
///     at its DC operating point, computed once per stage (<see cref="OperatingPoint" />).
/// </summary>
/// <remarks>
///     Nodes: input, base, collector, emitter, output, supply. Ports: the source (its resistance in series with its
///     inductance), the input capacitor (input to base), the emitter capacitor, the output capacitor (collector to
///     output). Resistors, in index order: base to supply, base to ground, emitter, collector, collector to base, load,
///     the source's shunt.
/// </remarks>
public sealed class TransistorStage {
    const double Absent = 1e12;
    const int BaseToSupply = 0;
    const int BaseToGround = 1;
    const int Emitter = 2;
    const int Collector = 3;
    const int Feedback = 4;
    const int Load = 5;
    const int Shunt = 6;

    static readonly WdfNetlist Circuit = Netlist();

    readonly CircuitRoot<Series<ResistiveVoltageSource, Inductor>, Bjt> root;
    readonly double[] start = new double[5];
    DriveStage? stage;
    double rate;
    double input = double.NaN;
    StageSource source;

    public CircuitRoot<Series<ResistiveVoltageSource, Inductor>, Bjt> Root => root;

    public TransistorStage() {
        root = new(
            Circuit,
            new(new(1e3), new(1e-9, 48_000)),
            [new(1e-6, 48_000), new(1e-6, 48_000), new(1e-6, 48_000)],
            new(BjtModel.AC128)
        ) {
            // The start carried along the last Jacobian is close enough that a 1 mV step still converges quadratically
            // to within microvolts; the reference tests match to -94 dB either way, and a confirming iteration is saved.
            AbsoluteTolerance = 1e-3,
            RelativeTolerance = 1e-5
        };
    }

    public static WdfNetlist Netlist() {
        var net = new WdfNetlist();
        var (input, @base, collector, emitter, output, supply) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, @base);
        net.Port(emitter, WdfNetlist.Ground);
        net.Port(collector, output);
        net.Resistor(@base, supply, 470e3);
        net.Resistor(@base, WdfNetlist.Ground, 68e3);
        net.Resistor(emitter, WdfNetlist.Ground, 3.9e3);
        net.Resistor(collector, supply, 10e3);
        net.Resistor(collector, @base, Absent);
        net.Resistor(output, WdfNetlist.Ground, 1e6);
        net.Resistor(input, WdfNetlist.Ground, Absent);
        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Transistor(collector, @base, emitter);
        net.Probe(output);
        return net;
    }

    /// <summary>
    ///     The DC operating point of <paramref name="stage" /> (capacitor voltages, then the junction voltages): the
    ///     circuit settled with every capacitor open. Control thread; allocates.
    /// </summary>
    public static double[] OperatingPoint(DriveStage stage) {
        var settling = new TransistorStage();
        settling.Configure(stage, stage.InputCapacitance, StageSource.Driven(1e3), 48_000);
        return settling.Settle();
    }

    /// <summary>
    ///     Sets the component values with an input capacitor of <paramref name="inputCapacitance" /> (a treble booster's
    ///     range) and the <paramref name="source" />. A new stage or rate starts at the operating point stored in
    ///     <see cref="DriveCircuits" />.
    /// </summary>
    public void Configure(DriveStage stage, double inputCapacitance, StageSource source, double rate) {
        var fresh = !ReferenceEquals(stage, this.stage) || rate != this.rate;
        if (!fresh && inputCapacitance == input && source == this.source) {
            return;
        }

        if (fresh) {
            this.stage = stage;
            this.rate = rate;
            root.Device = new(stage.Transistor);
            root.SetSupply(0, stage.Transistor.Polarity == BjtPolarity.Npn ? stage.Supply : -stage.Supply);
            root.SetResistance(BaseToSupply, stage.BaseToSupply > 0 ? stage.BaseToSupply : Absent);
            root.SetResistance(BaseToGround, stage.BaseToGround > 0 ? stage.BaseToGround : Absent);
            root.SetResistance(Emitter, stage.Emitter);
            root.SetResistance(Collector, stage.Collector);
            root.SetResistance(Feedback, stage.Feedback > 0 ? stage.Feedback : Absent);
            root.SetResistance(Load, stage.Load);
            root.Capacitor(1).Configure(stage.EmitterCapacitance, rate);
            root.Capacitor(2).Configure(stage.OutputCapacitance, rate);
        }

        input = inputCapacitance;
        this.source = source;
        source.Apply(ref root.Source, rate);
        root.SetResistance(Shunt, source.Shunt);
        root.Capacitor(0).Configure(inputCapacitance, rate);
        root.Propagate();
        if (fresh) {
            Reset();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        root.Source.A.Volts = x;
        return root.Tick();
    }

    /// <summary>Back to the operating point.</summary>
    public void Reset() {
        root.Reset();
        if (stage is not null && DriveCircuits.TryGetOperatingPoint(stage, out var point)) {
            root.StartAt(point);
        }
    }

    double[] Settle() => OperatingPoints.Settle(root, start, Tick, rate);
}

/// <summary>
///     The Fuzz Face pair (<see cref="StageKind.FuzzPair" />) for one channel: two direct-coupled common-emitter
///     transistors, the second one's emitter feeding DC (and, with the Fuzz pot below full, signal) back to the first
///     base through 100k; the Fuzz pot from the second emitter to ground with its wiper bypassed (Drive moves the wiper).
///     Both transistors are solved together at one root (K = 4). The first stage's low input impedance loads the
///     guitar's pickup and volume pot (<see cref="StageSource" />), which is what makes rolling the volume back clean it
///     up. It starts at its DC operating point, computed once per stage (<see cref="OperatingPoint" />).
/// </summary>
/// <remarks>
///     Nodes: input, first base, first collector (the second base), second emitter, second collector, the collector
///     resistors' junction, the Fuzz pot's wiper, output, supply. Ports: the source (its resistance in series with its
///     inductance), the input capacitor, the wiper's bypass capacitor, the output capacitor. Resistors, in index order:
///     first collector load, second collector, junction to supply, feedback, emitter to wiper, wiper to ground, load, the
///     source's shunt.
/// </remarks>
public sealed class FuzzPairStage {
    const double Absent = 1e12;
    const int FirstCollector = 0;
    const int SecondCollector = 1;
    const int CollectorSupply = 2;
    const int Feedback = 3;
    const int EmitterToWiper = 4;
    const int WiperToGround = 5;
    const int Load = 6;
    const int Shunt = 7;

    static readonly WdfNetlist Circuit = Netlist();

    readonly CircuitRoot<Series<ResistiveVoltageSource, Inductor>, Devices<Bjt, Bjt>> root;
    readonly double[] start = new double[7];
    DriveStage? stage;
    double rate;
    double drive = double.NaN;
    StageSource source;

    public CircuitRoot<Series<ResistiveVoltageSource, Inductor>, Devices<Bjt, Bjt>> Root => root;

    public FuzzPairStage() {
        root = new(
            Circuit,
            new(new(1e3), new(1e-9, 48_000)),
            [new(2.2e-6, 48_000), new(20e-6, 48_000), new(10e-9, 48_000)],
            new(new(BjtModel.AC128), new(BjtModel.AC128))
        ) {
            // The start carried along the last Jacobian is close enough that a 1 mV step still converges quadratically
            // to within microvolts; the reference tests match to -94 dB either way, and a confirming iteration is saved.
            AbsoluteTolerance = 1e-3,
            RelativeTolerance = 1e-5
        };
    }

    public static WdfNetlist Netlist() {
        var net = new WdfNetlist();
        var input = net.Node();
        var firstBase = net.Node();
        var firstCollector = net.Node();
        var secondEmitter = net.Node();
        var secondCollector = net.Node();
        var junction = net.Node();
        var wiper = net.Node();
        var output = net.Node();
        var supply = net.Node();
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, firstBase);
        net.Port(wiper, WdfNetlist.Ground);
        net.Port(secondCollector, output);
        net.Resistor(firstCollector, supply, 33e3);
        net.Resistor(secondCollector, junction, 470);
        net.Resistor(junction, supply, 8.2e3);
        net.Resistor(secondEmitter, firstBase, 100e3);
        net.Resistor(secondEmitter, wiper, 500);
        net.Resistor(wiper, WdfNetlist.Ground, 500);
        net.Resistor(output, WdfNetlist.Ground, 500e3);
        net.Resistor(input, WdfNetlist.Ground, Absent);
        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Transistor(firstCollector, firstBase, WdfNetlist.Ground);
        net.Transistor(secondCollector, firstCollector, secondEmitter);
        net.Probe(output);
        return net;
    }

    /// <summary>
    ///     The DC operating point of <paramref name="stage" />; see <see cref="TransistorStage.OperatingPoint" />.
    /// </summary>
    public static double[] OperatingPoint(DriveStage stage) {
        var settling = new FuzzPairStage();
        settling.Configure(stage, 0.5, StageSource.Driven(1e3), 48_000);
        return settling.Settle();
    }

    /// <summary>Sets the Fuzz pot to <paramref name="drive" /> (0..1) and the <paramref name="source" />.</summary>
    public void Configure(DriveStage stage, double drive, StageSource source, double rate) {
        var fresh = !ReferenceEquals(stage, this.stage) || rate != this.rate;
        if (!fresh && drive == this.drive && source == this.source) {
            return;
        }

        if (fresh) {
            this.stage = stage;
            this.rate = rate;
            root.Device = new(new(stage.Transistor), new(stage.Transistor));
            root.SetSupply(0, stage.Transistor.Polarity == BjtPolarity.Npn ? stage.Supply : -stage.Supply);
            root.SetResistance(FirstCollector, stage.Collector);
            root.SetResistance(SecondCollector, stage.SecondCollector);
            root.SetResistance(CollectorSupply, stage.CollectorSupply);
            root.SetResistance(Feedback, stage.Feedback);
            root.SetResistance(Load, stage.Load);
            root.Capacitor(0).Configure(stage.InputCapacitance, rate);
            root.Capacitor(1).Configure(stage.EmitterCapacitance, rate);
            root.Capacitor(2).Configure(stage.OutputCapacitance, rate);
        }

        this.drive = drive;
        this.source = source;
        source.Apply(ref root.Source, rate);
        root.SetResistance(Shunt, source.Shunt);
        root.SetResistance(EmitterToWiper, (1 - drive) * stage.Emitter);
        root.SetResistance(WiperToGround, drive * stage.Emitter);
        root.Propagate();
        if (fresh) {
            Reset();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        root.Source.A.Volts = x;
        return root.Tick();
    }

    /// <summary>
    ///     Back to the operating point. The Fuzz pot does not move it (the emitter sees the whole pot at DC), only the
    ///     bypass capacitor's charge, which sits at the wiper's share of the emitter voltage.
    /// </summary>
    public void Reset() {
        root.Reset();
        if (stage is null || !DriveCircuits.TryGetOperatingPoint(stage, out var point)) {
            return;
        }

        root.StartAt(point);
        var emitter = point[3] - point[4] - point[5]; // v_BE1 - v_BC1 - v_BE2, up from the grounded first emitter
        root.Capacitor(1).Precharge(drive * emitter);
    }

    double[] Settle() => OperatingPoints.Settle(root, start, Tick, rate);
}

/// <summary>The DC operating point of a transistor stage's root.</summary>
static class OperatingPoints {
    /// <summary>
    ///     Settles <paramref name="root" /> with zero input and every capacitor open, and writes the operating point into
    ///     <paramref name="start" />. A capacitor is opened by giving it a huge port resistance (a femtofarad) and, after
    ///     every sample, recharging it to the voltage across its port: with no current through it, that is the open-circuit
    ///     voltage the network puts there. (Left to itself, a capacitor that small rings at Nyquist for ever: the bilinear
    ///     transform maps its vanishing time constant to a pole at -1.) Newton walks the junctions in from zero meanwhile.
    /// </summary>
    public static double[] Settle<TSource, TDevice>(
        CircuitRoot<TSource, TDevice> root,
        double[] start,
        Func<double, double> tick,
        double rate
    )
        where TSource : struct, IWdfPort
        where TDevice : struct, INonlinearDevice {
        for (var i = 0; i < root.Capacitors; i++) {
            root.Capacitor(i).Configure(1e-15, rate);
        }

        root.Propagate();
        root.Reset();
        for (var n = 0; n < 4_000; n++) {
            tick(0);
            for (var i = 0; i < root.Capacitors; i++) {
                // Relax towards the port voltage (a + b) / 2: b is what the capacitor was precharged to, a what it now
                // holds.
                ref var capacitor = ref root.Capacitor(i);
                capacitor.Precharge(0.5 * (capacitor.Reflected + start[i]));
                start[i] = capacitor.Reflected;
            }
        }

        root.Snapshot(start);
        return [..start];
    }
}

/// <summary>
///     What drives a transistor stage: a resistance in series with an inductance (the guitar's pickup and the top of its
///     volume pot, or an earlier stage's output resistance with no inductance), with a shunt to ground at the stage input
///     (the bottom of the volume pot; absent between stages).
/// </summary>
public readonly record struct StageSource(double Resistance, double Inductance, double Shunt) {
    /// <summary>A stage driven by an earlier one through <paramref name="resistance" />.</summary>
    public static StageSource Driven(double resistance) => new(resistance, 1e-9, 1e12);

    /// <summary>Sets the series source port.</summary>
    public void Apply(ref Series<ResistiveVoltageSource, Inductor> port, double rate) {
        port.A.Value = Resistance;
        port.B.Configure(Inductance, rate);
    }
}

/// <summary>
///     A pedal's tone network (<see cref="ToneNetwork" />) at the base rate, for one channel: a linear R-type root with
///     the Tone pot inside the adaptor, so turning it recomputes the scattering matrix (allocation-free) rather than
///     approximating the network. Op-amps in the network (the Tube Screamer's tone amp, a Baxandall treble) are ideal
///     voltage-controlled voltage sources of high gain.
/// </summary>
public sealed class ToneStage {
    /// <summary>Open-loop gain of a tone network's op-amp (a VCVS).</summary>
    public const double OpenLoopGain = 1e5;

    static readonly WdfNetlist[] Netlists = [..Enumerable.Range(0, (int)ToneKind.Range).Select(static k => Netlist((ToneKind)k))];

    readonly CircuitRoot<ResistiveVoltageSource, NoDevice>[] roots;
    ToneNetwork? network;
    CircuitRoot<ResistiveVoltageSource, NoDevice>? root;
    double rate;
    double position = double.NaN;
    double sign = 1;

    public ToneStage() {
        roots = new CircuitRoot<ResistiveVoltageSource, NoDevice>[Netlists.Length];
        for (var kind = 0; kind < roots.Length; kind++) {
            var capacitors = Netlists[kind].Count(NetlistElementKind.Port) - 1;
            roots[kind] = new(Netlists[kind], new(1e3), [..Enumerable.Repeat(new Capacitor(1e-9, 48_000), capacitors)], default);
        }
    }

    /// <summary>
    ///     The netlist of a network kind: port 0 is the source, then one port per capacitor, from
    ///     <see cref="ToneNetwork.Capacitor1" /> on; resistor values are placeholders set by <see cref="Configure" />.
    /// </summary>
    public static WdfNetlist Netlist(ToneKind kind) {
        var net = new WdfNetlist();
        const int Ground = WdfNetlist.Ground;
        switch (kind) {
            case ToneKind.HighCut: {
                var (source, mid, output) = (net.Node(), net.Node(), net.Node());
                net.Port(source, Ground);
                net.Port(output, Ground);
                net.Resistor(source, mid, 1e3); // Resistor1
                net.Resistor(mid, output, 1e3); // the pot
                net.Resistor(output, Ground, 1e6); // load
                net.Probe(output);
                break;
            }

            case ToneKind.Blend: {
                var (source, low, high, wiper) = (net.Node(), net.Node(), net.Node(), net.Node());
                net.Port(source, Ground);
                net.Port(low, Ground);
                net.Port(source, high);
                net.Resistor(source, low, 1e3); // Resistor1
                net.Resistor(high, Ground, 1e3); // Resistor2
                net.Resistor(low, wiper, 1e3); // pot, bass side
                net.Resistor(wiper, high, 1e3); // pot, treble side
                net.Resistor(wiper, Ground, 1e6); // load
                net.Probe(wiper);
                break;
            }

            case ToneKind.Screamer: {
                var (source, plus, minus) = (net.Node(), net.Node(), net.Node());
                var (output, wiper, shunt) = (net.Node(), net.Node(), net.Node());
                net.Port(source, Ground);
                net.Port(plus, Ground);
                net.Port(shunt, Ground);
                net.Resistor(source, plus, 1e3); // Resistor1
                net.Resistor(plus, wiper, 1e3); // pot, treble-cut side
                net.Resistor(wiper, minus, 1e3); // pot, treble-boost side
                net.Resistor(wiper, shunt, 220); // Resistor2
                net.Resistor(output, minus, 1e3); // Resistor3
                net.Resistor(output, Ground, 1e6); // load
                net.Vcvs(output, Ground, plus, minus, OpenLoopGain);
                net.Probe(output);
                break;
            }

            case ToneKind.Treble: {
                var (source, minus, output) = (net.Node(), net.Node(), net.Node());
                var (inputEnd, outputEnd, wiper) = (net.Node(), net.Node(), net.Node());
                net.Port(source, Ground);
                net.Port(source, inputEnd);
                net.Port(output, outputEnd);
                net.Resistor(source, minus, 10e3); // Resistor1
                net.Resistor(output, minus, 10e3); // Resistor2
                net.Resistor(inputEnd, wiper, 1e3); // pot, boost side
                net.Resistor(wiper, outputEnd, 1e3); // pot, cut side
                net.Resistor(wiper, minus, 1e3); // Resistor3
                net.Resistor(output, Ground, 1e6); // load
                net.Vcvs(output, Ground, Ground, minus, OpenLoopGain);
                net.Probe(output);
                break;
            }

            case ToneKind.Stack: {
                var (source, top, wiper) = (net.Node(), net.Node(), net.Node());
                var (bottom, slope, mid) = (net.Node(), net.Node(), net.Node());
                net.Port(source, Ground);
                net.Port(source, top);
                net.Port(slope, bottom);
                net.Port(slope, mid);
                net.Resistor(source, slope, 33e3); // Resistor1, the slope resistor
                net.Resistor(top, wiper, 1e3); // treble pot, top
                net.Resistor(wiper, bottom, 1e3); // treble pot, bottom
                net.Resistor(bottom, mid, 1e3); // Resistor2, the bass pot
                net.Resistor(mid, Ground, 1e3); // Resistor3, the middle pot
                net.Resistor(wiper, Ground, 1e6); // load
                net.Probe(wiper);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "A range control has no network.");
        }

        return net;
    }

    /// <summary>
    ///     Sets the network and its Tone pot to <paramref name="tone" /> (0..1). Recomputes the matrix only when the
    ///     setting changed; a new network starts discharged.
    /// </summary>
    public void Configure(ToneNetwork network, double tone, double rate) {
        var fresh = !ReferenceEquals(network, this.network) || rate != this.rate;
        if (!fresh && tone == position) {
            return;
        }

        this.network = network;
        this.rate = rate;
        position = tone;
        sign = network.Kind == ToneKind.Treble ? -1 : 1; // the Baxandall amp inverts; keep the pedal's polarity
        root = network.Kind == ToneKind.Range ? null : roots[(int)network.Kind];
        if (root is null) {
            return;
        }

        Set(root, network, tone, rate);
        if (fresh) {
            root.Reset();
        }
    }

    /// <summary>
    ///     Sets the components of <paramref name="network" /> with its pot at <paramref name="tone" />, in netlist resistor
    ///     order, and recomputes the matrix.
    /// </summary>
    static void Set(CircuitRoot<ResistiveVoltageSource, NoDevice> root, ToneNetwork network, double tone, double rate) {
        var n = network;
        var amount = n.AudioTaper ? DriveCircuits.AudioTaper(tone) : tone;
        root.Source.Value = n.Source;
        root.Capacitor(0).Configure(n.Capacitor1, rate);
        if (root.Capacitors > 1) {
            root.Capacitor(1).Configure(n.Capacitor2, rate);
        }

        if (root.Capacitors > 2) {
            root.Capacitor(2).Configure(n.Capacitor3, rate);
        }

        switch (n.Kind) {
            case ToneKind.HighCut:
                root.SetResistance(0, n.Resistor1);
                root.SetResistance(1, n.Pot * (n.AudioTaper ? DriveCircuits.AudioTaper(1 - tone) : 1 - tone));
                root.SetResistance(2, n.Load);
                break;
            case ToneKind.Blend:
                root.SetResistance(0, n.Resistor1);
                root.SetResistance(1, n.Resistor2);
                root.SetResistance(2, n.Pot * amount);
                root.SetResistance(3, n.Pot * (1 - amount));
                root.SetResistance(4, n.Load);
                break;
            case ToneKind.Screamer:
                root.SetResistance(0, n.Resistor1);
                root.SetResistance(1, n.Pot * amount);
                root.SetResistance(2, n.Pot * (1 - amount));
                root.SetResistance(3, n.Resistor2);
                root.SetResistance(4, n.Resistor3);
                root.SetResistance(5, n.Load);
                break;
            case ToneKind.Treble:
                root.SetResistance(0, n.Resistor1);
                root.SetResistance(1, n.Resistor2);
                root.SetResistance(2, n.Pot * (1 - amount));
                root.SetResistance(3, n.Pot * amount);
                root.SetResistance(4, n.Resistor3);
                root.SetResistance(5, n.Load);
                break;
            case ToneKind.Stack:
                root.SetResistance(0, n.Resistor1);
                root.SetResistance(1, n.Pot * (1 - amount));
                root.SetResistance(2, n.Pot * amount);
                root.SetResistance(3, n.Resistor2 * DriveCircuits.AudioTaper(0.5));
                root.SetResistance(4, n.Resistor3 * 0.5);
                root.SetResistance(5, n.Load);
                break;
        }

        root.Propagate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        if (root is null) {
            return x;
        }

        root.Source.Volts = x;
        return sign * root.Tick();
    }

    public void Reset() {
        foreach (var root in roots) {
            root.Reset();
        }
    }

    /// <summary>Clears capacitor charge that has decayed into the subnormal range.</summary>
    public void FlushDenormals() => root?.FlushDenormals();
}
