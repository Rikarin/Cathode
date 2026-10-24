using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>IR import processing: minimum phase keeps the magnitude, auto-trim and polarity, channels and length.</summary>
public class IrImportTests {
    const int Rate = 48_000;

    [Fact]
    public void Minimum_phase_keeps_the_magnitude_response_and_moves_the_energy_forward() {
        // A mixed-phase IR: a delayed, ringing, two-path response.
        var ir = new float[2_048];
        var noise = Signals.Noise(1_500, 3);
        for (var i = 0; i < noise.Length; i++) {
            ir[200 + i] += noise[i] * MathF.Exp(-i / 200f);
            ir[260 + i] -= 0.6f * noise[i] * MathF.Exp(-i / 150f);
        }

        var minimum = IrImport.MinimumPhase(ir);

        Assert.Equal(ir.Length, minimum.Length);
        var peak = Spectrum(ir).Max();
        var original = Spectrum(ir);
        var processed = Spectrum(minimum);
        for (var k = 1; k < original.Length; k++) {
            if (original[k] > peak * 1e-2) {
                Assert.Equal(Analysis.ToDb(original[k]), Analysis.ToDb(processed[k]), 0.25);
            }
        }

        // Minimum phase: every leading part of the IR holds at least as much energy as the original's does.
        double Energy(float[] x, int n) => x.Take(n).Sum(static v => (double)v * v);
        foreach (var n in (int[])[50, 200, 400, 1_000]) {
            Assert.True(Energy(minimum, n) >= Energy(ir, n) * 0.999, $"first {n} samples");
        }

        Assert.True(Energy(minimum, 50) > 0.2 * Energy(minimum, 2_048));
    }

    [Fact]
    public void Auto_trim_removes_the_leading_silence_of_both_channels_alike() {
        var left = new float[1_000];
        var right = new float[1_000];
        left[300] = 0.5f;
        right[200] = 1e-5f; // below -60 dB of the peak: silence
        right[320] = -0.8f;
        left[310] = 1e-3f;

        var trimmed = IrImport.Process(new(Rate, [left, right]), new(IrProcessing.AutoTrim));

        Assert.Equal(700, trimmed.Frames);
        Assert.Equal(0.5f, trimmed.Channels[0][0]);
        Assert.Equal(-0.8f, trimmed.Channels[1][20]); // the image between the channels is kept
    }

    [Fact]
    public void Polarity_correction_makes_the_main_peak_positive() {
        var ir = new float[500];
        ir[10] = 0.3f;
        ir[12] = -0.9f;
        ir[20] = 0.4f;

        var fixedIr = IrImport.Process(new(Rate, [ir]), new(Polarity: true)).Channels[0];
        var asIs = IrImport.Process(new(Rate, [ir]), new()).Channels[0];

        Assert.Equal(0.9f, fixedIr[12]);
        Assert.Equal(-0.3f, fixedIr[10]);
        Assert.Equal(ir, asIs);
        var already = IrImport.Process(new(Rate, [fixedIr]), new(Polarity: true)).Channels[0];
        Assert.Equal(fixedIr, already); // a positive peak is left alone
    }

    [Theory]
    [InlineData(IrChannels.Both, 2)]
    [InlineData(IrChannels.Left, 1)]
    [InlineData(IrChannels.Right, 1)]
    [InlineData(IrChannels.Sum, 1)]
    public void A_stereo_file_keeps_the_chosen_channels(IrChannels channels, int count) {
        float[] left = [1f, 0.5f];
        float[] right = [0.2f, -0.5f];

        var result = IrImport.Process(new(Rate, [left, right]), new(Channels: channels));

        Assert.Equal(count, result.Channels.Length);
        var first = result.Channels[0];
        Assert.Equal(
            channels switch {
                IrChannels.Right => right,
                IrChannels.Sum => [0.6f, 0f],
                _ => left
            },
            first
        );
    }

    [Fact]
    public void A_maximum_length_cuts_with_a_fade() {
        var ir = Enumerable.Repeat(1f, 4_800).ToArray();

        var cut = IrImport.Process(new(Rate, [ir]), new(MaxLengthMs: 50)).Channels[0];

        Assert.Equal(2_400, cut.Length);
        Assert.Equal(1f, cut[2_000]);
        Assert.Equal(0f, cut[^1], 1e-6f);
        Assert.True(new IrImportSettings().IsDefault);
    }

    static double[] Spectrum(float[] ir) {
        const int n = 8_192;
        var padded = new float[n];
        ir.CopyTo(padded, 0);
        var magnitudes = new double[n / 2];
        for (var k = 0; k < magnitudes.Length; k += 16) {
            magnitudes[k] = Analysis.MagnitudeAt(padded, k * (double)Rate / n, Rate);
        }

        return magnitudes;
    }
}
