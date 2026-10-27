using Cathode.Dsp.Wdf;
using Cathode.Testing;
using System.Globalization;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The nonlinear device models: Jacobians, presets against datasheet operating points, op-amp dynamics.
/// </summary>
public class WdfDeviceTests(ITestOutputHelper output) {
    const double Rate = 192_000;

    public static TheoryData<string> Devices =>
        ["diode", "diode-rs", "clipper", "npn", "pnp", "germanium", "triode", "pentode", "opamp", "pair"];

    [Fact]
    public void Junction_limiting_keeps_the_direction_and_limits_only_the_documented_steps() {
        var model = DiodeModel.Silicon;
        var vt = model.EmissionVoltage(1);
        var critical = DeviceMath.CriticalVoltage(model.SaturationCurrent, vt);
        for (var previous = -1.0; previous <= 1.2; previous += 0.01) {
            for (var proposed = -2.0; proposed <= 3.0; proposed += 0.0037) {
                var limited = DeviceMath.LimitJunction(previous, proposed, vt, critical);
                var step = proposed - previous;
                var at = $"previous {previous:0.000}, proposed {proposed:0.0000}: {limited:0.0000}";
                Assert.True(double.IsFinite(limited), at);
                if (step > 2 * vt && proposed > critical) {
                    // Upwards, compressed: never past Newton's proposal, never backwards.
                    Assert.True(limited <= proposed && limited >= Math.Min(previous, critical) - 1e-12, at);
                } else if (step < -vt && previous > critical - 10 * vt) {
                    // The tangent predicts no current: at least 10 n Vt down, or Newton's proposal if lower.
                    Assert.Equal(Math.Min(proposed, previous - 10 * vt), limited);
                } else {
                    // Everything else, including downward steps of less than n Vt, is Newton's own step.
                    Assert.Equal(proposed, limited);
                }
            }

            Assert.Equal(previous, DeviceMath.LimitJunction(previous, previous, vt, critical));
        }
    }

    [Theory]
    [MemberData(nameof(Devices))]
    public void Jacobian_matches_finite_differences(string name) {
        var device = Create(name);
        var k = device.Ports;
        var random = new Random(name.Length);
        var v = new double[k];
        var current = new double[k];
        var jacobian = new double[k * k];
        var plus = new double[k];
        var minus = new double[k];
        var scratch = new double[k * k];
        for (var trial = 0; trial < 200; trial++) {
            for (var j = 0; j < k; j++) {
                v[j] = Sample(name, j, random);
            }

            Array.Clear(jacobian);
            device.Evaluate(v, current, jacobian, k);
            for (var c = 0; c < k; c++) {
                var h = 1e-6 * Math.Max(1, Math.Abs(v[c]));
                var shifted = (double[])v.Clone();
                shifted[c] = v[c] + h;
                device.Evaluate(shifted, plus, scratch, k);
                shifted[c] = v[c] - h;
                device.Evaluate(shifted, minus, scratch, k);
                for (var r = 0; r < k; r++) {
                    var numeric = (plus[r] - minus[r]) / (2 * h);
                    var analytic = jacobian[r * k + c];
                    var scale = Math.Max(Math.Abs(numeric), Math.Abs(analytic));
                    Assert.True(
                        Math.Abs(numeric - analytic) <= 1e-4 * scale + 1e-14 * Math.Abs(current[r]) / h + 1e-15,
                        $"{name}: ∂i{r}/∂v{c} at [{Format(v)}]: {analytic} analytic, {numeric} numeric"
                    );
                }
            }
        }
    }

    [Fact]
    public void Diode_with_series_resistance_satisfies_its_implicit_equation() {
        var model = DiodeModel.Silicon;
        var diode = new Diode(model, 1, 10);
        var current = new double[1];
        var jacobian = new double[1];
        for (var v = -1.0; v <= 3; v += 0.01) {
            diode.Evaluate([v], current, jacobian, 1);
            var junction = v - 10 * current[0];
            var expected = model.SaturationCurrent * (Math.Exp(junction / model.EmissionVoltage(1)) - 1);
            Assert.Equal(expected, current[0], 1e-9 * Math.Max(1, Math.Abs(expected)) + 1e-15);
        }
    }

    [Theory]
    [InlineData("12AX7", -2.0, 250.0, 1.2e-3)] // datasheet 1.2 mA; the refit gives 1.25 mA (Koren's 0.95)
    [InlineData("12AT7", -2.0, 250.0, 10e-3)]
    [InlineData("12AU7", -8.5, 250.0, 10.5e-3)]
    public void Triode_presets_land_near_the_datasheet_operating_point(
        string tube,
        double vgk,
        double vpk,
        double amps
    ) {
        var model = tube switch {
            "12AX7" => TriodeModel.Ecc83,
            "12AT7" => TriodeModel.Ecc81,
            _ => TriodeModel.Ecc82
        };
        var current = new double[2];
        new Triode(model).Evaluate([vgk, vpk], current, new double[4], 2);
        output.WriteLine($"{tube}: {current[1] * 1e3:0.00} mA at {vgk} V / {vpk} V (datasheet {amps * 1e3:0.0} mA)");
        Assert.InRange(current[1], 0.7 * amps, 1.3 * amps);
        Assert.True(current[0] < 1e-9, "grid current at a negative grid");
    }

    [Theory]
    [InlineData("EL34", -12.2, 250.0, 250.0, 100e-3)]
    [InlineData("6L6GC", -14.0, 250.0, 250.0, 72e-3)]
    [InlineData("EL84", -7.3, 250.0, 250.0, 48e-3)]
    public void Pentode_presets_land_near_the_datasheet_operating_point(
        string tube,
        double vg1,
        double vg2,
        double vp,
        double amps
    ) {
        var model = tube switch {
            "EL34" => PentodeModel.EL34,
            "6L6GC" => PentodeModel.SixL6GC,
            _ => PentodeModel.EL84
        };
        var current = new double[3];
        new Pentode(model).Evaluate([vg1, vg2, vp], current, new double[9], 3);
        output.WriteLine(
            $"{tube}: plate {current[2] * 1e3:0.0} mA, screen {current[1] * 1e3:0.0} mA (datasheet "
            + $"plate {amps * 1e3:0} mA)"
        );
        Assert.InRange(current[2], 0.75 * amps, 1.25 * amps);
    }

    /// <summary>
    ///     The slopes at the tabulated class-A points (RCA, GE and Tung-Sol data): transconductance, plate resistance
    ///     and their product μ, from the device's Jacobian. A Koren fit pins μ and the current; the slopes show
    ///     whether the curvature is right too. The 12AT7 is the loosest (Koren's fit: 12.8 mA, 7.2 mS, 7.9k against
    ///     10 mA, 5.5 mS, 10.9k).
    /// </summary>
    [Theory]
    [InlineData("12AX7", -2.0, 250.0, 1.6e-3, 62.5e3)]
    [InlineData("12AT7", -2.0, 250.0, 5.5e-3, 10.9e3)]
    [InlineData("12AU7", -8.5, 250.0, 2.2e-3, 7.7e3)]
    [InlineData("12AY7", -4.0, 250.0, 1.75e-3, 25e3)]
    [InlineData("5751", -3.0, 250.0, 1.2e-3, 58e3)]
    [InlineData("6EU7", -2.0, 250.0, 1.6e-3, 62.5e3)]
    public void Triode_presets_have_the_datasheet_slopes(
        string tube,
        double vgk,
        double vpk,
        double transconductance,
        double plateResistance
    ) {
        var model = tube switch {
            "12AX7" => TriodeModel.Ecc83,
            "12AT7" => TriodeModel.Ecc81,
            "12AU7" => TriodeModel.Ecc82,
            "12AY7" => TriodeModel.TwelveAY7,
            "5751" => TriodeModel.FiveSevenFiveOne,
            _ => TriodeModel.SixEU7
        };
        var jacobian = new double[4];
        new Triode(model).Evaluate([vgk, vpk], new double[2], jacobian, 2);
        var (gm, rp) = (jacobian[2], 1 / jacobian[3]);
        output.WriteLine(
            $"{tube}: gm {gm * 1e3:0.00} mS, rp {rp / 1e3:0.0}k, μ {gm * rp:0.0} (datasheet "
            + $"{transconductance * 1e3:0.00} mS, {plateResistance / 1e3:0.0}k, "
            + $"μ {transconductance * plateResistance:0})"
        );
        Assert.InRange(gm, 0.65 * transconductance, 1.35 * transconductance);
        Assert.InRange(rp, 0.65 * plateResistance, 1.35 * plateResistance);
        Assert.InRange(gm * rp, 0.8 * transconductance * plateResistance, 1.2 * transconductance * plateResistance);
    }

    /// <summary>
    ///     The presets refitted to their datasheets (issue #86) pinned there: current, transconductance and plate
    ///     resistance at each tabulated class-A point, within what the fit reaches (Tubes.cs records the residues).
    ///     The 12AX7 at RCA's two points, the 12AT7 at its 250 V point.
    /// </summary>
    [Theory]
    [InlineData("12AX7", -2.0, 250.0, 1.2e-3, 1.6e-3, 62.5e3, 0.05, 0.2, 0.2)]
    [InlineData("12AX7", -1.0, 100.0, 0.5e-3, 1.25e-3, 80e3, 0.05, 0.2, 0.2)]
    [InlineData("12AT7", -2.0, 250.0, 10e-3, 5.5e-3, 10.9e3, 0.02, 0.02, 0.1)]
    public void Refitted_triodes_reproduce_their_datasheet_points(
        string tube,
        double vgk,
        double vpk,
        double amps,
        double transconductance,
        double plateResistance,
        double currentTolerance,
        double slopeTolerance,
        double resistanceTolerance
    ) {
        var model = tube == "12AX7" ? TriodeModel.Ecc83 : TriodeModel.Ecc81;
        var current = new double[2];
        var jacobian = new double[4];
        new Triode(model).Evaluate([vgk, vpk], current, jacobian, 2);
        var (gm, rp) = (jacobian[2], 1 / jacobian[3]);
        output.WriteLine(
            $"{tube} {vpk} V, {vgk} V: {current[1] * 1e3:0.000} mA, {gm * 1e3:0.00} mS, {rp / 1e3:0.0}k (datasheet "
            + $"{amps * 1e3:0.0} mA, {transconductance * 1e3:0.00} mS, {plateResistance / 1e3:0.0}k)"
        );
        Assert.InRange(current[1], (1 - currentTolerance) * amps, (1 + currentTolerance) * amps);
        Assert.InRange(gm, (1 - slopeTolerance) * transconductance, (1 + slopeTolerance) * transconductance);
        Assert.InRange(rp, (1 - resistanceTolerance) * plateResistance, (1 + resistanceTolerance) * plateResistance);
    }

    /// <summary>
    ///     The 12AX7's textbook stage (100k plate, 1.5k cathode, from 250 V; RCA's and Blencowe's load-line examples)
    ///     idles near 1 mA with 1.3-1.7 V on its cathode, as measured amps do (issue #86: Koren's fit gave 0.80 mA and
    ///     1.20 V).
    /// </summary>
    [Fact]
    public void The_12ax7_textbook_stage_idles_where_amps_measure() {
        var (amps, plate, cathode) = Amp.OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, 250, 100e3, 1.5e3);
        output.WriteLine(
            $"12AX7, 100k / 1.5k from 250 V: {amps * 1e3:0.000} mA, plate {plate:0.0} V, cathode {cathode:0.000} V"
        );
        Assert.InRange(cathode, 1.3, 1.7);
        Assert.InRange(amps, 0.85e-3, 1.1e-3);
    }

    /// <summary>
    ///     The EL34's screen current at Mullard's class-A point (250 V plate and screen, -13.5 V: 100 mA plate, 14.9 mA
    ///     screen), which feeds the screen node's sag and a cathode-biased pair's bias (issue #86: Koren's K_g2 gave
    ///     4.8 mA).
    /// </summary>
    [Fact]
    public void El34_screen_current_matches_mullard() {
        var current = new double[3];
        new Pentode(PentodeModel.EL34).Evaluate([-13.5, 250, 250], current, new double[9], 3);
        output.WriteLine(
            $"EL34 250/250 V, -13.5 V: plate {current[2] * 1e3:0.0} mA, screen {current[1] * 1e3:0.00} mA"
        );
        Assert.InRange(current[1], 0.98 * 14.9e-3, 1.02 * 14.9e-3);
        Assert.InRange(current[2], 0.95 * 100e-3, 1.05 * 100e-3);
    }

    /// <summary>
    ///     The power tubes the operating-point test above leaves out, at their tabulated class-A points with plate and
    ///     screen at 250 V: plate current and transconductance.
    /// </summary>
    [Theory]
    [InlineData("EL34", -12.2, 100e-3, 11e-3)]
    [InlineData("6L6GC", -14.0, 72e-3, 6e-3)]
    [InlineData("EL84", -7.3, 48e-3, 11.3e-3)]
    [InlineData("6V6GT", -12.5, 45e-3, 4.1e-3)]
    [InlineData("KT66", -15.0, 85e-3, 7e-3)]
    [InlineData("KT88", -15.0, 140e-3, 11.5e-3)]
    [InlineData("6550", -14.0, 140e-3, 11e-3)]
    [InlineData("6AQ5", -12.5, 45e-3, 4.1e-3)]
    public void Pentode_presets_have_the_datasheet_current_and_slope(
        string tube,
        double vg1,
        double amps,
        double transconductance
    ) {
        var model = tube switch {
            "EL34" => PentodeModel.EL34,
            "6L6GC" => PentodeModel.SixL6GC,
            "EL84" => PentodeModel.EL84,
            "6V6GT" => PentodeModel.SixV6GT,
            "KT66" => PentodeModel.KT66,
            "KT88" => PentodeModel.KT88,
            "6550" => PentodeModel.SixtyFiveFifty,
            _ => PentodeModel.SixAQ5
        };
        var current = new double[3];
        var jacobian = new double[9];
        new Pentode(model).Evaluate([vg1, 250, 250], current, jacobian, 3);
        output.WriteLine(
            $"{tube}: {current[2] * 1e3:0.0} mA, gm {jacobian[6] * 1e3:0.00} mS (datasheet {amps * 1e3:0} mA, "
            + $"{transconductance * 1e3:0.0} mS)"
        );
        Assert.InRange(current[2], 0.75 * amps, 1.25 * amps);
        Assert.InRange(jacobian[6], 0.75 * transconductance, 1.25 * transconductance);
    }

    [Theory]
    [InlineData("2N3904", 0.60, 0.72)]
    [InlineData("BC108", 0.58, 0.70)]
    [InlineData("2N5088", 0.58, 0.70)]
    [InlineData("AC128", 0.12, 0.25)]
    [InlineData("NKT275", 0.10, 0.25)]
    public void Transistor_presets_have_plausible_base_emitter_voltages(string part, double low, double high) {
        var model = part switch {
            "2N3904" => BjtModel.Q2N3904,
            "BC108" => BjtModel.BC108,
            "2N5088" => BjtModel.Q2N5088,
            "AC128" => BjtModel.AC128,
            _ => BjtModel.NKT275
        };

        // Forward active, collector current 1 mA: V_BE = n Vt ln(I_C / Is), polarity aside.
        var vbe = model.EmissionVoltage * Math.Log(1e-3 / model.SaturationCurrent);
        output.WriteLine($"{part}: |V_BE| {vbe:0.000} V at 1 mA");
        Assert.InRange(vbe, low, high);

        var sign = model.Polarity == BjtPolarity.Npn ? 1 : -1;
        var current = new double[2];
        new Bjt(model).Evaluate([sign * vbe, sign * (vbe - 5)], current, new double[4], 2);
        var collector = -current[1];
        var emitter = current[0];
        Assert.Equal(sign * 1e-3, collector, 1e-6);
        // Germanium leakage (I_CBO) shifts the apparent gain by a few percent.
        Assert.Equal(model.ForwardBeta, collector / (emitter - collector), model.ForwardBeta * 0.1);
    }

    [Fact]
    public void Op_amp_closed_loop_gain_follows_the_gain_bandwidth_product() {
        // Non-inverting, gain 101, LM741 (1 MHz): flat at 1 kHz, -3 dB near GBW / 101 ≈ 9.9 kHz.
        static double Gain(double frequency) {
            var amp = NonInverting(OpAmpModel.LM741, 100e3, 1e3);
            var peak = 0.0;
            for (var n = 0; n < 19_200; n++) {
                var y = Drive(ref amp, 1e-3 * Math.Sin(2 * Math.PI * frequency * n / Rate));
                if (n > 9_600) {
                    peak = Math.Max(peak, Math.Abs(y));
                }
            }

            return Analysis.ToDb(peak / 1e-3);
        }

        var low = Gain(1_000);
        var corner = Gain(9_900);
        output.WriteLine($"LM741 gain-101 stage: {low:0.00} dB at 1 kHz, {corner:0.00} dB at 9.9 kHz");
        Assert.InRange(low, 40.0, 40.2);
        Assert.InRange(corner - low, -3.6, -2.4);
    }

    [Fact]
    public void Op_amp_output_slews_and_saturates() {
        // LM308 follower: a 2 V step slews at 0.3 V/µs; a 10 V input stops at the rail less the headroom.
        var follower = NonInverting(OpAmpModel.LM308, 1, 1e12);
        double previous = 0, fastest = 0;
        for (var n = 0; n < 960; n++) {
            var y = Drive(ref follower, n < 10 ? 0 : 2);
            fastest = Math.Max(fastest, (y - previous) * Rate);
            previous = y;
        }

        output.WriteLine($"LM308 follower: fastest slope {fastest / 1e6:0.000} V/µs, settles at {previous:0.000} V");
        Assert.InRange(fastest, 0.25e6, 0.31e6);
        Assert.Equal(2, previous, 1e-3);

        for (var n = 0; n < 960; n++) {
            previous = Drive(ref follower, 10);
        }

        output.WriteLine($"at 10 V in: {previous:0.000} V out (rail 4.5 V, headroom 1 V)");
        Assert.InRange(previous, 3.3, 3.5);
    }

    static double Drive(ref RTypeRoot<Ports<ResistiveVoltageSource>, OpAmp> amp, double x) {
        amp.Ports.A.Volts = x;
        return amp.Tick();
    }

    static string Format(double[] v) =>
        string.Join(", ", v.Select(static x => x.ToString("0.000", CultureInfo.InvariantCulture)));

    static RTypeRoot<Ports<ResistiveVoltageSource>, OpAmp> NonInverting(
        OpAmpModel model,
        double feedback,
        double ground
    ) {
        var net = new WdfNetlist();
        var (plus, minus, outputNode) = (net.Node(), net.Node(), net.Node());
        net.Port(plus, WdfNetlist.Ground);
        net.Resistor(outputNode, minus, feedback);
        net.Resistor(minus, WdfNetlist.Ground, ground);
        net.OpAmp(plus, minus, outputNode);
        net.Probe(outputNode);
        return new(net, new(new(1e3)), new(model, -4.5, 4.5, Rate));
    }

    static INonlinearDevice Create(string name) =>
        name switch {
            "diode" => new Diode(DiodeModel.Silicon),
            "diode-rs" => new Diode(DiodeModel.Led, 1, 15),
            "clipper" => new DiodeClipper(DiodeModel.Germanium, 1, DiodeModel.Silicon, 2),
            "npn" => new Bjt(BjtModel.Q2N3904),
            "pnp" => new Bjt(BjtModel.Q2N3904 with { Polarity = BjtPolarity.Pnp }),
            "germanium" => new Bjt(BjtModel.AC128),
            "triode" => new Triode(TriodeModel.Ecc83),
            "pentode" => new Pentode(PentodeModel.EL34),
            "opamp" => new OpAmp(OpAmpModel.TL072, -4.5, 4.5, Rate),
            _ => new Devices<Diode, Triode>(new(DiodeModel.Schottky), new(TriodeModel.Ecc82))
        };

    /// <summary>
    ///     Port voltages in each device's working range, avoiding the kinks (cut-off, zero plate voltage).
    /// </summary>
    static double Sample(string name, int port, Random random) {
        return name switch {
            "diode" or "clipper" => Uniform(-0.8, 0.8),
            "diode-rs" => Uniform(-1, 3),
            "npn" => Uniform(-1, 0.75),
            "pnp" => Uniform(-0.75, 1),
            "germanium" => Uniform(-0.3, 1),
            "triode" => port == 0 ? Uniform(-4, 1) : Uniform(20, 400),
            "pentode" => port switch {
                0 => Uniform(-30, 1),
                1 => Uniform(100, 400),
                _ => Uniform(10, 450)
            },
            "opamp" => port == 0 ? Uniform(-0.05, 0.05) : Uniform(-4, 4),
            _ => port == 0 ? Uniform(-0.5, 0.3) : port == 1 ? Uniform(-4, 1) : Uniform(20, 300)
        };
        double Uniform(double low, double high) => low + (high - low) * random.NextDouble();
    }
}
