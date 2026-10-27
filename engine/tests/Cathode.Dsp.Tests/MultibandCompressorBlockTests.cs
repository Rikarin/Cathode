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

    [Theory]
    [InlineData(0f)]
    [InlineData(50f)]
    [InlineData(100f)]
    public void Muting_two_bands_solos_the_third_at_any_mix(float mix) {
        const int LowBin = 34; // ~100 Hz, low band
        const int HighBin = 1_707; // ~5 kHz, high band
        using var block = TestBlocks.Create<MultibandCompressorBlock>(
            (MultibandCompressorBlock.Mute(0), 1f),
            (MultibandCompressorBlock.Mute(1), 1f),
            (MultibandCompressorBlock.Mix, mix)
        );
        var low = Signals.Sine(LowBin * BinHz, 48_000, 3 * Length, 0.01f);
        var high = Signals.Sine(HighBin * BinHz, 48_000, 3 * Length, 0.01f);

        var output = BlockRender.Mono(block, [..low.Zip(high, static (a, b) => a + b)], 64);
        var spectrum = Fft.Magnitudes(output.AsSpan(2 * Length));

        Assert.True(Analysis.ToDb(spectrum[LowBin] / 0.01) < -60, "the muted low band is heard");
        Assert.InRange(Analysis.ToDb(spectrum[HighBin] / 0.01), -0.6, 0.1); // the high band alone at 5 kHz
    }

    [Fact]
    public void Mix_blends_compression_and_keeps_the_band_levels() {
        const int LowBin = 11; // ~32 Hz, well inside the low band

        double[] Render(float mix) {
            using var block = TestBlocks.Create<MultibandCompressorBlock>(
                (MultibandCompressorBlock.Ratio(0), 4f),
                (MultibandCompressorBlock.BandLevel(0), -6f),
                (MultibandCompressorBlock.Mix, mix)
            );
            var output = BlockRender.Mono(block, Signals.Sine(LowBin * BinHz, 48_000, 3 * Length, 0.5f), 64);
            return Fft.Magnitudes(output.AsSpan(2 * Length));
        }

        var dry = Analysis.ToDb(Render(0f)[LowBin] / 0.5);
        var half = Analysis.ToDb(Render(50f)[LowBin] / 0.5);
        var wet = Analysis.ToDb(Render(100f)[LowBin] / 0.5);

        Assert.Equal(-6, dry, 0.2); // uncompressed, at the band's level
        Assert.True(wet < dry - 6, $"compressed {wet:0.0} dB");
        Assert.InRange(half, wet, dry);
    }

    [Theory]
    [InlineData(MultibandCompressorBlock.LowMute, 1f, 50.0)]
    [InlineData(MultibandCompressorBlock.Slope, MultibandCompressorBlock.Slope12, 600.0)]
    [InlineData(MultibandCompressorBlock.Slope, MultibandCompressorBlock.Slope12, 4_000.0)]
    public void Mute_and_slope_changes_fade_instead_of_clicking(int parameter, float value, double frequency) {
        // Below threshold (no compression), so the output is the tone through the crossover; a hard switch steps it.
        using var block = TestBlocks.Create<MultibandCompressorBlock>(
            (MultibandCompressorBlock.Threshold(0), 0f),
            (MultibandCompressorBlock.Threshold(1), 0f),
            (MultibandCompressorBlock.Threshold(2), 0f)
        );
        var tone = Signals.Sine(frequency, 48_000, 9_600, 0.5f);
        var maxStep = 2 * Math.PI * frequency / 48_000 * 0.5;

        var before = BlockRender.Mono(block, tone[..4_800], 64);
        block.Params.SetTarget(parameter, value);
        var after = BlockRender.Mono(block, tone[4_800..], 64);
        var output = before.Concat(after).ToArray();

        var largest = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).Skip(2_400).Max();
        Assert.True(largest < 1.5 * maxStep, $"step {largest:0.0000} against the tone's {maxStep:0.0000}");
        if (parameter == MultibandCompressorBlock.LowMute) {
            Assert.True(Analysis.Peak(after.AsSpan(2_400)) < 0.01, "the muted band should be gone after the fade");
        }
    }
}
