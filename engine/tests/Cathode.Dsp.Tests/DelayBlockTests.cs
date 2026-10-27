using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DelayBlockTests {
    [Fact]
    public void Integer_delay_times_are_exact() {
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 100f));

        var output = BlockRender.Mono(delay, Signals.Impulse(1_000), 64);

        Assert.Equal(480, Analysis.PeakIndex(output));
        Assert.Equal(1f, output[480], 1e-6f);
        Assert.Equal(0.0, Analysis.Rms(output) * Math.Sqrt(output.Length), 1.0 + 1e-6); // nothing else
    }

    [Fact]
    public void Repeats_decay_by_the_feedback_amount() {
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 50f), (DelayBlock.Mix, 100f));

        var output = BlockRender.Mono(delay, Signals.Impulse(3_000), 64);

        Assert.Equal(1f, output[480], 1e-5f);
        Assert.Equal(0.5f, output[960], 1e-5f);
        Assert.Equal(0.25f, output[1_440], 1e-5f);
    }

    [Theory]
    [InlineData(25f, 1f, 0.25f)]
    [InlineData(50f, 1f, 1f)]
    [InlineData(75f, 0.5f, 1f)]
    [InlineData(100f, 0f, 1f)]
    public void Mix_follows_the_delay_law(float mix, float dry, float wet) {
        // The sweetened law: the wet rises on a squared taper to full level by 50 % while the dry stays at full level,
        // then the dry fades out.
        using var delay = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, mix));

        var output = BlockRender.Mono(delay, Signals.Impulse(1_000), 64);

        Assert.Equal(dry, output[0], 1e-6f);
        Assert.Equal(wet, output[480], 1e-6f);
    }

    [Fact]
    public void Tempo_sync_overrides_the_time() {
        using var delay = Create((DelayBlock.Tempo, 3f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 100f)); // 1/4
        var output = (float[])Signals.Impulse(30_016).Clone();
        var right = new float[output.Length];

        for (var offset = 0; offset < output.Length; offset += 64) {
            delay.Process(
                new(output.AsSpan(offset, 64), right.AsSpan(offset, 64)),
                new(48_000, 120, offset)
            );
        }

        Assert.Equal(24_000, Analysis.PeakIndex(output)); // a quarter note at 120 BPM is 500 ms
    }

    [Fact]
    public void Changing_the_time_glides_without_clicks() {
        using var delay = Create((DelayBlock.Time, 300f), (DelayBlock.Feedback, 40f), (DelayBlock.Mix, 50f));
        var input = Signals.Sine(330, 48_000, 48_000, 0.5f);
        var first = BlockRender.Mono(delay, input[..24_000], 64);
        delay.Params.SetTarget(DelayBlock.Time, 120f);

        var second = BlockRender.Mono(delay, input[24_000..], 64);

        var output = first.Concat(second).ToArray();
        var maxStep = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.2f, $"max step {maxStep}");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Analog_and_tape_repeats_stay_bounded_at_full_feedback(float type) {
        using var delay = Create(
            (DelayBlock.Type, type),
            (DelayBlock.Time, 50f),
            (DelayBlock.Feedback, 100f),
            (DelayBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(delay, Signals.Noise(48_000 * 3, 1, 0.9f), 64);

        Assert.True(Analysis.Peak(output) < 3f, $"peak {Analysis.Peak(output)}");
    }

    [Fact]
    public void Master_feedback_scales_the_feedback_and_negative_feedback_flips_each_repeat() {
        using var scaled = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 25f),
            (DelayBlock.MasterFeedback, 200f),
            (DelayBlock.Mix, 50f)
        );
        using var inverted = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, -50f), (DelayBlock.Mix, 50f));

        var a = BlockRender.Mono(scaled, Signals.Impulse(2_000), 64);
        var b = BlockRender.Mono(inverted, Signals.Impulse(2_000), 64);

        Assert.Equal(0.5f, a[960], 1e-5f);
        Assert.Equal(0.25f, a[1_440], 1e-5f);
        Assert.Equal(-0.5f, b[960], 1e-5f);
        Assert.Equal(0.25f, b[1_440], 1e-5f);
    }

    [Fact]
    public void Feedback_beyond_unity_is_limited_and_stays_bounded() {
        using var delay = Create(
            (DelayBlock.Time, 20f),
            (DelayBlock.Feedback, 100f),
            (DelayBlock.MasterFeedback, 200f),
            (DelayBlock.Mix, 50f)
        );

        var output = BlockRender.Mono(delay, Signals.Noise(48_000 * 2, 4, 0.5f), 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output) < 3f, $"peak {Analysis.Peak(output)}");
        Assert.Equal(0.5f, DelayBlock.Limit(0.5f));
        Assert.True(DelayBlock.Limit(100f) <= 2f);
    }

    [Fact]
    public void Echo_pan_and_right_post_delay_place_the_repeats() {
        using var panned = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.EchoPan, -100f)
        );
        using var posted = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.RightPostDelay, 5f)
        );
        var impulse = Signals.Impulse(2_000);

        var (left, right) = BlockRender.Stereo(panned, impulse, impulse, static _ => 64);
        var (left2, right2) = BlockRender.Stereo(posted, impulse, impulse, static _ => 64);

        Assert.Equal(1f, left[480], 1e-6f);
        Assert.Equal(0f, right.Max(MathF.Abs));
        Assert.Equal(480, Analysis.PeakIndex(left2));
        Assert.Equal(720, Analysis.PeakIndex(right2));
        Assert.Equal(1f, right2[720], 1e-6f);
    }

    [Fact]
    public void Dual_lines_have_their_own_times_feedback_and_cross_feedback() {
        using var delay = Create(
            (DelayBlock.Type, DelayBlock.Dual),
            (DelayBlock.Time, 10f),
            (DelayBlock.TimeRight, 15f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.FeedbackRight, 50f),
            (DelayBlock.CrossLeftToRight, 50f),
            (DelayBlock.Mix, 100f)
        );
        var impulse = Signals.Impulse(4_000);

        var (left, right) = BlockRender.Stereo(delay, impulse, new float[4_000], static _ => 64);

        // Left: one echo at 10 ms. Right: the cross-fed left echo 15 ms later (0.5), then its own repeat (0.25).
        Assert.Equal(1f, left[480], 1e-5f);
        Assert.Equal(0f, left.Skip(481).Max(MathF.Abs), 1e-6f);
        Assert.Equal(0.5f, right[480 + 720], 1e-5f);
        Assert.Equal(0.25f, right[480 + 1_440], 1e-5f);
    }

    [Fact]
    public void Mono_tape_heads_play_at_their_ratio_and_motor_speed_stretches_the_time() {
        static float[] Render(float speed, out float[] right) {
            using var delay = Create(
                (DelayBlock.Type, DelayBlock.MonoTape),
                (DelayBlock.Time, 100f),
                (DelayBlock.Feedback, 0f),
                (DelayBlock.Mix, 100f),
                (DelayBlock.MotorSpeed, speed),
                (DelayBlock.Head2Ratio, 50f),
                (DelayBlock.Head1Pan, -100f),
                (DelayBlock.Head2Pan, 100f)
            );
            var impulse = Signals.Impulse(20_000);
            (var left, right) = BlockRender.Stereo(delay, impulse, impulse, static _ => 64);
            return left;
        }

        var left = Render(100f, out var right);
        var slowLeft = Render(50f, out var slowRight);

        // Wow and flutter move the heads by a fraction of a millisecond, and the loop's tape darkening delays the peak
        // by a few samples.
        Assert.InRange(Analysis.PeakIndex(left), 4_800 - 48, 4_800 + 48);
        Assert.InRange(Analysis.PeakIndex(right), 2_400 - 40, 2_400 + 40);
        Assert.InRange(Analysis.PeakIndex(slowLeft), 9_600 - 60, 9_600 + 60);
        Assert.InRange(Analysis.PeakIndex(slowRight), 4_800 - 60, 4_800 + 60);
    }

    [Fact]
    public void Reverse_plays_each_chunk_backwards() {
        // Time 10 ms = 480-sample chunks. The head reads 2p + 3 back at p samples into a chunk, so an input at sample
        // k of one chunk comes out at 2 * 480 - k - 3, and a later input comes out first.
        using var delay = Create(
            (DelayBlock.Type, DelayBlock.Reverse),
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.ReverseCrossfade, 0f)
        );
        var input = new float[3_000];
        input[100] = 1f;
        input[300] = 0.5f;

        var output = BlockRender.Mono(delay, input, 64);

        Assert.Equal(0.5f, output[960 - 303], 1e-5f);
        Assert.Equal(1f, output[960 - 103], 1e-5f);
        Assert.Equal(960 - 303, Array.FindIndex(output, static s => MathF.Abs(s) > 1e-6f));
        Assert.Equal(1.25, output.Sum(static s => (double)s * s), 1e-5);
    }

    [Fact]
    public void Reverse_crossfade_keeps_unity_gain_and_run_off_silences_it() {
        using var faded = Create(
            (DelayBlock.Type, DelayBlock.Reverse),
            (DelayBlock.Time, 100f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.ReverseCrossfade, 50f)
        );
        using var stopped = Create(
            (DelayBlock.Type, DelayBlock.Reverse),
            (DelayBlock.Time, 100f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.ReverseRun, 0f)
        );
        var dc = Signals.Step(48_000);

        var output = BlockRender.Mono(faded, dc, 64);
        var silent = BlockRender.Mono(stopped, dc, 64);

        // A constant input reversed is still constant: the two heads' gains sum to one through every crossfade.
        Assert.All(output.Skip(20_000), static s => Assert.Equal(1f, s, 1e-4f));
        Assert.Equal(0f, silent.Max(MathF.Abs));
    }

    [Fact]
    public void Reverse_trigger_restart_starts_a_chunk_on_a_new_note() {
        // With a 1 s chunk and no restart, a note 0.3 s into the first chunk comes out 0.3 s before the end of the
        // second; with Trigger Restart a chunk starts on the note, so it comes out at the very end of the next chunk.
        static float[] Render(float restart) {
            using var delay = Create(
                (DelayBlock.Type, DelayBlock.Reverse),
                (DelayBlock.Time, 1_000f),
                (DelayBlock.Feedback, 0f),
                (DelayBlock.Mix, 100f),
                (DelayBlock.ReverseCrossfade, 0f),
                (DelayBlock.ReverseRestart, restart)
            );
            var input = new float[150_000];
            input[14_400] = 1f;
            return BlockRender.Mono(delay, input, 64);
        }

        Assert.Equal(96_000 - 14_400 - 3, Analysis.PeakIndex(Render(0f)));
        Assert.Equal(14_400 + 96_000 - 3, Analysis.PeakIndex(Render(1f)));
    }

    [Fact]
    public void Static_sweep_is_a_band_pass_at_its_frequency() {
        using var delay = Create(
            (DelayBlock.Type, DelayBlock.Sweep),
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.SweepStart, 1_000f),
            (DelayBlock.SweepStop, 1_000f),
            (DelayBlock.SweepResonance, 5f)
        );

        var response = BlockRender.Mono(delay, Signals.Impulse(16_384), 64);

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, 48_000)), 0.1);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 250, 48_000)) < -20);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 4_000, 48_000)) < -20);
    }

    [Fact]
    public void Sweep_moves_the_band_from_start_to_stop() {
        // A 0.5 Hz triangle starts at the start frequency and reaches the stop frequency after one second.
        using var delay = Create(
            (DelayBlock.Type, DelayBlock.Sweep),
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.SweepRate, 0.5f),
            (DelayBlock.SweepStart, 300f),
            (DelayBlock.SweepStop, 3_000f),
            (DelayBlock.SweepResonance, 5f)
        );

        var output = BlockRender.Mono(delay, Signals.Sine(3_000, 48_000, 60_000, 0.5f), 64);

        var nearStart = Analysis.Rms(output.AsSpan(2_400, 4_800));
        var nearStop = Analysis.Rms(output.AsSpan(45_600, 4_800));
        Assert.True(Analysis.ToDb(nearStop / nearStart) > 20, $"{Analysis.ToDb(nearStop / nearStart):0.0} dB");
    }

    [Theory]
    [InlineData(0, -6.99)]
    [InlineData(1, -12.30)]
    [InlineData(2, -18.13)]
    [InlineData(3, -24.10)]
    public void Loop_high_cut_slopes_are_butterworth(int slope, double octaveAboveDb) {
        // An n-th order Butterworth low-pass is 10 log10(1 + 4^n) down an octave above its cut-off.
        var filter = LoopFilter.Design(48_000, 20f, 1, LoopFilter.ButterworthQ, 1_000f, slope, LoopFilter.ButterworthQ);
        var state = new LoopFilterState();
        var response = Signals.Impulse(16_384).Select(x => state.Tick(filter, x)).ToArray();

        Assert.Equal(-3.01, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, 48_000)), 0.05);
        Assert.Equal(octaveAboveDb, Analysis.ToDb(Analysis.MagnitudeAt(response, 2_000, 48_000)), 0.2);
    }

    [Fact]
    public void Loop_peaking_bands_and_resonance_shape_the_repeats() {
        var filter = LoopFilter.Design(
            48_000,
            20f,
            1,
            LoopFilter.ButterworthQ,
            20_000f,
            1,
            LoopFilter.ButterworthQ,
            (500f, 1f, 12f),
            (4_000f, 2f, -6f)
        );
        var resonant = LoopFilter.Design(48_000, 20f, 1, LoopFilter.ButterworthQ, 2_000f, 1, 2f);
        var a = new LoopFilterState();
        var b = new LoopFilterState();
        var peaked = Signals.Impulse(16_384).Select(x => a.Tick(filter, x)).ToArray();
        var peaking = Signals.Impulse(16_384).Select(x => b.Tick(resonant, x)).ToArray();

        Assert.Equal(12, Analysis.ToDb(Analysis.MagnitudeAt(peaked, 500, 48_000)), 0.1);
        Assert.Equal(-6, Analysis.ToDb(Analysis.MagnitudeAt(peaked, 4_000, 48_000)), 0.3); // the 500 Hz band's skirt
        Assert.Equal(Analysis.ToDb(2), Analysis.ToDb(Analysis.MagnitudeAt(peaking, 2_000, 48_000)), 0.1); // Q 2: +6 dB
    }

    [Fact]
    public void Loop_eq_shapes_every_repeat_once_more_than_the_last() {
        using var delay = Create(
            (DelayBlock.Time, 100f),
            (DelayBlock.Feedback, 100f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.Eq1Frequency, 1_000f),
            (DelayBlock.Eq1Gain, -6f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(4_800 * 3), 64);

        var first = output.AsSpan(4_800 - 100, 4_800).ToArray();
        var second = output.AsSpan(9_600 - 100, 4_800).ToArray();
        Assert.Equal(-6, Analysis.ToDb(Analysis.MagnitudeAt(first, 1_000, 48_000)), 0.2);
        Assert.Equal(-12, Analysis.ToDb(Analysis.MagnitudeAt(second, 1_000, 48_000)), 0.3);
    }

    [Theory]
    [InlineData(DelayBlock.Digital, 2_000f, 2_000f)]
    [InlineData(DelayBlock.Analog, DelayBlock.HighCutOff, 2_800f)]
    [InlineData(DelayBlock.Analog, 1_000f, 1_000f)]
    [InlineData(DelayBlock.Reverse, 2_000f, 2_000f)]
    public void High_cut_and_type_darkening_filter_the_first_repeat(int type, float highCut, float cutoff) {
        // The loop's filters sit before the output tap, so the first repeat is already 3 dB down at the cut-off (a
        // Butterworth corner) and the second, one pass later, 6 dB. A quiet impulse keeps Analog's saturation linear.
        using var delay = Create(
            (DelayBlock.Type, type),
            (DelayBlock.Time, 100f),
            (DelayBlock.Feedback, 100f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.HighCut, highCut),
            (DelayBlock.ReverseCrossfade, 0f)
        );
        var input = new float[4_800 * 3];
        input[0] = 0.01f;

        var output = BlockRender.Mono(delay, input, 64);

        // Reverse plays each chunk backwards, so a repeat ends at the end of its chunk instead of starting at its start;
        // a time-reversed response has the same magnitude.
        var start = type == DelayBlock.Reverse ? 4_900 : 4_700;
        var first = output.AsSpan(start, 4_700).ToArray();
        var second = output.AsSpan(start + 4_800, 4_700).ToArray();
        Assert.Equal(-3.01, Analysis.ToDb(Analysis.MagnitudeAt(first, cutoff, 48_000) / 0.01), 0.15);
        Assert.Equal(-6.02, Analysis.ToDb(Analysis.MagnitudeAt(second, cutoff, 48_000) / 0.01), 0.3);
    }

    [Fact]
    public void Lfo_targets_move_only_their_line_and_the_depth_range_scales_the_swing() {
        var dry = Signals.Sine(50, 48_000, 48_000, 0.5f);

        float[] Render(float target, float range, out float[] right) {
            using var delay = Create(
                (DelayBlock.Time, 50f),
                (DelayBlock.Feedback, 0f),
                (DelayBlock.Mix, 100f),
                (DelayBlock.ModDepth, 1f),
                (DelayBlock.ModRate, 2f),
                (DelayBlock.Lfo1Target, target),
                (DelayBlock.DepthRange, range)
            );
            (var left, right) = BlockRender.Stereo(delay, dry, dry, static _ => 64);
            return left;
        }

        var left = Render(DelayBlock.TargetLeft, 1, out var right);

        // The right line is an exact 50 ms echo plus the fixed offset that keeps the swing positive (1 ms = 48 samples).
        Assert.True(Analysis.NullResidualDb(right.AsSpan(10_000, 30_000), dry.AsSpan(10_000 - 2_448, 30_000)) < -100);
        Assert.True(Analysis.NullResidualDb(left.AsSpan(10_000, 30_000), dry.AsSpan(10_000 - 2_448, 30_000)) > -40);

        // The high range swings ten times as far (±10 ms around its 10 ms offset) as the normal range (±1 ms).
        var high = Render(DelayBlock.TargetBoth, 2, out _);
        var normal = Analysis.NullResidualDb(left.AsSpan(10_000, 30_000), dry.AsSpan(10_000 - 2_448, 30_000));
        var wide = Analysis.NullResidualDb(high.AsSpan(10_000, 30_000), dry.AsSpan(10_000 - 2_880, 30_000));
        Assert.True(wide > normal + 10, $"high {wide:0.0} dB, normal {normal:0.0} dB");
    }

    [Fact]
    public void Lfo_3_on_level_swings_the_wet_between_full_and_the_depth() {
        using var delay = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.Lfo3Target, DelayBlock.Lfo3Level),
            (DelayBlock.Lfo3Rate, 2f),
            (DelayBlock.Lfo3Depth, 100f)
        );

        var output = BlockRender.Mono(delay, Signals.Step(48_000), 64);

        Assert.Equal(1f, output.Skip(480).Max(), 1e-3f);
        Assert.Equal(0f, output.Skip(480).Min(), 1e-3f);
    }

    [Fact]
    public void Diffusion_smears_an_echo_without_changing_its_energy() {
        using var plain = Create((DelayBlock.Time, 10f), (DelayBlock.Feedback, 0f), (DelayBlock.Mix, 100f));
        using var diffused = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.DiffusionMix, 100f),
            (DelayBlock.DiffusionTime, 20f)
        );
        var impulse = Signals.Impulse(48_000);

        var a = BlockRender.Mono(plain, impulse, 64);
        var b = BlockRender.Mono(diffused, impulse, 64);

        Assert.Equal(1.0, b.Sum(static s => (double)s * s), 0.01); // allpasses are lossless
        Assert.True(Analysis.Peak(b) < 0.7, $"peak {Analysis.Peak(b)}");
        Assert.True(b.Count(static s => MathF.Abs(s) > 1e-3f) > 50 * a.Count(static s => MathF.Abs(s) > 1e-3f));
    }

    [Fact]
    public void Stack_holds_the_repeats_and_keeps_adding_the_input() {
        using var delay = Create(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.Hold, DelayBlock.Stack)
        );
        var input = new float[4_000];
        input[0] = 0.5f;
        input[1_000] = 0.25f;

        var output = BlockRender.Mono(delay, input, 64);

        // Both notes repeat at unity every 10 ms for as long as Stack is on.
        for (var k = 1; k <= 6; k++) {
            Assert.Equal(0.5f, output[480 * k], 1e-5f);
        }

        Assert.Equal(0.25f, output[1_000 + 480 * 5], 1e-5f);
    }

    [Fact]
    public void Hold_through_a_boosting_feedback_eq_stays_bounded() {
        // Hold loops at unity; a +12 dB band and a resonant high cut in the loop then add gain on every pass. The
        // limiter must take over, or the repeats grow until they overflow.
        using var delay = Create(
            (DelayBlock.Time, 5f),
            (DelayBlock.Mix, 50f),
            (DelayBlock.HighCut, 1_000f),
            (DelayBlock.HighCutQ, 3f),
            (DelayBlock.Eq1Frequency, 8_000f),
            (DelayBlock.Eq1Q, 4f),
            (DelayBlock.Eq1Gain, 12f)
        );
        BlockRender.Mono(delay, Signals.Noise(4_800, 1, 0.5f), 64);
        delay.Params.SetTarget(DelayBlock.Hold, DelayBlock.HoldOn);

        var output = BlockRender.Mono(delay, new float[48_000], 64);

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output) < 3, $"peak {Analysis.Peak(output)}");
    }

    [Fact]
    public void Conformance_case_13QvSe6CE2ha_stays_finite() {
        // The parameters (start, then a jump mid-stream) of the CsCheck case that overflowed: Vintage Digital jumping
        // to Hold with a +12 dB band and a Q 3 high cut in the loop.
        float[] start = [
            2.1818182f, 11515.689f, 21f, 100f, 100f, 1125.1163f, 9402.439f, 0.05f, 6.6252065f, -71.2763f, -31.64711f,
            17.045454f, 1f, 6.737446f, 8.339896f, 100f, 24.444445f, -60f, 2000f, 0.23252869f, 1.6764705f, 113.74173f,
            100f,
            88.2353f, 1f, 1.2179918f, 100f, -80f, -10.978172f, -100f, -21.052628f, 83.33334f, -100f, 2855.67f, 10000f,
            4.7222223f, 1.0974838f, 8.190909f, 12.46875f, 79.97551f, 110.832344f, 0f, -56.90077f, 0f, -78.125f,
            41.935486f, -100f, 0.05993545f, 6.466E-34f, 50f, 2.0084997E-34f, 0.87974954f, 1.6428572f, 2.2762651f,
            320.4382f, 10f, -11.999998f, 3502.041f, 4.4785604f, -3.7792997f, 0.26865673f, 5.7756567f, 0f, 180f, 2f, 0f,
            0.05f, 8.82f, 0f, 7.497E-11f, 0.52459013f, 0.7619048f, 0f, 3.0673144f, 1.3501235f, 4.9459998E-12f,
            5.012E-24f,
            85.28378f, 1.2748142f, 0f
        ];
        float[] jump = [
            3.649126f, 1f, 0f, -100f, 44.144737f, 433.13007f, 1013.1579f, 7.505862f, 0f, -68.96673f, -54.166664f, 0f,
            97.614456f, 6.333333f, 5.375068f, -72.70429f, 33.333332f, -10f, 361.17648f, 1.3031688f, 2.256557f, 200f,
            -78.57143f, 1000f, 2571.1062f, 8.042554f, 100f, 0f, -36.61017f, -30.769226f, -20f, -100f, -100f, 3437.0789f,
            4204.6406f, 0.5f, 5.7654E-10f, 9.381221f, 16.79341f, 44.829647f, 25f, 0f, 0f, -80f, -10.637756f, 100f,
            -100f,
            1f, 0.64285713f, 43.333332f, 2.1058621f, 1.2906522f, 1.9285715f, 3f, 7871.1343f, 3.9556558f, 12f, 50f,
            4.798007f, -12f, 2f, 0f, 1.8000001f, 180f, 2f, 7.7727275f, 2.451724f, 0f, 0f, 90f, 1f, 0f, 9f, 10f, 21f, 0f,
            0f,
            96.01496f, 4.4305105f, 0f
        ];
        using var delay = Create([..start.Select(static (v, i) => (i, v))]);
        var input = Signals.Noise(9_600, 0);

        var (left, right) = BlockRender.Stereo(delay, input, input, static _ => 64);
        for (var i = 0; i < jump.Length; i++) {
            delay.Params.SetTarget(i, jump[i]);
        }

        var (left2, right2) = BlockRender.Stereo(delay, input, input, static _ => 64);

        foreach (var output in new[] { left, right, left2, right2 }) {
            Assert.True(Analysis.AllFinite(output), "non-finite output");
            Assert.True(Analysis.Peak(output) < 10, $"peak {Analysis.Peak(output)}");
        }
    }

    [Fact]
    public void Reverse_hides_the_time_ratio_and_the_time_lfos() {
        static string Label(string id, int type) => DelayBlock.Info.Params.Single(p => p.Id == id).TypeLabels![type];
        string[] hidden = [
            "ratio", "depthRange", "lfo1Type", "lfo1Rate", "lfo1Tempo", "lfo1Depth", "lfo1Phase", "lfo1Target",
            "lfo2Type", "lfo2Rate", "lfo2Tempo", "lfo2Depth", "lfo2Phase", "lfo2Target"
        ];

        Assert.All(hidden, id => Assert.Empty(Label(id, DelayBlock.Reverse)));
        Assert.All(hidden.Skip(1), id => Assert.NotEmpty(Label(id, DelayBlock.Digital)));
        Assert.Equal("L/R Time Ratio", Label("ratio", DelayBlock.PingPong));
        Assert.Null(DelayBlock.Info.Params.Single(static p => p.Id == "lfo3Depth").TypeLabels);
    }

    static DelayBlock Create(params (int Index, float Value)[] settings) {
        var delay = new DelayBlock();
        foreach (var (index, value) in settings) {
            delay.Params.SetTarget(index, value);
        }

        delay.Prepare(new(48_000, 64));
        return delay;
    }
}
