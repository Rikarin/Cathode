using Cathode.Dsp.Blocks;
using Cathode.Dsp.Pitch;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class PitchBlockTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    public static TheoryData<int> Types => [..Enum.GetValues<PitchType>().Select(static t => (int)t)];

    [Theory]
    [InlineData(12, 440.0)]
    [InlineData(-12, 110.0)]
    [InlineData(7, 329.63)]
    public void Capo_moves_the_pitch_and_removes_the_dry_signal(int semitones, double expected) {
        using var capo = Create(
            (PitchBlock.Type, (float)PitchType.VirtualCapo),
            (PitchBlock.Capo, semitones + 12f),
            (PitchBlock.Mix, 0f) // ignored: the capo is always fully wet
        );

        var shifted = BlockRender.Mono(capo, Signals.Sine(220, Rate, 48_000, 0.3f), 64)[24_000..];

        Assert.Equal(expected, PeakFrequency(shifted), expected * 0.01);
        Assert.True(Level(shifted, expected) > 0.25, $"shifted tone at {Level(shifted, expected):0.000}");
        Assert.True(Level(shifted, 220) < 0.01 * Level(shifted, expected), "dry pitch is still present");
    }

    [Theory]
    [InlineData(0f, 0.0)]
    [InlineData(25f, 3.0)]
    [InlineData(50f, 6.0)]
    [InlineData(100f, 12.0)]
    public void Whammy_pedal_interpolates_between_start_and_end(float pedal, double semitones) {
        using var whammy = Create(
            (PitchBlock.Type, (float)PitchType.Whammy),
            (PitchBlock.WhammyStart, 0f),
            (PitchBlock.WhammyEnd, 12f),
            (PitchBlock.Pedal, pedal)
        );

        var shifted = BlockRender.Mono(whammy, Signals.Sine(220, Rate, 48_000, 0.3f), 64)[24_000..];

        var expected = 220 * Math.Pow(2, semitones / 12);
        Assert.Equal(expected, PeakFrequency(shifted), expected * 0.01);
    }

    [Fact]
    public void Whammy_follows_a_pedal_sweep_driven_mid_stream() {
        using var whammy = Create((PitchBlock.Type, (float)PitchType.Whammy), (PitchBlock.WhammyEnd, -12f));
        var input = Signals.Sine(330, Rate, 24_000, 0.3f);

        var heel = BlockRender.Mono(whammy, input, 64)[12_000..];
        whammy.Params.SetTarget(PitchBlock.Pedal, 100f);
        var toe = BlockRender.Mono(whammy, input, 64)[12_000..];

        Assert.Equal(330, PeakFrequency(heel), 3.3);
        Assert.Equal(165, PeakFrequency(toe), 1.65);
    }

    [Theory]
    [InlineData(100f, 0f, 55.0)]
    [InlineData(0f, 100f, 27.5)]
    public void Octave_divider_adds_the_sub_octaves(float octave1, float octave2, double expected) {
        using var divider = Create(
            (PitchBlock.Type, (float)PitchType.OctaveDivider),
            (PitchBlock.Octave1Level, octave1),
            (PitchBlock.Octave2Level, octave2),
            (PitchBlock.Mix, 50f)
        );

        var mixed = BlockRender.Mono(divider, Signals.Sine(110, Rate, 48_000, 0.3f), 64)[12_000..];

        output.WriteLine($"sub {Level(mixed, expected):0.0000}, dry {Level(mixed, 110):0.0000}");
        Assert.True(Level(mixed, expected) > 0.03, $"sub-octave level {Level(mixed, expected):0.000}");
        Assert.Equal(0.15, Level(mixed, 110), 0.03); // the dry note is still there, at half level
    }

    [Theory]
    [InlineData(0, 261.63, 277.18)] // C major: a third above A is C
    [InlineData(9, 277.18, 261.63)] // A major: a third above A is C#
    public void Diatonic_harmony_picks_the_interval_for_the_key(int key, double expected, double wrong) {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.Key, key),
            (PitchBlock.Scale, 0f),
            (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 2f),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f)
        );

        var harmony = BlockRender.Mono(
            harmonizer,
            PitchTrackerTests.HarmonicTone(220, 96_000),
            64
        )[24_000..];

        output.WriteLine($"expected {Level(harmony, expected):0.0000}, wrong {Level(harmony, wrong):0.0000}");
        Assert.True(Level(harmony, expected) > 0.05, "no harmony voice");
        Assert.True(Level(harmony, expected) > 10 * Level(harmony, wrong), "wrong interval");
        Assert.True(Level(harmony, 220) < 0.1 * Level(harmony, expected), "the voice still sounds the input pitch");
    }

    [Theory]
    [InlineData(7.0)]
    [InlineData(12.0)]
    [InlineData(-5.0)]
    public void Chromatic_voice_shifts_a_harmonic_tone_by_fixed_semitones(double semitones) {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Chromatic),
            (PitchBlock.Voice1Shift, (float)semitones),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f)
        );

        var harmony = BlockRender.Mono(
            harmonizer,
            PitchTrackerTests.HarmonicTone(196, 96_000),
            64
        )[24_000..];

        // PSOLA keeps the spectral envelope, so check the new harmonic series rather than a single peak.
        var shifted = 196 * Math.Pow(2, semitones / 12);
        var onSeries = Level(harmony, shifted) + Level(harmony, 2 * shifted) + Level(harmony, 3 * shifted);
        var original = Level(harmony, 196) + Level(harmony, 5 * 196); // 3 x 196 Hz is on the series of a fifth
        output.WriteLine($"shifted series {onSeries:0.0000}, original series {original:0.0000}");
        Assert.True(onSeries > 0.1, "no shifted voice");
        Assert.True(onSeries > 10 * original, "the voice still sounds the input pitch");
    }

    [Fact]
    public void Crystals_repeat_reversed_an_octave_up_after_the_delay_time() {
        using var crystals = Create(
            (PitchBlock.Type, (float)PitchType.Crystals),
            (PitchBlock.CrystalsTime, 200f),
            (PitchBlock.CrystalsFeedback, 0f),
            (PitchBlock.Mix, 100f)
        );
        var input = Signals.Concat(Signals.Sine(220, Rate, 24_000, 0.3f), new float[48_000]);

        var wet = BlockRender.Mono(crystals, input, 64);

        Assert.True(Analysis.Peak(wet.AsSpan(0, 9_000)) < 1e-6, "wet output before the delay time");
        var repeats = wet[14_400..24_000];
        Assert.Equal(440, PeakFrequency(repeats), 4.4);
        Assert.True(Analysis.Peak(wet.AsSpan(48_000)) < 1e-6, "no feedback, so nothing after the last repeat");
    }

    [Fact]
    public void Arpeggiator_steps_through_the_sequence_in_time() {
        using var arpeggiator = Create(
            (PitchBlock.Type, (float)PitchType.Arpeggiator),
            (PitchBlock.ArpTempo, 3f), // 1/16 at 120 BPM: 125 ms, 6000 samples per step
            (PitchBlock.ArpSteps, 1f), // two steps
            (PitchBlock.ArpStep1, 0f),
            (PitchBlock.ArpStep1 + 1, 12f),
            (PitchBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(arpeggiator, Signals.Sine(220, Rate, 48_000, 0.3f), 64);

        for (var step = 2; step < 8; step++) {
            var expected = step % 2 == 0 ? 220.0 : 440.0;
            var segment = output[(step * 6_000 + 1_500)..(step * 6_000 + 5_500)];
            Assert.Equal(expected, PeakFrequency(segment), expected * 0.03);
        }
    }

    [Fact]
    public void Detune_voices_sit_either_side_of_the_input() {
        using var detune = Create(
            (PitchBlock.Type, (float)PitchType.Detune),
            (PitchBlock.Detune, 50f),
            (PitchBlock.Detune2, -50f),
            (PitchBlock.Mix, 100f)
        );

        var (left, right) = BlockRender.Stereo(
            detune,
            Signals.Sine(440, Rate, 96_000, 0.3f),
            Signals.Sine(440, Rate, 96_000, 0.3f),
            static _ => 64
        );

        // Voice 1 (+50 cents) is panned left, voice 2 (-50 cents) right.
        Assert.Equal(440 * Math.Pow(2, 0.5 / 12), PeakFrequency(left.AsSpan(48_000)), 2.0);
        Assert.Equal(440 * Math.Pow(2, -0.5 / 12), PeakFrequency(right.AsSpan(48_000)), 2.0);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_type_is_block_size_independent_deterministic_and_decays(int type) {
        var input = Signals.Concat(PitchTrackerTests.HarmonicTone(147, 9_600), Signals.Noise(4_800, 2, 0.5f));
        using var fixedSize = Create((PitchBlock.Type, type));
        using var variableSize = Create((PitchBlock.Type, type));
        var random = new Random(9);

        var reference = BlockRender.Mono(fixedSize, input, 64);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, 65)).Left;
        Assert.True(Analysis.NullResidualDb(reference, varied) < -140, "output depends on the block size");

        fixedSize.Reset();
        Assert.Equal(reference, BlockRender.Mono(fixedSize, input, 64));

        var tail = BlockRender.Mono(fixedSize, new float[48_000 * 5], 64);
        Assert.False(Analysis.AnySubnormal(tail), "subnormal samples");
        Assert.True(Analysis.Peak(tail.AsSpan(tail.Length - 4_800)) < 1e-6, "tail did not decay");
    }

    [Theory]
    [InlineData(57, 0, 0, 2, 3)] // A, C major, a third up: C
    [InlineData(57, 9, 0, 2, 4)] // A, A major: C#
    [InlineData(59, 0, 0, 2, 3)] // B, C major: D
    [InlineData(60, 0, 0, 4, 7)] // C, C major, a fifth up: G
    [InlineData(64, 0, 0, -2, -4)] // E, C major, a third down: C
    [InlineData(61, 0, 0, 2, 4)] // C# is out of C major: it harmonises as C, a major third up
    [InlineData(57, 9, 10, 1, 3)] // A, A minor pentatonic, one step up: C
    [InlineData(57, 0, 0, 7, 12)] // an octave is seven steps of a seven-note scale
    public void Diatonic_intervals_follow_the_scale(int note, int key, int scale, int steps, int semitones) {
        Assert.Equal(semitones, Scales.HarmonySemitones(note, key, scale, steps));
    }

    [Fact]
    public void Parameter_constants_match_the_descriptor() {
        var info = PitchBlock.Info;
        Assert.Equal(PitchBlock.Capo, info.IndexOf("capo"));
        Assert.Equal(PitchBlock.ArpStep1 + PitchBlock.MaxArpSteps - 1, info.IndexOf("arpStep16"));
        Assert.Equal(PitchBlock.Voice3Shift, info.IndexOf("voice3Shift"));
        Assert.Equal(PitchBlock.Voice4Delay, info.IndexOf("voice4Delay"));
        Assert.Equal(PitchBlock.KeySource, info.IndexOf("keySource"));
        Assert.Equal(PitchBlock.CustomTable, info.IndexOf("custom1C"));
        Assert.Equal(PitchBlock.CustomTable + 13, info.IndexOf("custom2Cs"));
        Assert.Equal(PitchBlock.CustomTable + 47, info.IndexOf("custom4B"));
        Assert.Equal(PitchBlock.CustomTable + 48, PitchBlock.Balance);
        Assert.Equal(PitchBlock.Detune, info.IndexOf("detune1"));
        Assert.Equal(PitchBlock.Detune4, info.IndexOf("detune4"));
        Assert.Equal(PitchBlock.MasterDelay, info.IndexOf("masterDelay"));
        Assert.Equal(PitchBlock.Feedback1 + 3, info.IndexOf("feedback4"));
        Assert.Equal(PitchBlock.DelayTempo1 + 3, info.IndexOf("delayTempo4"));
        Assert.Equal(PitchBlock.Glide, info.IndexOf("glide"));
        Assert.Equal(PitchBlock.CrystalsFeedbackType, info.IndexOf("crystalsFeedbackType"));
        Assert.Equal(PitchBlock.DetectorSource, info.IndexOf("detectorSource"));
        Assert.Equal(PitchBlock.GlobalMix, info.IndexOf("globalMix"));
        Assert.Equal(PitchBlock.InputSelect, info.IndexOf("inputSelect"));
        Assert.Equal(PitchBlock.Learn, info.IndexOf("learn"));
        Assert.Equal(PitchBlock.CustomTonic, info.IndexOf("customTonic"));
        Assert.Equal(PitchBlock.CustomNote1 + PitchBlock.MaxCustomNotes - 1, info.IndexOf("customNote8"));
        Assert.Equal(PitchBlock.KillDry, info.IndexOf("killDry"));
        Assert.Equal(PitchBlock.KillDry + 1, PitchBlock.FormantCorrection);
        Assert.Equal(PitchBlock.FormantShift1 + PitchBlock.VoiceCount, info.Params.Count);
        Assert.Equal("Virtual Capo", info.Params[PitchBlock.Type].Options![(int)PitchType.VirtualCapo]);
        Assert.Equal("Custom Shifter", info.Params[PitchBlock.Type].Options![(int)PitchType.CustomShifter]);

        // New voices start silent, so a preset saved before them sounds the same.
        Assert.Equal(Decibels.SilenceDb, info.Params[PitchBlock.Voice3Level].Default);
        Assert.Equal(Decibels.SilenceDb, info.Params[PitchBlock.Voice4Level].Default);
        Assert.Equal((float)PitchKeySource.Fixed, info.Params[PitchBlock.KeySource].Default);
    }

    [Fact]
    public void Custom_tables_default_to_major_key_harmony() {
        var table = Enumerable.Range(0, 48)
            .Select(static i => PitchBlock.Info.Params[PitchBlock.CustomTable + i].Default)
            .ToArray();

        Assert.Equal((float[])[4, 4, 3, 3, 3, 4, 4, 4, 4, 3, 3, 3], table[..12]); // a third: C-E, D-F, E-G, ...
        Assert.Equal((float[])[7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 6], table[12..24]); // a fifth, B-F diminished
        Assert.All(table[24..36], static s => Assert.Equal(12f, s));
        Assert.All(table[36..], static s => Assert.Equal(-12f, s));
    }

    [Fact]
    public void Four_diatonic_voices_sound_their_intervals() {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.Key, 0f),
            (PitchBlock.Scale, 0f),
            (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 2f), // a third up: C
            (PitchBlock.Voice2Harmony, PitchBlock.MaxHarmonySteps + 4f), // a fifth up: E
            (PitchBlock.Voice3Harmony, PitchBlock.MaxHarmonySteps + 7f), // an octave up: A
            (PitchBlock.Voice4Harmony, PitchBlock.MaxHarmonySteps - 2f), // a third down: F
            (PitchBlock.Voice3Level, 0f),
            (PitchBlock.Voice4Level, 0f),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Voice2Pan, 0f),
            (PitchBlock.Voice3Pan, 0f),
            (PitchBlock.Voice4Pan, 0f),
            (PitchBlock.Mix, 100f)
        );

        var harmony = BlockRender.Mono(harmonizer, PitchTrackerTests.HarmonicTone(220, 96_000), 64)[24_000..];

        double[] expected = [261.63, 329.63, 440.0, 174.61];
        foreach (var frequency in expected) {
            output.WriteLine($"{frequency} Hz: {Level(harmony, frequency):0.0000}");
            Assert.True(Level(harmony, frequency) > 0.03, $"no voice at {frequency} Hz");
        }

        output.WriteLine($"input pitch: {Level(harmony, 220):0.0000}");
        Assert.True(Level(harmony, 220) < 0.1 * expected.Min(f => Level(harmony, f)), "the input pitch is still there");
    }

    [Fact]
    public void A_silent_voice_is_skipped_and_comes_back_clean_when_raised() {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Chromatic),
            (PitchBlock.Voice1Level, Decibels.SilenceDb),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice3Shift, 7f),
            (PitchBlock.Voice3Pan, 0f),
            (PitchBlock.Mix, 100f)
        );
        var input = PitchTrackerTests.HarmonicTone(196, 48_000);

        var silent = BlockRender.Mono(harmonizer, input, 64);
        harmonizer.Params.SetTarget(PitchBlock.Voice3Level, 0f);
        var raised = BlockRender.Mono(harmonizer, input, 64)[12_000..];

        Assert.Equal(0, Analysis.Peak(silent));
        var shifted = 196 * Math.Pow(2, 7 / 12.0);
        var onSeries = Level(raised, shifted) + Level(raised, 2 * shifted) + Level(raised, 3 * shifted);
        var original = Level(raised, 196) + Level(raised, 5 * 196);
        output.WriteLine($"shifted series {onSeries:0.0000}, original series {original:0.0000}");
        Assert.True(onSeries > 0.1, "voice 3 did not come in");
        Assert.True(onSeries > 10 * original, "voice 3 is not a fifth up");
    }

    [Theory]
    [InlineData(9, 7, 1f, 349.23)] // key A: E is the key's fifth (the "G" entry), set to +1: F
    [InlineData(0, 4, -4f, 261.63)] // key C: E is the key's third (the "E" entry), set to -4: C
    public void Custom_shifter_maps_each_note_of_the_key_through_the_table(
        int key,
        int degree,
        float shift,
        double expected
    ) {
        using var shifter = Create(
            (PitchBlock.Type, (float)PitchType.CustomShifter),
            (PitchBlock.Key, key),
            (PitchBlock.CustomTable + degree, shift),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f)
        );

        var harmony = BlockRender.Mono(shifter, PitchTrackerTests.HarmonicTone(329.63, 96_000), 64)[24_000..];

        // The default for this note (a diatonic third up) would be G, 392 Hz.
        output.WriteLine($"expected {Level(harmony, expected):0.0000}, default {Level(harmony, 392):0.0000}");
        Assert.True(Level(harmony, expected) > 0.05, "no shifted voice");
        Assert.True(Level(harmony, expected) > 10 * Level(harmony, 392), "the table entry was not used");
        Assert.True(Level(harmony, expected) > 10 * Level(harmony, 329.63), "the voice still sounds the input pitch");
    }

    [Theory]
    [InlineData(new[] { 52, 56, 59 }, 415.30, 392.0)] // E major held: a third above E is G#, not C major's G
    [InlineData(new[] { 57, 60, 64 }, 440.0, 392.0)] // A minor: G is no chord tone, so the voice snaps to A
    public void Midi_key_source_follows_the_held_chord(int[] chord, double expected, double wrong) {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.Key, 0f),
            (PitchBlock.Scale, 0f),
            (PitchBlock.KeySource, (float)PitchKeySource.Midi),
            (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 2f),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f)
        );
        var input = PitchTrackerTests.HarmonicTone(329.63, 96_000);

        var held = BlockRender.Mono(harmonizer, input, 64, HeldNotes.Of(chord))[24_000..];
        var released = BlockRender.Mono(harmonizer, input, 64)[24_000..]; // the last chord stays

        Assert.Equal(chord[0] % 12, harmonizer.HarmonyKey);
        foreach (var harmony in new[] { held, released }) {
            output.WriteLine($"expected {Level(harmony, expected):0.0000}, wrong {Level(harmony, wrong):0.0000}");
            Assert.True(Level(harmony, expected) > 0.05, "no harmony voice");
            Assert.True(Level(harmony, expected) > 10 * Level(harmony, wrong), "the voice ignores the chord");
        }
    }

    [Fact]
    public void Auto_key_source_finds_the_key_of_a_melody_and_harmonises_in_it() {
        using var harmonizer = Create(
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.Key, 0f), // C major, which the melody is not in
            (PitchBlock.Scale, 0f),
            (PitchBlock.KeySource, (float)PitchKeySource.Auto),
            (PitchBlock.Voice1Harmony, PitchBlock.MaxHarmonySteps + 2f),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f)
        );

        // A melody in A major, a quarter of a second per note.
        int[] notes = [69, 71, 73, 74, 76, 78, 80, 81, 76, 73, 69, 74, 73, 71, 69, 76, 73, 69];
        var melody = Signals.Concat([..notes.Select(static n => PitchTrackerTests.HarmonicTone(Frequency(n), 12_000))]);
        BlockRender.Mono(harmonizer, melody, 64);

        output.WriteLine($"key {Scales.Keys[harmonizer.HarmonyKey]} {Scales.Names[harmonizer.HarmonyScale]}");
        Assert.Equal(9, harmonizer.HarmonyKey);
        Assert.False(Scales.IsMinor(harmonizer.HarmonyScale));

        // C# in A major: a third up is E. Fixed to C major it would harmonise as C, a major third up: F.
        var harmony = BlockRender.Mono(harmonizer, PitchTrackerTests.HarmonicTone(277.18, 48_000), 64)[12_000..];
        output.WriteLine($"E {Level(harmony, 329.63):0.0000}, F {Level(harmony, 349.23):0.0000}");
        Assert.True(Level(harmony, 329.63) > 10 * Level(harmony, 349.23), "the voice is not in A major");
    }

    [Theory]
    [InlineData((int)PitchKeySource.Midi)]
    [InlineData((int)PitchKeySource.Auto)]
    public void Key_sources_are_block_size_independent_and_deterministic(int source) {
        int[] melody = [57, 59, 61, 62, 64, 57, 61, 64];
        var input = Signals.Concat([..melody.Select(static n => PitchTrackerTests.HarmonicTone(Frequency(n), 9_600))]);
        var notes = HeldNotes.Of(57, 61, 64);
        (int, float)[] settings = [
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.KeySource, source),
            (PitchBlock.Voice3Level, 0f),
            (PitchBlock.Voice4Level, -6f)
        ];
        using var fixedSize = Create(settings);
        using var variableSize = Create(settings);
        var random = new Random(3);

        var reference = BlockRender.Mono(fixedSize, input, 64, notes);
        var varied = BlockRender.Stereo(variableSize, input, input, _ => random.Next(1, 65), notes: notes).Left;
        Assert.True(Analysis.NullResidualDb(reference, varied) < -140, "output depends on the block size");

        fixedSize.Reset();
        Assert.Equal(reference, BlockRender.Mono(fixedSize, input, 64, notes));
    }

    [Theory]
    [InlineData(9, new[] { 9, 0, 4 }, 0, 1)] // A minor over Major: Minor
    [InlineData(7, new[] { 7, 11, 2, 5 }, 0, 5)] // G7: Mixolydian
    [InlineData(0, new[] { 0, 4, 7 }, 4, 4)] // C major in Lydian: Lydian holds it
    [InlineData(0, new[] { 0, 1, 2, 3 }, 0, 0)] // a cluster no scale holds: the preferred scale
    public void Chord_scale_is_the_first_that_holds_every_chord_tone(int root, int[] tones, int preferred, int scale) {
        var chord = tones.Aggregate(0, static (mask, t) => mask | (1 << t));
        Assert.Equal(scale, Scales.ChordScale(root, chord, preferred));
    }

    [Theory]
    [InlineData(64, 2, 3)] // E, a third up: G is a chord tone
    [InlineData(62, 2, 5)] // D, a third up: F is not; E would rub a tone against D, so G
    [InlineData(64, -2, -4)] // E, a third down: C
    [InlineData(65, -2, -5)] // F, a third down: D is not a chord tone, E too close, so C
    [InlineData(62, 7, 12)] // octaves never snap
    public void Harmony_snaps_to_chord_tones(int note, int steps, int semitones) {
        var cMajor = (1 << 0) | (1 << 4) | (1 << 7);
        Assert.Equal(semitones, Scales.HarmonySemitones(note, 0, 0, steps, cMajor));
    }

    [Fact]
    public void Key_estimate_needs_a_clear_change_to_move() {
        var keys = new KeyEstimator();
        int[] cMajor = [0, 2, 4, 5, 7, 9, 11, 0, 4, 7];
        int[] gMajor = [7, 9, 11, 0, 2, 4, 6, 7, 11, 2];
        int[] aMinor = [9, 11, 0, 2, 4, 5, 8, 9, 0, 4];

        keys.Add(0, 0.1);
        Assert.Equal(-1, keys.Key); // not enough heard yet
        for (var bar = 0; bar < 4; bar++) {
            foreach (var pc in cMajor) {
                keys.Add(pc, 0.1);
            }
        }

        Assert.Equal((0, false), (keys.Key, keys.Minor));
        keys.Add(6, 0.3); // a passing F#
        Assert.Equal((0, false), (keys.Key, keys.Minor));

        for (var bar = 0; bar < 12; bar++) {
            foreach (var pc in gMajor) {
                keys.Add(pc, 0.1);
            }
        }

        Assert.Equal((7, false), (keys.Key, keys.Minor));

        keys.Reset();
        for (var bar = 0; bar < 4; bar++) {
            foreach (var pc in aMinor) {
                keys.Add(pc, 0.1);
            }
        }

        Assert.Equal((9, true), (keys.Key, keys.Minor));
    }

    static double Frequency(int note) => 440 * Math.Pow(2, (note - 69) / 12.0);

    static PitchBlock Create(params (int Index, float Value)[] settings) {
        var block = new PitchBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    /// <summary>Amplitude of the component at <paramref name="frequency" /> (Hann-windowed DTFT).</summary>
    internal static double Level(ReadOnlySpan<float> signal, double frequency) {
        double re = 0, im = 0, windowSum = 0;
        var w = 2 * Math.PI * frequency / Rate;
        for (var n = 0; n < signal.Length; n++) {
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (signal.Length - 1));
            re += window * signal[n] * Math.Cos(w * n);
            im -= window * signal[n] * Math.Sin(w * n);
            windowSum += window;
        }

        return 2 * Math.Sqrt(re * re + im * im) / windowSum;
    }

    /// <summary>Frequency of the strongest component between 20 Hz and 2 kHz, to about 0.1 Hz.</summary>
    internal static double PeakFrequency(ReadOnlySpan<float> signal) {
        // Coarse: Hann-windowed, zero-padded FFT. Fine: the windowed DTFT around the coarse peak.
        const int Size = 1 << 16;
        var padded = new float[Size];
        for (var n = 0; n < Math.Min(signal.Length, Size); n++) {
            padded[n] = (float)(signal[n] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (signal.Length - 1))));
        }

        var magnitudes = Fft.Magnitudes(padded);
        var binHz = Rate / Size;
        var best = (int)(20 / binHz);
        for (var k = best; k < 2_000 / binHz; k++) {
            if (magnitudes[k] > magnitudes[best]) {
                best = k;
            }
        }

        var peak = best * binHz;
        var peakLevel = 0.0;
        for (var f = (best - 1) * binHz; f <= (best + 1) * binHz; f += 0.05) {
            var level = Level(signal, f);
            if (level > peakLevel) {
                (peak, peakLevel) = (f, level);
            }
        }

        return peak;
    }
}
