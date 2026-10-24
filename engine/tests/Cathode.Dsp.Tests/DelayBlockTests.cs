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
    [InlineData(25f, 1f, 0.5f)]
    [InlineData(50f, 1f, 1f)]
    [InlineData(75f, 0.5f, 1f)]
    public void Mix_follows_the_delay_law(float mix, float dry, float wet) {
        // The wet rises to full level by 50 % while the dry stays at full level, then the dry fades out.
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

        var (left, right) = BlockRender.Stereo(panned, impulse, impulse, _ => 64);
        var (left2, right2) = BlockRender.Stereo(posted, impulse, impulse, _ => 64);

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

        var (left, right) = BlockRender.Stereo(delay, impulse, new float[4_000], _ => 64);

        // Left: one echo at 10 ms. Right: the cross-fed left echo 15 ms later (0.5), then its own repeat (0.25).
        Assert.Equal(1f, left[480], 1e-5f);
        Assert.Equal(0f, left.Skip(481).Max(MathF.Abs), 1e-6f);
        Assert.Equal(0.5f, right[480 + 720], 1e-5f);
        Assert.Equal(0.25f, right[480 + 1_440], 1e-5f);
    }

    [Fact]
    public void Mono_tape_heads_play_at_their_ratio_and_motor_speed_stretches_the_time() {
        float[] Render(float speed, out float[] right) {
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
            (var left, right) = BlockRender.Stereo(delay, impulse, impulse, _ => 64);
            return left;
        }

        var left = Render(100f, out var right);
        var slowLeft = Render(50f, out var slowRight);

        // Wow and flutter move the heads by a fraction of a millisecond.
        Assert.InRange(Analysis.PeakIndex(left), 4_800 - 40, 4_800 + 40);
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
        float[] Render(float restart) {
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
    public void Feedback_eq_shapes_each_repeat_but_not_the_first() {
        using var delay = Create(
            (DelayBlock.Time, 100f),
            (DelayBlock.Feedback, 100f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.Eq1Frequency, 1_000f),
            (DelayBlock.Eq1Gain, -6f)
        );

        var output = BlockRender.Mono(delay, Signals.Impulse(4_800 * 3), 64);

        Assert.Equal(1f, output[4_800], 1e-6f);
        var second = output.AsSpan(9_600 - 100, 4_800).ToArray();
        Assert.Equal(-6, Analysis.ToDb(Analysis.MagnitudeAt(second, 1_000, 48_000)), 0.2);
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
            (var left, right) = BlockRender.Stereo(delay, dry, dry, _ => 64);
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

    static DelayBlock Create(params (int Index, float Value)[] settings) {
        var delay = new DelayBlock();
        foreach (var (index, value) in settings) {
            delay.Params.SetTarget(index, value);
        }

        delay.Prepare(new(48_000, 64));
        return delay;
    }
}
