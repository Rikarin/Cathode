using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     A three-position switch of an amp is two on/off switches on the Circuit Amp's panel, both off being the middle
///     position (<see cref="AmpPanel.ThreeWay" />). With both on, a position the amp does not have, the model takes
///     the pair's winning side alone.
/// </summary>
public class AmpThreeWaySwitchTests {
    const double Rate = 48_000;

    /// <summary>The model, the switch that yields and the switch that wins.</summary>
    static readonly (string Type, int Yields, int Wins)[] Models = [
        ("Mark V", CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2),
        ("Dual Rectifier 3-Ch", CircuitAmpBlock.Bright, CircuitAmpBlock.Switch1),
        ("Triple Rectifier", CircuitAmpBlock.Bright, CircuitAmpBlock.Switch1),
        ("SVT", CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3),
        ("Prometheus", CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3)
    ];

    public static TheoryData<string, int, int> Pairs => [..Models];

    /// <summary>
    ///     Both switches on renders bit for bit what the winning one renders alone; each switch alone still renders
    ///     its own position, different from the middle one and from the other's.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void Both_sides_on_is_the_winning_position(string type, int yields, int wins) {
        float[] Render(float yielding, float winning) {
            var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == type));
            block.Params.ResetToDefaults();
            block.Params.SetTarget(yields, yielding);
            block.Params.SetTarget(wins, winning);
            block.Prepare(new(Rate, 64));
            block.Reset();
            using (block) {
                var input = Signals.Concat(Signals.Noise(4_000, 5, 0.05f), Signals.Sine(196, Rate, 8_000, 0.1f));
                return BlockRender.Mono(block, input, 64);
            }
        }

        var (middle, yielding, winning, both) = (Render(0, 0), Render(1, 0), Render(0, 1), Render(1, 1));
        Assert.Equal(winning, both);
        Assert.NotEqual(middle, yielding);
        Assert.NotEqual(middle, winning);
        Assert.NotEqual(yielding, winning);
    }

    /// <summary>The pairs are declared on these models' panels and on no other model's.</summary>
    [Fact]
    public void Only_the_amps_with_a_three_position_switch_declare_one() {
        var declared = AmpDesign.All.Where(static a => a.Panel.ThreeWay.Count > 0).Select(static a => a.Name);
        Assert.Equal(Models.Select(static m => m.Type).Order(), declared.Order());
    }

    /// <summary>
    ///     <see cref="AmpPanel.Settle" /> turns the yielding switch off only while the winning one is on, and leaves a
    ///     panel without pairs alone.
    /// </summary>
    [Fact]
    public void Settling_turns_off_the_yielding_side_only_when_both_are_on() {
        var panel = AmpPanel.Standard with { ThreeWay = [new(SwitchRole.Bright, SwitchRole.Switch3)] };
        var both = new AmpControls(0.5, 0.5, 0.5, 0.5, 0.5, true, Switch1: true, Switch3: true);
        Assert.Equal(both with { Bright = false }, panel.Settle(both));
        Assert.Equal(both, AmpPanel.Standard.Settle(both));
        var yielding = both with { Switch3 = false };
        Assert.Equal(yielding, panel.Settle(yielding));
        var winning = both with { Bright = false };
        Assert.Equal(winning, panel.Settle(winning));
    }
}
