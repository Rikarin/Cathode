using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp's Preamp switch (docs/02 §5.8, the Hybrid tier): off, the block is the model's power amp alone
///     and takes another preamp's output, at the level a Circuit Amp with its power amp off puts out.
/// </summary>
public class CircuitAmpHybridTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>A model with its master in the preamp and a Presence knob: the first one there is.</summary>
    static readonly int Model = Enumerable.Range(0, AmpDesign.All.Count)
        .First(static i => AmpFeatures.HasMaster(AmpDesign.All[i]) && AmpDesign.All[i].Panel.Presence is not null);

    [Fact]
    public void A_preamp_only_amp_into_a_power_amp_only_amp_is_the_whole_amp() {
        // One model three times: whole, its preamp alone and its power amp alone. The preamp's supply is stiff in all
        // three, since two blocks cannot pass the power amp's sag back to the preamp.
        using var whole = Create((CircuitAmpBlock.PreampSag, 0));
        using var preamp = Create((CircuitAmpBlock.PreampSag, 0), (CircuitAmpBlock.PowerAmpModel, 0));
        using var power = Create((CircuitAmpBlock.PreampModel, 0), (CircuitAmpBlock.Master, 10));
        var input = CircuitAmpBlockTests.Chord(19_200, 0.1f);

        var reference = BlockRender.Mono(whole, input, 64);
        var chained = BlockRender.Mono(power, BlockRender.Mono(preamp, input, 64), 64);

        // The chain passes the resampling filters twice, so it is that much later.
        var delay = preamp.LatencySamples;
        var settled = 4_800;
        var a = reference.AsSpan(settled, reference.Length - settled - delay);
        var b = chained.AsSpan(settled + delay, a.Length);
        var residual = Analysis.NullResidualDb(a, b);
        output.WriteLine(
            $"{AmpDesign.All[Model].Name}: whole {Analysis.ToDb(Analysis.Rms(a)):0.00} dB, "
            + $"chained {Analysis.ToDb(Analysis.Rms(b)):0.00} dB, residual {residual:0.0} dB"
        );
        Assert.Equal(Analysis.ToDb(Analysis.Rms(a)), Analysis.ToDb(Analysis.Rms(b)), 0.2);
        Assert.True(residual < -30, $"residual {residual:0.0} dB");
    }

    [Fact]
    public void With_the_preamp_off_its_knobs_do_nothing_and_the_power_amps_still_work() {
        var input = Signals.Noise(9_600, 5, 0.02f);

        float[] Render(params (int Index, float Value)[] settings) {
            using var block = Create([(CircuitAmpBlock.PreampModel, 0), ..settings]);
            return BlockRender.Mono(block, input, 64);
        }

        var plain = Render();
        Assert.Equal(
            plain,
            Render((CircuitAmpBlock.Gain, 9.5f), (CircuitAmpBlock.Bass, 1), (CircuitAmpBlock.LowCut, 300))
        );

        static double TopDb(float[] signal) => Analysis.OctaveBandLevelsDb(signal, Rate, [4_000.0])[0];
        var dull = Render((CircuitAmpBlock.Presence, 0));
        var bright = Render((CircuitAmpBlock.Presence, 10));
        Assert.True(TopDb(bright) - TopDb(dull) > 1, $"presence moved the top by {TopDb(bright) - TopDb(dull):0.0} dB");
    }

    [Fact]
    public void With_the_preamp_off_the_master_is_a_volume_control_ahead_of_the_inverter() {
        // Quiet enough that the power amp is linear: the level then follows the pot.
        var input = Signals.Sine(220, Rate, 9_600, 0.001f);

        double LevelDb(float master, float trim = 0) {
            using var block = Create(
                (CircuitAmpBlock.PreampModel, 0),
                (CircuitAmpBlock.Master, master),
                (CircuitAmpBlock.InputTrim, trim)
            );
            return Analysis.ToDb(Analysis.Rms(BlockRender.Mono(block, input, 64).AsSpan(4_800)));
        }

        var pot = new Pot(1e6, PotTaper.Audio);
        Assert.Equal(Analysis.ToDb(pot.Fraction(1) / pot.Fraction(0.5)), LevelDb(10) - LevelDb(5), 0.2);
        Assert.Equal(6, LevelDb(5, 6) - LevelDb(5), 0.2); // Input Trim still trims
        Assert.True(LevelDb(0) < LevelDb(5) - 20);
    }

    [Fact]
    public void The_switch_is_on_by_default_and_follows_every_older_parameter() {
        var parameter = CircuitAmpBlock.Info.Params[CircuitAmpBlock.PreampModel];
        Assert.Equal("preamp", parameter.Id);
        Assert.Equal(1f, parameter.Default);
        Assert.Equal(CircuitAmpBlock.PreampModel, CircuitAmpBlock.Info.Params.Count - 1);
        Assert.DoesNotContain("preamp", CircuitAmpBlock.Info.TypeResets);
    }

    [Fact]
    public void A_channel_with_its_preamp_off_ticks_its_power_amp_alone() {
        var settings = ChannelSettings.Stock with { Preamp = false };
        var hybrid = new AmpChannel(AmpDesign.All[Model], 96_000);
        var twin = new AmpChannel(AmpDesign.All[Model], 96_000);
        hybrid.Apply(PreampSettings.Stock, PowerSettings.Stock, settings);

        for (var i = 0; i < 2_000; i++) {
            var x = 5 * Math.Sin(2 * Math.PI * 220 * i / 96_000);
            twin.TickPreamp(0);
            Assert.Equal(twin.TickPowerAmp(x), hybrid.Tick(x), 1e-9);
        }
    }

    static CircuitAmpBlock Create(params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Model);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
