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
            (PitchBlock.Mix, 100f)
        );

        var (left, right) = BlockRender.Stereo(
            detune,
            Signals.Sine(440, Rate, 96_000, 0.3f),
            Signals.Sine(440, Rate, 96_000, 0.3f),
            _ => 64
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

    static PitchBlock Create(params (int Index, float Value)[] settings) {
        var block = new PitchBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
