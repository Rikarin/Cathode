using System.Diagnostics;
using System.Numerics;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The five wahs that were filter voicings and are now circuits: Clyde, Colorsound and V845 as inductor circuits
///     (their operating point and response against the reference simulator are in <see cref="WahCircuitTests" />, which
///     runs every entry of <see cref="WahBlock.Circuits" />), Morley and Parapedal as inductor-less op-amp band-passes.
///     Each sweeps its pedal's range, its centre and Q agree with what its parts give, and the Basic page still moves it.
/// </summary>
public class WahPedalCircuitTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Clyde = 3;
    const int Colorsound = 4;
    const int Morley = 5;
    const int V845 = 6;
    const int Parapedal = 7;

    public static TheoryData<int> Pedals => [Clyde, Colorsound, Morley, V845, Parapedal];

    [Fact]
    public void The_five_types_keep_their_names_and_places() {
        var names = WahBlock.Info.Params[WahBlock.Type].Options!;
        Assert.Equal(["Clyde", "Colorsound", "Morley", "V845", "Parapedal"], names.Skip(3).Take(5));
        Assert.Equal(["Cry Baby", "V846"], names.Skip(WahBlock.FirstCircuit));
        foreach (var type in Pedals) {
            Assert.True(WahBlock.IsCircuit(type));
        }

        Assert.False(WahBlock.IsCircuit(0)); // the default type, and so the golden render, is a filter voicing
        Assert.NotNull(WahBlock.ActiveCircuit(Morley));
        Assert.NotNull(WahBlock.ActiveCircuit(Parapedal));
        Assert.NotNull(WahBlock.InductorCircuit(Clyde));
    }

    /// <summary>
    ///     Heel and toe through the block, against each pedal's documented character: the Clyde high and the most vocal,
    ///     the Colorsound wide with a broad peak, the Morley the widest with the Q rising toward the toe (constant
    ///     bandwidth), the V845 narrower and milder than the V846, the Parapedal low with one Q over the whole sweep.
    /// </summary>
    [Theory]
    [InlineData(Clyde, 0f, 420, 490, 14, 24)]
    [InlineData(Clyde, 100f, 2_200, 2_450, 3.5, 6)]
    [InlineData(Colorsound, 0f, 350, 410, 10, 17)]
    [InlineData(Colorsound, 100f, 2_300, 2_650, 1.5, 3)]
    [InlineData(Morley, 0f, 300, 360, 0.6, 1.1)]
    [InlineData(Morley, 100f, 3_100, 3_700, 6.5, 9.5)]
    [InlineData(V845, 0f, 430, 500, 6, 10)]
    [InlineData(V845, 100f, 1_800, 2_050, 1.6, 3)]
    [InlineData(Parapedal, 0f, 240, 290, 4, 5.5)]
    [InlineData(Parapedal, 100f, 2_000, 2_400, 4, 5.5)]
    public void Each_pedal_sweeps_its_range(int type, float position, double low, double high, double minQ, double maxQ) {
        var (frequency, q, gain) = Peak(type, position);
        output.WriteLine($"type {type} at {position} %: {frequency:0} Hz, Q {q:0.0}, {Analysis.ToDb(gain):0.0} dB");
        Assert.InRange(frequency, low, high);
        Assert.InRange(q, minQ, maxQ);
        Assert.InRange(Analysis.ToDb(gain), 3, 14); // near the other types' level
    }

    /// <summary>
    ///     The inductor circuits against their parts: at the toe the loop gain is nearly gone and the resonance sits near
    ///     L1 with C3, 1 / 2π√(LC); toward the heel the loop gain pulls the frequency down and the Q up by the same factor
    ///     √(1 + T₀) (see <see cref="WahVoicing" />), so the Q ratio follows the frequency ratio.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Inductor_circuits_resonate_where_their_parts_put_them(int index) {
        var voicing = WahBlock.Circuits[index];
        var heel = CircuitPeak(voicing, 0);
        var toe = CircuitPeak(voicing, 1);
        var lc = 1 / (2 * Math.PI * Math.Sqrt(voicing.Inductance * voicing.Tuning));
        output.WriteLine(
            $"{voicing.Name}: heel {heel.Frequency:0} Hz Q {heel.Q:0.0}, toe {toe.Frequency:0} Hz Q {toe.Q:0.0}; LC {lc:0} Hz"
        );
        Assert.InRange(toe.Frequency / lc, 0.9, 1.0);
        var predicted = toe.Frequency / heel.Frequency;
        Assert.InRange(heel.Q / toe.Q / predicted, 0.7, 1.3);
    }

    /// <summary>The op-amp circuits' simulated response against the closed forms of their band-pass from the parts.</summary>
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(0, 0.5)]
    [InlineData(0, 1.0)]
    [InlineData(1, 0.0)]
    [InlineData(1, 0.5)]
    [InlineData(1, 1.0)]
    public void Op_amp_circuits_match_their_closed_form(int index, double toe) {
        var voicing = WahBlock.ActiveCircuits[index];
        var circuit = new ActiveWahCircuit(voicing, Rate);
        circuit.SetPosition(toe);
        circuit.Reset();
        const double Height = 1e-4;
        var response = new double[65_536];
        for (var n = 0; n < response.Length; n++) {
            response[n] = circuit.Tick(n == 0 ? Height : 0) / Height;
        }

        var measured = Resonance(f => Magnitude(response, f));
        var expected = voicing.Response(toe);
        output.WriteLine(
            $"{voicing.Name} toe {toe}: {measured.Frequency:0.0} Hz Q {measured.Q:0.00} gain {measured.Gain:0.000}; "
            + $"parts {expected.Frequency:0.0} Hz Q {expected.Q:0.00} gain {expected.Gain:0.000}"
        );
        // The bilinear transform's warping is pre-compensated at the centre, not at the band edges: the Q drifts a
        // little near the toe.
        Assert.InRange(measured.Frequency / expected.Frequency, 0.995, 1.005);
        Assert.InRange(measured.Q / expected.Q, 0.95, 1.05);
        Assert.InRange(measured.Gain / expected.Gain, 0.99, 1.01);
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Min_and_max_frequency_move_each_pedal(int type) {
        var heel = Peak(type, 0f).Frequency;
        var toe = Peak(type, 100f).Frequency;
        var lowered = Peak(type, 0f, (WahBlock.MinFrequency, 70f)).Frequency;
        var raised = Peak(type, 100f, (WahBlock.MaxFrequency, 130f)).Frequency;
        output.WriteLine($"type {type}: heel {heel:0} -> {lowered:0} Hz, toe {toe:0} -> {raised:0} Hz");
        Assert.Equal(0.7, lowered / heel, 0.05);
        Assert.Equal(1.3, raised / toe, 0.07);
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Inductor_bias_raises_each_pedal(int type) {
        var stock = Peak(type, 50f).Frequency;
        var biased = Peak(type, 50f, (WahBlock.InductorBias, 100f)).Frequency;
        output.WriteLine($"type {type}: {stock:0} -> {biased:0} Hz");
        Assert.InRange(biased / stock, 1.4, 1.8); // 1 / √0.4 = 1.58
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Resonance_sharpens_each_pedal(int type) {
        var stock = Peak(type, 50f);
        var sharp = Peak(type, 50f, (WahBlock.Resonance, 2f));
        output.WriteLine($"type {type}: Q {stock.Q:0.0} -> {sharp.Q:0.0}");
        Assert.InRange(sharp.Q / stock.Q, 1.1, 2.5); // the Clyde least: its 100k already leaves the Q to the other losses
        Assert.InRange(sharp.Frequency / stock.Frequency, 0.9, 1.1);
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void The_eq_page_still_follows(int type) {
        var flat = Peak(type, 50f);
        var cut = Peak(type, 50f, (WahBlock.Eq1 + 4, -12f)); // 1 kHz, near the middle of every sweep
        Assert.True(cut.Gain < flat.Gain / 2, $"{Analysis.ToDb(flat.Gain):0.0} -> {Analysis.ToDb(cut.Gain):0.0} dB");
    }

    /// <summary>Drive pushes the op-amps to their rails, as it pushes the transistors of the inductor circuits.</summary>
    [Theory]
    [MemberData(nameof(Pedals))]
    public void Drive_distorts_each_pedal(int type) {
        double Thd(float drive) {
            using var wah = Create(type, 50f, (WahBlock.Drive, drive));
            var rendered = BlockRender.Mono(wah, Signals.Sine(750, Rate, 16_384, 0.3f), 64);
            return Analysis.Thd(rendered.AsSpan(8_192, 8_192), 128); // 750 Hz is bin 128 of 8192 at 48 kHz
        }

        var clean = Thd(0f);
        var driven = Thd(100f);
        output.WriteLine($"type {type}: THD {100 * clean:0.00} % -> {100 * driven:0.00} %");
        Assert.True(driven > 2 * clean && driven > 0.01, $"{100 * clean:0.00} % -> {100 * driven:0.00} %");
    }

    /// <summary>
    ///     The Morley's photocell lags the treadle: rocked from heel to toe it falls to its lit resistance within a few
    ///     of its 4 ms attack, and rocked back it climbs more slowly (its 40 ms release).
    /// </summary>
    [Fact]
    public void The_morley_cell_lags_the_treadle_faster_up_than_down() {
        var voicing = WahBlock.ActiveCircuits[0];
        var circuit = new ActiveWahCircuit(voicing, Rate, 16);
        circuit.SetPosition(0);
        int Steps(double toe, double settle) {
            var target = Math.Log(voicing.Cell(toe));
            var start = Math.Log(circuit.CellResistance);
            for (var step = 1; step < 10_000; step++) {
                circuit.SetPosition(toe);
                if (Math.Abs(Math.Log(circuit.CellResistance) - target) < settle * Math.Abs(start - target)) {
                    return step;
                }
            }

            return int.MaxValue;
        }

        var up = Steps(1, 0.05) * 16 / Rate;
        var down = Steps(0, 0.05) * 16 / Rate;
        output.WriteLine($"to 5 %: lit in {1_000 * up:0.0} ms, dark in {1_000 * down:0.0} ms");
        Assert.InRange(up, 2.5 * voicing.CellAttack, 3.5 * voicing.CellAttack); // ln 20 = 3.0 time constants
        Assert.InRange(down, 2.5 * voicing.CellRelease, 3.5 * voicing.CellRelease);
    }

    /// <summary>
    ///     Each new circuit costs no more than the circuits already there: its block against the Cry Baby's, timed
    ///     interleaved, block by block, the median ratio taken (a busy machine slows both alike).
    /// </summary>
    [Theory]
    [MemberData(nameof(Pedals))]
    public void Each_pedal_costs_no_more_than_the_cry_baby(int type) {
        var input = Signals.Noise(48_000, 4);
        using var reference = Create(WahBlock.FirstCircuit, 50f);
        using var pedal = Create(type, 50f);
        var buffer = new float[64];
        var ratios = new List<double>();
        for (var pass = 0; pass < 3; pass++) {
            for (var offset = 0; offset < input.Length; offset += 64) {
                var first = (offset / 64 + pass) % 2 == 0;
                var position = 50f + 50f * MathF.Sin(offset / 4_800f);
                reference.Params.SetTarget(WahBlock.Position, position);
                pedal.Params.SetTarget(WahBlock.Position, position);
                input.AsSpan(offset, 64).CopyTo(buffer);
                var a = Stopwatch.GetTimestamp();
                (first ? reference : pedal).Process(new(buffer, buffer), default);
                var b = Stopwatch.GetTimestamp();
                input.AsSpan(offset, 64).CopyTo(buffer);
                (first ? pedal : reference).Process(new(buffer, buffer), default);
                var c = Stopwatch.GetTimestamp();
                if (pass > 0) {
                    var (referenceTicks, pedalTicks) = first ? (b - a, c - b) : (c - b, b - a);
                    ratios.Add((double)pedalTicks / Math.Max(referenceTicks, 1));
                }
            }
        }

        ratios.Sort();
        var median = ratios[ratios.Count / 2];
        output.WriteLine($"type {type}: {median:0.00} times the Cry Baby per block");
        Assert.True(median < 1.3, $"{median:0.00} times the Cry Baby");
    }

    /// <summary>Peak frequency, Q (from the -3 dB points either side) and gain at the peak, measured through the block.</summary>
    static (double Frequency, double Q, double Gain) Peak(
        int type,
        float position,
        params (int Index, float Value)[] settings
    ) {
        const float Height = 1e-3f;
        using var wah = Create(type, position, settings);
        var response = BlockRender.Mono(wah, Signals.Impulse(32_768, Height), 64);
        return Resonance(f => Analysis.MagnitudeAt(response, f, Rate) / Height);
    }

    static (double Frequency, double Q) CircuitPeak(WahVoicing voicing, double toe) {
        var circuit = new WahCircuit(voicing, Rate);
        circuit.SetPosition(toe);
        circuit.Reset();
        const double Height = 1e-4;
        var quiet = new double[32_768];
        for (var n = 0; n < quiet.Length; n++) {
            quiet[n] = circuit.Tick(0);
        }

        circuit.Reset();
        var response = new double[quiet.Length];
        for (var n = 0; n < response.Length; n++) {
            response[n] = (circuit.Tick(n == 0 ? Height : 0) - quiet[n]) / Height;
        }

        var (frequency, q, _) = Resonance(f => Magnitude(response, f));
        return (frequency, q);
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

    /// <summary>The highest peak between 150 Hz and 6 kHz, refined, and its Q from the -3 dB points either side.</summary>
    static (double Frequency, double Q, double Gain) Resonance(Func<double, double> magnitude) {
        var peak = 150.0;
        var best = 0.0;
        for (var f = 150.0; f < 6_000; f *= 1.01) {
            var m = magnitude(f);
            if (m > best) {
                (best, peak) = (m, f);
            }
        }

        var (low, high) = (peak / 1.01, peak * 1.01);
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
        var below = Crossing(magnitude, peak, peak / 8, target);
        var above = Crossing(magnitude, peak, peak * 8, target);
        return (peak, peak / (above - below), best);
    }

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

    static WahBlock Create(int type, float position, params (int Index, float Value)[] settings) {
        var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Params.SetTarget(WahBlock.Position, position);
        foreach (var (index, value) in settings) {
            wah.Params.SetTarget(index, value);
        }

        wah.Prepare(new(48_000, 64));
        return wah;
    }
}
