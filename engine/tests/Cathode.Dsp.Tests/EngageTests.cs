using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>"On engage" behaviour (<see cref="IEngageAware" />): LFO Bypass Reset and Tremolo Start Phase.</summary>
public class EngageTests {
    [Fact]
    public void Flanger_lfo_bypass_reset_restarts_the_sweep() {
        AssertRestarts(static reset => TestBlocks.Create<FlangerBlock>(
                (FlangerBlock.LfoReset, reset),
                (FlangerBlock.Rate, 1.3f)
            )
        );
    }

    [Fact]
    public void Phaser_lfo_bypass_reset_restarts_the_sweep() {
        AssertRestarts(static reset => TestBlocks.Create<PhaserBlock>(
                (PhaserBlock.LfoReset, reset),
                (PhaserBlock.Rate, 1.3f)
            )
        );
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(270f)]
    public void Tremolo_starts_at_its_start_phase_when_engaged(float startPhase) {
        using var engaged = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 3f),
            (TremoloBlock.StartPhase, startPhase)
        );
        using var fresh = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 3f),
            (TremoloBlock.StartPhase, startPhase)
        );
        BlockRender.Mono(engaged, Signals.Step(7_777), 64);

        engaged.Engage();
        var afterEngage = BlockRender.Mono(engaged, Signals.Step(4_800), 64);
        var fromReset = BlockRender.Mono(fresh, Signals.Step(4_800), 64);

        Assert.Equal(fromReset, afterEngage);
        // A sine LFO at phase p gives the gain 1 - (1 - sin(2 pi p)) / 2 at full depth.
        var expected = 0.5f * (1 + MathF.Sin(2 * MathF.PI * startPhase / 360f));
        Assert.Equal(expected, afterEngage[0], 1e-3f);
    }

    /// <summary>
    ///     After silence (so only the LFO differs from a fresh block), engaging with the reset on matches a fresh block
    ///     (to rounding: the delay lines' write positions differ); with it off the sweep carries on from where it was.
    /// </summary>
    static void AssertRestarts<T>(Func<float, T> create)
        where T : IDspBlock, IEngageAware {
        var input = Signals.Sine(330, 48_000, 9_600, 0.5f);
        using var fresh = create(1f);
        var reference = BlockRender.Mono(fresh, input, 64);

        foreach (var reset in new[] { 1f, 0f }) {
            using var block = create(reset);
            BlockRender.Mono(block, new float[12_345], 64);
            block.Engage();
            var output = BlockRender.Mono(block, input, 64);
            var difference = output.Zip(reference, static (a, b) => MathF.Abs(a - b)).Max();
            if (reset == 1f) {
                Assert.True(difference < 1e-5f, $"differs from a fresh block by {difference}");
            } else {
                Assert.True(difference > 1e-2f, $"the sweep restarted anyway ({difference})");
            }
        }
    }
}
