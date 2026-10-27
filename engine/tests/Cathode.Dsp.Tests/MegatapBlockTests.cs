using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class MegatapBlockTests {
    [Fact]
    public void Taps_spread_evenly_with_a_fade_out() {
        // Eight taps across 400 ms (19200 samples): one every 2400, amplitudes 8/8 .. 1/8 normalised to unit power.
        using var megatap = Create(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 8f),
            (MegatapBlock.LevelShape, MegatapBlock.FadeOut),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(20_000), 64);

        var norm = 1f / MathF.Sqrt(Enumerable.Range(1, 8).Sum(static a => a * a / 64f));
        AssertTaps(output, [..Enumerable.Range(0, 8).Select(k => (2_400 * (k + 1), (8 - k) / 8f * norm))]);
    }

    [Fact]
    public void A_hundred_and_twenty_eight_taps_after_a_pre_delay() {
        using var megatap = Create(
            (MegatapBlock.Time, 1_280f),
            (MegatapBlock.Taps, 128f),
            (MegatapBlock.PreDelay, 100f),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(70_000), 64);

        var amplitude = 1f / MathF.Sqrt(128);
        AssertTaps(output, [..Enumerable.Range(1, 128).Select(k => (4_800 + 480 * k, amplitude))]);
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(-100f)]
    public void Time_alpha_bends_the_spacing_and_keeps_the_last_tap_on_time(float alpha) {
        using var megatap = Create(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 10f),
            (MegatapBlock.TimeAlpha, alpha),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(20_000), 64);

        var positions = Enumerable.Range(0, 10)
            .Select(k => 19_200 * MegatapBlock.TapPosition(k, 10, alpha / 100f))
            .ToArray();
        var gaps = positions.Zip(positions.Skip(1), static (a, b) => b - a).ToArray();
        Assert.Equal(19_200f, positions[^1], 1e-2f);
        var growing = gaps.Zip(gaps.Skip(1)).All(static g => g.Second > g.First);
        var shrinking = gaps.Zip(gaps.Skip(1)).All(static g => g.Second < g.First);
        Assert.True(alpha > 0 ? growing : shrinking, "spacing should grow for positive alpha and shrink for negative");
        AssertHeardOnlyAt(output, positions, 1f / MathF.Sqrt(10));
    }

    [Fact]
    public void Time_randomize_moves_each_tap_within_half_a_spacing_repeatably() {
        static float[] Render() {
            using var megatap = Create(
                (MegatapBlock.Time, 400f),
                (MegatapBlock.Taps, 10f),
                (MegatapBlock.TimeRandom, 100f),
                (MegatapBlock.LevelShape, MegatapBlock.Flat),
                (MegatapBlock.PanShape, MegatapBlock.Center),
                (MegatapBlock.Mix, 100f)
            );
            return BlockRender.Mono(megatap, Signals.Impulse(20_000), 64);
        }

        var output = Render();

        var positions = Enumerable.Range(0, 10)
            .Select(static k => 1_920f * (k + 1) + 1_920f * (MegatapBlock.Jitter(k, 1) - 0.5f))
            .ToArray();
        Assert.Contains(positions, static p => MathF.Abs(p % 1_920f) > 100f); // they did move
        AssertHeardOnlyAt(output, positions, 1f / MathF.Sqrt(10));
        Assert.Equal(output, Render());
    }

    [Fact]
    public void Amplitude_alpha_deepens_the_exponential_floor_and_randomize_keeps_unit_power() {
        using var steep = Create(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 8f),
            (MegatapBlock.LevelShape, MegatapBlock.Exponential),
            (MegatapBlock.LevelAlpha, 50f), // power 2: the floor doubles to -80 dB
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );
        using var random = Create(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 8f),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.LevelRandom, 80f),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );
        var impulse = Signals.Impulse(20_000);

        var a = BlockRender.Mono(steep, impulse, 64);
        var b = BlockRender.Mono(random, impulse, 64);

        Assert.Equal(-80, Analysis.ToDb(a[19_200] / a[2_400]), 1e-2);
        var levels = Enumerable.Range(1, 8).Select(k => b[2_400 * k]).ToArray();
        Assert.True(levels.Max() / levels.Min() > 1.5f, "the taps should differ in level");
        Assert.Equal(1.0, levels.Sum(static l => (double)l * l), 1e-5);
    }

    [Fact]
    public void Pan_alpha_curves_the_pan_positions_and_negative_spread_mirrors_them() {
        static float Pan(float alpha, float spread) {
            using var megatap = Create(
                (MegatapBlock.Time, 400f),
                (MegatapBlock.Taps, 5f),
                (MegatapBlock.LevelShape, MegatapBlock.Flat),
                (MegatapBlock.PanShape, MegatapBlock.LeftToRight),
                (MegatapBlock.PanAlpha, alpha),
                (MegatapBlock.Spread, spread),
                (MegatapBlock.Mix, 100f)
            );
            var impulse = Signals.Impulse(20_000);
            var (left, right) = BlockRender.Stereo(megatap, impulse, impulse, static _ => 64);

            // Tap 4 of 5 sits at +0.5 with alpha 0; with alpha 50 % (power 2) at +0.25. Balance law: left = 1 - pan.
            return 1f - left[15_360] / right[15_360];
        }

        Assert.Equal(0.5f, Pan(0f, 100f), 1e-5f);
        Assert.Equal(0.25f, Pan(50f, 100f), 1e-5f);
        Assert.True(Pan(0f, -100f) < 0, "negative spread puts the tap on the left");
    }

    [Fact]
    public void Feedback_from_the_chosen_tap_repeats_the_pattern() {
        // Two taps at 50 and 100 ms; tap 2 feeds back at 50 %, so the pattern recurs every 100 ms at half the level.
        using var megatap = Create(
            (MegatapBlock.Time, 100f),
            (MegatapBlock.Taps, 2f),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Feedback, 50f),
            (MegatapBlock.FeedbackTap, 2f),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(15_000), 64);

        var a = 1f / MathF.Sqrt(2);
        Assert.Equal(a, output[2_400], 1e-5f);
        Assert.Equal(a, output[4_800], 1e-5f);
        Assert.Equal(0.5f * a, output[7_200], 1e-5f);
        Assert.Equal(0.5f * a, output[9_600], 1e-5f);
        Assert.Equal(0.25f * a, output[12_000], 1e-5f);
    }

    [Fact]
    public void Envelope_swells_the_input_into_the_taps() {
        using var swelled = Create(
            (MegatapBlock.Time, 10f),
            (MegatapBlock.Taps, 1f),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f),
            (MegatapBlock.EnvelopeAttack, 100f),
            (MegatapBlock.EnvelopeThreshold, -20f)
        );

        var output = BlockRender.Mono(swelled, Signals.Step(20_000, 0.5f), 64);

        // The swell opens on the first sample and reaches unity 100 ms (4800 samples) later, along an S-curve.
        Assert.Equal(0f, output[480], 1e-6f);
        Assert.Equal(0.25f, output[480 + 2_400], 1e-3f);
        Assert.Equal(0.5f, output[480 + 4_800], 1e-6f);
        Assert.Equal(0.5f, output[^1], 1e-6f);
    }

    [Fact]
    public void Diffusion_keeps_the_tap_energy() {
        using var diffused = Create(
            (MegatapBlock.Time, 100f),
            (MegatapBlock.Taps, 4f),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.DiffusionMix, 100f),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(diffused, Signals.Impulse(48_000), 64);

        Assert.Equal(1.0, output.Sum(static s => (double)s * s), 0.01);
        Assert.True(Analysis.Peak(output) < 0.3, $"peak {Analysis.Peak(output)}");
    }

    [Fact]
    public void Exponential_shape_ends_40_dB_down_and_alternate_pan_splits_the_taps() {
        using var megatap = Create(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 8f),
            (MegatapBlock.LevelShape, MegatapBlock.Exponential),
            (MegatapBlock.PanShape, MegatapBlock.Alternate),
            (MegatapBlock.Spread, 100f),
            (MegatapBlock.Mix, 100f)
        );
        var impulse = Signals.Impulse(20_000);

        var (left, right) = BlockRender.Stereo(megatap, impulse, impulse, static _ => 64);

        Assert.Equal(-40, Analysis.ToDb(right[19_200] / left[2_400]), 1e-3);
        Assert.Equal(0f, right[2_400]);
        Assert.Equal(0f, left[19_200]);
    }

    [Fact]
    public void Tempo_sync_sets_the_span() {
        using var megatap = Create(
            (MegatapBlock.Tempo, 2f), // 1/2 at 120 BPM = 1 s
            (MegatapBlock.Taps, 1f),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(50_000), 64);

        Assert.Equal(48_000, Analysis.PeakIndex(output));
        Assert.Equal(1f, output[48_000], 1e-6f);
    }

    [Theory]
    [InlineData(2)] // last tap at 1.24 x Time
    [InlineData(3)]
    [InlineData(128)]
    public void Time_randomize_at_full_time_and_pre_delay_keeps_every_tap_inside_the_line(int taps) {
        using var megatap = Create(
            (MegatapBlock.Time, MegatapBlock.MaxTimeMs),
            (MegatapBlock.PreDelay, MegatapBlock.MaxPreDelayMs),
            (MegatapBlock.Taps, taps),
            (MegatapBlock.TimeRandom, 100f),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );
        var positions = Enumerable.Range(0, taps)
            .Select(k => 48_000f + 192_000f * Math.Max(0f, (k + 1f) / taps + (MegatapBlock.Jitter(k, 1) - 0.5f) / taps))
            .ToArray();

        var output = BlockRender.Mono(megatap, Signals.Impulse((int)positions.Max() + 64), 64);

        // A read past the line wraps onto the newest samples: an un-delayed copy of the impulse at the start.
        AssertHeardOnlyAt(output, positions, 1f / MathF.Sqrt(taps));
    }

    [Fact]
    public void Feedback_at_unity_is_limited_and_stays_bounded() {
        // A steady input at 100 % feedback used to add its level on every pass: 0.5 x 100 passes in 2 s.
        using var megatap = Create(
            (MegatapBlock.Time, 20f),
            (MegatapBlock.Taps, 1f),
            (MegatapBlock.Feedback, 100f),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Step(48_000 * 2, 0.5f), 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output) < 2.5f, $"peak {Analysis.Peak(output)}");
    }

    /// <summary>
    ///     Every tap is heard near its position (Hermite spreads fractional ones over a few samples), nothing else is.
    /// </summary>
    static void AssertHeardOnlyAt(float[] output, float[] positions, float amplitude) {
        var heard = new bool[output.Length];
        foreach (var position in positions) {
            var centre = (int)MathF.Round(position);
            Assert.True(
                output.AsSpan(centre - 2, 5).ToArray().Max(MathF.Abs) > 0.5f * amplitude,
                $"no tap near {position:0.0}"
            );
            for (var i = centre - 3; i <= centre + 3; i++) {
                heard[i] = true;
            }
        }

        Assert.True(output.Where((_, i) => !heard[i]).All(static s => MathF.Abs(s) < 1e-3f));
    }

    /// <summary>The signal holds exactly these taps (to float precision) and nothing else.</summary>
    static void AssertTaps(float[] signal, params (int Index, float Value)[] taps) {
        var energy = 0.0;
        foreach (var (index, value) in taps) {
            Assert.Equal(value, signal[index], 1e-5f);
            energy += value * value;
        }

        var total = signal.Sum(static s => (double)s * s);
        Assert.Equal(energy, total, 1e-5);
    }

    static MegatapBlock Create(params (int Index, float Value)[] settings) => TestBlocks.Create<MegatapBlock>(settings);
}
