using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class VolumePanTests {
    private static VolumePanBlock Create(float levelDb = 0, float balance = 0) {
        var block = new VolumePanBlock();
        block.Params.SetTarget(VolumePanBlock.Level, levelDb);
        block.Params.SetTarget(VolumePanBlock.Balance, balance);
        block.Prepare(new ProcessSpec(48_000, 64));
        return block;
    }

    [Fact]
    public void Unity_settings_are_bit_transparent() {
        using var block = Create();
        var input = Signals.Noise(4_800, seed: 1);

        Assert.Equal(input, BlockRender.Mono(block, input, 64));
    }

    [Fact]
    public void Level_and_balance_set_the_channel_gains() {
        using var block = Create(levelDb: -6.0206f, balance: -100);
        var input = Signals.Step(256);

        var (left, right) = BlockRender.Stereo(block, input, input, _ => 64);

        Assert.All(left, static s => Assert.Equal(0.5f, s, 1e-4f));
        Assert.All(right, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Level_changes_ramp_without_steps() {
        using var block = Create(levelDb: Dsp.Decibels.SilenceDb);
        var input = Signals.Step(48_000);
        block.Params.SetTarget(VolumePanBlock.Level, 20);

        var output = BlockRender.Mono(block, input, 64);

        // 20 ms linear ramp in dB, gain interpolated per sample: the largest per-sample step stays small.
        var maxStep = output.Zip(output.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.5f, $"max step {maxStep}");
        Assert.Equal(10f, output[^1], 1e-3f);
    }
}
