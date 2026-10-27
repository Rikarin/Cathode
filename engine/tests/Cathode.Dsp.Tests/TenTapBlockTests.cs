using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class TenTapBlockTests {
    [Fact]
    public void Ten_taps_are_evenly_spaced_with_a_linear_decay() {
        // Time 200 ms: ten taps every 960 samples, full decay fades them from 1 down to 0; at 50 % the constant-power
        // law puts the dry and the taps at -3 dB.
        using var delay = Create(
            (TenTapBlock.Time, 200f),
            (TenTapBlock.Mix, 50f),
            (TenTapBlock.Decay, 100f),
            (TenTapBlock.Spread, 0f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(10_000), 64);

        const float g = 0.70710677f;
        AssertTaps(output, [(0, g), ..Enumerable.Range(0, 9).Select(static k => (960 * (k + 1), g * (1f - k / 9f)))]);
    }

    [Fact]
    public void Shuffle_delays_the_off_beats_and_spread_alternates_sides() {
        using var delay = Create(
            (TenTapBlock.Time, 200f),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Decay, 0f),
            (TenTapBlock.Spread, 100f),
            (TenTapBlock.Shuffle, 100f)
        );
        var impulse = Signals.Impulse(10_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, static _ => 64);

        // Off-beat taps (1st, 3rd, ...) move half a step later and sit left; on-beat taps stay and sit right.
        AssertTaps(left, [..Enumerable.Range(0, 5).Select(static i => (1_920 * i + 1_440, 1f))]);
        AssertTaps(right, [..Enumerable.Range(1, 5).Select(static i => (1_920 * i, 1f))]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Shuffle_with_an_odd_tap_count_keeps_the_last_tap_on_time_at_the_longest_time(int taps) {
        // The last tap of an odd count is an off-beat; swung, it sat at up to 1.5 x Time, past the line.
        using var delay = Create(
            (TenTapBlock.Time, TenTapBlock.MaxTimeMs),
            (TenTapBlock.Taps, taps),
            (TenTapBlock.Mode, TenTapBlock.Mono),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Decay, 0f),
            (TenTapBlock.Shuffle, 100f)
        );
        const int time = 192_000;

        var output = BlockRender.Mono(delay, Signals.Impulse(time + 64), 64);

        var swung = Enumerable.Range(0, taps - 1)
            .Select(k => (time * (k + 1) / taps + (k % 2 == 0 ? time / taps / 2 : 0), 1f));
        AssertTaps(output, [..swung, (time, 1f)]);
    }

    [Fact]
    public void Number_of_taps_tap_levels_and_mono_mode() {
        using var delay = Create(
            (TenTapBlock.Time, 100f),
            (TenTapBlock.Taps, 4f),
            (TenTapBlock.Mode, TenTapBlock.Mono),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Decay, 0f),
            (TenTapBlock.Tap1Level + 1, 50f),
            (TenTapBlock.Tap1Level + 3, 25f)
        );
        var impulse = Signals.Impulse(6_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, static _ => 64);

        AssertTaps(left, (1_200, 1f), (2_400, 0.5f), (3_600, 1f), (4_800, 0.25f));
        Assert.Equal(left, right);
    }

    [Fact]
    public void Ratio_moves_the_right_taps_and_pan_alpha_curves_the_pan() {
        using var delay = Create(
            (TenTapBlock.Time, 100f),
            (TenTapBlock.Taps, 1f),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Ratio, 50f),
            (TenTapBlock.PanShape, TenTapBlock.Center)
        );
        var impulse = Signals.Impulse(6_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, impulse, static _ => 64);

        AssertTaps(left, (4_800, 1f));
        AssertTaps(right, (2_400, 1f));
    }

    [Fact]
    public void Feedback_repeats_the_pattern_at_time() {
        using var delay = Create(
            (TenTapBlock.Time, 100f),
            (TenTapBlock.Taps, 1f),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Feedback, 50f),
            (TenTapBlock.PanShape, TenTapBlock.Center)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(15_000), 64);

        AssertTaps(output, (4_800, 1f), (9_600, 0.5f), (14_400, 0.25f));
    }

    [Fact]
    public void Rhythm_taps_sit_at_their_times_and_quantise_to_the_tempo() {
        static float[] Render(float quantize) {
            using var delay = Create(
                (TenTapBlock.Type, TenTapBlock.RhythmTap),
                (TenTapBlock.Time, 1_000f),
                (TenTapBlock.Taps, 2f),
                (TenTapBlock.Mix, 100f),
                (TenTapBlock.Decay, 0f),
                (TenTapBlock.PanShape, TenTapBlock.Center),
                (TenTapBlock.Tap1Time, 30f),
                (TenTapBlock.Tap1Time + 1, 70f),
                (TenTapBlock.Quantize, quantize)
            );
            return BlockRender.Mono(delay, Signals.Impulse(50_000), 64);
        }

        AssertTaps(Render(0f), (14_400, 1f), (33_600, 1f));
        AssertTaps(Render(6f), (12_000, 1f), (36_000, 1f)); // 1/8 at 120 BPM: a 250 ms grid
    }

    [Fact]
    public void Learn_takes_the_rhythm_from_the_playing() {
        using var delay = Create(
            (TenTapBlock.Type, TenTapBlock.RhythmTap),
            (TenTapBlock.Time, 1_000f),
            (TenTapBlock.Mix, 100f),
            (TenTapBlock.Decay, 0f),
            (TenTapBlock.PanShape, TenTapBlock.Center),
            (TenTapBlock.Learn, TenTapBlock.Listen)
        );
        var input = new float[48_000];
        input[0] = 1f;
        input[15_000] = 1f; // notes more than the detector release apart, so each one re-arms it
        input[33_000] = 1f;

        BlockRender.Mono(delay, input, 64);

        Assert.Equal([0.3125f, 0.6875f], delay.LearnedPattern.ToArray());
    }

    static void AssertTaps(float[] signal, params (int Index, float Value)[] taps) {
        var energy = 0.0;
        foreach (var (index, value) in taps) {
            Assert.Equal(value, signal[index], 1e-5f);
            energy += value * value;
        }

        Assert.Equal(energy, signal.Sum(static s => (double)s * s), 1e-5);
    }

    static TenTapBlock Create(params (int Index, float Value)[] settings) => TestBlocks.Create<TenTapBlock>(settings);
}
