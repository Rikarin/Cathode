using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Pitch block's formant correction and shift (docs/08 §6.22): a vowel-like tone (a pulse train through three
///     formant resonators) shifted an octave either way keeps its formants with correction on and carries them along
///     with correction off; the shift moves them by its interval.
/// </summary>
/// <remarks>
///     The formants are measured from the output's harmonics: their levels against the vowel's own envelope (the
///     resonators' response) stretched by a factor s, the s that fits best being where the formants went (1 = kept).
/// </remarks>
public class PitchFormantTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const double Fundamental = 120;

    /// <summary>A semitone and a half either way.</summary>
    const double Tolerance = 1.09;

    static readonly double[] Formants = [700, 1_800, 2_800];

    public static TheoryData<int> Types => [..Enum.GetValues<PitchType>().Select(static t => (int)t)];

    public static TheoryData<int, int> Shifters => new() {
        { (int)PitchType.Whammy, PitchBlock.WhammyMono },
        { (int)PitchType.Whammy, PitchBlock.WhammyPoly },
        { (int)PitchType.VirtualCapo, 0 }
    };

    [Theory]
    [MemberData(nameof(Shifters))]
    public void Correction_keeps_the_formants_while_the_pitch_moves_an_octave(int type, int tracking) {
        foreach (var semitones in (int[]) [12, -12]) {
            var ratio = Math.Pow(2, semitones / 12.0);
            var corrected = Render(Shifter(type, tracking, semitones, true));
            var plain = Render(Shifter(type, tracking, semitones, false));

            var on = EnvelopeScale(corrected, ratio);
            var off = EnvelopeScale(plain, ratio);
            output.WriteLine($"{(PitchType)type}/{tracking} {semitones:+0;-0} st: formants x{on:0.00} on, x{off:0.00} off");

            AssertPitchMovedAnOctave(corrected, semitones);
            AssertPitchMovedAnOctave(plain, semitones);
            Assert.InRange(on, 1 / Tolerance, Tolerance);
            Assert.InRange(off, ratio / Tolerance, ratio * Tolerance);

            // About as loud as without correction.
            var levelDb = 20 * Math.Log10(Analysis.Rms(corrected) / Analysis.Rms(plain));
            output.WriteLine($"  level {levelDb:+0.0;-0.0} dB against the uncorrected shift");
            Assert.InRange(levelDb, -6, 6);
        }
    }

    [Theory]
    [MemberData(nameof(Shifters))]
    public void Formant_shift_moves_the_formants_by_its_interval(int type, int tracking) {
        foreach (var (semitones, shift) in ((int, float)[]) [(12, 7f), (-12, -5f), (7, -7f), (0, 5f)]) {
            var block = Shifter(type, tracking, semitones, true);
            block.Params.SetTarget(PitchBlock.FormantShift1, shift);
            var shifted = Render(block);

            var scale = EnvelopeScale(shifted, Math.Pow(2, semitones / 12.0));
            output.WriteLine($"{(PitchType)type}/{tracking} {semitones:+0;-0} st, formant {shift:+0;-0}: x{scale:0.00}");
            var expected = Math.Pow(2, shift / 12);
            Assert.InRange(scale, expected / Tolerance, expected * Tolerance);
        }
    }

    [Theory]
    [InlineData(12, 0f)]
    [InlineData(-12, 0f)]
    [InlineData(12, 7f)]
    [InlineData(-12, -5f)]
    [InlineData(0, 5f)]
    [InlineData(0, -7f)]
    public void Psola_voices_keep_their_formants_and_take_the_shift(int semitones, float shift) {
        var block = Create(
            (PitchBlock.Type, (float)PitchType.Chromatic),
            (PitchBlock.Voice1Shift, semitones),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Pan, 0f),
            (PitchBlock.Mix, 100f),
            (PitchBlock.FormantCorrection, 1f),
            (PitchBlock.FormantShift1, shift)
        );
        var shifted = Render(block);

        var scale = EnvelopeScale(shifted, Math.Pow(2, semitones / 12.0));
        output.WriteLine($"Chromatic {semitones:+0;-0} st, formant {shift:+0;-0}: x{scale:0.00}");
        AssertPitchMovedAnOctave(shifted, semitones);
        var expected = Math.Pow(2, shift / 12);
        Assert.InRange(scale, expected / Tolerance, expected * Tolerance);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Off_by_default_and_the_shift_alone_changes_nothing(int type) {
        var input = Signals.Concat(Vowel(24_000), Signals.Noise(9_600, 3, 0.3f));
        using var reference = Create((PitchBlock.Type, type));
        using var shiftOnly = Create(
            (PitchBlock.Type, type),
            (PitchBlock.FormantShift1, 7f),
            (PitchBlock.FormantShift1 + 1, -5f),
            (PitchBlock.FormantShift1 + 2, 3f),
            (PitchBlock.FormantShift1 + 3, -12f)
        );

        Assert.Equal(0f, PitchBlock.Info.Params[PitchBlock.FormantCorrection].Default);
        Assert.Equal(BlockRender.Mono(reference, input, 64), BlockRender.Mono(shiftOnly, input, 64));
    }

    [Theory]
    [InlineData((int)PitchType.Detune)]
    [InlineData((int)PitchType.OctaveDivider)]
    [InlineData((int)PitchType.Crystals)]
    public void Types_without_the_control_hide_it_and_ignore_it(int type) {
        var input = Vowel(24_000);
        using var reference = Create((PitchBlock.Type, type));
        using var on = Create(
            (PitchBlock.Type, type),
            (PitchBlock.FormantCorrection, 1f),
            (PitchBlock.FormantShift1, 7f)
        );

        Assert.Equal(BlockRender.Mono(reference, input, 64), BlockRender.Mono(on, input, 64));
        Assert.Equal(string.Empty, PitchBlock.Info.Params[PitchBlock.FormantCorrection].TypeLabels![type]);
        Assert.Equal(string.Empty, PitchBlock.Info.Params[PitchBlock.FormantShift1].TypeLabels![type]);
    }

    [Fact]
    public void Labels_show_one_shift_on_the_single_voice_types_and_four_on_the_harmonisers() {
        var info = PitchBlock.Info;
        Assert.Equal("Formant Shift", info.Params[PitchBlock.FormantShift1].TypeLabels![(int)PitchType.Whammy]);
        Assert.Equal("Formant Shift 1", info.Params[PitchBlock.FormantShift1].TypeLabels![(int)PitchType.Diatonic]);
        Assert.Equal(
            "Formant Shift 4",
            info.Params[PitchBlock.FormantShift1 + 3].TypeLabels![(int)PitchType.Chromatic]
        );
        Assert.Equal(string.Empty, info.Params[PitchBlock.FormantShift1 + 3].TypeLabels![(int)PitchType.Whammy]);
        Assert.Equal(PitchBlock.FormantCorrection, info.IndexOf("formant"));
        Assert.Equal(PitchBlock.FormantShift1 + 3, info.IndexOf("formantShift4"));
        Assert.Equal(PitchBlock.FormantShift1 + PitchBlock.VoiceCount, info.Params.Count);
    }

    [Theory]
    [InlineData((int)PitchType.Whammy, PitchBlock.WhammyMono)]
    [InlineData((int)PitchType.Whammy, PitchBlock.WhammyPoly)]
    [InlineData((int)PitchType.Arpeggiator, 0)]
    [InlineData((int)PitchType.VirtualCapo, 0)]
    [InlineData((int)PitchType.Chromatic, 0)]
    [InlineData((int)PitchType.Diatonic, 0)]
    public void Correction_is_bounded_allocation_free_and_decays(int type, int tracking) {
        using var block = Create(
            (PitchBlock.Type, type),
            (PitchBlock.WhammyTracking, tracking),
            (PitchBlock.WhammyEnd, 12f),
            (PitchBlock.Pedal, 100f),
            (PitchBlock.InputMode, 1f),
            (PitchBlock.Voice3Level, 0f),
            (PitchBlock.Voice4Level, 0f),
            (PitchBlock.FormantCorrection, 1f),
            (PitchBlock.FormantShift1, 12f),
            (PitchBlock.FormantShift1 + 1, -12f),
            (PitchBlock.FormantShift1 + 2, 5f),
            (PitchBlock.FormantShift1 + 3, -7f)
        );

        // A loud vowel, full-scale noise, a step and silence: bounded throughout.
        var loud = Signals.Concat(
            [..Vowel(24_000).Select(static s => 2 * s)],
            Signals.Noise(24_000, 5, 1f),
            Signals.Step(4_800, 0.9f),
            new float[4_800]
        );
        var (left, right) = BlockRender.Stereo(block, loud, [..loud.Select(static s => -s)], static _ => 64);
        Assert.True(left.All(float.IsFinite) && right.All(float.IsFinite), "non-finite output");
        var peak = Math.Max(Analysis.Peak(left), Analysis.Peak(right));
        output.WriteLine($"peak {peak:0.00}");
        Assert.True(peak < 8, $"peak {peak:0.00}");

        // Allocation-free once warmed up.
        var blockLeft = new float[64];
        var blockRight = new float[64];
        var noise = new Random(3);
        for (var b = 0; b < 200; b++) {
            Fill();
            block.Process(new(blockLeft, blockRight), new(Rate, 120, 0));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var b = 0; b < 2_000; b++) {
            Fill();
            block.Process(new(blockLeft, blockRight), new(Rate, 120, 0));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        var tail = BlockRender.Mono(block, new float[48_000 * 5], 64);
        Assert.False(Analysis.AnySubnormal(tail), "subnormal samples");
        Assert.True(Analysis.Peak(tail.AsSpan(tail.Length - 4_800)) < 1e-6, "tail did not decay");

        void Fill() {
            for (var i = 0; i < 64; i++) {
                blockLeft[i] = (float)(noise.NextDouble() - 0.5);
                blockRight[i] = -blockLeft[i];
            }
        }
    }

    static PitchBlock Shifter(int type, int tracking, int semitones, bool correct) =>
        type == (int)PitchType.VirtualCapo
            ? Create(
                (PitchBlock.Type, type),
                (PitchBlock.Capo, semitones + 12f),
                (PitchBlock.FormantCorrection, correct ? 1f : 0f)
            )
            : Create(
                (PitchBlock.Type, type),
                (PitchBlock.WhammyTracking, tracking),
                (PitchBlock.WhammyStart, semitones),
                (PitchBlock.WhammyEnd, semitones),
                (PitchBlock.FormantCorrection, correct ? 1f : 0f)
            );

    static PitchBlock Create(params (int Index, float Value)[] settings) {
        var block = new PitchBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    /// <summary>The block's steady-state output for the vowel: its last second.</summary>
    static float[] Render(PitchBlock block) {
        using (block) {
            return BlockRender.Mono(block, Vowel(96_000), 64)[48_000..];
        }
    }

    /// <summary>The resonators' pole radius, for a 100 Hz bandwidth.</summary>
    static double Pole => Math.Exp(-Math.PI * 100 / Rate);

    /// <summary>A pulse train at <see cref="Fundamental" /> through resonators at <see cref="Formants" /> (100 Hz wide).</summary>
    static float[] Vowel(int length) {
        var signal = new double[length];
        var period = (int)(Rate / Fundamental);
        for (var n = 0; n < length; n += period) {
            signal[n] = 1;
        }

        foreach (var formant in Formants) {
            var r = Pole;
            var c = 2 * r * Math.Cos(2 * Math.PI * formant / Rate);
            double y1 = 0, y2 = 0;
            for (var n = 0; n < length; n++) {
                var y = signal[n] + c * y1 - r * r * y2;
                (y2, y1) = (y1, y);
                signal[n] = y * (1 - r);
            }
        }

        var peak = signal.Max(Math.Abs); // the same for any length of a period or more
        return [..signal.Select(s => (float)(0.4 * s / peak))];
    }

    /// <summary>The vowel's spectral envelope in dB: the resonators' response.</summary>
    static double VowelDb(double frequency) {
        var w = 2 * Math.PI * frequency / Rate;
        var db = 0.0;
        foreach (var formant in Formants) {
            var r = Pole;
            var c = 2 * r * Math.Cos(2 * Math.PI * formant / Rate);
            var re = 1 - c * Math.Cos(w) + r * r * Math.Cos(2 * w);
            var im = c * Math.Sin(w) - r * r * Math.Sin(2 * w);
            db += 20 * Math.Log10((1 - r) / Math.Sqrt(re * re + im * im));
        }

        return db;
    }

    /// <summary>
    ///     How far the signal's formants sit from the vowel's, as a frequency ratio: the levels of its harmonics (the
    ///     vowel's, moved by <paramref name="pitchRatio" />) between 300 Hz and 3 kHz against the vowel's envelope at
    ///     f / s, in dB with the overall level removed; the s that fits best over a range of two octaves either way.
    /// </summary>
    static double EnvelopeScale(float[] signal, double pitchRatio) {
        var spectrum = Spectrum(signal);
        var f0 = Fundamental * pitchRatio;
        var harmonics = new List<(double Frequency, double Db)>();
        for (var h = 1; h * f0 <= 3_000; h++) {
            if (h * f0 >= 300) {
                harmonics.Add((h * f0, HarmonicDb(spectrum, h * f0, f0)));
            }
        }

        var (best, bestError) = (1.0, double.MaxValue);
        for (var log = -2.0; log <= 2.0; log += 0.005) {
            var s = Math.Pow(2, log);
            double sum = 0, sumSquares = 0;
            foreach (var (frequency, db) in harmonics) {
                var d = db - VowelDb(frequency / s);
                sum += d;
                sumSquares += d * d;
            }

            var error = sumSquares / harmonics.Count - sum * sum / harmonics.Count / harmonics.Count;
            if (error < bestError) {
                (best, bestError) = (s, error);
            }
        }

        return best;
    }

    /// <summary>The pitch moved an octave: up, the old odd harmonics are gone; down, the new odd ones are there.</summary>
    static void AssertPitchMovedAnOctave(float[] signal, int semitones) {
        if (semitones is not (12 or -12)) {
            return;
        }

        var spectrum = Spectrum(signal);
        var low = semitones > 0 ? Fundamental : Fundamental / 2; // the lower series; its odd members are not on the higher
        double odd = 0, even = 0;
        for (var h = 1; h <= 24; h++) {
            var level = Math.Pow(10, HarmonicDb(spectrum, h * low, low) / 20);
            if (h % 2 == 1) {
                odd += level;
            } else {
                even += level;
            }
        }

        if (semitones > 0) {
            Assert.True(odd < 0.1 * even, $"the input pitch remains: odd {odd:0.0000}, even {even:0.0000}");
        } else {
            Assert.True(odd > 0.3 * even, $"no octave-down series: odd {odd:0.0000}, even {even:0.0000}");
        }
    }

    /// <summary>The Hann-windowed magnitude spectrum, zero-padded to 2^17 points.</summary>
    static double[] Spectrum(float[] signal) {
        var padded = new float[1 << 17];
        for (var n = 0; n < signal.Length; n++) {
            padded[n] = (float)(signal[n] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (signal.Length - 1))));
        }

        return Fft.Magnitudes(padded);
    }

    /// <summary>The level in dB of the strongest bin within a quarter of <paramref name="spacing" /> of <paramref name="frequency" />.</summary>
    static double HarmonicDb(double[] spectrum, double frequency, double spacing) {
        var binHz = Rate / (1 << 17);
        var peak = 1e-12;
        for (var k = (int)((frequency - spacing / 4) / binHz); k <= (int)((frequency + spacing / 4) / binHz); k++) {
            peak = Math.Max(peak, spectrum[k]);
        }

        return 20 * Math.Log10(peak);
    }
}
