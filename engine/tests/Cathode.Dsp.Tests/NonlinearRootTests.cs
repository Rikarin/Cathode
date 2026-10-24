using System.Diagnostics;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     R-type roots with nonlinear multiport devices against the reference simulator (trapezoidal MNA with a full
///     Newton solve per step, run 16 times faster than the WDF): operating points, time-domain renders at moderate level,
///     and Newton's behaviour under abuse.
/// </summary>
public class NonlinearRootTests(ITestOutputHelper output) {
    const double Rate = 192_000; // a 4x-oversampled 48 kHz stage
    const int Oversampling = 16;

    public static TheoryData<string> Stages => ["bjt", "triode", "opamp", "pentode"];

    [Theory]
    [InlineData("bjt")]
    [InlineData("triode")]
    [InlineData("pentode")]
    public void Operating_point_reached_from_switch_on_matches_the_reference(string name) {
        // The WDF starts discharged with the supply switched on, at 48 kHz, and must settle where the reference's DC
        // solve says.
        var stage = Create(name, 48_000);
        var reference = new ReferenceSimulator(stage.Reference(static _ => 0, 48_000));
        reference.OperatingPoint();
        for (var i = 0; i < 96_000; i++) {
            stage.Tick(0);
        }

        var nodes = name == "pentode" ? 7 : 6;
        for (var node = 1; node <= nodes; node++) {
            var expected = reference.Voltage(node);
            var actual = stage.Node(node);
            output.WriteLine($"{name} node {node}: WDF {actual:0.000000} V, reference {expected:0.000000} V");
            Assert.True(
                Math.Abs(actual - expected) <= 0.01 * Math.Abs(expected) + 1e-6,
                $"node {node}: {actual} V against {expected} V"
            );
        }
    }

    [Theory]
    [InlineData("bjt", 0.01)]
    [InlineData("triode", 1.0)]
    [InlineData("opamp", 0.1)]
    [InlineData("pentode", 3.0)]
    public void Moderate_drive_matches_the_reference_in_the_time_domain(string name, double amplitude) {
        // 1 kHz with a 220 Hz component, so the stage sees a changing slope as well as a level.
        double Input(double t) =>
            amplitude * (0.7 * Math.Sin(2 * Math.PI * 1_000 * t) + 0.3 * Math.Sin(2 * Math.PI * 220 * t));

        var stage = Create(name, Rate);
        var reference = new ReferenceSimulator(stage.Reference(Input, Rate * Oversampling));
        if (name != "opamp") {
            reference.OperatingPoint();
            stage.StartAt(reference.Voltage);
        } else {
            reference.ZeroState();
        }

        const int Samples = 3_840; // 20 ms
        double error = 0, energy = 0, mean = 0, peak = 0;
        var wdf = new double[Samples];
        var expected = new double[Samples];
        var h = 1 / (Rate * Oversampling);
        for (var n = 0; n < Samples; n++) {
            var t = n / Rate;
            for (var s = 1; s <= Oversampling && n > 0; s++) {
                reference.Step(t - (Oversampling - s) * h, h);
            }

            wdf[n] = stage.Tick(Input(t));
            expected[n] = reference.Voltage(stage.OutputNode);
            mean += expected[n] / Samples;
        }

        for (var n = 0; n < Samples; n++) {
            error += (wdf[n] - expected[n]) * (wdf[n] - expected[n]);
            energy += (expected[n] - mean) * (expected[n] - mean);
            peak = Math.Max(peak, Math.Abs(expected[n] - mean));
        }

        var errorDb = 10 * Math.Log10(error / energy);
        output.WriteLine(
            $"{name}: output {peak:0.000} V peak, error {errorDb:0.0} dB, Newton {stage.Newton.MeanIterations:0.00} "
            + $"iterations per sample (most {stage.Newton.MostIterations}), reference most {reference.MostIterations}"
        );
        Assert.Equal(0, reference.NewtonFailures);
        Assert.Equal(0, stage.Newton.Unconverged);
        Assert.True(errorDb < -40, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Newton_stays_bounded_and_finite_under_hard_drive(string name) {
        var stage = Create(name, Rate);
        var amplitude = name switch {
            "bjt" => 5,
            "opamp" => 20,
            _ => 200
        };

        // A square wave, a burst of loud noise and a sudden stop: steps of hundreds of volts at the grid.
        var random = new Random(3);
        var finite = true;
        var peak = 0.0;
        for (var n = 0; n < 38_400; n++) {
            var x = n switch {
                < 19_200 => (n / 96 % 2 == 0 ? 1 : -1) * amplitude,
                < 28_800 => amplitude * (2 * random.NextDouble() - 1),
                _ => 0
            };
            var y = stage.Tick(x);
            finite &= double.IsFinite(y);
            peak = Math.Max(peak, Math.Abs(y));
        }

        var newton = stage.Newton;
        output.WriteLine(
            $"{name}: mean {newton.MeanIterations:0.00} iterations, most {newton.MostIterations}, "
            + $"unconverged {newton.Unconverged}, non-finite {newton.NonFinite}, output peak {peak:0.0} V"
        );
        Assert.True(finite, "non-finite output");
        Assert.Equal(0, newton.NonFinite);
        Assert.True(newton.MeanIterations < 6, $"{newton.MeanIterations:0.0} iterations per sample");
        Assert.True(newton.Unconverged <= newton.Samples / 1_000, $"{newton.Unconverged} unconverged samples");
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Too_few_iterations_degrade_gracefully(string name) {
        var stage = Create(name, Rate);
        stage.Newton.MaxIterations = 1;
        var finite = true;
        for (var n = 0; n < 9_600; n++) {
            finite &= double.IsFinite(stage.Tick(n / 48 % 2 == 0 ? 2 : -2));
        }

        output.WriteLine(
            $"{name}: {stage.Newton.Unconverged} of {stage.Newton.Samples} samples unconverged at one iteration"
        );
        Assert.True(finite);
        Assert.Equal(0, stage.Newton.NonFinite);
    }

    [Fact]
    public void A_diode_device_at_an_r_type_root_matches_the_diode_pair_root() {
        // The RC diode clipper twice: the closed-form DiodePair root, and the generic Newton root with one device port.
        const double R = 2.2e3, C = 10e-9;
        var classic = new WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, DiodePair>(
            new(new(R), new(C, Rate)),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        classic.Propagate();

        var net = new WdfNetlist();
        var node = net.Node();
        net.Port(node, WdfNetlist.Ground);
        net.Port(node, WdfNetlist.Ground);
        net.Diode(node, WdfNetlist.Ground);
        net.Probe(node);
        var generic = new RTypeRoot<Ports<ResistiveVoltageSource, Capacitor>, DiodeClipper>(
            net,
            new(new(R), new(C, Rate)),
            DiodeClipper.Symmetric(DiodeModel.Silicon)
        );

        var worst = 0.0;
        for (var n = 0; n < 9_600; n++) {
            var x = 3 * Math.Sin(2 * Math.PI * 2_000 * n / Rate);
            classic.Tree.A.Volts = x;
            generic.Ports.A.Volts = x;
            worst = Math.Max(worst, Math.Abs(classic.Tick() - generic.Tick()));
        }

        output.WriteLine($"worst difference {worst:E2} V, {generic.Newton.MeanIterations:0.00} iterations per sample");
        Assert.True(worst < 1e-6, $"{worst:E2} V apart");
    }

    [Fact]
    public void Ticking_a_nonlinear_root_does_not_allocate() {
        var stage = new OpAmpClipperStage(Rate, OpAmpModel.TL072);
        var triode = new CommonCathodeStage(Rate, TriodeModel.Ecc83);
        var n = 0;
        Assert.Equal(
            0,
            AllocationProbe.Measure(() => {
                    for (var i = 0; i < 1_000; i++, n++) {
                        stage.Tick(Math.Sin(n * 0.05));
                        triode.Tick(Math.Sin(n * 0.05));
                    }
                }
            )
        );
    }

    [Fact]
    public void Cost_per_sample_of_each_root_is_reported() {
        const int Samples = 192_000;
        var input = new double[4_096];
        for (var i = 0; i < input.Length; i++) {
            input[i] = Math.Sin(2 * Math.PI * 440 * i / Rate) + 0.3 * Math.Sin(2 * Math.PI * 1_333 * i / Rate);
        }

        double Measure(Func<double, double> tick, double gain) {
            var best = double.PositiveInfinity;
            for (var pass = 0; pass < 6; pass++) {
                var sum = 0.0;
                var watch = Stopwatch.StartNew();
                for (var n = 0; n < Samples; n++) {
                    sum += tick(gain * input[n & 4_095]);
                }

                best = Math.Min(best, watch.Elapsed.TotalNanoseconds / Samples);
                Assert.True(double.IsFinite(sum));
            }

            return best;
        }

        var exact = new WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, DiodePair>(
            new(new(2.2e3), new(10e-9, Rate)),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        exact.Propagate();
        var antialiased = exact;
        antialiased.Root.Antialiased = true;
        var stack = new FenderToneStack(Rate);
        stack.Set(0.5, 0.5, 0.5);
        var clipperNet = new WdfNetlist();
        var clipperNode = clipperNet.Node();
        clipperNet.Port(clipperNode, WdfNetlist.Ground);
        clipperNet.Port(clipperNode, WdfNetlist.Ground);
        clipperNet.Diode(clipperNode, WdfNetlist.Ground);
        clipperNet.Probe(clipperNode);
        var clipper = new RTypeRoot<Ports<ResistiveVoltageSource, Capacitor>, DiodeClipper>(
            clipperNet,
            new(new(2.2e3), new(10e-9, Rate)),
            DiodeClipper.Symmetric(DiodeModel.Silicon)
        );
        var bjt = new CommonEmitterStage(Rate, BjtModel.Q2N3904);
        var triode = new CommonCathodeStage(Rate, TriodeModel.Ecc83);
        var opamp = new OpAmpClipperStage(Rate, OpAmpModel.JRC4558);
        var pentode = new PentodeStage(Rate, PentodeModel.EL84);
        for (var i = 0; i < 48_000; i++) {
            bjt.Tick(0);
            triode.Tick(0);
            pentode.Tick(0);
        }

        var rows = new (string Name, double Ns)[] {
            ("DiodePair root, exact (K = 1, closed form + Newton)", Measure(
                x => {
                    exact.Tree.A.Volts = x;
                    return exact.Tick();
                },
                3
            )),
            ("DiodePair root, antialiased", Measure(
                x => {
                    antialiased.Tree.A.Volts = x;
                    return antialiased.Tick();
                },
                3
            )),
            ("R-type root, linear (tone stack, 4 ports)", Measure(stack.Tick, 1)),
            ("R-type root, diode pair (K = 1)",
                Measure(
                    x => {
                        clipper.Ports.A.Volts = x;
                        return clipper.Tick();
                    },
                    3
                )),
            ("R-type root, BJT common emitter (K = 2)", Measure(bjt.Tick, 0.01)),
            ("R-type root, 12AX7 common cathode (K = 2)", Measure(triode.Tick, 1)),
            ("R-type root, op-amp + diodes (K = 3)", Measure(opamp.Tick, 0.1)),
            ("R-type root, EL84 single-ended (K = 3)", Measure(pentode.Tick, 3))
        };
        foreach (var (name, ns) in rows) {
            output.WriteLine(
                $"{name,-52} {ns,7:0.0} ns/sample, {ns * 48_000 * 4 / 1e9:P2} of a core per channel at 4x 48 kHz"
            );
        }

        Assert.All(rows, static r => Assert.True(r.Ns < 20_000, $"{r.Name}: {r.Ns:0} ns"));
    }

    static ITestStage Create(string name, double rate) =>
        name switch {
            "bjt" => new CommonEmitterStage(rate, BjtModel.Q2N3904),
            "triode" => new CommonCathodeStage(rate, TriodeModel.Ecc83),
            "opamp" => new OpAmpClipperStage(rate, OpAmpModel.JRC4558),
            _ => new PentodeStage(rate, PentodeModel.EL84)
        };
}
