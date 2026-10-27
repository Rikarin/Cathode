using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests.Circuits;

/// <summary>
///     A nonlinear stage built twice, as a wave digital filter and as a <see cref="ReferenceCircuit" /> with the same
///     node numbering (the reference's source node comes last), so that operating points carry over node by node.
/// </summary>
interface ITestStage {
    /// <summary>Node whose voltage is the stage's output (AC coupled into its load).</summary>
    int OutputNode { get; }

    NewtonSolver Newton { get; }

    /// <summary>One WDF sample; returns the output node's voltage.</summary>
    double Tick(double input);

    /// <summary>The WDF's voltage at <paramref name="node" /> after the last sample.</summary>
    double Node(int node);

    /// <summary>Precharges every capacitor and sets Newton's start from a reference operating point.</summary>
    void StartAt(Func<int, double> voltage);

    /// <summary>The same circuit for the reference simulator, its devices built for <paramref name="rate" />.</summary>
    ReferenceCircuit Reference(Func<double, double> input, double rate);
}

/// <summary>
///     A common-emitter stage: 9 V, 47k / 10k base divider, 4.7k collector, 1k emitter bypassed by 47 µF, 1 µF in and
///     out, 100k load, driven from 1 kΩ.
/// </summary>
sealed class CommonEmitterStage : ITestStage {
    const double Supply = 9;

    readonly BjtModel model;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor>, Bjt> root;

    public int OutputNode { get; }

    public NewtonSolver Newton => root.Newton;

    public CommonEmitterStage(double rate, BjtModel model) {
        this.model = model;
        var net = new WdfNetlist();
        var (input, baseNode, collector, emitter, supply, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, baseNode);
        net.Port(emitter, WdfNetlist.Ground);
        net.Port(collector, output);
        net.Resistor(supply, baseNode, 47e3);
        net.Resistor(baseNode, WdfNetlist.Ground, 10e3);
        net.Resistor(supply, collector, 4.7e3);
        net.Resistor(emitter, WdfNetlist.Ground, 1e3);
        net.Resistor(output, WdfNetlist.Ground, 100e3);
        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Transistor(collector, baseNode, emitter);
        for (var node = 1; node <= net.Nodes; node++) {
            net.Probe(node);
        }

        root = new(net, new(new(1e3), new(1e-6, rate), new(47e-6, rate), new(1e-6, rate)), new(model));
        root.SetVoltage(0, Supply);
        OutputNode = output;
    }

    public double Tick(double input) {
        root.Ports.A.Volts = input;
        root.Tick();
        return root.Probe(OutputNode - 1);
    }

    public double Node(int node) => root.Probe(node - 1);

    public void StartAt(Func<int, double> voltage) {
        root.Ports.B.Precharge(voltage(1) - voltage(2));
        root.Ports.C.Precharge(voltage(4));
        root.Ports.D.Precharge(voltage(3) - voltage(6));
        root.SetInitialVoltages([voltage(2) - voltage(4), voltage(2) - voltage(3)]);
    }

    public ReferenceCircuit Reference(Func<double, double> input, double rate) {
        var c = new ReferenceCircuit();
        var (inputNode, baseNode, collector, emitter, supply, output) = (c.Node(), c.Node(), c.Node(), c.Node(),
            c.Node(),
            c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, inputNode, 1e3);
        c.Capacitor(inputNode, baseNode, 1e-6);
        c.Capacitor(emitter, 0, 47e-6);
        c.Capacitor(collector, output, 1e-6);
        c.Resistor(supply, baseNode, 47e3);
        c.Resistor(baseNode, 0, 10e3);
        c.Resistor(supply, collector, 4.7e3);
        c.Resistor(emitter, 0, 1e3);
        c.Resistor(output, 0, 100e3);
        c.Source(supply, 0, static _ => Supply);
        c.Device(new Bjt(model), (baseNode, emitter), (baseNode, collector));
        return c;
    }
}

/// <summary>
///     A common-cathode triode stage: 300 V supply, 100k plate resistor, 1.5k cathode resistor bypassed by 22 µF, 1M
///     grid leak, 22 nF in and out, 1M load (the next grid), driven from 1 kΩ.
/// </summary>
sealed class CommonCathodeStage : ITestStage {
    const double Supply = 300;

    readonly TriodeModel model;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor>, Triode> root;

    public int OutputNode { get; }

    public NewtonSolver Newton => root.Newton;

    public CommonCathodeStage(double rate, TriodeModel model) {
        this.model = model;
        var net = new WdfNetlist();
        var (input, grid, plate, cathode, supply, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, grid);
        net.Port(cathode, WdfNetlist.Ground);
        net.Port(plate, output);
        net.Resistor(grid, WdfNetlist.Ground, 1e6);
        net.Resistor(supply, plate, 100e3);
        net.Resistor(cathode, WdfNetlist.Ground, 1.5e3);
        net.Resistor(output, WdfNetlist.Ground, 1e6);
        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Triode(plate, grid, cathode);
        for (var node = 1; node <= net.Nodes; node++) {
            net.Probe(node);
        }

        root = new(net, new(new(1e3), new(22e-9, rate), new(22e-6, rate), new(22e-9, rate)), new(model));
        root.SetVoltage(0, Supply);
        OutputNode = output;
    }

    public double Tick(double input) {
        root.Ports.A.Volts = input;
        root.Tick();
        return root.Probe(OutputNode - 1);
    }

    public double Node(int node) => root.Probe(node - 1);

    public void StartAt(Func<int, double> voltage) {
        root.Ports.B.Precharge(voltage(1) - voltage(2));
        root.Ports.C.Precharge(voltage(4));
        root.Ports.D.Precharge(voltage(3) - voltage(6));
        root.SetInitialVoltages([voltage(2) - voltage(4), voltage(3) - voltage(4)]);
    }

    public ReferenceCircuit Reference(Func<double, double> input, double rate) {
        var c = new ReferenceCircuit();
        var (inputNode, grid, plate, cathode, supply, output) = (c.Node(), c.Node(), c.Node(), c.Node(), c.Node(),
            c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, inputNode, 1e3);
        c.Capacitor(inputNode, grid, 22e-9);
        c.Capacitor(cathode, 0, 22e-6);
        c.Capacitor(plate, output, 22e-9);
        c.Resistor(grid, 0, 1e6);
        c.Resistor(supply, plate, 100e3);
        c.Resistor(cathode, 0, 1.5e3);
        c.Resistor(output, 0, 1e6);
        c.Source(supply, 0, static _ => Supply);
        c.Device(new Triode(model), (grid, cathode), (plate, cathode));
        return c;
    }
}

/// <summary>
///     A Tube Screamer-like clipping amplifier: non-inverting op-amp (±4.5 V rails) with 100k ∥ 51 pF ∥ two silicon
///     diodes in the feedback, 4.7k + 47 nF to ground, 1M input bias resistor, driven from 1 kΩ. Two devices at one
///     root: the op-amp's two ports and the diode pair's one.
/// </summary>
sealed class OpAmpClipperStage : ITestStage {
    const double Rail = 4.5;

    readonly OpAmpModel model;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor>, Devices<OpAmp, DiodeClipper>> root;

    public int OutputNode { get; }

    public NewtonSolver Newton => root.Newton;

    public OpAmpClipperStage(double rate, OpAmpModel model) {
        this.model = model;
        var net = new WdfNetlist();
        var (plus, minus, output, leg) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(plus, WdfNetlist.Ground);
        net.Port(output, minus);
        net.Port(leg, WdfNetlist.Ground);
        net.Resistor(plus, WdfNetlist.Ground, 1e6);
        net.Resistor(output, minus, 100e3);
        net.Resistor(minus, leg, 4.7e3);
        net.OpAmp(plus, minus, output);
        net.Diode(output, minus);
        for (var node = 1; node <= net.Nodes; node++) {
            net.Probe(node);
        }

        root = new(
            net,
            new(new(1e3), new(51e-12, rate), new(47e-9, rate)),
            new(new(model, -Rail, Rail, rate), DiodeClipper.Symmetric(DiodeModel.Silicon))
        );
        OutputNode = output;
    }

    public double Tick(double input) {
        root.Ports.A.Volts = input;
        root.Tick();
        return root.Probe(OutputNode - 1);
    }

    public double Node(int node) => root.Probe(node - 1);

    public void StartAt(Func<int, double> voltage) { }

    public ReferenceCircuit Reference(Func<double, double> input, double rate) {
        var c = new ReferenceCircuit();
        var (plus, minus, output, leg) = (c.Node(), c.Node(), c.Node(), c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, plus, 1e3);
        c.Capacitor(output, minus, 51e-12);
        c.Capacitor(leg, 0, 47e-9);
        c.Resistor(plus, 0, 1e6);
        c.Resistor(output, minus, 100e3);
        c.Resistor(minus, leg, 4.7e3);
        c.Device(new OpAmp(model, -Rail, Rail, rate), (plus, minus), (output, 0));
        c.Device(DiodeClipper.Symmetric(DiodeModel.Silicon), (output, minus));
        return c;
    }
}

/// <summary>
///     A single-ended EL84 stage: 300 V supply, 1k plate load (standing in for the transformer), screen fed through
///     470 Ω, 135 Ω cathode resistor bypassed by 47 µF, 470k grid leak, 22 nF in, 100 nF out into 8k, driven from 1 kΩ.
/// </summary>
sealed class PentodeStage : ITestStage {
    const double Supply = 300;

    readonly PentodeModel model;
    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor>, Pentode> root;

    public int OutputNode { get; }

    public NewtonSolver Newton => root.Newton;

    public PentodeStage(double rate, PentodeModel model) {
        this.model = model;
        var net = new WdfNetlist();
        var (input, grid, plate, screen, cathode, supply, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, grid);
        net.Port(cathode, WdfNetlist.Ground);
        net.Port(plate, output);
        net.Resistor(grid, WdfNetlist.Ground, 470e3);
        net.Resistor(supply, plate, 1e3);
        net.Resistor(supply, screen, 470);
        net.Resistor(cathode, WdfNetlist.Ground, 135);
        net.Resistor(output, WdfNetlist.Ground, 8e3);
        net.VoltageSource(supply, WdfNetlist.Ground);
        net.Pentode(plate, screen, grid, cathode);
        for (var node = 1; node <= net.Nodes; node++) {
            net.Probe(node);
        }

        root = new(net, new(new(1e3), new(22e-9, rate), new(47e-6, rate), new(100e-9, rate)), new(model));
        root.SetVoltage(0, Supply);
        OutputNode = output;
    }

    public double Tick(double input) {
        root.Ports.A.Volts = input;
        root.Tick();
        return root.Probe(OutputNode - 1);
    }

    public double Node(int node) => root.Probe(node - 1);

    public void StartAt(Func<int, double> voltage) {
        root.Ports.B.Precharge(voltage(1) - voltage(2));
        root.Ports.C.Precharge(voltage(5));
        root.Ports.D.Precharge(voltage(3) - voltage(7));
        root.SetInitialVoltages([voltage(2) - voltage(5), voltage(4) - voltage(5), voltage(3) - voltage(5)]);
    }

    public ReferenceCircuit Reference(Func<double, double> input, double rate) {
        var c = new ReferenceCircuit();
        var (inputNode, grid, plate, screen, cathode, supply, output) =
            (c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node(), c.Node());
        var source = c.Node();
        c.Source(source, 0, input);
        c.Resistor(source, inputNode, 1e3);
        c.Capacitor(inputNode, grid, 22e-9);
        c.Capacitor(cathode, 0, 47e-6);
        c.Capacitor(plate, output, 100e-9);
        c.Resistor(grid, 0, 470e3);
        c.Resistor(supply, plate, 1e3);
        c.Resistor(supply, screen, 470);
        c.Resistor(cathode, 0, 135);
        c.Resistor(output, 0, 8e3);
        c.Source(supply, 0, static _ => Supply);
        c.Device(new Pentode(model), (grid, cathode), (screen, cathode), (plate, cathode));
        return c;
    }
}
