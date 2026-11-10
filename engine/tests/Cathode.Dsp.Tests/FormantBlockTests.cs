using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class FormantBlockTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(0f, new[] { 730.0, 1_090, 2_440 })] // start: A
    [InlineData(50f, new[] { 530.0, 1_840, 2_480 })] // mid: E
    [InlineData(100f, new[] { 270.0, 2_290, 3_010 })] // end: I
    public void Response_peaks_at_the_vowel_formants(float control, double[] formants) {
        using var block = TestBlocks.Create<FormantBlock>((FormantBlock.Control, control));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);
        var peaks = LocalMaxima(response, 150, 5_000);

        Assert.Equal(FormantBlock.Formants, peaks.Count);
        for (var i = 0; i < formants.Length; i++) {
            Assert.InRange(peaks[i], formants[i] * 0.97, formants[i] * 1.03);
        }

        // Resonant: each formant stands well clear of the valley next to it.
        var valley = Analysis.MagnitudeAt(response, Math.Sqrt(formants[0] * formants[1]), Rate);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, formants[0], Rate) / valley) > 6);
    }

    [Theory]
    [InlineData(0, new[] { 730.0, 1_090, 2_440 })] // A, "father"
    [InlineData(1, new[] { 530.0, 1_840, 2_480 })] // E, "bed"
    [InlineData(2, new[] { 270.0, 2_290, 3_010 })] // I, "heed"
    [InlineData(3, new[] { 570.0, 840, 2_410 })] // O, "ought"
    [InlineData(4, new[] { 300.0, 870, 2_240 })] // U, "food"
    [InlineData(5, new[] { 390.0, 1_990, 2_550 })] // IH, "hid"
    [InlineData(6, new[] { 660.0, 1_720, 2_410 })] // AE, "had"
    [InlineData(7, new[] { 440.0, 1_020, 2_240 })] // OO, "hood"
    [InlineData(8, new[] { 640.0, 1_190, 2_390 })] // UH, "hud"
    [InlineData(9, new[] { 490.0, 1_350, 1_690 })] // ER, "heard"
    public void Every_vowel_of_the_list_sits_on_its_Peterson_Barney_formants(int vowel, double[] formants) {
        // The first five keep their indices: a preset stored before the list grew says the same vowels.
        Assert.Equal(10, FormantBlock.Info.Params[FormantBlock.Start].Options!.Count);

        using var block = TestBlocks.Create<FormantBlock>((FormantBlock.Start, vowel));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);
        var peaks = LocalMaxima(response, 150, 5_000);

        Assert.Equal(FormantBlock.Formants, peaks.Count);
        for (var i = 0; i < formants.Length; i++) {
            Assert.InRange(peaks[i], formants[i] * 0.97, formants[i] * 1.03);
        }
    }

    [Fact]
    public void Control_morphs_formants_geometrically() {
        // A to E at 25 %: F2 halfway between 1090 and 1840 Hz on a log scale.
        Assert.Equal(Math.Sqrt(1_090.0 * 1_840), FormantBlock.FormantFrequency(1, 0, 1, 2, 0.25f), 0.5);
    }

    [Fact]
    public void Shift_scales_every_formant() {
        using var block = TestBlocks.Create<FormantBlock>((FormantBlock.Shift, 12f));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);
        var peaks = LocalMaxima(response, 150, 8_000);

        Assert.InRange(peaks[0], 1_460 * 0.97, 1_460 * 1.03); // A's F1, an octave up
    }

    /// <summary>Frequencies of the local maxima of the magnitude response, on a 0.5 % log grid.</summary>
    static List<double> LocalMaxima(float[] response, double from, double to) {
        var frequencies = new List<double>();
        for (var f = from; f <= to; f *= 1.005) {
            frequencies.Add(f);
        }

        var magnitudes = frequencies.Select(f => Analysis.MagnitudeAt(response, f, Rate)).ToArray();
        var peaks = new List<double>();
        for (var i = 1; i < magnitudes.Length - 1; i++) {
            if (magnitudes[i] > magnitudes[i - 1] && magnitudes[i] >= magnitudes[i + 1]) {
                peaks.Add(frequencies[i]);
            }
        }

        return peaks;
    }
}
