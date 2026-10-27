using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class MultiDelayBlockTests {
    const double Rate = 48_000;

    public static TheoryData<int> MultiDelayTypes => [
        MultiDelayBlock.QuadTap,
        MultiDelayBlock.QuadParallel,
        MultiDelayBlock.QuadSeries,
        MultiDelayBlock.QuadDiffusor,
        MultiDelayBlock.QuadTape
    ];

    [Fact]
    public void Quad_taps_land_at_their_time_level_and_pan() {
        // Time 100 ms = 4800 samples; taps at 25/50/75/100 % with levels 1, 0.5, -0.25 (inverted) and 0.1, panned L, R,
        // C, L.
        using var delay = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Level, 100f),
            (MultiDelayBlock.Tap1Level + 1, 50f),
            (MultiDelayBlock.Tap1Level + 2, -25f),
            (MultiDelayBlock.Tap1Level + 3, 10f),
            (MultiDelayBlock.Tap1Pan, -100f),
            (MultiDelayBlock.Tap1Pan + 1, 100f),
            (MultiDelayBlock.Tap1Pan + 2, 0f),
            (MultiDelayBlock.Tap1Pan + 3, -100f)
        );
        var impulse = Signals.Impulse(6_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, static _ => 64);

        AssertTaps(left, (1_200, 1f), (3_600, -0.25f), (4_800, 0.1f));
        AssertTaps(right, (2_400, 0.5f), (3_600, -0.25f));
    }

    [Fact]
    public void A_tap_feedback_repeats_the_whole_quad_pattern_and_master_feedback_scales_it() {
        using var delay = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 25f),
            (MultiDelayBlock.MasterFeedback, 200f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Pan, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(12_000), 64);

        Assert.Equal(1f, output[1_200], 1e-5f);
        Assert.Equal(0.5f, output[1_200 + 4_800], 1e-5f);
        Assert.Equal(0.25f, output[1_200 + 9_600], 1e-5f);
    }

    [Fact]
    public void Tap_tempo_overrides_its_share_of_the_master_time() {
        using var delay = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Tempo, 9f), // 1/16 at 120 BPM = 125 ms
            (MultiDelayBlock.Tap1Pan, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(8_000), 64);

        AssertTaps(output, (6_000, 1f));
    }

    [Fact]
    public void Quad_parallel_lines_feed_back_into_themselves_and_across() {
        // Line 1 at 25 ms feeds line 2 (50 ms) by its cross feedback; line 2 repeats itself.
        using var delay = Create(
            (MultiDelayBlock.Type, MultiDelayBlock.QuadParallel),
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Tap1Feedback + 1, 50f),
            (MultiDelayBlock.Cross1, 50f),
            (MultiDelayBlock.Tap1Pan, 0f),
            (MultiDelayBlock.Tap1Pan + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(20_000), 64);

        var level2 = 100f * Decibels.ToGain(-3f) / 100f;
        Assert.Equal(1f, output[1_200], 1e-5f);
        Assert.Equal(level2, output[2_400], 1e-5f);
        Assert.Equal(level2 * 0.5f, output[1_200 + 2_400], 1e-5f); // line 1's echo crossed into line 2
        Assert.Equal(level2 * 0.5f, output[2_400 + 2_400], 1e-5f); // line 2's own repeat
        Assert.Equal(level2 * 0.25f, output[3_600 + 2_400], 1e-5f); // the crossed echo's repeat
    }

    [Fact]
    public void Quad_series_chains_the_lines_and_returns_the_first_three() {
        // 10, 20, 30 and 40 ms in series: an impulse comes out of line 4 after 100 ms; Return 0 mutes the others.
        using var delay = Create(
            (MultiDelayBlock.Type, MultiDelayBlock.QuadSeries),
            (MultiDelayBlock.Time, 40f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.SeriesReturn, 0f),
            (MultiDelayBlock.SeriesSend, 50f),
            (MultiDelayBlock.Tap1Level + 3, 100f),
            (MultiDelayBlock.Tap1Pan + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(8_000), 64);

        AssertTaps(output, (4_800, 0.125f)); // three sends of 50 %
    }

    [Fact]
    public void Quad_diffusor_smears_each_echo_without_colouring_its_energy() {
        using var delay = Create(
            (MultiDelayBlock.Type, MultiDelayBlock.QuadDiffusor),
            (MultiDelayBlock.Time, 200f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Feedback, 0f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Tap1Feedback + 2, 70f),
            (MultiDelayBlock.Tap1Level, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 100f),
            (MultiDelayBlock.Tap1Level + 3, 0f),
            (MultiDelayBlock.Tap1Pan + 2, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(96_000), 64);

        // A Schroeder allpass: -g at once, then (1 - g²) g^(k-1) every 150 ms; lossless overall.
        Assert.Equal(-0.7f, output[0], 1e-5f);
        Assert.Equal(1f - 0.49f, output[7_200], 1e-5f);
        Assert.Equal((1f - 0.49f) * 0.7f, output[14_400], 1e-5f);
        Assert.Equal(1.0, output.Sum(static s => (double)s * s), 1e-3);
    }

    [Fact]
    public void Voice_band_pass_filter_shapes_its_tap_and_the_master_scaler_moves_it() {
        static float Peak(float scale) {
            using var delay = Create(
                (MultiDelayBlock.Time, 100f),
                (MultiDelayBlock.Tap1Feedback + 3, 0f),
                (MultiDelayBlock.Mix, 100f),
                (MultiDelayBlock.Tap1Pan, 0f),
                (MultiDelayBlock.Tap1Level + 1, 0f),
                (MultiDelayBlock.Tap1Level + 2, 0f),
                (MultiDelayBlock.Tap1Level + 3, 0f),
                (MultiDelayBlock.Filter1Type, MultiDelayBlock.FilterBandPass),
                (MultiDelayBlock.Filter1Frequency, 1_000f),
                (MultiDelayBlock.Filter1Q, 4f),
                (MultiDelayBlock.MasterFilterFrequency, scale)
            );
            var response = BlockRender.Mono(delay, Signals.Impulse(16_384 + 1_200), 64).AsSpan(1_200).ToArray();
            return (float)Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000 * scale / 100f, Rate));
        }

        Assert.Equal(0f, Peak(100f), 0.1f);
        Assert.Equal(0f, Peak(200f), 0.1f);
    }

    [Fact]
    public void Voice_comb_never_adds_gain_and_ring_mod_moves_the_tone() {
        using var comb = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Pan, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 0f),
            (MultiDelayBlock.Comb1Time, 1f),
            (MultiDelayBlock.Comb1Feedback, 90f)
        );
        using var ring = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Pan, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 0f),
            (MultiDelayBlock.Ring1Frequency, 200f),
            (MultiDelayBlock.Ring1Mix, 100f)
        );

        var response = BlockRender.Mono(comb, Signals.Impulse(16_384 + 1_200), 64).AsSpan(1_200).ToArray();
        var tone = BlockRender.Mono(ring, Signals.Sine(1_000, Rate, 48_000, 0.5f), 64).AsSpan(4_800, 32_768).ToArray();

        // The comb's peaks (every 1 kHz) sit at unity and its troughs well below.
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.1);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 1_500, Rate)) < -20);

        // A 1 kHz tone ring modulated at 200 Hz becomes 800 and 1200 Hz, with no 1 kHz left.
        var magnitudes = Fft.Magnitudes(tone);
        double Bin(double hz) => magnitudes[(int)Math.Round(hz * tone.Length / Rate)];
        Assert.True(Bin(800) > 100 * Bin(1_000) && Bin(1_200) > 100 * Bin(1_000));
    }

    [Fact]
    public void Voice_chorus_and_lfo_move_the_tap_time() {
        using var plain = Create((MultiDelayBlock.Time, 100f), (MultiDelayBlock.Mix, 100f));
        using var chorus = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Chorus1Depth, 2f)
        );
        using var lfo = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Lfo1Depth, 2f)
        );
        var input = Signals.Sine(300, Rate, 48_000, 0.5f);

        var a = BlockRender.Mono(plain, input, 64);
        var b = BlockRender.Mono(chorus, input, 64);
        var c = BlockRender.Mono(lfo, input, 64);

        Assert.True(Analysis.NullResidualDb(a.AsSpan(10_000), b.AsSpan(10_000)) > -20);
        Assert.True(Analysis.NullResidualDb(a.AsSpan(10_000), c.AsSpan(10_000)) > -20);
    }

    [Fact]
    public void Quad_tape_motor_speed_stretches_the_heads() {
        using var delay = Create(
            (MultiDelayBlock.Type, MultiDelayBlock.QuadTape),
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.MotorSpeed, 50f),
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Pan, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(8_000), 64);

        Assert.InRange(Analysis.PeakIndex(output), 2_400 - 60, 2_400 + 60); // 25 ms at half speed, give or take the wow
    }

    [Fact]
    public void Envelope_and_ducker_shape_the_wet() {
        using var ducked = Create(
            (MultiDelayBlock.Time, 100f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.DuckAttenuation, 20f),
            (MultiDelayBlock.DuckThreshold, -30f)
        );
        using var open = Create((MultiDelayBlock.Time, 100f), (MultiDelayBlock.Mix, 100f));
        var input = Signals.Sine(440, Rate, 24_000, 0.5f);

        var a = BlockRender.Mono(ducked, input, 64);
        var b = BlockRender.Mono(open, input, 64);

        Assert.Equal(
            -20,
            Analysis.ToDb(Analysis.Rms(a.AsSpan(12_000, 9_600)) / Analysis.Rms(b.AsSpan(12_000, 9_600))),
            0.2
        );
    }

    [Fact]
    public void Tempo_sync_sets_the_master_time() {
        using var delay = Create(
            (MultiDelayBlock.Tempo, 3f), // 1/4 at 120 BPM = 500 ms
            (MultiDelayBlock.Tap1Feedback + 3, 0f),
            (MultiDelayBlock.Mix, 100f),
            (MultiDelayBlock.Tap1Level, 0f),
            (MultiDelayBlock.Tap1Level + 1, 0f),
            (MultiDelayBlock.Tap1Level + 2, 0f),
            (MultiDelayBlock.Tap1Level + 3, 100f),
            (MultiDelayBlock.Tap1Pan + 3, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(30_016), 64);

        Assert.Equal(24_000, Analysis.PeakIndex(output));
    }

    [Theory]
    [MemberData(nameof(MultiDelayTypes))]
    public void Every_type_is_block_size_independent_and_decays_by_default(int type) {
        var input = Signals.Concat(Signals.Noise(4_800, 3), new float[48_000 * 5]);
        using var fixedSize = Create((MultiDelayBlock.Type, type));
        using var variableSize = Create((MultiDelayBlock.Type, type));
        var random = new Random(5);

        var reference = BlockRender.Mono(fixedSize, input, 64);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, 65)).Left;

        Assert.True(Analysis.NullResidualDb(reference, varied) < -140);
        Assert.False(Analysis.AnySubnormal(reference), "subnormal samples in output");
        Assert.True(
            Analysis.ToDb(Analysis.Peak(reference.AsSpan(reference.Length - 4_800))) < -120,
            "tail did not decay"
        );
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

    static MultiDelayBlock Create(params (int Index, float Value)[] settings) =>
        TestBlocks.Create<MultiDelayBlock>(settings);
}
