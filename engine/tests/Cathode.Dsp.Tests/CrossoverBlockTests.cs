using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class CrossoverBlockTests {
    const double Rate = 48_000;
    const float Off = Decibels.SilenceDb;

    static readonly double[] Probes = [30, 100, 300, 700, 1_000, 1_400, 3_000, 8_000, 16_000];

    [Fact]
    public void Bands_sum_flat() {
        using var block = TestBlocks.Create<CrossoverBlock>();

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        foreach (var f in Probes) {
            Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.1);
        }
    }

    [Fact]
    public void Each_band_is_6_dB_down_at_the_crossover_and_rolls_off_at_24_dB_per_octave() {
        using var low = TestBlocks.Create<CrossoverBlock>(
            (CrossoverBlock.HighLevelLeft, Off),
            (CrossoverBlock.HighLevelRight, Off)
        );
        using var high = TestBlocks.Create<CrossoverBlock>(
            (CrossoverBlock.LowLevelLeft, Off),
            (CrossoverBlock.LowLevelRight, Off)
        );

        var lowResponse = BlockRender.Mono(low, Signals.Impulse(16_384), 64);
        var highResponse = BlockRender.Mono(high, Signals.Impulse(16_384), 64);

        Assert.Equal(-6.02, Analysis.ToDb(Analysis.MagnitudeAt(lowResponse, 1_000, Rate)), 0.05);
        Assert.Equal(-6.02, Analysis.ToDb(Analysis.MagnitudeAt(highResponse, 1_000, Rate)), 0.05);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(lowResponse, 100, Rate)), 0.05);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(highResponse, 10_000, Rate)), 0.1);
        Assert.InRange(Analysis.ToDb(Analysis.MagnitudeAt(lowResponse, 4_000, Rate)), -52, -44); // two octaves: ~-48 dB
        Assert.InRange(Analysis.ToDb(Analysis.MagnitudeAt(highResponse, 250, Rate)), -52, -44);
    }

    [Fact]
    public void Split_mode_sends_low_left_and_high_right() {
        using var block = TestBlocks.Create<CrossoverBlock>((CrossoverBlock.Mode, 1f));
        var impulse = Signals.Impulse(16_384);

        var (left, right) = BlockRender.Stereo(block, impulse, impulse, _ => 64);

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(left, 100, Rate)), 0.05);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(left, 8_000, Rate)) < -60);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(right, 8_000, Rate)), 0.05);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(right, 100, Rate)) < -60);
    }

    [Fact]
    public void Band_pan_moves_only_that_band() {
        using var block = TestBlocks.Create<CrossoverBlock>((CrossoverBlock.LowPanLeft, 100f));
        var impulse = Signals.Impulse(16_384);

        var (left, right) = BlockRender.Stereo(block, impulse, impulse, _ => 64);

        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(left, 100, Rate)) < -40, "the left low band leaves the left side");
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(left, 8_000, Rate)), 0.05);
        Assert.Equal(6.02, Analysis.ToDb(Analysis.MagnitudeAt(right, 100, Rate)), 0.05); // both low bands on the right
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(right, 8_000, Rate)), 0.05);
    }

    [Fact]
    public void Band_levels_are_per_side() {
        using var block = TestBlocks.Create<CrossoverBlock>(
            (CrossoverBlock.LowLevelRight, Off),
            (CrossoverBlock.HighLevelLeft, -6.02f)
        );
        var impulse = Signals.Impulse(16_384);

        var (left, right) = BlockRender.Stereo(block, impulse, impulse, _ => 64);

        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(left, 100, Rate)), 0.05);
        Assert.Equal(-6.02, Analysis.ToDb(Analysis.MagnitudeAt(left, 8_000, Rate)), 0.05);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(right, 100, Rate)) < -40, "the right low band is off");
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(right, 8_000, Rate)), 0.05);
    }
}
