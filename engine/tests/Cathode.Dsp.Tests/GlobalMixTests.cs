using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>Global Mix: the rig's Effects Mix offset reaches the blocks whose Global Mix switch is on.</summary>
public class GlobalMixTests {
    public static TheoryData<string, int, int> Blocks => new() {
        { "chorus", ChorusBlock.Mix, ChorusBlock.GlobalMix },
        { "delay", DelayBlock.Mix, DelayBlock.GlobalMix },
        { "flanger", FlangerBlock.Mix, FlangerBlock.GlobalMix },
        { "megatap", MegatapBlock.Mix, MegatapBlock.GlobalMix },
        { "multidelay", MultiDelayBlock.Mix, MultiDelayBlock.GlobalMix },
        { "phaser", PhaserBlock.Mix, PhaserBlock.GlobalMix },
        { "pitch", PitchBlock.Mix, PitchBlock.GlobalMix },
        { "resonator", ResonatorBlock.Mix, ResonatorBlock.GlobalMix },
        { "reverb", ReverbBlock.Mix, ReverbBlock.GlobalMix },
        { "synth", SynthBlock.Mix, SynthBlock.GlobalMix }
    };

    [Theory]
    [InlineData(30f, 0f, 1f, 30f)]
    [InlineData(30f, 25f, 1f, 55f)]
    [InlineData(30f, 25f, 0f, 30f)]
    [InlineData(80f, 50f, 1f, 100f)]
    [InlineData(20f, -50f, 1f, 0f)]
    public void The_offset_is_added_and_clamped(float mix, float offset, float on, float expected) =>
        Assert.Equal(expected, EffectsMix.Apply(mix, on, new BlockContext(48_000, 120, 0, MixOffset: offset)));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_dry_block_turns_wet_only_with_global_mix_on(string type, int mix, int globalMix) {
        var input = Signals.Sine(220, 48_000, 28_800, 0.25f); // longer than the default delay time
        var dry = Render(type, mix, globalMix, false, 0f, input);
        var offsetOff = Render(type, mix, globalMix, false, 50f, input);
        var offsetOn = Render(type, mix, globalMix, true, 50f, input);

        Assert.Equal(dry, offsetOff);
        Assert.True(
            Analysis.Rms(offsetOn.Zip(dry, static (a, b) => a - b).ToArray()) > 1e-3,
            "the offset made no difference"
        );
    }

    static float[] Render(string type, int mix, int globalMix, bool on, float offset, float[] input) {
        using var block = BlockRegistry.CreateDefault().Create(type);
        block.Params.SetTarget(mix, 0f);
        block.Params.SetTarget(globalMix, on ? 1f : 0f);
        block.Prepare(new(48_000, 64));
        var left = (float[])input.Clone();
        var right = (float[])input.Clone();
        for (var offsetSamples = 0; offsetSamples < input.Length; offsetSamples += 64) {
            block.Process(
                new(left.AsSpan(offsetSamples, 64), right.AsSpan(offsetSamples, 64)),
                new(48_000, 120, offsetSamples, HeldNotes.Of(57), offset)
            );
        }

        return left;
    }
}
