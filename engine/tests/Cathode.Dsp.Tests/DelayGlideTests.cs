using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     A delay time moved while audio runs (a pedal on Manual, a modifier on Pre-Delay) glides sample by sample: the
///     render does not depend on the callback size, as it would if the time stepped once per block.
/// </summary>
public class DelayGlideTests {
    const int Half = 2_400;

    [Fact]
    public void Chorus_delay_glides() =>
        AssertGlides(static () => TestBlocks.Create<ChorusBlock>((ChorusBlock.Mix, 100f)), ChorusBlock.Delay, 30f);

    [Fact]
    public void Flanger_manual_glides() =>
        AssertGlides(static () => TestBlocks.Create<FlangerBlock>((FlangerBlock.Mix, 100f)), FlangerBlock.Delay, 10f);

    [Fact]
    public void Flanger_thru_zero_dry_delay_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<FlangerBlock>((FlangerBlock.ThruZero, FlangerBlock.ThruZeroManual)),
            FlangerBlock.DryDelay,
            10f
        );

    [Theory]
    [InlineData(EnhancerBlock.Haas)]
    [InlineData(EnhancerBlock.Classic)]
    public void Enhancer_time_glides(int type) =>
        AssertGlides(() => TestBlocks.Create<EnhancerBlock>((EnhancerBlock.Type, type)), EnhancerBlock.Time, 30f);

    [Fact]
    public void Reverb_pre_delay_glides() =>
        AssertGlides(static () => TestBlocks.Create<ReverbBlock>((ReverbBlock.Mix, 100f)), ReverbBlock.PreDelay, 40f);

    static void AssertGlides(Func<IDspBlock> create, int parameter, float to) {
        var tone = Signals.Sine(110, 48_000, 2 * Half, 0.5f);

        // Both sides, one after the other (the Haas delay is on the right only).
        float[] Render(int blockSize, bool move) {
            using var block = create();
            var first = tone[..Half];
            var second = tone[Half..];
            var (left, right) = BlockRender.Stereo(block, first, first, _ => blockSize);
            if (move) {
                block.Params.SetTarget(parameter, to);
            }

            var (left2, right2) = BlockRender.Stereo(block, second, second, _ => blockSize, Half);
            return [..left, ..left2, ..right, ..right2];
        }

        var held = Render(64, false);
        Assert.Equal(held, Render(1, false)); // nothing else depends on the callback size
        var perBlock = Render(64, true);
        var perSample = Render(1, true);

        Assert.True(Analysis.Peak(Difference(held, perBlock)) > 0.05, "the change should be heard");
        var error = Analysis.Peak(Difference(perBlock, perSample));
        Assert.True(error < 2e-3, $"64-sample callbacks differ from 1-sample ones by {error:0.0000}");
    }

    static float[] Difference(float[] a, float[] b) => [..a.Zip(b, static (x, y) => x - y)];
}
