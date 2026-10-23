using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class MultibandCompressorBlockTests {
    const int Length = 16_384;
    const double BinHz = 48_000.0 / Length;

    [Fact]
    public void Below_threshold_the_bands_sum_flat() {
        using var block = TestBlocks.Create<MultibandCompressorBlock>();

        var response = BlockRender.Mono(block, Signals.Impulse(Length, 0.001f), 64); // -60 dBFS: no compression

        foreach (var f in new[] { 30.0, 150, 200, 260, 1_000, 2_500, 3_000, 10_000, 18_000 }) {
            Assert.Equal(-60, Analysis.ToDb(Analysis.MagnitudeAt(response, f, 48_000)), 0.1);
        }
    }

    [Fact]
    public void Only_the_band_with_energy_above_threshold_is_compressed() {
        const int LowBin = 34; // ~100 Hz, low band
        const int HighBin = 1_707; // ~5 kHz, high band
        using var block = TestBlocks.Create<MultibandCompressorBlock>(
            (MultibandCompressorBlock.Ratio(0), 4f),
            (MultibandCompressorBlock.Release(0), 500f)
        );
        var loud = Signals.Sine(LowBin * BinHz, 48_000, 3 * Length, 0.5f); // -6 dBFS, 14 dB over
        var quiet = Signals.Sine(HighBin * BinHz, 48_000, 3 * Length, 0.01f); // -40 dBFS
        var input = loud.Zip(quiet, static (a, b) => a + b).ToArray();

        var output = BlockRender.Mono(block, input, 64);
        var spectrum = Fft.Magnitudes(output.AsSpan(2 * Length));

        Assert.True(block.GainReductionDb(0) < -6, $"low band reduced {block.GainReductionDb(0):0.0} dB");
        Assert.Equal(0, block.GainReductionDb(1), 0.5);
        Assert.Equal(0, block.GainReductionDb(2));
        Assert.True(Analysis.ToDb(spectrum[LowBin] / 0.5) < -6, "the loud low tone should come down");
        Assert.Equal(0, Analysis.ToDb(spectrum[HighBin] / 0.01), 0.2); // the quiet high tone is untouched
    }

    [Fact]
    public void Band_level_scales_only_its_band() {
        using var block = TestBlocks.Create<MultibandCompressorBlock>((MultibandCompressorBlock.BandLevel(2), -12f));

        var response = BlockRender.Mono(block, Signals.Impulse(Length, 0.001f), 64);

        Assert.Equal(-60, Analysis.ToDb(Analysis.MagnitudeAt(response, 60, 48_000)), 0.1);
        Assert.Equal(-72, Analysis.ToDb(Analysis.MagnitudeAt(response, 15_000, 48_000)), 0.1);
    }
}
