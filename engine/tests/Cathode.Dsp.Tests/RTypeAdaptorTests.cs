using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;
using System.Diagnostics;

namespace Cathode.Dsp.Tests;

/// <summary>
///     R-type adaptors (bridged, non-series-parallel networks) against the reference simulator's AC analysis. A linear
///     WDF with trapezoidal reactances is exactly the bilinear transform of its analog prototype, so the reference is
///     evaluated at the prewarped frequency 2 fs tan(π f / fs), and the two must agree to rounding.
/// </summary>
public class RTypeAdaptorTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Length = 32_768;
    const double ToleranceDb = 0.1;

    static readonly double[] Frequencies = [20, 50, 100, 200, 500, 1_000, 2_000, 5_000, 10_000, 15_000, 20_000];

    [Fact]
    public void Bridged_t_root_matches_the_reference_response() {
        var root = BridgedTRoot();
        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            root.Ports.A.Volts = i == 0 ? 1 : 0;
            impulse[i] = (float)root.Tick();
        }

        var (circuit, source, probe) = BridgedTReference();
        var worst = Compare(impulse, new(circuit), source, probe);
        output.WriteLine($"bridged-T as root: worst {worst:0.000000} dB");
        Assert.True(worst < ToleranceDb, $"{worst:0.000} dB from the reference");
    }

    [Fact]
    public void Bridged_t_as_an_internal_adaptor_matches_the_reference_response() {
        // The same network below an open-circuit root: the output is the adaptor's parent port, adapted to the
        // Thévenin resistance of the network.
        var net = new WdfNetlist();
        var (input, mid, outputNode) = (net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, outputNode);
        net.Port(mid, WdfNetlist.Ground);
        net.Resistor(input, mid, 10e3);
        net.Resistor(mid, outputNode, 10e3);
        net.Resistor(outputNode, WdfNetlist.Ground, 100e3);
        net.Up(outputNode, WdfNetlist.Ground);
        var circuit = new WdfCircuit<RTypeAdaptor<Ports<ResistiveVoltageSource, Capacitor, Capacitor>>, OpenCircuit>(
            new(net, new(new(600), new(10e-9, Rate), new(100e-9, Rate))),
            default
        );
        circuit.Propagate();
        Assert.Equal(0, circuit.Tree.Scattering.Row(circuit.Tree.Scattering.UpRow)[circuit.Tree.Scattering.UpColumn]);

        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            circuit.Tree.Ports.A.Volts = i == 0 ? 1 : 0;
            impulse[i] = (float)circuit.Tick();
        }

        var (reference, source, probe) = BridgedTReference();
        var worst = Compare(impulse, new(reference), source, probe);
        output.WriteLine($"bridged-T as adaptor: worst {worst:0.000000} dB, R_up {circuit.Tree.Resistance:0.0} Ω");
        Assert.True(worst < ToleranceDb, $"{worst:0.000} dB from the reference");
    }

    [Theory]
    [InlineData(0.5, 0.5, 0.5)]
    [InlineData(0.9, 0.1, 0.3)]
    [InlineData(0.1, 1.0, 0.8)]
    [InlineData(1.0, 0.0, 0.0)]
    public void Fender_tone_stack_matches_the_reference_at_any_setting(double treble, double bass, double middle) {
        var stack = new FenderToneStack(Rate);
        stack.Set(0.5, 0.5, 0.5); // start elsewhere: the setting below is reached by a recompute, as a pot move would
        stack.Set(treble, bass, middle);
        var impulse = new float[Length];
        for (var i = 0; i < Length; i++) {
            impulse[i] = (float)stack.Tick(i == 0 ? 1 : 0);
        }

        var (circuit, source, probe) = FenderToneStack.Reference(treble, bass, middle);
        var worst = Compare(impulse, new(circuit), source, probe);
        output.WriteLine($"tone stack t {treble} b {bass} m {middle}: worst {worst:0.000000} dB");
        Assert.True(worst < ToleranceDb, $"{worst:0.000} dB from the reference");
    }

    [Theory]
    [InlineData(0.5, 0.5, 0.5)]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(0.0, 1.0, 0.0)]
    public void Fender_tone_stack_scattering_at_its_end_stops_matches_an_exact_solve(
        double treble,
        double bass,
        double middle
    ) {
        // A pot at an end stop keeps RTypeScattering.EndStop of its track. At 1 µΩ that put 1e6 S beside the 1 MΩ grid
        // leak: cond∞ 7e10 to 2.1e11 at these end stops, and scattering coefficients up to 2.7e-6 from the exact solve
        // (five digits lost). At 1 mΩ both are a thousand times smaller (cond∞ 7e7 to 2.1e8, error at most 3.2e-9); at
        // noon (cond∞ 1e3, error 1e-15) nothing changes.
        var stack = new FenderToneStack(Rate);
        stack.Set(0.5, 0.5, 0.5);
        stack.Set(treble, bass, middle);
        var (error, condition) = ExactScattering.Compare(stack.Netlist, stack.Scattering);
        output.WriteLine($"t {treble} b {bass} m {middle}: cond∞ {condition:E2}, scattering error {error:E2}");
        Assert.True(condition < 1e9, $"cond∞ {condition:E2}");
        Assert.True(error < 1e-8, $"scattering error {error:E2}");
    }

    [Fact]
    public void Factor_rejects_a_nearly_singular_matrix_and_accepts_a_badly_scaled_regular_one() {
        Span<int> pivots = stackalloc int[2];
        // Two nodes joined by g, each held to ground by 1 µS: the second pivot is what is left after cancelling g, 2 µS.
        static double[] Joined(double g) => [g + 1e-6, -g, -g, g + 1e-6];

        // A 1 mΩ end stop (1000 S) leaves the pivot 2e-9 of its row: regular.
        Assert.True(DenseLu.Factor(Joined(1e3), 2, pivots));
        // A nanohm (1e9 S) leaves 2e-15: rounding noise, which the old 1e-300 threshold let through.
        Assert.False(DenseLu.Factor(Joined(1e9), 2, pivots));
        Assert.True(DenseLu.Factor(Joined(1e9), 2, pivots, 1e-300));
        // The same in its plainest form: rows that differ in the sixteenth digit.
        Assert.False(DenseLu.Factor([1, 1, 1, 1 + 1e-15], 2, pivots));

        // A node held only by the 1e-15 S safety conductance beside a 1000 S branch is many decades below the largest
        // entry, but its row does not cancel: regular, as before.
        Span<int> three = stackalloc int[3];
        Assert.True(DenseLu.Factor([1e3 + 1e-6, -1e3, 0, -1e3, 1e3 + 1e-6, 0, 0, 0, 1e-15], 3, three));
        // Not finite, or a row of zeros: singular.
        Assert.False(DenseLu.Factor([1, 0, 0, double.NaN], 2, pivots));
        Assert.False(DenseLu.Factor([1, 2, 0, 0], 2, pivots));
    }

    [Fact]
    public void Recomputing_the_tone_stack_is_allocation_free_and_cheap() {
        var stack = new FenderToneStack(Rate);
        var position = 0.0;

        void Turn() {
            for (var i = 0; i < 1_000; i++) {
                position = position > 0.99 ? 0 : position + 0.013;
                stack.Set(position, 1 - position, 0.5);
                stack.Tick(0.1);
            }
        }

        Assert.Equal(0, AllocationProbe.Measure(Turn));

        // Timed in batches, the median batch taken: a busy machine pre-empts a few batches, not the median one.
        const int Batches = 100;
        const int PerBatch = 1_000;
        var batches = new double[Batches];
        for (var batch = 0; batch < Batches; batch++) {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < PerBatch; i++) {
                position = position > 0.99 ? 0 : position + 0.013;
                stack.Set(position, 1 - position, 0.5);
            }

            batches[batch] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / PerBatch;
        }

        Array.Sort(batches);
        var recompute = batches[Batches / 2];
        output.WriteLine(
            $"tone stack recompute ({stack.Size}-unknown MNA, {stack.Columns} columns): {recompute:0} ns, "
            + $"{recompute / (64 / Rate * 1e9):P3} of a 64-frame block at 48 kHz"
        );
        Assert.True(recompute < 50_000, $"{recompute:0} ns per recompute");
    }

    [Fact]
    public void An_r_type_adaptor_reproduces_series_and_parallel_adaptors() {
        // R + (C ∥ R) below a diode root, once with the classic adaptors, once as one R-type adaptor.
        const double R1 = 4.7e3, R2 = 22e3, C = 47e-9, Fast = 192_000;
        var classic = new WdfCircuit<Series<ResistiveVoltageSource, Parallel<Capacitor, Resistor>>, DiodePair>(
            new(new(R1), new(new(C, Fast), new(R2))),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        classic.Propagate();

        var net = new WdfNetlist();
        var (source, top) = (net.Node(), net.Node());
        net.Port(source, WdfNetlist.Ground);
        net.Port(top, source);
        net.Resistor(top, source, R2);
        net.Up(top, WdfNetlist.Ground);
        var rType = new WdfCircuit<RTypeAdaptor<Ports<ResistiveVoltageSource, Capacitor>>, DiodePair>(
            new(net, new(new(R1), new(C, Fast))),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        rType.Propagate();
        Assert.Equal(classic.Tree.Resistance, rType.Tree.Resistance, 1e-9 * classic.Tree.Resistance);

        // The R-type's parent port runs top → ground, the series adaptor's source → top, so the voltages differ in sign.
        var worst = 0.0;
        for (var n = 0; n < 4_800; n++) {
            var x = 3 * Math.Sin(2 * Math.PI * 700 * n / Fast);
            classic.Tree.A.Volts = x;
            rType.Tree.Ports.A.Volts = -x;
            worst = Math.Max(worst, Math.Abs(classic.Tick() + rType.Tick()));
        }

        output.WriteLine($"R-type against series/parallel: worst {worst:E2} V");
        Assert.True(worst < 1e-9, $"{worst:E2} V apart");
    }

    [Fact]
    public void A_root_whose_network_is_singular_from_the_start_fails_at_construction() {
        // Two ideal sources in parallel: no unique solution, so the root used to run on an all-zero matrix.
        var net = new WdfNetlist();
        var node = net.Node();
        net.Port(node, WdfNetlist.Ground);
        net.VoltageSource(node, WdfNetlist.Ground);
        net.VoltageSource(node, WdfNetlist.Ground);
        net.Probe(node);
        Assert.Throws<ArgumentException>(() =>
            new RTypeRoot<Ports<ResistiveVoltageSource>, NoDevice>(net, new(new(600)), default)
        );
    }

    [Fact]
    public void An_adaptor_with_no_valid_parent_resistance_fails_instead_of_handing_its_parent_zero_ohms() {
        // The Up port sits across an ideal source: R_up = 0, which a parent adaptor would turn into 1/0.
        var net = new WdfNetlist();
        var node = net.Node();
        net.Port(node, WdfNetlist.Ground);
        net.VoltageSource(node, WdfNetlist.Ground);
        net.Up(node, WdfNetlist.Ground);
        var circuit = new WdfCircuit<RTypeAdaptor<Ports<ResistiveVoltageSource>>, OpenCircuit>(
            new(net, new(new(600))),
            default
        );
        Assert.Throws<InvalidOperationException>(() => circuit.Propagate());
    }

    [Fact]
    public void A_setting_that_makes_the_network_singular_keeps_the_last_matrix_and_is_counted() {
        // A VCVS from y to x driven by its own output: gain 0 is a wire, gain 1 leaves its branch row empty.
        var net = new WdfNetlist();
        var (input, x, y) = (net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Resistor(input, x, 1e3);
        net.Resistor(x, WdfNetlist.Ground, 1e3);
        net.Vcvs(y, x, y, x, 0);
        net.Resistor(y, WdfNetlist.Ground, 1e3);
        net.Probe(y);
        var root = new RTypeRoot<Ports<ResistiveVoltageSource>, NoDevice>(net, new(new(1e3)), default);
        root.Ports.A.Volts = 1;
        var before = root.Tick();
        Assert.True(before > 0.1, $"{before} V");

        root.SetGain(0, 1);
        Assert.False(root.Propagate());
        Assert.False(root.Scattering.IsValid);
        Assert.Equal(1, root.Newton.FailedRecomputes);
        Assert.Equal(1, root.Scattering.Failures);
        Assert.Equal(before, root.Tick());

        root.SetGain(0, 0);
        Assert.True(root.Propagate());
        Assert.True(root.Scattering.IsValid);
        Assert.Equal(1, root.Newton.FailedRecomputes);
    }

    [Theory]
    [InlineData(-700.0)]
    [InlineData(-24.0001)]
    [InlineData(-23.9999)]
    [InlineData(-5.0)]
    [InlineData(0.0)]
    [InlineData(7.99999)]
    [InlineData(8.00001)]
    [InlineData(1_000.0)]
    [InlineData(16_777_215.0)]
    [InlineData(16_777_217.0)]
    [InlineData(1e12)]
    public void Fast_wright_omega_is_accurate_to_1e_11_across_its_segments(double x) {
        var precise = WrightOmega.Precise(x);
        Assert.True(
            Math.Abs(WrightOmega.Fast(x) - precise) <= 1e-11 * precise,
            $"ω({x}) = {WrightOmega.Fast(x)}, not {precise}"
        );
        var worst = 0.0;
        for (var i = -200; i <= 200; i++) {
            var y = x * (1 + i * 1e-4) + i * 1e-3;
            var p = WrightOmega.Precise(y);
            worst = Math.Max(worst, p > 0 ? Math.Abs(WrightOmega.Fast(y) - p) / p : 0);
        }

        Assert.True(worst <= 1e-11, $"relative error {worst:E2} near {x}");
    }

    /// <summary>Worst |dB| difference between the impulse response's DTFT and the reference at the warped frequency.</summary>
    static double Compare(float[] impulse, ReferenceSimulator reference, int source, int probe) {
        var worst = 0.0;
        foreach (var f in Frequencies) {
            var warped = 2 * Rate * Math.Tan(Math.PI * f / Rate);
            var expected = reference.Ac(new(0, warped), source, probe).Magnitude;
            var actual = Analysis.MagnitudeAt(impulse, f, Rate);
            worst = Math.Max(worst, Math.Abs(Analysis.ToDb(actual) - Analysis.ToDb(expected)));
        }

        return worst;
    }

    /// <summary>
    ///     A bridged-T notch: input → 10k → mid → 10k → output, 10 nF bridging input and output, 100 nF from mid to
    ///     ground, 100k load, driven from 600 Ω. The bridge makes it neither series nor parallel.
    /// </summary>
    static RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor>, NoDevice> BridgedTRoot() {
        var net = new WdfNetlist();
        var (input, mid, outputNode) = (net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, outputNode);
        net.Port(mid, WdfNetlist.Ground);
        net.Resistor(input, mid, 10e3);
        net.Resistor(mid, outputNode, 10e3);
        net.Resistor(outputNode, WdfNetlist.Ground, 100e3);
        net.Probe(outputNode);
        return new(net, new(new(600), new(10e-9, Rate), new(100e-9, Rate)), default);
    }

    static (ReferenceCircuit Circuit, int Source, int Probe) BridgedTReference() {
        var c = new ReferenceCircuit();
        var (source, input, mid, outputNode) = (c.Node(), c.Node(), c.Node(), c.Node());
        c.Source(source, 0, static _ => 0);
        c.Resistor(source, input, 600);
        c.Capacitor(input, outputNode, 10e-9);
        c.Resistor(input, mid, 10e3);
        c.Capacitor(mid, 0, 100e-9);
        c.Resistor(mid, outputNode, 10e3);
        c.Resistor(outputNode, 0, 100e3);
        return (c, 0, outputNode);
    }
}

/// <summary>
///     The Fender Bassman 5F6-A tone stack (Yeh and Smith, "Discretization of the '59 Fender Bassman tone stack",
///     DAFx-06): 250 pF to the treble pot, 56k slope resistor, 20 nF to the bass pot, 20 nF to the mid pot, pots 250k
///     treble, 1M bass, 25k mid, into a 1M grid; driven from a 100 Ω cathode follower. The pots are resistors inside the
///     R-type root, so turning one recomputes the scattering matrix.
/// </summary>
sealed class FenderToneStack {
    const double SlopeResistor = 56e3;
    const double TreblePot = 250e3;
    const double BassPot = 1e6;
    const double MiddlePot = 25e3;
    const double Load = 1e6;

    RTypeRoot<Ports<ResistiveVoltageSource, Capacitor, Capacitor, Capacitor>, NoDevice> root;
    readonly int trebleTop;
    readonly int trebleBottom;
    readonly int bass;
    readonly int middle;

    public int Size { get; }

    public int Columns { get; }

    public WdfNetlist Netlist { get; }

    public RTypeScattering Scattering => root.Scattering;

    public FenderToneStack(double rate) {
        var net = new WdfNetlist();
        var input = net.Node();
        var top = net.Node();
        var wiper = net.Node();
        var bottom = net.Node();
        var slope = net.Node();
        var mid = net.Node();
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, top);
        net.Port(slope, bottom);
        net.Port(slope, mid);
        net.Resistor(input, slope, SlopeResistor);
        trebleTop = net.Resistor(top, wiper, TreblePot / 2);
        trebleBottom = net.Resistor(wiper, bottom, TreblePot / 2);
        bass = net.Resistor(bottom, mid, BassPot / 2);
        middle = net.Resistor(mid, WdfNetlist.Ground, MiddlePot / 2);
        net.Resistor(wiper, WdfNetlist.Ground, Load);
        net.Probe(wiper);
        root = new(net, new(new(100), new(250e-12, rate), new(20e-9, rate), new(20e-9, rate)), default);
        Netlist = net;
        Size = net.Nodes;
        Columns = root.Scattering.Columns;
    }

    /// <summary>Pot positions 0..1 (linear law, for the test).</summary>
    public void Set(double treble, double bass, double middle) {
        root.SetResistance(trebleTop, (1 - treble) * TreblePot);
        root.SetResistance(trebleBottom, treble * TreblePot);
        root.SetResistance(this.bass, bass * BassPot);
        root.SetResistance(this.middle, middle * MiddlePot);
        root.Propagate();
    }

    public double Tick(double x) {
        root.Ports.A.Volts = x;
        return root.Tick();
    }

    public static (ReferenceCircuit Circuit, int Source, int Probe) Reference(
        double treble,
        double bass,
        double middle
    ) {
        const double Floor = RTypeScattering.EndStop; // the adaptor's minimum resistance for a pot at its end stop
        var c = new ReferenceCircuit();
        var source = c.Node();
        var input = c.Node();
        var top = c.Node();
        var wiper = c.Node();
        var bottom = c.Node();
        var slope = c.Node();
        var mid = c.Node();
        c.Source(source, 0, static _ => 0);
        c.Resistor(source, input, 100);
        c.Capacitor(input, top, 250e-12);
        c.Resistor(input, slope, SlopeResistor);
        c.Capacitor(slope, bottom, 20e-9);
        c.Capacitor(slope, mid, 20e-9);
        c.Resistor(top, wiper, Math.Max((1 - treble) * TreblePot, Floor));
        c.Resistor(wiper, bottom, Math.Max(treble * TreblePot, Floor));
        c.Resistor(bottom, mid, Math.Max(bass * BassPot, Floor));
        c.Resistor(mid, 0, Math.Max(middle * MiddlePot, Floor));
        c.Resistor(wiper, 0, Load);
        return (c, 0, wiper);
    }
}
