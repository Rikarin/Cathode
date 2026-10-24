using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The per-block mix laws (docs/08-blocks-parity.md §3): the shared gains, and each block's measured dry and wet
///     gains at 50 %, solved from its renders at 0, 50 and 100 % (the wet path is the same in each fresh render).
/// </summary>
public class MixLawTests {
    const int Length = 24_000;

    public static TheoryData<string, int, float> Blocks =>
        new() {
            { "chorus", ChorusBlock.Mix, 0.7071f },
            { "flanger", FlangerBlock.Mix, 0.7071f },
            { "phaser", PhaserBlock.Mix, 0.7071f },
            { "reverb", ReverbBlock.Mix, 0.7071f },
            { "multidelay", MultiDelayBlock.Mix, 0.7071f },
            { "rotary", RotaryBlock.Mix, 0.7071f },
            { "tentap", TenTapBlock.Mix, 0.7071f },
            { "delay", DelayBlock.Mix, 1f },
            { "vocoder", VocoderBlock.Mix, 1f },
            { "megatap", MegatapBlock.Mix, 0.5f },
            { "plex", PlexBlock.Mix, 0.5f },
            { "pitch", PitchBlock.Mix, 0.5f },
            { "synth", SynthBlock.Mix, 0.5f },
            { "ringmod", RingModulatorBlock.Mix, 0.5f }
        };

    public static TheoryData<string, int, int> KillDryBlocks =>
        new() {
            { "delay", DelayBlock.Mix, DelayBlock.KillDry },
            { "multidelay", MultiDelayBlock.Mix, MultiDelayBlock.KillDry },
            { "pitch", PitchBlock.Mix, PitchBlock.KillDry },
            { "plex", PlexBlock.Mix, PlexBlock.KillDry },
            { "reverb", ReverbBlock.Mix, ReverbBlock.KillDry }
        };

    [Theory]
    [InlineData(MixLaw.Linear, 0.5f, 0.5f)]
    [InlineData(MixLaw.ConstantPower, 0.70710677f, 0.70710677f)]
    [InlineData(MixLaw.Sweetened, 1f, 1f)]
    [InlineData(MixLaw.DryUnity, 1f, 1f)]
    public void Each_law_has_its_gains_at_the_middle(MixLaw law, float dry, float wet) {
        var gains = MixLaws.Gains(law, 0.5f);
        Assert.Equal(dry, gains.Dry, 5);
        Assert.Equal(wet, gains.Wet, 5);
    }

    [Theory]
    [InlineData(MixLaw.Linear)]
    [InlineData(MixLaw.ConstantPower)]
    [InlineData(MixLaw.Sweetened)]
    [InlineData(MixLaw.DryUnity)]
    public void Every_law_is_exact_at_the_ends_and_kill_dry_mutes_the_dry(MixLaw law) {
        Assert.Equal((1f, 0f), MixLaws.Gains(law, 0f));
        Assert.Equal((0f, 1f), MixLaws.Gains(law, 1f));
        Assert.Equal(0f, MixLaws.Gains(law, 0.3f, true).Dry);
        Assert.Equal(MixLaws.Gains(law, 0.3f).Wet, MixLaws.Gains(law, 0.3f, true).Wet);
    }

    [Fact]
    public void The_sweetened_law_spends_its_travel_on_quiet_echoes() {
        // A quarter of the way the echo is at -12 dB, where a linear law would already be at -12 dB only at 25 % too
        // but with the dry falling; here the dry holds 0 dB to the middle.
        Assert.Equal(0.25f, MixLaws.Gains(MixLaw.Sweetened, 0.25f).Wet, 5);
        Assert.Equal(1f, MixLaws.Gains(MixLaw.Sweetened, 0.25f).Dry);
        Assert.Equal(0.5f, MixLaws.Gains(MixLaw.Sweetened, 0.75f).Dry, 5);
    }

    [Fact]
    public void A_ramp_lands_on_the_law_and_a_steady_ramp_is_exact() {
        var ramp = new MixRamp(MixLaw.ConstantPower, 0f, 1f, 64);
        for (var i = 0; i < 64; i++) {
            ramp.Advance();
        }

        Assert.Equal(0f, ramp.Dry, 5);
        Assert.Equal(1f, ramp.Wet, 5);
        var steady = new MixRamp(MixLaw.ConstantPower, 1f, 1f, 64);
        steady.Advance();
        Assert.Equal((0f, 1f), (steady.Dry, steady.Wet));
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Each_block_follows_its_law(string type, int mix, float expectedDry) {
        var input = Input();
        var wet = Render(type, mix, 100f, -1, input);
        var half = Render(type, mix, 50f, -1, input);
        var (dry, wetGain) = Solve(input, wet, half);

        Assert.InRange(dry, expectedDry - 0.03, expectedDry + 0.03);
        Assert.InRange(wetGain, expectedDry - 0.03, expectedDry + 0.03); // every law is symmetric at 50 %
    }

    [Theory]
    [MemberData(nameof(KillDryBlocks))]
    public void Kill_dry_leaves_only_the_wet(string type, int mix, int killDry) {
        var input = Input();
        var wet = Render(type, mix, 100f, killDry, input);
        var half = Render(type, mix, 50f, killDry, input);
        var (dry, _) = Solve(input, wet, half);

        Assert.InRange(dry, -0.01, 0.01);
        Assert.Equal(0, Analysis.Peak(Render(type, mix, 0f, killDry, input)), 6); // no wet, no dry: silence
        Assert.Equal(input, Render(type, mix, 0f, -1, input)); // and off, Mix 0 is the dry signal exactly
    }

    static float[] Input() => Signals.Concat(Signals.Noise(Length / 2, 7, 0.25f), Signals.Sine(330, 48_000, Length / 2, 0.3f));

    /// <summary>Least squares for <c>half = d · dry + w · wet</c>.</summary>
    static (double Dry, double Wet) Solve(float[] dry, float[] wet, float[] half) {
        double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
        for (var i = 0; i < dry.Length; i++) {
            aa += dry[i] * (double)dry[i];
            ab += dry[i] * (double)wet[i];
            bb += wet[i] * (double)wet[i];
            ay += dry[i] * (double)half[i];
            by += wet[i] * (double)half[i];
        }

        var det = aa * bb - ab * ab;
        return ((ay * bb - by * ab) / det, (by * aa - ay * ab) / det);
    }

    static float[] Render(string type, int mix, float value, int killDry, float[] input) {
        using var block = BlockRegistry.CreateDefault().Create(type);
        block.Params.SetTarget(mix, value);
        if (killDry >= 0) {
            block.Params.SetTarget(killDry, 1f);
        }

        block.Prepare(new(48_000, 64));
        var left = (float[])input.Clone();
        var right = (float[])input.Clone();
        for (var offset = 0; offset < input.Length; offset += 64) {
            block.Process(
                new(left.AsSpan(offset, 64), right.AsSpan(offset, 64)),
                new(48_000, 120, offset, HeldNotes.Of(57))
            );
        }

        return left;
    }
}
