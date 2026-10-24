using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The nonlinear device models: Jacobians, presets against datasheet operating points, op-amp dynamics.</summary>
public class WdfDeviceTests(ITestOutputHelper output) {
    const double Rate = 192_000;

    public static TheoryData<string> Devices =>
        ["diode", "diode-rs", "clipper", "npn", "pnp", "germanium", "triode", "pentode", "opamp", "pair"];

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
    [InlineData("12AX7", -2.0, 250.0, 1.2e-3)] // datasheet 1.2 mA; Koren's fit gives about 1.0 mA
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
            $"{tube}: plate {current[2] * 1e3:0.0} mA, screen {current[1] * 1e3:0.0} mA (datasheet plate {amps * 1e3:0} mA)"
        );
        Assert.InRange(current[2], 0.75 * amps, 1.25 * amps);
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
        double Gain(double frequency) {
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
        string.Join(", ", v.Select(static x => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));

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

    /// <summary>Port voltages in each device's working range, avoiding the kinks (cut-off, zero plate voltage).</summary>
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
