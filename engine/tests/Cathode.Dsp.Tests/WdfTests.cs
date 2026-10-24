using System.Numerics;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class WdfTests {
    const double Rate = 48_000;

    [Fact]
    public void Resistive_divider_and_parallel_currents_match_ohms_law() {
        var divider = new WdfCircuit<Series<Resistor, Resistor>, IdealVoltageSource>(new(new(1e3), new(3e3)), default);
        divider.Propagate();
        divider.Root.Volts = 2;
        divider.Tick();
        Assert.Equal(1.5, divider.Tree.B.Voltage, 1e-12);
        Assert.Equal(0.5, divider.Tree.A.Voltage, 1e-12);
        Assert.Equal(0.5e-3, divider.Tree.A.Current, 1e-15);

        var parallel = new WdfCircuit<Parallel<Resistor, Resistor>, IdealVoltageSource>(
            new(new(1e3), new(4e3)),
            default
        );
        parallel.Propagate();
        parallel.Root.Volts = 2;
        parallel.Tick();
        Assert.Equal(2e-3, parallel.Tree.A.Current, 1e-15);
        Assert.Equal(0.5e-3, parallel.Tree.B.Current, 1e-15);
        Assert.Equal(2.5e-3, parallel.Tree.Current, 1e-15);
    }

    [Theory]
    [InlineData(50.0)]
    [InlineData(1_000.0)]
    [InlineData(10_000.0)]
    [InlineData(20_000.0)]
    public void Rc_low_pass_is_the_bilinear_transform_of_its_prototype(double frequency) {
        const double R = 4.7e3, C = 47e-9; // 720 Hz
        var circuit = new WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, OpenCircuit>(
            new(new(R), new(C, Rate)),
            default
        );
        circuit.Propagate();
        var impulse = new float[16_384];
        for (var i = 0; i < impulse.Length; i++) {
            circuit.Tree.A.Volts = i == 0 ? 1 : 0;
            impulse[i] = (float)circuit.Tick();
        }

        // The bilinear transform maps analog frequency (2 fs) tan(π f / fs) to f.
        var warped = 2 * Rate * Math.Tan(Math.PI * frequency / Rate);
        var expected = 1 / Complex.Abs(new(1, warped * R * C));
        Assert.Equal(expected, Analysis.MagnitudeAt(impulse, frequency, Rate), 1e-6);
    }

    [Fact]
    public void Rc_step_response_follows_the_exponential_charge() {
        const double R = 1e3, C = 1e-6, Fast = 4_800_000; // τ = 1 ms, fine steps so the discretisation error is tiny
        var circuit = new WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, OpenCircuit>(
            new(new(R), new(C, Fast)),
            default
        );
        circuit.Propagate();
        circuit.Tree.A.Volts = 1;
        for (var n = 1; n <= 24_000; n++) {
            var v = circuit.Tick();
            Assert.Equal(1 - Math.Exp(-(n - 0.5) / Fast / (R * C)), v, 1e-6);
        }
    }

    [Fact]
    public void Series_rc_current_decays_and_rl_current_rises() {
        const double R = 1e3, C = 1e-6, L = 1, Fast = 4_800_000;
        var rc = new WdfCircuit<Series<Resistor, Capacitor>, IdealVoltageSource>(new(new(R), new(C, Fast)), default);
        var rl = new WdfCircuit<Series<ResistiveVoltageSource, Inductor>, ShortCircuit>(
            new(new(R), new(L, Fast)),
            default
        );
        rc.Propagate();
        rl.Propagate();
        rc.Root.Volts = 1;
        rl.Tree.A.Volts = 1;
        for (var n = 1; n <= 24_000; n++) {
            rc.Tick();
            rl.Tick();
            var t = (n - 0.5) / Fast;
            Assert.Equal(Math.Exp(-t / (R * C)) / R, rc.Tree.A.Current, 1e-9);
            Assert.Equal((1 - Math.Exp(-t * R / L)) / R, -rl.Tree.A.Current, 1e-9);

            // The reactive ports read their own voltage and current after a sample, not just after a precharge.
            Assert.Equal(1 - R * rc.Tree.A.Current, rc.Tree.B.Voltage, 1e-9);
            Assert.Equal(rc.Tree.A.Current, rc.Tree.B.Current, 1e-12);
            Assert.Equal(-rl.Tree.A.Voltage, rl.Tree.B.Voltage, 1e-9); // a series loop: the port voltages sum to zero
            Assert.Equal(rl.Tree.A.Current, rl.Tree.B.Current, 1e-12);
        }
    }

    [Theory]
    [InlineData(-30.0)]
    [InlineData(-3.0)]
    [InlineData(-0.77)]
    [InlineData(0.0)]
    [InlineData(5.0)]
    [InlineData(40.0)]
    [InlineData(1_000.0)]
    public void Wright_omega_solves_its_defining_equation(double x) {
        var precise = WrightOmega.Precise(x);
        Assert.Equal(x, precise + Math.Log(precise), 1e-12 * Math.Max(1, Math.Abs(x)));
        Assert.Equal(precise, WrightOmega.Fast(x), 1e-5 * precise);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(0, 3)]
    public void Diode_pair_satisfies_the_implicit_port_equation(int forwardCount, int reverseCount) {
        var silicon = DiodeModel.Silicon;
        var germanium = DiodeModel.Germanium;
        foreach (var resistance in new[] { 10.0, 1e3, 100e3 }) {
            var root = new DiodePair(silicon, forwardCount, germanium, reverseCount);
            root.Adapt(resistance);
            for (var a = -50.0; a <= 50; a += 0.37) {
                var expected = Bisect(a, resistance, silicon, forwardCount, germanium, reverseCount);
                Assert.Equal(expected, root.Solve(a), 1e-6);
                Assert.Equal(2 * root.Solve(a) - a, root.Scatter(a), 1e-12);
            }
        }
    }

    [Fact]
    public void Diode_clipper_matches_an_iterated_trapezoidal_solve_exactly() {
        // A WDF with bilinear capacitors and an exact root is the trapezoidal rule, so it must agree with a Newton
        // solve of the same discretisation iterated to convergence.
        const double R = 2.2e3, C = 10e-9, Fs = 192_000;
        var input = Drive(Fs, 2_000, 8_000);
        var wdf = RenderWdf(input, R, C, Fs);
        var reference = Trapezoidal(input, R, C, Fs, 1);

        for (var i = 0; i < input.Length; i++) {
            Assert.Equal(reference[i], wdf[i], 1e-6);
        }
    }

    [Fact]
    public void Diode_clipper_converges_to_a_high_resolution_reference() {
        const double R = 2.2e3, C = 10e-9, Fs = 192_000, Factor = 64;
        var input = Drive(Fs, 2_000, 8_000);
        var fine = Drive(Fs * Factor, 2_000, (int)(8_000 * Factor));
        var wdf = RenderWdf(input, R, C, Fs);
        var reference = Trapezoidal(fine, R, C, Fs * Factor, 1);

        double error = 0, energy = 0;
        for (var i = 1_000; i < input.Length; i++) {
            var r = reference[(int)(i * Factor)];
            error += (wdf[i] - r) * (wdf[i] - r);
            energy += r * r;
        }

        var errorDb = 10 * Math.Log10(error / energy);
        Assert.True(errorDb < -40, $"WDF at 192 kHz is {errorDb:0.0} dB from the 12 MHz reference");
    }

    /// <summary>The root of the monotonic (a - v) / R - i(v) by bisection, to double precision.</summary>
    static double Bisect(
        double a,
        double r,
        DiodeModel forward,
        int forwardCount,
        DiodeModel reverse,
        int reverseCount
    ) {
        double low = -Math.Abs(a) - 1, high = Math.Abs(a) + 1;
        for (var i = 0; i < 200; i++) {
            var v = 0.5 * (low + high);
            if ((a - v) / r - Current(forward, forwardCount, reverse, reverseCount, v) > 0) {
                low = v;
            } else {
                high = v;
            }
        }

        return 0.5 * (low + high);
    }

    static double Current(DiodeModel forward, int forwardCount, DiodeModel reverse, int reverseCount, double v) {
        var i = 0.0;
        if (forwardCount > 0) {
            i += forward.SaturationCurrent * (Math.Exp(v / forward.EmissionVoltage(forwardCount)) - 1);
        }

        if (reverseCount > 0) {
            i -= reverse.SaturationCurrent * (Math.Exp(-v / reverse.EmissionVoltage(reverseCount)) - 1);
        }

        return i;
    }

    /// <summary>A 2 V sine: hard enough to drive silicon diodes deep into conduction.</summary>
    static double[] Drive(double rate, double frequency, int length) {
        var x = new double[length];
        for (var i = 0; i < length; i++) {
            x[i] = 2 * Math.Sin(2 * Math.PI * frequency * i / rate);
        }

        return x;
    }

    static double[] RenderWdf(double[] input, double r, double c, double rate) {
        var circuit = new WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, DiodePair>(
            new(new(r), new(c, rate)),
            DiodePair.Symmetric(DiodeModel.Silicon)
        );
        circuit.Propagate();
        var output = new double[input.Length];
        for (var i = 0; i < input.Length; i++) {
            circuit.Tree.A.Volts = input[i];
            output[i] = circuit.Tick();
        }

        return output;
    }

    /// <summary>
    ///     C dv/dt = (x - v) / R - 2 Is sinh(v / n Vt), trapezoidal rule, Newton iterated to 1e-14 V at each step.
    /// </summary>
    static double[] Trapezoidal(double[] input, double r, double c, double rate, int count) {
        var diode = DiodeModel.Silicon;
        var vt = diode.EmissionVoltage(count);
        double F(double x, double v) => ((x - v) / r - 2 * diode.SaturationCurrent * Math.Sinh(v / vt)) / c;
        double Slope(double v) => (-1 / r - 2 * diode.SaturationCurrent / vt * Math.Cosh(v / vt)) / c;

        var h = 1 / rate;
        var output = new double[input.Length];
        double previous = 0, previousX = 0;
        for (var n = 0; n < input.Length; n++) {
            var known = previous + 0.5 * h * F(previousX, previous);
            var v = previous;
            for (var k = 0; k < 100; k++) {
                var g = v - known - 0.5 * h * F(input[n], v);
                var step = g / (1 - 0.5 * h * Slope(v));
                v -= step;
                if (Math.Abs(step) < 1e-14) {
                    break;
                }
            }

            output[n] = v;
            previous = v;
            previousX = input[n];
        }

        return output;
    }
}
