using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class MultiDelayBlockTests {
    const double Rate = 48_000;

    public static TheoryData<int> MultiDelayTypes =>
        [
            MultiDelayBlock.QuadTap,
            MultiDelayBlock.TenTap,
            MultiDelayBlock.Plex,
            MultiDelayBlock.Ducking,
            MultiDelayBlock.Reverse,
            MultiDelayBlock.Sweep
        ];

    [Fact]
    public void Quad_taps_land_at_their_time_level_and_pan() {
        // Time 100 ms = 4800 samples; taps at 25/50/75/100 % with 0/-6/-12/-20 dB, panned L, R, C, L.
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Level, 0f),
            (MultiDelayBlock.Tap1Level + 1, -6f),
            (MultiDelayBlock.Tap1Level + 2, -12f),
            (MultiDelayBlock.Tap1Level + 3, -20f),
            (MultiDelayBlock.Tap1Pan, -100f),
            (MultiDelayBlock.Tap1Pan + 1, 100f),
            (MultiDelayBlock.Tap1Pan + 2, 0f),
            (MultiDelayBlock.Tap1Pan + 3, -100f)
        );
        var impulse = Signals.Impulse(6_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, _ => 64);

        var tap2 = (float)Decibels.ToGain(-6.0);
        var tap3 = (float)Decibels.ToGain(-12.0);
        var tap4 = (float)Decibels.ToGain(-20.0);
        AssertTaps(left, (1_200, 1f), (3_600, tap3), (4_800, tap4));
        AssertTaps(right, (2_400, tap2), (3_600, tap3));
    }

    [Fact]
    public void Feedback_repeats_the_whole_quad_pattern_every_time() {
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Feedback, 50f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Pan, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(12_000), 64);

        Assert.Equal(1f, output[1_200], 1e-5f);
        Assert.Equal(0.5f, output[1_200 + 4_800], 1e-5f);
        Assert.Equal(0.25f, output[1_200 + 9_600], 1e-5f);
    }

    [Fact]
    public void Ten_taps_are_evenly_spaced_with_a_linear_taper() {
        // Time 200 ms: ten taps every 960 samples, full taper fades them from 1 down to 0.
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.TenTap),
            (MultiDelayBlock.Time, 200f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Taper, 100f),
            (MultiDelayBlock.Spread, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(10_000), 64);

        AssertTaps(output, [..Enumerable.Range(0, 9).Select(static k => (960 * (k + 1), 1f - k / 9f))]);
    }

    [Fact]
    public void Ten_tap_shuffle_delays_the_off_beats_and_spread_alternates_sides() {
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.TenTap),
            (MultiDelayBlock.Time, 200f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Taper, 0f),
            (MultiDelayBlock.Spread, 100f),
            (MultiDelayBlock.Shuffle, 100f)
        );
        var impulse = Signals.Impulse(10_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, _ => 64);

        // Off-beat taps (1st, 3rd, ...) move half a step later and sit left; on-beat taps stay and sit right.
        AssertTaps(left, [..Enumerable.Range(0, 5).Select(static i => (1_920 * i + 1_440, 1f))]);
        AssertTaps(right, [..Enumerable.Range(1, 5).Select(static i => (1_920 * i, 1f))]);
    }

    [Fact]
    public void Plex_first_echoes_arrive_at_the_line_lengths_and_diffusion_raises_echo_density() {
        var impulse = Signals.Impulse(96_000);
        int Density(float diffusion, out float[] left) {
            using var plex = Create<MultiDelayBlock>(
                (MultiDelayBlock.Type, MultiDelayBlock.Plex),
                (MultiDelayBlock.Time, 300f),
                (MultiDelayBlock.Feedback, 60f),
                (MultiDelayBlock.Mix, 100f),
                (MultiDelayBlock.Diffusion, diffusion)
            );
            var (l, r) = BlockRender.Stereo(plex, impulse, impulse, _ => 64);
            left = l;
            return l.Concat(r).Count(static s => MathF.Abs(s) > 1e-4f);
        }

        var dense = Density(100f, out _);
        var sparse = Density(0f, out var undiffused);

        // Without diffusion line 0 (ratio 1) is a clean echo at exactly Time, carrying half the input, and the left
        // side is silent until line 2 (ratio 0.743) arrives.
        Assert.Equal(0.5f, undiffused[14_400], 1e-5f);
        Assert.Equal(0f, undiffused.AsSpan(0, 10_690).ToArray().Max(MathF.Abs));
        Assert.True(dense > 20 * sparse, $"diffused {dense} vs plain {sparse} audible samples");
    }

    [Fact]
    public void Ducking_delay_ping_pongs_with_feedback_on_every_hop() {
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.Ducking),
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Feedback, 50f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.DuckAttenuation, 0f)
        );
        var impulse = Signals.Impulse(16_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, _ => 64);

        AssertTaps(left, (4_800, 1f), (14_400, 0.25f));
        AssertTaps(right, (9_600, 0.5f));
    }

    [Fact]
    public void Ducking_attenuates_the_repeats_while_playing_and_releases_after() {
        // One second of a -6 dB sine, then silence. Compare against the same delay with no attenuation.
        var input = Signals.Concat(Signals.Sine(440, Rate, 48_000, 0.5f), new float[48_000]);
        float[] Render(float attenuation) {
            using var delay = Create<MultiDelayBlock>(
                (MultiDelayBlock.Type, MultiDelayBlock.Ducking),
                (MultiDelayBlock.Time, 150f),
                (MultiDelayBlock.Feedback, 70f),
                (MultiDelayBlock.Mix, 100f),
                (MultiDelayBlock.DuckThreshold, -30f),
                (MultiDelayBlock.DuckAttenuation, attenuation),
                (MultiDelayBlock.DuckRelease, 100f)
            );
            return BlockRender.Mono(delay, input, 64);
        }

        var ducked = Render(20f);
        var open = Render(0f);

        double RatioDb(int from, int to) =>
            Analysis.ToDb(Analysis.Rms(ducked.AsSpan(from, to - from)) / Analysis.Rms(open.AsSpan(from, to - from)));

        Assert.Equal(-20, RatioDb(24_000, 43_200), 0.1); // 0.5-0.9 s: playing
        Assert.Equal(0, RatioDb(76_800, 86_400), 0.25); // 1.6-1.8 s: released
    }

    [Fact]
    public void Reverse_plays_each_chunk_backwards_under_crossfaded_windows() {
        // Time 500 ms = 24000-sample chunks. Two impulses inside one chunk come out in reverse order: head B (whose
        // chunk starts at 12000) plays the input at s at 24000 - s - 3 with weight cos²(π (s + 3) / 24000), head A
        // plays it one chunk later at 48000 - s - 3 with the complementary sin² weight.
        const int N = 24_000;
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.Reverse),
            (MultiDelayBlock.Time, 500f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f)
        );
        var input = new float[50_000];
        input[1_000] = 1f;
        input[3_000] = 0.5f;

        var output = BlockRender.Mono(delay, input, 64);

        float Weight(int s, bool cosine) {
            var angle = Math.PI * (s + 3) / N;
            return (float)Math.Pow(cosine ? Math.Cos(angle) : Math.Sin(angle), 2);
        }

        AssertTaps(
            output,
            (N - 3_003, 0.5f * Weight(3_000, true)),
            (N - 1_003, Weight(1_000, true)),
            (2 * N - 3_003, 0.5f * Weight(3_000, false)),
            (2 * N - 1_003, Weight(1_000, false))
        );
        Assert.True(Array.FindIndex(output, static s => s != 0) == N - 3_003, "the later input must come out first");
    }

    [Fact]
    public void Static_sweep_is_a_band_pass_at_its_frequency() {
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.Sweep),
            (MultiDelayBlock.Time, 10f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.SweepStart, 1_000f),
            (MultiDelayBlock.SweepStop, 1_000f),
            (MultiDelayBlock.SweepResonance, 5f)
        );

        var response = BlockRender.Mono(delay, Signals.Impulse(16_384), 64);

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.1);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 250, Rate)) < -20);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 4_000, Rate)) < -20);
    }

    [Fact]
    public void Sweep_moves_the_band_from_start_to_stop() {
        // A 0.5 Hz triangle starts at the start frequency and reaches the stop frequency after one second.
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Type, MultiDelayBlock.Sweep),
            (MultiDelayBlock.Time, 10f),
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.SweepRate, 0.5f),
            (MultiDelayBlock.SweepStart, 300f),
            (MultiDelayBlock.SweepStop, 3_000f),
            (MultiDelayBlock.SweepResonance, 5f)
        );

        var output = BlockRender.Mono(delay, Signals.Sine(3_000, Rate, 60_000, 0.5f), 64);

        var nearStart = Analysis.Rms(output.AsSpan(2_400, 4_800));
        var nearStop = Analysis.Rms(output.AsSpan(45_600, 4_800));
        Assert.True(Analysis.ToDb(nearStop / nearStart) > 20, $"{Analysis.ToDb(nearStop / nearStart):0.0} dB");
    }

    [Fact]
    public void Tempo_sync_sets_the_master_time() {
        using var delay = Create<MultiDelayBlock>(
            (MultiDelayBlock.Tempo, 3f), // 1/4 at 120 BPM = 500 ms
            (MultiDelayBlock.Feedback, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Level, Decibels.SilenceDb),
            (MultiDelayBlock.Tap1Level + 1, Decibels.SilenceDb),
            (MultiDelayBlock.Tap1Level + 2, Decibels.SilenceDb),
            (MultiDelayBlock.Tap1Level + 3, 0f),
            (MultiDelayBlock.Tap1Pan + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(30_016), 64);

        Assert.Equal(24_000, Analysis.PeakIndex(output));
    }

    [Theory]
    [MemberData(nameof(MultiDelayTypes))]
    public void Every_type_is_block_size_independent_and_decays_by_default(int type) {
        var input = Signals.Concat(Signals.Noise(4_800, 3), new float[48_000 * 5]);
        using var fixedSize = Create<MultiDelayBlock>((MultiDelayBlock.Type, type));
        using var variableSize = Create<MultiDelayBlock>((MultiDelayBlock.Type, type));
        var random = new Random(5);

        var reference = BlockRender.Mono(fixedSize, input, 64);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, 65)).Left;

        Assert.True(Analysis.NullResidualDb(reference, varied) < -140);
        Assert.False(Analysis.AnySubnormal(reference), "subnormal samples in output");
        Assert.True(Analysis.ToDb(Analysis.Peak(reference.AsSpan(reference.Length - 4_800))) < -120, "tail did not decay");
    }

    [Fact]
    public void Megatap_spreads_taps_evenly_with_a_fade_out() {
        // Eight taps across 400 ms (19200 samples): one every 2400, amplitudes 8/8 .. 1/8 normalised to unit power.
        using var megatap = Create<MegatapBlock>(
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

    [Theory]
    [InlineData(100f)]
    [InlineData(-100f)]
    public void Megatap_time_shape_bends_the_spacing_and_keeps_the_last_tap_on_time(float shape) {
        using var megatap = Create<MegatapBlock>(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 10f),
            (MegatapBlock.TimeShape, shape),
            (MegatapBlock.LevelShape, MegatapBlock.Flat),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(20_000), 64);

        var positions = Enumerable.Range(0, 10)
            .Select(k => 19_200 * MegatapBlock.TapPosition(k, 10, shape / 100f))
            .ToArray();
        var gaps = positions.Zip(positions.Skip(1), static (a, b) => b - a).ToArray();
        Assert.Equal(19_200f, positions[^1], 1e-2f);
        var growing = gaps.Zip(gaps.Skip(1)).All(static g => g.Second > g.First);
        var shrinking = gaps.Zip(gaps.Skip(1)).All(static g => g.Second < g.First);
        Assert.True(shape > 0 ? growing : shrinking, "spacing should grow for positive shape and shrink for negative");

        // Every tap is heard near its position (Hermite spreads fractional ones over a few samples), nothing else is.
        var amplitude = 1f / MathF.Sqrt(10);
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

    [Fact]
    public void Megatap_exponential_shape_ends_40_dB_down_and_alternate_pan_splits_the_taps() {
        using var megatap = Create<MegatapBlock>(
            (MegatapBlock.Time, 400f),
            (MegatapBlock.Taps, 8f),
            (MegatapBlock.LevelShape, MegatapBlock.Exponential),
            (MegatapBlock.PanShape, MegatapBlock.Alternate),
            (MegatapBlock.Spread, 100f),
            (MegatapBlock.Mix, 100f)
        );
        var impulse = Signals.Impulse(20_000);

        var (left, right) = BlockRender.Stereo(megatap, impulse, impulse, _ => 64);

        Assert.Equal(-40, Analysis.ToDb(right[19_200] / left[2_400]), 1e-3);
        Assert.Equal(0f, right[2_400]);
        Assert.Equal(0f, left[19_200]);
    }

    [Fact]
    public void Megatap_tempo_sync_sets_the_span() {
        using var megatap = Create<MegatapBlock>(
            (MegatapBlock.Tempo, 2f), // 1/2 at 120 BPM = 1 s
            (MegatapBlock.Taps, 1f),
            (MegatapBlock.PanShape, MegatapBlock.Center),
            (MegatapBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(megatap, Signals.Impulse(50_000), 64);

        Assert.Equal(48_000, Analysis.PeakIndex(output));
        Assert.Equal(1f, output[48_000], 1e-6f);
    }

    /// <summary>The signal holds exactly these taps (to float precision) and nothing else.</summary>
    static void AssertTaps(float[] signal, params (int Index, float Value)[] taps) {
        var energy = 0.0;
        foreach (var (index, value) in taps) {
            Assert.Equal(value, signal[index], 1e-5f);
            energy += value * value;
        }

        var total = signal.Sum(static s => (double)s * s);
        Assert.Equal(energy, total, 1e-6);
    }

    static T Create<T>(params (int Index, float Value)[] settings)
        where T : IDspBlock, new() {
        var block = new T();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
