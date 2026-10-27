using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Solver pieces the per-model references reach only indirectly, each against an independent computation:
///     inductor ports and an internal R-type adaptor's ideal sources against the reference simulator's trapezoidal
///     transient (a linear WDF with trapezoidal reactances is the same discretisation, so the two agree to rounding),
///     the device limiters against their defining properties, <see cref="DenseLu" /> against matrices with a known
///     inverse, and the AC supply's ripple against Schade's estimate.
/// </summary>
public class SolverVerificationTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Samples = 4_800;
    const double Rounding = 1.1e-16; // the unit roundoff of a double

    /// <summary>Two tones that start from zero, so the zero state is the circuit's state at t = 0.</summary>
    static double Drive(double t) => Math.Sin(2 * Math.PI * 310 * t) + 0.5 * Math.Sin(2 * Math.PI * 3_100 * t);

    /// <summary>A slower signal for the internal sources, independent of <see cref="Drive" />.</summary>
    static double Slow(double t) => Math.Sin(2 * Math.PI * 97 * t) * Math.Cos(2 * Math.PI * 13 * t);

    [Fact]
    public void Inductor_ports_of_an_r_type_root_match_the_reference_transient() {
        // A bridged-T with an inductor across the bridge and another, beside the load, to ground: neither series nor
        // parallel, and the inductors' trapezoidal waves (b = -a[n-1]) are the only memory besides one capacitor.
        var net = new WdfNetlist();
        var (input, mid, outputNode) = (net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Port(input, outputNode);
        net.Port(mid, WdfNetlist.Ground);
        net.Port(outputNode, WdfNetlist.Ground);
        net.Resistor(input, mid, 1e3);
        net.Resistor(mid, outputNode, 1e3);
        net.Resistor(outputNode, WdfNetlist.Ground, 10e3);
        net.Probe(outputNode);
        var root = new RTypeRoot<Ports<ResistiveVoltageSource, Inductor, Capacitor, Inductor>, NoDevice>(
            net,
            new(new(600), new(100e-3, Rate), new(100e-9, Rate), new(0.5, Rate)),
            default
        );

        var c = new ReferenceCircuit();
        var (source, refInput, refMid, refOutput) = (c.Node(), c.Node(), c.Node(), c.Node());
        c.Source(source, 0, Drive);
        c.Resistor(source, refInput, 600);
        c.Inductor(refInput, refOutput, 100e-3);
        c.Capacitor(refMid, 0, 100e-9);
        c.Inductor(refOutput, 0, 0.5);
        c.Resistor(refInput, refMid, 1e3);
        c.Resistor(refMid, refOutput, 1e3);
        c.Resistor(refOutput, 0, 10e3);

        var worst = Transient(
            c,
            refOutput,
            t => {
                root.Ports.A.Volts = Drive(t);
                return root.Tick();
            }
        );
        output.WriteLine($"inductors in an R-type root: worst {worst:E2} of the peak");
        Assert.True(worst < 1e-9, $"{worst:E2} of the peak from the reference");
    }

    [Fact]
    public void Inductors_in_series_and_parallel_adaptors_match_the_reference_transient() {
        // Source (100 Ω) → 10 mH → node, with 1 µF ∥ 50 mH ∥ 1 kΩ from node to ground: an inductor as the child of a
        // series adaptor and as the child of a parallel one. The open root reads the node.
        var circuit = new WdfCircuit<
            Parallel<Series<ResistiveVoltageSource, Inductor>, Parallel<Capacitor, Parallel<Inductor, Resistor>>>,
            OpenCircuit>(
                new(new(new(100), new(10e-3, Rate)), new(new(1e-6, Rate), new(new(50e-3, Rate), new(1e3)))),
                default
            );
        circuit.Propagate();

        var c = new ReferenceCircuit();
        var (source, top, node) = (c.Node(), c.Node(), c.Node());
        c.Source(source, 0, Drive);
        c.Resistor(source, top, 100);
        c.Inductor(top, node, 10e-3);
        c.Capacitor(node, 0, 1e-6);
        c.Inductor(node, 0, 50e-3);
        c.Resistor(node, 0, 1e3);

        var worst = Transient(
            c,
            node,
            t => {
                circuit.Tree.A.A.Volts = Drive(t);
                return circuit.Tick();
            }
        );
        output.WriteLine($"inductors in series and parallel adaptors: worst {worst:E2} of the peak");
        Assert.True(worst < 1e-9, $"{worst:E2} of the peak from the reference");
    }

    [Fact]
    public void An_internal_r_type_adaptor_with_ideal_sources_matches_the_reference_transient() {
        // Below its parent, an R-type adaptor's ideal sources enter every port's wave through the source columns of
        // the scattering matrix, which the Up port's Sherman-Morrison update must carry as well. Here: a voltage
        // source in series, a current source (beside its own resistor), a VCVS, a capacitor and an inductor, with the
        // Up port loaded by a resistor in the parent series adaptor.
        const double Load = 3.3e3;
        const double Shunt = 4.7e3;
        var net = new WdfNetlist();
        var (input, a, b, c, d, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Port(input, WdfNetlist.Ground);
        net.Resistor(input, a, 2.2e3);
        var voltage = net.VoltageSource(a, b);
        net.Port(b, WdfNetlist.Ground);
        var current = net.CurrentSource(c, WdfNetlist.Ground);
        net.Resistor(c, WdfNetlist.Ground, Shunt);
        net.Resistor(b, c, 10e3);
        net.Vcvs(d, WdfNetlist.Ground, b, WdfNetlist.Ground, 2);
        net.Resistor(d, top, 1e3);
        net.Port(top, c);
        net.Up(top, WdfNetlist.Ground);
        var circuit = new WdfCircuit<
            Series<RTypeAdaptor<Ports<ResistiveVoltageSource, Capacitor, Inductor>>, Resistor>, ShortCircuit>(
                new(new(net, new(new(1e3), new(220e-9, Rate), new(10e-3, Rate))), new(Load)),
                default
            );
        circuit.Propagate();

        // The current source and its resistor are, to the rest of the network, a source of I R behind R.
        var reference = new ReferenceCircuit();
        var source = reference.Node();
        var (refInput, refA, refB, norton, refC, refD, refTop) = (reference.Node(), reference.Node(),
            reference.Node(), reference.Node(), reference.Node(), reference.Node(), reference.Node());
        reference.Source(source, 0, Drive);
        reference.Resistor(source, refInput, 1e3);
        reference.Resistor(refInput, refA, 2.2e3);
        reference.Source(refA, refB, static t => 0.8 * Slow(t));
        reference.Capacitor(refB, 0, 220e-9);
        reference.Source(norton, 0, static t => 1e-4 * Drive(1.3 * t) * Shunt);
        reference.Resistor(norton, refC, Shunt);
        reference.Resistor(refB, refC, 10e3);
        reference.Vcvs(refD, 0, refB, 0, 2);
        reference.Resistor(refD, refTop, 1e3);
        reference.Inductor(refTop, refC, 10e-3);
        reference.Resistor(refTop, 0, Load);

        var worst = Transient(
            reference,
            refTop,
            t => {
                ref var adaptor = ref circuit.Tree.A;
                adaptor.Ports.A.Volts = Drive(t);
                adaptor.SetVoltage(voltage, 0.8 * Slow(t));
                adaptor.SetCurrent(current, 1e-4 * Drive(1.3 * t));
                circuit.Tick();
                return adaptor.Voltage;
            }
        );
        output.WriteLine($"sources inside an R-type adaptor: worst {worst:E2} of the peak");
        Assert.True(worst < 1e-9, $"{worst:E2} of the peak from the reference");
    }

    [Fact]
    public void Limited_exp_is_the_exponential_continued_by_its_tangent() {
        for (var x = -50.0; x <= DeviceMath.MaxExponent; x += 0.25) {
            var value = DeviceMath.LimitedExp(x, out var slope);
            Assert.Equal(Math.Exp(x), value);
            Assert.Equal(Math.Exp(x), slope);
        }

        // C¹ at the joint: value and slope meet from both sides.
        var top = Math.Exp(DeviceMath.MaxExponent);
        var above = DeviceMath.LimitedExp(DeviceMath.MaxExponent + 1e-9, out var aboveSlope);
        Assert.Equal(top, above, 1e-8 * top);
        Assert.Equal(top, aboveSlope);

        // Beyond it: the tangent line, below the exponential (it is convex), increasing and finite far out.
        var previous = top;
        foreach (var x in new[] { 41.0, 45, 60, 100, 700, 1e3, 1e6, 1e12 }) {
            var value = DeviceMath.LimitedExp(x, out var slope);
            Assert.Equal(top * (1 + x - DeviceMath.MaxExponent), value, 1e-12 * value);
            Assert.Equal(top, slope);
            Assert.True(double.IsFinite(value) && value > previous, $"e^{x} limited to {value}");
            Assert.True(x > 700 || value < Math.Exp(x), $"above the exponential at {x}");
            previous = value;
        }
    }

    [Theory]
    [InlineData(2.52e-9, 1.752)] // silicon
    [InlineData(2e-7, 1.3)] // germanium
    [InlineData(1e-8, 1.04)] // Schottky
    [InlineData(1e-14, 1.0)] // a transistor's junction
    public void Critical_voltage_is_where_the_junction_curves_most(double saturationCurrent, double ideality) {
        // pnjlim's critical voltage maximises the curvature i'' / (1 + i'²)^(3/2) of i = Is (e^(v / n Vt) - 1).
        var vt = ideality * DiodeModel.ThermalVoltage;

        double Curvature(double v) {
            var e = Math.Exp(v / vt);
            var slope = saturationCurrent / vt * e;
            return saturationCurrent / (vt * vt) * e / Math.Pow(1 + slope * slope, 1.5);
        }

        // Golden-section search for the maximum over a range that brackets it by volts on each side.
        var (low, high) = (-0.5, 2.0);
        var ratio = (Math.Sqrt(5) - 1) / 2;
        for (var i = 0; i < 200; i++) {
            var x1 = high - ratio * (high - low);
            var x2 = low + ratio * (high - low);
            if (Curvature(x1) < Curvature(x2)) {
                low = x1;
            } else {
                high = x2;
            }
        }

        var numeric = 0.5 * (low + high);
        var critical = DeviceMath.CriticalVoltage(saturationCurrent, vt);
        output.WriteLine($"Is {saturationCurrent:E2}, n {ideality}: {critical:0.000000} V, numeric {numeric:0.000000}");
        Assert.Equal(numeric, critical, 1e-6);
    }

    [Fact]
    public void Tube_limiters_bound_only_downward_steps_of_a_positive_plate() {
        // (previous, proposed, halving limit, tenth limit)
        (double Previous, double Proposed, double Half, double Tenth)[] cases = [
            (200, 50, 100, 50), // down by three quarters: halved; the tenth floor lets it through
            (200, 10, 100, 20), // down past a tenth: both limit
            (200, 120, 120, 120), // a small step down
            (200, 400, 400, 400), // upwards: never limited
            (200, -300, 100, 20), // through zero
            (0, -50, -50, -50), // from zero or below: Newton's own step
            (-5, -80, -80, -80)
        ];
        foreach (var (previous, proposed, half, tenth) in cases) {
            var triode = new Triode(TriodeModel.Ecc83);
            double[] step = [-3, proposed];
            triode.Limit([1, previous], step);
            Assert.Equal(-3, step[0]); // the grid is never limited
            Assert.Equal(half, step[1]);

            var pentode = new Pentode(PentodeModel.EL34);
            double[] three = [-3, proposed, proposed];
            pentode.Limit([1, previous, previous], three);
            Assert.Equal(-3, three[0]);
            Assert.Equal(half, three[1]); // screen
            Assert.Equal(half, three[2]); // plate

            Assert.Equal(tenth, TubeLimit.Plate(previous, proposed));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Dense_lu_solves_hilbert_matrices_to_their_condition_number(int n) {
        // The Hilbert matrix 1 / (i + j + 1) is the textbook ill-conditioned matrix (cond∞ 2.9e10 at order 8), and its
        // inverse is known in closed form, so the condition number is exact. Partial pivoting is backward stable: the
        // residual is at rounding level whatever the conditioning, and the error at most cond × rounding.
        var a = new double[n * n];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                a[i * n + j] = 1.0 / (i + j + 1);
            }
        }

        var condition = Norm(a, n) * Norm(HilbertInverse(n), n);
        var x = new double[n];
        for (var i = 0; i < n; i++) {
            x[i] = (i % 2 == 0 ? 1 : -1) * (1 + 0.25 * i);
        }

        var b = Multiply(a, n, x);
        var lu = (double[])a.Clone();
        var pivots = new int[n];
        Assert.True(DenseLu.Factor(lu, n, pivots), $"order {n} rejected as singular");
        var solved = (double[])b.Clone();
        DenseLu.Solve(lu, n, pivots, solved);

        var residual = Multiply(a, n, solved);
        var (error, backward, scale) = (0.0, 0.0, 0.0);
        for (var i = 0; i < n; i++) {
            error = Math.Max(error, Math.Abs(solved[i] - x[i]));
            backward = Math.Max(backward, Math.Abs(residual[i] - b[i]));
            scale = Math.Max(scale, Math.Abs(x[i]));
        }

        var relative = error / scale;
        var relativeResidual = backward / (Norm(a, n) * solved.Max(Math.Abs));
        output.WriteLine($"order {n}: cond∞ {condition:E2}, error {relative:E2}, residual {relativeResidual:E2}");
        Assert.True(relativeResidual < 4 * n * Rounding, $"residual {relativeResidual:E2}");
        Assert.True(relative < 10 * condition * Rounding, $"error {relative:E2} at cond∞ {condition:E2}");

        // Several right-hand sides at once give each column's single solve to the bit (the paired path at even orders
        // against the column loop).
        var columns = new double[n * 3];
        for (var i = 0; i < n; i++) {
            columns[i * 3] = b[i];
            columns[i * 3 + 1] = x[i];
            columns[i * 3 + 2] = -2 * b[i] + i;
        }

        DenseLu.Solve(lu, n, pivots, columns, 3);
        for (var col = 0; col < 3; col++) {
            var single = new double[n];
            for (var i = 0; i < n; i++) {
                single[i] = col switch {
                    0 => b[i],
                    1 => x[i],
                    _ => -2 * b[i] + i
                };
            }

            DenseLu.Solve(lu, n, pivots, single);
            for (var i = 0; i < n; i++) {
                Assert.Equal(single[i], columns[i * 3 + col]);
            }
        }
    }

    [Fact]
    public void Ac_supply_ripple_follows_the_reservoir_and_the_line_frequency() {
        // A full-wave rectifier recharges the reservoir every half cycle and the load drains it in between: Schade's
        // estimate of the ripple is I / (2 f C) peak to peak, a little less in practice (the rectifier conducts for
        // part of the cycle). Twice the reservoir halves it, 50 Hz mains raise it by 6/5.
        var design = Plexi.Design.Power;

        (double Mean, double Swing, double Draw) Run(SupplyMode mode, double timeConstant) {
            var power = new PowerAmp(design, 96_000);
            power.Apply(PowerSettings.Stock with { Supply = mode, TimeConstant = timeConstant });
            power.Reset();
            const int Settle = 96_000 * 2;
            const int Length = 96_000 / 5; // a whole number of cycles at 50 and 60 Hz
            var (mean, low, high, draw) = (0.0, double.MaxValue, double.MinValue, 0.0);
            for (var i = 0; i < Settle + Length; i++) {
                power.Tick(0);
                if (i < Settle) {
                    continue;
                }

                var v = power.Supply;
                mean += v / Length;
                (low, high) = (Math.Min(low, v), Math.Max(high, v));
                var (a, b) = power.PlateCurrents;
                var (sa, sb) = power.ScreenCurrents;
                draw += (design.TubesPerSide * (a + b + sa + sb) + design.PowerSupply.PreampCurrent) / Length;
            }

            return (mean, high - low, draw);
        }

        var stock = Run(SupplyMode.Ac60, 1);
        var doubled = Run(SupplyMode.Ac60, 2);
        var fifty = Run(SupplyMode.Ac50, 1);
        var schade = stock.Draw / (2 * 60 * design.PowerSupply.Reservoir);
        output.WriteLine(
            $"60 Hz: mean {stock.Mean:0.0} V, ripple {stock.Swing:0.000} V p-p at {stock.Draw * 1e3:0.0} mA "
            + $"(Schade {schade:0.000} V); twice the reservoir {doubled.Swing:0.000} V; 50 Hz {fifty.Swing:0.000} V"
        );
        Assert.InRange(stock.Swing, 0.6 * schade, 1.05 * schade);
        Assert.InRange(doubled.Swing / stock.Swing, 0.45, 0.55);
        Assert.InRange(fifty.Swing / stock.Swing, 1.1, 1.3);
        Assert.InRange(stock.Mean, 0.97 * design.Supply, 1.03 * design.Supply);
        Assert.InRange(doubled.Mean, 0.97 * design.Supply, 1.03 * design.Supply);
    }

    /// <summary>
    ///     Runs the WDF (one call per sample, given the sample's time) and the reference from the zero state with the
    ///     same step for <see cref="Samples" /> samples; returns the worst difference relative to the reference's
    ///     peak.
    /// </summary>
    static double Transient(ReferenceCircuit circuit, int node, Func<double, double> tick) {
        var reference = new ReferenceSimulator(circuit);
        reference.ZeroState();
        const double Step = 1 / Rate;
        var (worst, peak) = (0.0, 0.0);
        for (var n = 0; n < Samples; n++) {
            var t = n * Step;
            if (n > 0) {
                reference.Step(t, Step);
            }

            var wdf = tick(t);
            var expected = reference.Voltage(node);
            worst = Math.Max(worst, Math.Abs(wdf - expected));
            peak = Math.Max(peak, Math.Abs(expected));
        }

        Assert.True(peak > 1e-3, $"the reference barely moves ({peak:E2} V peak)");
        return worst / peak;
    }

    /// <summary>The Hilbert matrix's inverse, (-1)^(i+j) (i+j+1) C(n+i, n-j-1) C(n+j, n-i-1) C(i+j, i)².</summary>
    static double[] HilbertInverse(int n) {
        static double Binomial(int m, int k) {
            if (k < 0 || k > m) {
                return 0;
            }

            var r = 1.0;
            for (var i = 1; i <= k; i++) {
                r = r * (m - k + i) / i;
            }

            return Math.Round(r);
        }

        var inverse = new double[n * n];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                var c = Binomial(i + j, i);
                inverse[i * n + j] = ((i + j) % 2 == 0 ? 1 : -1)
                    * (i + j + 1)
                    * Binomial(n + i, n - j - 1)
                    * Binomial(n + j, n - i - 1)
                    * c
                    * c;
            }
        }

        return inverse;
    }

    static double Norm(double[] a, int n) {
        var norm = 0.0;
        for (var i = 0; i < n; i++) {
            var row = 0.0;
            for (var j = 0; j < n; j++) {
                row += Math.Abs(a[i * n + j]);
            }

            norm = Math.Max(norm, row);
        }

        return norm;
    }

    static double[] Multiply(double[] a, int n, double[] x) {
        var y = new double[n];
        for (var i = 0; i < n; i++) {
            for (var j = 0; j < n; j++) {
                y[i] += a[i * n + j] * x[j];
            }
        }

        return y;
    }
}
