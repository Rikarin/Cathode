using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Testing;

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
