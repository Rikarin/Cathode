using System.Numerics;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The component-level wahs against the reference MNA simulator, which builds the same schematic from its own netlist:
///     operating point, small-signal response at heel, middle and toe, and a hot sine through the transistors.
/// </summary>
public class WahCircuitTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    public static TheoryData<int> Voicings => [..Enumerable.Range(0, WahBlock.Circuits.Count)];

    public static TheoryData<int, double> Positions {
        get {
            var data = new TheoryData<int, double>();
            for (var v = 0; v < WahBlock.Circuits.Count; v++) {
                foreach (var toe in new[] { 0.0, 0.5, 1.0 }) {
                    data.Add(v, toe);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Voicings))]
    public void Operating_point_matches_the_reference(int index) {
        var voicing = WahBlock.Circuits[index];
        var circuit = new WahCircuit(voicing, Rate);
        var (reference, nodes) = Reference(voicing, 0.5, static _ => 0);
        var simulator = new ReferenceSimulator(reference);
        simulator.OperatingPoint();

        for (var node = 1; node <= WahCircuit.Nodes; node++) {
            var expected = simulator.Voltage(nodes[node]);
            output.WriteLine($"node {node,2}: {circuit.OperatingPoint[node - 1],9:0.0000} V (reference {expected,9:0.0000} V)");
            Assert.Equal(expected, circuit.OperatingPoint[node - 1], 1e-4);
        }
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void Small_signal_response_matches_the_reference(int index, double toe) {
        var voicing = WahBlock.Circuits[index];
        var response = ImpulseResponse(voicing, toe, 65_536);
        var (reference, nodes) = Reference(voicing, toe, static _ => 0);
        var simulator = new ReferenceSimulator(reference);
        simulator.OperatingPoint();
        var input = 0; // the signal source is the first source
        var outputNode = nodes[WahCircuit.OutputNode];

        var wdf = Resonance(f => Magnitude(response, f));
        var analog = Resonance(f => simulator.Ac(new(0, 2 * Math.PI * f), input, outputNode, 0, linearised: true).Magnitude);

        output.WriteLine(
            $"{voicing.Name} toe {toe:0.0}: WDF {wdf.Frequency:0.0} Hz {Db(wdf.Gain):0.00} dB Q {wdf.Q:0.00}; " +
            $"reference {analog.Frequency:0.0} Hz {Db(analog.Gain):0.00} dB Q {analog.Q:0.00}"
        );
        Assert.InRange(wdf.Frequency / analog.Frequency, 0.98, 1.02);
        Assert.InRange(Db(wdf.Gain) - Db(analog.Gain), -1, 1);
        Assert.InRange(wdf.Q / analog.Q, 0.95, 1.05);
    }

    /// <summary>
    ///     A hot sine near the resonance drives the transistors into compression: the WDF at 48 kHz
    ///     against the reference stepped 16 times finer, compared on the fundamental and the distortion.
    /// </summary>
    [Theory]
    [InlineData(0, 0.5, 0.05, 750)]
    [InlineData(0, 0.5, 0.5, 750)]
    [InlineData(1, 0.5, 0.5, 750)]
    [InlineData(0, 0, 1, 375)] // saturates: the generic solver takes over
    public void Hot_signal_matches_the_reference(int index, double toe, double amplitude, double frequency) {
        const int Oversampling = 16;
        const int Length = 9_600;
        const int Window = 3_840; // whole periods at the end, after the coupling capacitors settle
        var voicing = WahBlock.Circuits[index];
        Func<double, double> input = t => amplitude * Math.Sin(2 * Math.PI * frequency * t);

        var circuit = new WahCircuit(voicing, Rate);
        circuit.SetPosition(toe);
        circuit.Reset();
        var wdf = new double[Length];
        for (var n = 0; n < Length; n++) {
            wdf[n] = circuit.Tick(input(n / Rate));
        }

        var (reference, nodes) = Reference(voicing, toe, input);
        var simulator = new ReferenceSimulator(reference);
        simulator.OperatingPoint();
        var analog = new double[Length];
        var step = 1 / (Rate * Oversampling);
        for (var n = 0; n < Length; n++) {
            for (var s = 1; s <= Oversampling; s++) {
                simulator.Step(((n - 1) * Oversampling + s) * step, step);
            }

            analog[n] = simulator.Voltage(nodes[WahCircuit.OutputNode]);
        }

        var (wdfFundamental, wdfThd) = Harmonics(wdf.AsSpan(Length - Window), frequency);
        var (analogFundamental, analogThd) = Harmonics(analog.AsSpan(Length - Window), frequency);
        output.WriteLine(
            $"{voicing.Name} toe {toe} {amplitude} V: WDF {wdfFundamental:0.000} V THD {100 * wdfThd:0.00} %, " +
            $"reference {analogFundamental:0.000} V THD {100 * analogThd:0.00} % (fallbacks {circuit.Fallbacks})"
        );
        Assert.Equal(0, simulator.NewtonFailures);
        Assert.InRange(Db(wdfFundamental / analogFundamental), -0.5, 0.5);
        Assert.InRange(Db(wdfThd / analogThd), -1.5, 1.5);
    }

    static double Db(double gain) => 20 * Math.Log10(gain);

    /// <summary>Amplitude of the fundamental and the THD (harmonics 2 to 9) of a window of whole periods.</summary>
    static (double Fundamental, double Thd) Harmonics(ReadOnlySpan<double> signal, double frequency) {
        Span<double> amplitudes = stackalloc double[10];
        for (var h = 1; h < amplitudes.Length; h++) {
            var sum = Complex.Zero;
            for (var n = 0; n < signal.Length; n++) {
                sum += signal[n] * Complex.FromPolarCoordinates(1, -2 * Math.PI * h * frequency * n / Rate);
            }

            amplitudes[h] = 2 * sum.Magnitude / signal.Length;
        }

        var distortion = 0.0;
        for (var h = 2; h < amplitudes.Length; h++) {
            distortion += amplitudes[h] * amplitudes[h];
        }

        return (amplitudes[1], Math.Sqrt(distortion) / amplitudes[1]);
    }

    /// <summary>The WDF's response to a small impulse, less its output without one: its linearisation.</summary>
    static double[] ImpulseResponse(WahVoicing voicing, double toe, int length) {
        const double Height = 1e-4;
        var circuit = new WahCircuit(voicing, Rate);
        circuit.SetPosition(toe);
        circuit.Reset();
        var quiet = new double[length];
        for (var n = 0; n < length; n++) {
            quiet[n] = circuit.Tick(0);
        }

        circuit.Reset();
        var response = new double[length];
        for (var n = 0; n < length; n++) {
            response[n] = (circuit.Tick(n == 0 ? Height : 0) - quiet[n]) / Height;
        }

        return response;
    }

    static double Magnitude(double[] response, double frequency) {
        var rotation = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / Rate);
        var phasor = Complex.One;
        var sum = Complex.Zero;
        for (var n = 0; n < response.Length; n++) {
            sum += response[n] * phasor;
            phasor *= rotation;
        }

        return sum.Magnitude;
    }

    /// <summary>The highest peak between 150 Hz and 5 kHz, and its Q from the -3 dB points either side.</summary>
    static (double Frequency, double Gain, double Q) Resonance(Func<double, double> magnitude) {
        var peak = 150.0;
        var best = 0.0;
        for (var f = 150.0; f < 5_000; f *= 1.005) {
            var m = magnitude(f);
            if (m > best) {
                (best, peak) = (m, f);
            }
        }

        // Golden-section refinement within the grid step.
        var (low, high) = (peak / 1.005, peak * 1.005);
        for (var i = 0; i < 30; i++) {
            var a = high - (high - low) / 1.618;
            var b = low + (high - low) / 1.618;
            if (magnitude(a) > magnitude(b)) {
                high = b;
            } else {
                low = a;
            }
        }

        peak = 0.5 * (low + high);
        best = magnitude(peak);
        var target = best / Math.Sqrt(2);
        var below = Crossing(magnitude, peak, peak / 4, target);
        var above = Crossing(magnitude, peak, peak * 4, target);
        return (peak, best, peak / (above - below));
    }

    /// <summary>Bisects for the frequency between <paramref name="inside" /> and <paramref name="outside" /> where the magnitude falls to <paramref name="target" />.</summary>
    static double Crossing(Func<double, double> magnitude, double inside, double outside, double target) {
        for (var i = 0; i < 40; i++) {
            var middle = Math.Sqrt(inside * outside);
            if (magnitude(middle) > target) {
                inside = middle;
            } else {
                outside = middle;
            }
        }

        return Math.Sqrt(inside * outside);
    }

    /// <summary>
    ///     The wah after Holters and Zölzer's schematic, for the reference simulator, built from the voicing's values
    ///     alone. Returns the circuit and, for each <see cref="WahCircuit" /> node number, the reference's node.
    /// </summary>
    static (ReferenceCircuit Circuit, int[] Nodes) Reference(WahVoicing v, double toe, Func<double, double> input) {
        var c = new ReferenceCircuit();
        var source = c.Node();
        var x = c.Node();
        var b1 = c.Node();
        var c1 = c.Node();
        var e1 = c.Node();
        var bias = c.Node();
        var resonator = c.Node();
        var winding = c.Node();
        var pot = c.Node();
        var wiper = c.Node();
        var b2 = c.Node();
        var c2 = c.Node();
        var e2 = c.Node();
        var vcc = c.Node();
        c.Source(source, 0, input);
        c.Source(vcc, 0, _ => v.Supply);
        c.Resistor(source, x, v.InputResistor);
        c.Capacitor(x, b1, v.InputCapacitor);
        c.Resistor(vcc, c1, v.Collector1);
        c.Resistor(e1, 0, v.Emitter1);
        c.Resistor(c1, bias, v.BiasFeedback);
        c.Resistor(bias, 0, v.BiasGround);
        c.Capacitor(bias, 0, v.Bypass);
        c.Resistor(b1, resonator, v.Feedback);
        c.Inductor(resonator, winding, v.Inductance);
        c.Resistor(winding, bias, v.InductorResistance);
        c.Resistor(resonator, bias, v.QResistor);
        c.Capacitor(resonator, e2, v.Tuning);
        c.Capacitor(c1, pot, v.Coupling);
        var w = WahCircuit.WiperFraction(v, toe);
        c.Resistor(pot, wiper, (1 - w) * v.Pot);
        c.Resistor(wiper, 0, w * v.Pot);
        c.Capacitor(wiper, b2, v.Coupling);
        c.Resistor(c1, b2, v.Base2);
        c.Resistor(vcc, c2, v.Collector2);
        c.Resistor(e2, 0, v.Emitter2);
        c.Device(new Wdf.Bjt(v.Transistor), (b1, e1), (b1, c1));
        c.Device(new Wdf.Bjt(v.Transistor), (b2, e2), (b2, c2));

        var nodes = new int[WahCircuit.Nodes + 1];
        nodes[WahCircuit.WindingNode] = winding;
        nodes[WahCircuit.InputNode] = x;
        nodes[WahCircuit.Base1] = b1;
        nodes[WahCircuit.Collector1] = c1;
        nodes[WahCircuit.Emitter1] = e1;
        nodes[WahCircuit.BiasNode] = bias;
        nodes[WahCircuit.ResonatorNode] = resonator;
        nodes[WahCircuit.OutputNode] = pot;
        nodes[WahCircuit.WiperNode] = wiper;
        nodes[WahCircuit.Base2] = b2;
        nodes[WahCircuit.Collector2] = c2;
        nodes[WahCircuit.Emitter2] = e2;
        nodes[WahCircuit.SupplyNode] = vcc;
        return (c, nodes);
    }
}
