using Cathode.Dsp.Blocks;
using Cathode.Dsp.Pitch;

namespace Cathode.Dsp.Tests;

/// <summary>The Pitch block's Learn and custom scale (docs/08-blocks-parity.md §6.22).</summary>
public class PitchLearnAndCustomScaleTests {
    const double Rate = 48_000;

    [Fact]
    public void Learn_latches_the_played_note_as_the_key_and_keeps_it_after_learning() {
        using var harmonizer = Diatonic((PitchBlock.Learn, 1f));

        BlockRender.Mono(harmonizer, Tone(62, 12_000), 64); // D
        Assert.Equal(2, harmonizer.HarmonyKey);

        harmonizer.Params.SetTarget(PitchBlock.Learn, 0f);
        BlockRender.Mono(harmonizer, Tone(67, 12_000), 64); // G, played after Learn is off
        Assert.Equal(2, harmonizer.HarmonyKey);

        harmonizer.Params.SetTarget(PitchBlock.Key, 5f); // moving Key ends the learned key
        BlockRender.Mono(harmonizer, Tone(67, 12_000), 64);
        Assert.Equal(5, harmonizer.HarmonyKey);
    }

    [Fact]
    public void Without_learn_the_key_parameter_rules() {
        using var harmonizer = Diatonic();

        BlockRender.Mono(harmonizer, Tone(62, 12_000), 64);

        Assert.Equal(0, harmonizer.HarmonyKey);
    }

    [Fact]
    public void A_custom_scale_harmonises_in_its_own_degrees() {
        // A four-note scale C, D#, G, A# (1, b3, 5, b7): a "third" above C is D#, a minor third, where C major gives E.
        using var custom = Diatonic(
            (PitchBlock.Scale, Scales.Custom),
            (PitchBlock.CustomNoteCount, 0f), // 4 notes
            (PitchBlock.CustomNote1, 0f),
            (PitchBlock.CustomNote1 + 1, 3f),
            (PitchBlock.CustomNote1 + 2, 7f),
            (PitchBlock.CustomNote1 + 3, 10f),
            (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 1f) // one degree up
        );

        var output = BlockRender.Mono(custom, Tone(60, 24_000), 64)[12_000..];

        var minorThird = PitchBlockTests.Level(output, Frequency(63));
        var majorThird = PitchBlockTests.Level(output, Frequency(64));
        Assert.True(minorThird > 0.05 && minorThird > 10 * majorThird, $"{minorThird:0.000} vs {majorThird:0.000}");
    }

    [Fact]
    public void Custom_degrees_are_rising_unique_and_rooted() {
        Span<int> degrees = stackalloc int[12];

        var count = Scales.CustomDegrees([7, 3, 3, 10, 1], 4, degrees);

        Assert.Equal([0, 3, 7, 10], degrees[..count].ToArray());
        Assert.True(Scales.IsMinor(degrees[..count]));
        Assert.Equal(4, Scales.HarmonySemitones(60, 0, [0, 4, 7], 1)); // C to E in a triad scale
        Assert.Equal(12, Scales.HarmonySemitones(60, 0, [0, 4, 7], 3)); // three degrees up: the octave
    }

    [Fact]
    public void The_default_custom_scale_is_the_major_scale() {
        Span<int> degrees = stackalloc int[12];
        int[] notes = [
            ..Enumerable.Range(0, PitchBlock.MaxCustomNotes)
                .Select(static n => (int)PitchBlock.Info.Params[PitchBlock.CustomNote1 + n].Default)
        ];
        var count = 4 + (int)PitchBlock.Info.Params[PitchBlock.CustomNoteCount].Default;

        var length = Scales.CustomDegrees(notes, count, degrees);

        Assert.Equal([0, 2, 4, 5, 7, 9, 11], degrees[..length].ToArray());
    }

    static PitchBlock Diatonic(params (int Index, float Value)[] settings) {
        var block = new PitchBlock();
        foreach (var (index, value) in (ReadOnlySpan<(int, float)>) [
                     (PitchBlock.Type, (float)PitchType.Diatonic),
                     (PitchBlock.Key, 0f),
                     (PitchBlock.Scale, 0f),
                     (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 2f),
                     (PitchBlock.Voice2Level, Decibels.SilenceDb),
                     (PitchBlock.Voice1Pan, 0f),
                     (PitchBlock.Mix, 100f)
                 ]) {
            block.Params.SetTarget(index, value);
        }

        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    static float[] Tone(int note, int length) => PitchTrackerTests.HarmonicTone(Frequency(note), length);

    static double Frequency(int note) => 440 * Math.Pow(2, (note - 69) / 12.0);
}
