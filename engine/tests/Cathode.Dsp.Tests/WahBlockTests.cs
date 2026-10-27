using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class WahBlockTests(ITestOutputHelper output) {
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 50f)]
    [InlineData(0f, 100f)]
    [InlineData(2f, 25f)]
    public void Resonant_peak_follows_the_pedal_position(float type, float position) {
        using var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Params.SetTarget(WahBlock.Position, position);
        wah.Prepare(new(48_000, 64));

        var response = BlockRender.Mono(wah, Signals.Impulse(16_384), 64);

        var peak = 100.0;
        var best = 0.0;
        for (var f = 100.0; f < 6_000; f *= 1.01) {
            var magnitude = Analysis.MagnitudeAt(response, f, 48_000);
            if (magnitude > best) {
                best = magnitude;
                peak = f;
            }
        }

        var expected = WahBlock.Frequency((int)type, position);
        Assert.InRange(peak, expected * 0.95, expected * 1.05);
        Assert.True(Analysis.ToDb(best) > 4, $"resonance only {Analysis.ToDb(best):0.0} dB");
    }

    /// <summary>
    ///     The component-level types, at heel and toe, measured through the block: within the published sweeps (Cry
    ///     Baby about 350 Hz to 2.2 kHz; this V846 from its component values, a little higher at the heel with a lower
    ///     Q).
    /// </summary>
    [Theory]
    [InlineData(WahBlock.FirstCircuit, 0f, 360, 420, 12)]
    [InlineData(WahBlock.FirstCircuit, 100f, 1_950, 2_200, 2)]
    [InlineData(WahBlock.FirstCircuit + 1, 0f, 370, 430, 10)]
    [InlineData(WahBlock.FirstCircuit + 1, 100f, 1_900, 2_150, 2)]
    public void Component_types_sweep_their_range(int type, float position, double low, double high, double minimumQ) {
        using var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Params.SetTarget(WahBlock.Position, position);
        wah.Prepare(new(48_000, 64));

        var response = BlockRender.Mono(wah, Signals.Impulse(32_768, 1e-3f), 64);

        var peak = 100.0;
        var best = 0.0;
        for (var f = 100.0; f < 6_000; f *= 1.002) {
            var magnitude = Analysis.MagnitudeAt(response, f, 48_000);
            if (magnitude > best) {
                best = magnitude;
                peak = f;
            }
        }

        var bandwidth = 0.0;
        for (var f = peak;
             f < 2 * peak && Analysis.MagnitudeAt(response, f, 48_000) > best / Math.Sqrt(2);
             f *= 1.002) {
            bandwidth = f - peak;
        }

        var q = peak / (2 * bandwidth); // twice the upper half-bandwidth: the peak is nearly symmetric in log frequency
        output.WriteLine(
            $"type {type} at {position} %: {peak:0} Hz, Q about {q:0.0}, {Analysis.ToDb(best / 1e-3):0.0} dB"
        );
        Assert.InRange(peak, low, high);
        Assert.True(q > minimumQ, $"Q {q:0.0}");
    }

    /// <summary>
    ///     The conformance properties (BlockConformanceTests, which runs the default type) for each circuit type.
    /// </summary>
    [Theory]
    [InlineData(WahBlock.FirstCircuit)]
    [InlineData(WahBlock.FirstCircuit + 1)]
    [InlineData(3)] // Clyde
    [InlineData(4)] // Colorsound
    [InlineData(5)] // Morley
    [InlineData(6)] // V845
    [InlineData(7)] // Parapedal
    public void Component_types_are_deterministic_and_independent_of_block_size(int type) {
        var input = Signals.Noise(9_600, 11);
        using var first = Create(type, 30f);
        using var second = Create(type, 30f);
        var random = new Random(5);

        var a = BlockRender.Mono(first, input, 64);
        var b = BlockRender.Stereo(second, input, input, _ => random.Next(1, 65)).Left;
        first.Reset();
        var c = BlockRender.Mono(first, input, 64);

        Assert.True(Analysis.NullResidualDb(a, b) < -140);
        Assert.Equal(a, c);
    }

    [Theory]
    [InlineData(WahBlock.FirstCircuit)]
    [InlineData(WahBlock.FirstCircuit + 1)]
    [InlineData(3)] // Clyde
    [InlineData(4)] // Colorsound
    [InlineData(5)] // Morley
    [InlineData(6)] // V845
    [InlineData(7)] // Parapedal
    public void Component_types_decay_to_silence_from_a_full_scale_burst(int type) {
        using var wah = Create(type, 0f);
        var square = Signals.Sine(997, 48_000, 4_800).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var input = Signals.Concat(square, Signals.Noise(4_800, 3), new float[48_000 * 5]);

        var output = BlockRender.Mono(wah, input, 64);
        var tail = output.AsSpan(output.Length - 4_800);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.ToDb(Analysis.Peak(output)) < 40);
        Assert.False(Analysis.AnySubnormal(output), "subnormal samples in output");
        Assert.True(Analysis.ToDb(Analysis.Peak(tail)) < -120, "tail did not decay");
    }

    [Fact]
    public void Component_types_do_not_allocate_while_the_pedal_and_type_move() {
        using var wah = Create(WahBlock.FirstCircuit, 50f);
        var left = Signals.Noise(64, 1);
        var right = Signals.Noise(64, 2);

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 2_000; i++) {
                    wah.Params.SetTarget(WahBlock.Position, i % 200 < 100 ? 0f : 100f);
                    if (i % 250 == 0) {
                        wah.Params.SetTarget(WahBlock.Type, 3 + i / 250 % (WahBlock.TypeCount - 3)); // every circuit
                    }

                    wah.Process(new(left, right), default);
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_type_change_does_not_solve_an_operating_point_on_the_audio_thread() {
        // Each inductor voicing is solved once, at Prepare; the audio thread then only loads what was found.
        using var wah = Create(WahBlock.FirstCircuit, 50f);
        var solved = wah.OperatingPointSolves;
        var left = Signals.Noise(64, 1);
        var right = Signals.Noise(64, 2);

        for (var i = 0; i < 2 * WahBlock.TypeCount; i++) {
            wah.Params.SetTarget(WahBlock.Type, i % WahBlock.TypeCount);
            wah.Process(new(left, right), default);
        }

        Assert.Equal(WahBlock.Circuits.Count + 2, solved); // every voicing, plus each channel's constructor
        Assert.Equal(solved, wah.OperatingPointSolves);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(WahBlock.FirstCircuit)]
    [InlineData(WahBlock.FirstCircuit + 1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Sweeping_the_pedal_does_not_click(int type) {
        using var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Prepare(new(48_000, 64));
        var input = Signals.Sine(440, 48_000, 48_000, 0.3f);
        var rendered = new float[input.Length];
        var right = new float[input.Length];

        for (var offset = 0; offset < input.Length; offset += 64) {
            wah.Params.SetTarget(
                WahBlock.Position,
                50f + 50f * MathF.Sin(offset / 48_000f * 2 * MathF.PI * 2)
            ); // 2 Hz rock
            input.AsSpan(offset, 64).CopyTo(rendered.AsSpan(offset, 64));
            wah.Process(new(rendered.AsSpan(offset, 64), right.AsSpan(offset, 64)), default);
        }

        var maxStep = rendered.Zip(rendered.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.15f, $"max step {maxStep}");
    }

    [Theory]
    [InlineData(0f, 50f, 100f, 0.5)] // heel, Min Frequency 50 %: an octave down
    [InlineData(0f, 100f, 200f, 1.0)] // heel, Max only: unchanged
    [InlineData(100f, 100f, 150f, 1.5)] // toe, Max Frequency 150 %
    [InlineData(100f, 50f, 100f, 1.0)] // toe, Min only: unchanged
    public void Min_and_max_frequency_move_the_circuits_resonance(
        float position,
        float minimum,
        float maximum,
        double ratio
    ) {
        var stock = CircuitPeak(position, 100f, 100f);
        var moved = CircuitPeak(position, minimum, maximum);

        Assert.Equal(stock * ratio, moved, stock * ratio * 0.06);
    }

    [Fact]
    public void The_sweep_scale_runs_geometrically_from_min_to_max() {
        Assert.Equal(0.5, WahBlock.SweepScale(0.5, 2, 0), 1e-12);
        Assert.Equal(1.0, WahBlock.SweepScale(0.5, 2, 0.5), 1e-12);
        Assert.Equal(2.0, WahBlock.SweepScale(0.5, 2, 1), 1e-12);
    }

    /// <summary>The Cry Baby circuit's resonant peak at a pedal position and Min/Max Frequency, Hz.</summary>
    static double CircuitPeak(float position, float minimum, float maximum) {
        using var wah = Create(WahBlock.FirstCircuit, position);
        wah.Params.SetTarget(WahBlock.MinFrequency, minimum);
        wah.Params.SetTarget(WahBlock.MaxFrequency, maximum);
        wah.Reset();
        var response = BlockRender.Mono(wah, Signals.Impulse(32_768, 1e-3f), 64);
        var peak = 100.0;
        var best = 0.0;
        for (var f = 100.0; f < 8_000; f *= 1.002) {
            var magnitude = Analysis.MagnitudeAt(response, f, 48_000);
            if (magnitude > best) {
                (best, peak) = (magnitude, f);
            }
        }

        return peak;
    }

    static WahBlock Create(int type, float position) {
        var wah = new WahBlock();
        wah.Params.SetTarget(WahBlock.Type, type);
        wah.Params.SetTarget(WahBlock.Position, position);
        wah.Prepare(new(48_000, 64));
        return wah;
    }
}
