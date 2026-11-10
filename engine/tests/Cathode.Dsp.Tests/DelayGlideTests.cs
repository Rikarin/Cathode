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

    [Fact]
    public void Delay_time_ratio_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<DelayBlock>(
                (DelayBlock.Mix, 100f),
                (DelayBlock.Time, 20f),
                (DelayBlock.Ratio, 50f)
            ),
            DelayBlock.Ratio,
            100f
        );

    [Fact]
    public void Delay_right_post_delay_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<DelayBlock>(
                (DelayBlock.Mix, 100f),
                (DelayBlock.Time, 5f),
                (DelayBlock.RightPostDelay, 5f)
            ),
            DelayBlock.RightPostDelay,
            12f
        );

    [Fact]
    public void Delay_mono_tape_head_2_ratio_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<DelayBlock>(
                (DelayBlock.Type, DelayBlock.MonoTape),
                (DelayBlock.Mix, 100f),
                (DelayBlock.Time, 20f),
                (DelayBlock.Head1Level, -80f)
            ),
            DelayBlock.Head2Ratio,
            100f
        );

    [Theory]
    [InlineData(FilterBlock.FeedForwardComb)]
    [InlineData(FilterBlock.FeedbackComb)]
    public void Filter_comb_time_glides(int type) =>
        AssertGlides(() => TestBlocks.Create<FilterBlock>((FilterBlock.Type, type)), FilterBlock.CombTime, 10f);

    [Fact]
    public void Multi_delay_comb_time_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<MultiDelayBlock>(
                (MultiDelayBlock.Mix, 100f),
                (MultiDelayBlock.Time, 20f),
                (MultiDelayBlock.Comb1Feedback, 80f),
                (MultiDelayBlock.Comb1Feedback + 1, 80f),
                (MultiDelayBlock.Comb1Feedback + 2, 80f),
                (MultiDelayBlock.Comb1Feedback + 3, 80f),
                (MultiDelayBlock.Comb1Time, 2f)
            ),
            MultiDelayBlock.Comb1Time,
            10f
        );

    [Fact]
    public void Plex_pre_delay_glides() =>
        AssertGlides(
            static () => TestBlocks.Create<PlexBlock>(
                (PlexBlock.Mix, 100f),
                (PlexBlock.Delays, 1f),
                (PlexBlock.Time1, 5f),
                (PlexBlock.PreDelay, 5f)
            ),
            PlexBlock.PreDelay,
            20f
        );

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
