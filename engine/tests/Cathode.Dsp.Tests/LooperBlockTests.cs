using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Testing;
using System.Diagnostics;

namespace Cathode.Dsp.Tests;

public class LooperBlockTests {
    const int Loop = 4_800;

    static readonly float[] Ramp = [..Enumerable.Range(0, Loop).Select(static i => (i + 1f) / Loop * 0.5f)];

    [Fact]
    public void A_recording_plays_back_sample_exact_and_repeats() {
        using var block = Create();
        var looper = block.Looper!;

        looper.Apply(LooperAction.Record);
        Render(block, Ramp);
        looper.Apply(LooperAction.Play);
        var played = Render(block, new float[2 * Loop]);

        Assert.Equal(Loop, looper.Length);
        Assert.Equal(LooperState.Playing, looper.State);
        Assert.Equal(Ramp, played[..Loop]);
        Assert.Equal(Ramp, played[Loop..]);
    }

    [Fact]
    public void Once_plays_a_single_pass_then_stops() {
        using var block = Create((LooperBlock.Once, 1f));
        Record(block, Ramp);

        var played = Render(block, new float[2 * Loop]);

        Assert.Equal(Ramp, played[..Loop]);
        Assert.All(played[Loop..], static s => Assert.Equal(0f, s));
        Assert.Equal(LooperState.Stopped, block.Looper!.State);
    }

    [Fact]
    public void Trims_play_only_the_window_between_start_and_end() {
        using var block = Create((LooperBlock.TrimStart, 25f), (LooperBlock.TrimEnd, 75f));
        Record(block, Ramp);

        var played = Render(block, new float[Loop]);

        Assert.Equal(Ramp[1_200..3_600], played[..2_400]);
        Assert.Equal(Ramp[1_200..3_600], played[2_400..]);
    }

    [Fact]
    public void Threshold_recording_starts_on_the_first_sample_above_the_level() {
        using var block = Create((LooperBlock.Threshold, 1f), (LooperBlock.ThresholdLevel, -20f));
        var input = new float[1_000 + Loop];
        input[500] = 0.05f; // below -20 dB: ignored
        Ramp.AsSpan().CopyTo(input.AsSpan(1_000));
        input[1_000] = 0.2f; // the first sample above it

        block.Looper!.Apply(LooperAction.Record);
        Assert.Equal(LooperState.Armed, block.Looper.State);
        Render(block, input);
        block.Looper.Apply(LooperAction.Play);
        var played = Render(block, new float[Loop]);

        Assert.Equal(Loop, block.Looper.Length);
        Assert.Equal(input[1_000..], played);
    }

    [Fact]
    public void Overdub_level_scales_the_loop_on_every_pass_and_undo_restores_it() {
        using var block = Create((LooperBlock.OverdubLevel, 50f));
        Record(block, Ramp);
        var looper = block.Looper!;

        looper.Apply(LooperAction.Overdub);
        Render(block, new float[Loop]); // one silent pass
        looper.Apply(LooperAction.Play);
        var halved = Render(block, new float[Loop]);
        looper.Apply(LooperAction.Undo);
        var restored = Render(block, new float[Loop]);

        for (var i = 0; i < Loop; i++) {
            Assert.Equal(Ramp[i] * 0.5f, halved[i], 1e-7f);
            Assert.Equal(Ramp[i], restored[i], 1e-7f);
        }
    }

    [Fact]
    public void Overdubs_add_to_the_loop_and_undo_removes_only_the_last_pass() {
        using var block = Create();
        Record(block, Ramp);
        var looper = block.Looper!;

        looper.Apply(LooperAction.Overdub);
        Render(block, Ramp);
        looper.Apply(LooperAction.Overdub); // back to playing
        var doubled = Render(block, new float[Loop]);
        looper.Apply(LooperAction.Overdub);
        Render(block, Ramp);
        looper.Apply(LooperAction.Undo);
        var undone = Render(block, new float[Loop]);

        for (var i = 0; i < Loop; i++) {
            Assert.Equal(2 * Ramp[i], doubled[i], 1e-6f);
            Assert.Equal(2 * Ramp[i], undone[i], 1e-6f);
        }
    }

    [Fact]
    public void Crossfade_carries_the_playing_across_the_loop_seam() {
        static double SeamStep(float crossfadeMs) {
            using var block = Create((LooperBlock.Crossfade, crossfadeMs));
            const int length = 4_850; // 44.6 periods: a hard cut lands mid-cycle
            var sine = Signals.Sine(441, 48_000, 2 * length, 0.5f);
            block.Looper!.Apply(LooperAction.Record);
            Render(block, sine[..length]);
            block.Looper.Apply(LooperAction.Play);

            // The playing goes on through the crossfade, then stops.
            var input = new float[2 * length];
            sine.AsSpan(length, 480).CopyTo(input);
            var played = Render(block, input);
            return Math.Abs(played[length] - played[length - 1]);
        }

        var natural = 0.5 * 2 * Math.PI * 441 / 48_000; // the largest step of the sine itself
        Assert.True(SeamStep(10f) < 1.05 * natural, $"seam step {SeamStep(10f)} with a crossfade");
        Assert.True(SeamStep(0f) > 4 * natural, $"seam step {SeamStep(0f)} without one");
    }

    [Theory]
    [InlineData(LooperAction.Stop, 1f)]
    [InlineData(LooperAction.Record, 2f)] // Record 2nd Press = Stop
    public void A_recording_closed_into_stopped_keeps_its_start_when_played_later(LooperAction close, float press) {
        using var block = Create((LooperBlock.Crossfade, 10f), (LooperBlock.SecondPress, press));
        var looper = block.Looper!;
        looper.Apply(LooperAction.Record);
        Render(block, Ramp);
        looper.Apply(close);
        Assert.Equal(LooperState.Stopped, looper.State);
        Render(block, new float[Loop]); // stopped for a while

        // Unrelated playing while the loop starts: none of it may be faded into the loop's first samples.
        looper.Apply(LooperAction.Play);
        var first = Render(block, [..Enumerable.Repeat(0.4f, Loop)]);
        var second = Render(block, new float[Loop]);

        // Play fades in over 2 ms (96 samples); the 10 ms crossfade would have covered 480.
        Assert.Equal(Ramp[96..], first[96..]);
        Assert.Equal(Ramp, second);
    }

    [Theory]
    [InlineData(26_000, 24_000)] // 120 BPM: a beat is 24 000 samples
    [InlineData(40_000, 48_000)]
    [InlineData(5_000, 24_000)]
    public void Loop_quantize_rounds_the_length_to_whole_beats(int recorded, int expected) {
        using var block = Create((LooperBlock.Quantize, 1f));
        var input = Signals.Noise(recorded, 3, 0.25f);

        Record(block, input);
        var played = Render(block, new float[expected]);

        Assert.Equal(expected, block.Looper!.Length);
        var kept = Math.Min(recorded, expected);
        Assert.Equal(input[..kept], played[..kept]);
        Assert.All(played[kept..], static s => Assert.Equal(0f, s)); // padded with silence
    }

    [Theory]
    [InlineData(2f, 2.0)]
    [InlineData(1f, 0.5)]
    public void Speed_scales_how_fast_the_loop_plays(float speed, double ratio) {
        using var block = Create((LooperBlock.Speed, speed));
        Record(block, Ramp);

        var played = Render(block, new float[1_200]);

        Assert.Equal((999 * ratio + 1) / Loop * 0.5, played[999], 1e-6); // the ramp, interpolated
    }

    [Theory]
    [InlineData(1.0, true, 1f)] // the Half Speed action
    [InlineData(0.5, false, 1f)]
    [InlineData(0.5, true, 1f)] // quarter speed
    [InlineData(2.0, false, 1f)]
    [InlineData(2.0, false, 0.5f)] // the overdub level reaches every sample of the loop too
    public void An_overdub_at_another_speed_is_stored_once_per_loop_sample(double speed, bool half, float level) {
        const int length = 1_000;
        using var looper = new Looper(48_000, 1);
        var settings = LooperSettings.Default with { OverdubLevel = level };
        looper.Configure(settings);
        looper.Apply(LooperAction.Record);
        for (var i = 0; i < length; i++) {
            looper.Tick(0.1f, 0.1f);
        }

        looper.Apply(LooperAction.Play);
        looper.Configure(settings with { Speed = speed });
        if (half) {
            looper.Apply(LooperAction.HalfSpeed);
        }

        // One pass of the loop at this speed, overdubbing a constant.
        looper.Apply(LooperAction.Overdub);
        var ticks = (int)(length / (speed * (half ? 0.5 : 1.0)));
        for (var i = 0; i < ticks; i++) {
            looper.Tick(0.2f, 0.2f);
        }

        looper.Apply(LooperAction.Play);
        Assert.Equal(0, looper.Position);

        // Read back at normal speed: the overdub at the level it was played, on every sample.
        if (half) {
            looper.Apply(LooperAction.HalfSpeed);
        }

        looper.Configure(settings);
        for (var i = 0; i < length; i++) {
            var (left, right) = looper.Tick(0f, 0f);
            Assert.Equal(0.1f * level + 0.2f, left, 1e-6f);
            Assert.Equal(left, right);
        }

        looper.Apply(LooperAction.Undo);
        for (var i = 0; i < length; i++) {
            Assert.Equal(0.1f, looper.Tick(0f, 0f).Left, 1e-6f);
        }
    }

    [Fact]
    public void A_double_speed_overdub_fills_the_loop_samples_between_its_input_samples() {
        const int length = 1_000;
        using var looper = new Looper(48_000, 1);
        looper.Apply(LooperAction.Record);
        for (var i = 0; i < length; i++) {
            looper.Tick(0f, 0f);
        }

        looper.Apply(LooperAction.Play);
        looper.Configure(LooperSettings.Default with { Speed = 2 });
        looper.Apply(LooperAction.Overdub);
        for (var i = 0; i < length / 2; i++) {
            looper.Tick(i * 0.001f, 0f); // a ramp of 0.001 per input sample
        }

        looper.Apply(LooperAction.Play);
        looper.Configure(LooperSettings.Default);
        var played = new float[length];
        for (var i = 0; i < length; i++) {
            played[i] = looper.Tick(0f, 0f).Left;
        }

        // The same ramp at half the slope, with no gaps: every step is half an input step.
        for (var i = 2; i < length; i++) {
            Assert.Equal(0.0005f, played[i] - played[i - 1], 1e-6f);
        }
    }

    [Fact]
    public void Undo_on_a_long_loop_takes_effect_at_once_and_a_new_overdub_can_follow_it() {
        // Long enough that Undo restores it over many slices, pressed where the first slice does not reach.
        const int length = 25 * Looper.SliceSamples - 4_800;
        const int at = 18 * Looper.SliceSamples;
        var loop = Signals.Noise(length, 5, 0.25f);
        var layer = Signals.Noise(length, 6, 0.25f);
        using var looper = new Looper(48_000, 10);
        looper.Configure(LooperSettings.Default with { OverdubLevel = 0.5f });
        looper.Apply(LooperAction.Record);
        foreach (var sample in loop) {
            looper.Tick(sample, sample);
        }

        looper.Apply(LooperAction.Play);
        looper.Apply(LooperAction.Overdub);
        foreach (var sample in layer) {
            looper.Tick(sample, sample);
        }

        looper.Apply(LooperAction.Play);
        for (var i = 0; i < at; i++) {
            Assert.Equal(loop[i] * 0.5f + layer[i], looper.Tick(0f, 0f).Left, 1e-6f);
        }

        // Undo: the very next samples are the loop without the pass.
        looper.Apply(LooperAction.Undo);
        Assert.True(looper.HasPendingWork);
        for (var i = at; i < at + 100; i++) {
            Assert.Equal(loop[i], looper.Tick(0f, 0f).Left, 1e-6f);
        }

        // A new pass straight after it, over the loop's end, lands on restored samples.
        const int resumed = at + 100;
        const int wrapped = 10_000;
        looper.Apply(LooperAction.Overdub);
        for (var i = resumed; i < length + wrapped; i++) {
            Assert.Equal(loop[i % length], looper.Tick(0.25f, 0.25f).Right, 1e-6f);
        }

        looper.Apply(LooperAction.Play);
        for (var i = wrapped; i < length + wrapped; i++) {
            var index = i % length;
            var expected = index is >= resumed or < wrapped ? loop[index] * 0.5f + 0.25f : loop[index];
            Assert.Equal(expected, looper.Tick(0f, 0f).Left, 1e-6f);
        }

        looper.Apply(LooperAction.Undo);
        for (var i = wrapped; i < length + wrapped; i++) {
            Assert.Equal(loop[i % length], looper.Tick(0f, 0f).Left, 1e-6f);
        }

        Assert.False(looper.HasPendingWork);
    }

    [Fact]
    public void Erasing_a_long_loop_leaves_none_of_it_in_the_next_recording() {
        const int length = 20 * Looper.SliceSamples;
        var old = Signals.Noise(length, 7, 0.25f);
        using var looper = new Looper(48_000, 10);

        void RecordOld() {
            looper.Apply(LooperAction.Record);
            foreach (var sample in old) {
                looper.Tick(sample, sample);
            }

            looper.Apply(LooperAction.Overdub); // the layer holds samples too
            foreach (var sample in old) {
                looper.Tick(sample, sample);
            }
        }

        // A shorter recording started on the sample after the erase.
        var next = Signals.Noise(30_000, 8, 0.25f);
        RecordOld();
        looper.Apply(LooperAction.Clear);
        Assert.Equal(LooperState.Empty, looper.State);
        Assert.Equal(0, looper.Length);
        looper.Apply(LooperAction.Record);
        foreach (var sample in next) {
            looper.Tick(sample, sample);
        }

        looper.Apply(LooperAction.Play);
        for (var i = 0; i < 2 * next.Length; i++) {
            var (left, right) = looper.Tick(0f, 0f);
            Assert.Equal(next[i % next.Length], left);
            Assert.Equal(left, right);
        }

        // A few samples that Loop Quantize pads to a beat, past where the erase has got to: the padding is silent.
        looper.Apply(LooperAction.Clear);
        RecordOld();
        looper.Apply(LooperAction.Clear);
        looper.Configure(LooperSettings.Default with { BeatSamples = 24_000 });
        looper.Apply(LooperAction.Record);
        for (var i = 0; i < 10; i++) {
            looper.Tick(0.5f, 0.5f);
        }

        looper.Apply(LooperAction.Play);
        Assert.Equal(24_000, looper.Length);
        for (var i = 0; i < 24_000; i++) {
            Assert.Equal(i < 10 ? 0.5f : 0f, looper.Tick(0f, 0f).Left);
        }

        // Erased twice in a row, the second before the first is done, then a longer loop than any before.
        looper.Configure(LooperSettings.Default);
        looper.Apply(LooperAction.Clear);
        RecordOld();
        looper.Apply(LooperAction.Clear);
        looper.Apply(LooperAction.Record);
        looper.Tick(0.5f, 0.5f);
        looper.Apply(LooperAction.Clear);
        var longer = Signals.Noise(length + 20_000, 9, 0.25f);
        looper.Apply(LooperAction.Record);
        foreach (var sample in longer) {
            looper.Tick(sample, sample);
        }

        looper.Apply(LooperAction.Play);
        foreach (var sample in longer) {
            Assert.Equal(sample, looper.Tick(0f, 0f).Left);
        }
    }

    [Fact]
    public void An_erase_finishes_while_the_looper_stays_empty() {
        const int length = 20 * Looper.SliceSamples;
        using var looper = new Looper(48_000, 10);
        looper.Apply(LooperAction.Record);
        for (var i = 0; i < length; i++) {
            looper.Tick(0.5f, 0.5f);
        }

        looper.Apply(LooperAction.Clear);
        Assert.True(looper.HasPendingWork);
        var silence = new float[64];
        for (var block = 0; block < 20; block++) {
            looper.Process(silence, silence); // empty: nothing ticks, the slices still run
        }

        Assert.False(looper.HasPendingWork);

        // Loop Quantize pads a one-sample recording to the old loop's length: all of it is silent by now.
        looper.Configure(LooperSettings.Default with { BeatSamples = length });
        looper.Apply(LooperAction.Record);
        looper.Tick(0.25f, 0.25f);
        looper.Apply(LooperAction.Play);
        Assert.Equal(length, looper.Length);
        Assert.Equal(0.25f, looper.Tick(0f, 0f).Left);
        for (var i = 1; i < length; i++) {
            Assert.Equal(0f, looper.Tick(0f, 0f).Left);
        }
    }

    /// <summary>
    ///     The cost of the callback an Erase or an Undo arrives in, on a loop of the full
    ///     <see cref="Looper.MaxSeconds" /> with an overdub pass over all of it. Done in one go, the Erase zeroes
    ///     230 MB and the Undo walks 14 million samples, tens of milliseconds either way, where a 64-sample callback
    ///     has 1.3 ms. Each must return within a fraction of that and leave the rest to the slices, which must be as
    ///     cheap. The fastest of three runs counts, as another process can take the core during any one. (An Undo
    ///     pressed while the Undo before it is still being restored finishes that one first, in one go: see
    ///     <see cref="Looper" />. It is not measured here.)
    /// </summary>
    [Theory]
    [InlineData(LooperAction.Clear)]
    [InlineData(LooperAction.Undo)]
    public void An_erase_or_an_undo_of_a_full_length_loop_fits_in_a_callback(LooperAction action) {
        const double budgetMs = 0.25;
        using var looper = new Looper(48_000, Looper.MaxSeconds);
        looper.Configure(LooperSettings.Default with { MaxSamples = looper.Capacity });
        var left = new float[64];
        var right = new float[64];
        var pressed = double.MaxValue;
        var slices = double.MaxValue;
        for (var run = 0; run < 3; run++) {
            looper.Apply(LooperAction.Record);
            while (looper.State == LooperState.Recording) {
                Fill();
                looper.Process(left, right);
            }

            looper.Apply(LooperAction.Overdub);
            for (var done = 0; done < looper.Capacity; done += left.Length) {
                Fill();
                looper.Process(left, right);
            }

            Assert.Equal(looper.Capacity, looper.Length);
            var start = Stopwatch.GetTimestamp();
            looper.Apply(action);
            looper.Process(left, right);
            pressed = Math.Min(pressed, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            // The rest of the work is spread over many callbacks, each as cheap. Their 99th percentile counts: on a
            // busy machine a few of some two thousand callbacks lose the core for milliseconds.
            var times = new List<double>();
            while (looper.HasPendingWork) {
                start = Stopwatch.GetTimestamp();
                looper.Process(left, right);
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            Assert.True(
                times.Count >= looper.Capacity / Looper.SliceSamples - 1,
                $"the pending work took {times.Count} callbacks"
            );
            times.Sort();
            slices = Math.Min(slices, times[times.Count * 99 / 100]);
            looper.Apply(LooperAction.Clear);
            while (looper.HasPendingWork) {
                looper.Process(left, right);
            }
        }

        // Measured on an M-series Mac: 7 µs for the Erase and 15 µs for the Undo.
        Assert.True(
            pressed < budgetMs && slices < budgetMs,
            FormattableString.Invariant(
                $"{action} took {pressed:0.000} ms in its callback and {slices:0.000} ms in a slice of the work after"
            )
        );

        void Fill() {
            Array.Fill(left, 0.25f);
            Array.Fill(right, 0.25f);
        }
    }

    [Theory]
    [InlineData(0f, LooperState.Overdubbing)]
    [InlineData(1f, LooperState.Playing)]
    [InlineData(2f, LooperState.Stopped)]
    public void A_second_record_press_follows_its_setting(float mode, LooperState expected) {
        using var block = Create((LooperBlock.SecondPress, mode));
        block.Looper!.Apply(LooperAction.Record);
        Render(block, Ramp);

        block.Looper.Apply(LooperAction.Record);

        Assert.Equal(expected, block.Looper.State);
        Assert.Equal(Loop, block.Looper.Length);
    }

    [Fact]
    public void Max_loop_time_closes_the_recording_into_playback() {
        using var block = Create((LooperBlock.MaxTime, 1f));
        block.Looper!.Apply(LooperAction.Record);

        Render(block, Signals.Noise(60_000, 4, 0.25f));

        Assert.Equal(48_000, block.Looper.Length);
        Assert.Equal(LooperState.Playing, block.Looper.State);
    }

    [Fact]
    public void Dry_and_playback_levels_mix_the_input_and_the_loop() {
        using var block = Create((LooperBlock.DryLevel, -6.0206f), (LooperBlock.PlaybackLevel, 6.0206f));
        Record(block, Ramp);
        var input = Enumerable.Repeat(0.1f, Loop).ToArray();

        var output = Render(block, input);

        Assert.Equal(0.05f + 2 * Ramp[^1], output[^1], 1e-4f);
    }

    [Fact]
    public void Reset_empties_the_loop() {
        using var block = Create();
        Record(block, Ramp);

        block.Reset();

        Assert.Equal(LooperState.Empty, block.Looper!.State);
        Assert.Equal(0, block.Looper.Length);
        Assert.All(Render(block, new float[Loop]), static s => Assert.Equal(0f, s));
    }

    /// <summary>A looper with the dry signal off and no seam crossfade, so the output is the loop alone.</summary>
    static LooperBlock Create(params (int Index, float Value)[] settings) {
        var block = new LooperBlock();
        block.Params.SetTarget(LooperBlock.DryLevel, Decibels.SilenceDb);
        block.Params.SetTarget(LooperBlock.Crossfade, 0f);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(48_000, 64));
        return block;
    }

    static void Record(LooperBlock block, float[] input) {
        block.Looper!.Apply(LooperAction.Record);
        Render(block, input);
        block.Looper.Apply(LooperAction.Play);
    }

    static float[] Render(IDspBlock block, float[] input) => BlockRender.Mono(block, input, 64);
}
