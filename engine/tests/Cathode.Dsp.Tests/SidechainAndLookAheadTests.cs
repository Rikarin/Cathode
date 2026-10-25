using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>Compressor and Gate sidechain sources, and the compressor's look-ahead.</summary>
public class SidechainAndLookAheadTests {
    const int Rate = 48_000;

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(2f, 48)] // 1 ms
    [InlineData(6f, 480)] // 10 ms
    public void Look_ahead_is_reported_as_latency_and_delays_the_audio(float choice, int samples) {
        using var block = TestBlocks.Create<CompressorBlock>(
            (CompressorBlock.LookAhead, choice),
            (CompressorBlock.Threshold, 0f)
        );
        var impulse = Signals.Impulse(1_024, 0.1f);

        var output = BlockRender.Mono(block, impulse, 64);

        Assert.Equal(samples, block.LatencySamples);
        Assert.Equal(samples, Array.IndexOf(output, output.Max()));
        Assert.Equal(0.1f, output.Max(), 1e-6f);
    }

    [Fact]
    public void Look_ahead_catches_the_transient() {
        // A slow attack lets a sudden step through; with look-ahead the gain is already down when it arrives.
        var step = Signals.Concat(new float[4_800], Signals.Step(4_800));
        var settings = new[] {
            (CompressorBlock.Threshold, -20f), (CompressorBlock.Ratio, 20f), (CompressorBlock.Attack, 2f),
            (CompressorBlock.Knee, 0f)
        };
        using var plain = TestBlocks.Create<CompressorBlock>(settings);
        using var ahead = TestBlocks.Create<CompressorBlock>([..settings, (CompressorBlock.LookAhead, 5f)]); // 5 ms

        var plainPeak = BlockRender.Mono(plain, step, 64).Max();
        var aheadPeak = BlockRender.Mono(ahead, step, 64).Max();

        Assert.True(plainPeak > 0.5f, $"plain peak {plainPeak}");
        Assert.True(aheadPeak < 0.25f, $"look-ahead peak {aheadPeak}");
    }

    [Fact]
    public void The_compressor_follows_the_selected_sidechain() {
        var quiet = Signals.Sine(500, Rate, 9_600, Decibels.ToGain(-40f));
        var loud = Signals.Sine(500, Rate, 9_600);
        var settings = new[] { (CompressorBlock.Threshold, -30f), (CompressorBlock.Ratio, 20f) };
        using var own = TestBlocks.Create<CompressorBlock>([..settings, (CompressorBlock.Sidechain, 0f)]);
        using var row = TestBlocks.Create<CompressorBlock>([..settings, (CompressorBlock.Sidechain, 3f)]); // Row 2

        var ownOut = Run(own, quiet, loud, 1);
        var rowOut = Run(row, quiet, loud, 1);

        Assert.Equal(quiet, ownOut); // below threshold on its own input
        Assert.True(Analysis.Peak(rowOut.AsSpan(4_800)) < Decibels.ToGain(-60f), "the loud row did not duck it");
    }

    [Fact]
    public void A_row_that_is_not_offered_falls_back_to_the_block_input() {
        using var block = TestBlocks.Create<CompressorBlock>(
            (CompressorBlock.Threshold, -30f),
            (CompressorBlock.Ratio, 20f),
            (CompressorBlock.Sidechain, 5f) // Row 4
        );
        var quiet = Signals.Sine(500, Rate, 9_600, Decibels.ToGain(-40f));

        Assert.Equal(quiet, Run(block, quiet, Signals.Sine(500, Rate, 9_600), 1));
    }

    [Fact]
    public void The_gate_opens_on_the_grid_input() {
        var quiet = Signals.Sine(500, Rate, 9_600, Decibels.ToGain(-70f));
        var loud = Signals.Sine(500, Rate, 9_600, 0.5f);
        var settings = new[] { (GateBlock.Threshold, -50f), (GateBlock.Type, GateBlock.Gate), (GateBlock.Release, 5f) };
        using var own = TestBlocks.Create<GateBlock>(settings);
        using var input = TestBlocks.Create<GateBlock>([..settings, (GateBlock.Sidechain, 1f)]); // Input

        var closed = Run(own, quiet, loud, -1);
        var open = Run(input, quiet, loud, -1);

        Assert.True(Analysis.Peak(closed.AsSpan(4_800)) < Decibels.ToGain(-110.0), "closed on its own input");
        Assert.Equal(quiet.AsSpan(4_800).ToArray(), open.AsSpan(4_800).ToArray());
    }

    /// <summary>Processes <paramref name="main" />, offering <paramref name="side" /> as row <paramref name="row" />.</summary>
    static float[] Run<T>(T block, float[] main, float[] side, int row)
        where T : IDspBlock, ISideInputBlock {
        var left = (float[])main.Clone();
        var right = (float[])main.Clone();
        var sideRight = (float[])side.Clone();
        for (var offset = 0; offset < main.Length; offset += 64) {
            block.SideInput(SideInputKind.Tap, row, new(side.AsSpan(offset, 64), sideRight.AsSpan(offset, 64)), default);
            block.Process(new(left.AsSpan(offset, 64), right.AsSpan(offset, 64)), new(Rate, 120, offset));
        }

        return left;
    }
}
