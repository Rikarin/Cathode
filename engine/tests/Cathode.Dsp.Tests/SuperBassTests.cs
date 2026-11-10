using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Super Bass 100's preamp against the reference MNA simulator as a whole chain, as the other vintage
///     Marshalls' (<see cref="BritishVintageChains" />).
/// </summary>
public partial class AmpStageTests {
    [Theory]
    [InlineData(0.002, -30)]
    [InlineData(0.05, -20)]
    public void The_super_bass_chain_matches_the_reference(double amplitude, double limitDb) =>
        British_vintage_chains_match_the_reference(SuperBassTests.Model, false, 3, amplitude, limitDb);
}

/// <summary>
///     The Marshall 1992 Super Bass (#366) against Unicord's drawing 70-13-11: the values read from it, the rails its
///     droppers leave, and what sets it apart from the Super Lead of the same sheet family. The knob, switch and sweep
///     checks are the Marshall family's (<see cref="MarshallModelTests" />).
/// </summary>
public class SuperBassTests(ITestOutputHelper output) {
    public const string Model = "Super Bass 100";
    const double Rate = 48_000;

    static AmpDesign Design => SuperBass100.Design;

    [Fact]
    public void The_model_has_the_drawings_values() {
        Assert.Same(Design, AmpDesign.All.Single(static a => a.Name == Model));

        var stack = SuperBass100.Stack;
        Assert.Equal(
            (250e-12, 56e3, 22e-9, 22e-9),
            (stack.TrebleCap, stack.SlopeResistor, stack.BassCap, stack.MiddleCap)
        );
        Assert.Equal((250e3, 1e6, 25e3), (stack.Treble.Ohms, stack.Bass.Ohms, stack.Middle.Ohms));
        Assert.True(stack.MiddleOnWiper);

        var second = (TriodeStageDesign)Design.Preamp[1];
        Assert.Equal((100e3, 820.0, 0.0), (second.PlateResistor, second.CathodeResistor, second.CathodeBypass));

        var power = Design.Power;
        var inverter = (LongTailPairDesign)power.PhaseInverter;
        Assert.Equal(
            (82e3, 100e3, 10e3, 470.0),
            (inverter.PlateResistorA, inverter.PlateResistorB, inverter.TailResistor, inverter.BiasResistor)
        );
        Assert.Equal(
            (0.1e-6, 220e3, 5.6e3, 1e3),
            (power.CouplingCap, power.GridLeak, power.GridStopper, power.ScreenResistor)
        );
        Assert.Equal(SuperLead100.Design.Power.Feedback, power.Feedback); // 47k from the 16 Ω tap into the 5k pot
        Assert.Equal(
            SuperLead100.Design.Power with {
                PhaseInverter = inverter, CouplingCap = 0.1e-6, PowerSupply = power.PowerSupply
            },
            power
        );

        // 20k into both halves of the 50 + 50 µF can, then 10k and 50 µF twice.
        Assert.Equal((20e3, 100e-6), (power.PowerSupply.PreampResistance, power.PowerSupply.PreampCap));
        Assert.Equal(
            [(10e3, 50e-6, 0), (10e3, 50e-6, 1)],
            Design.PreampSupply!.Nodes.Select(static n => (n.Resistance, n.Cap, n.Parent))
        );
        Assert.Equal([2, 1, 1], Design.Preamp.Select(static s => s.SupplyNode ?? 0));
    }

    [Fact]
    public void The_panel_is_two_volumes_without_a_master_or_a_bright_switch() {
        Assert.Equal("Volume I", Label("gain"));
        Assert.Equal("Volume II", Label("control1"));
        Assert.Equal("Jumper", Label("switch1"));
        Assert.Equal("Normal Input", Label("switch2"));
        foreach (var id in new[] { "master", "bright", "cut", "depth", "switch3", "control2" }) {
            Assert.Empty(Label(id));
        }

        foreach (var id in new[] { "bass", "middle", "treble", "presence" }) {
            Assert.NotEmpty(Label(id));
        }
    }

    /// <summary>
    ///     The sheet gives the chain and no voltages: 20k from the screens' node to the inverter's, 10k on to V2's and
    ///     10k on to V1's. The model's rails are the ones those droppers leave with its own stages' idle currents, and
    ///     its supply draws are those currents (as the JCM800's, #198).
    /// </summary>
    [Fact]
    public void The_rails_follow_from_the_drawings_droppers() {
        var inverter = (LongTailPairDesign)Design.Power.PhaseInverter;
        var supply = Design.Power.PowerSupply;
        var nodes = Design.PreampSupply!.Nodes;
        var rails = (Inverter: inverter.Supply, Second: ((TriodeStageDesign)Design.Preamp[1]).Supply,
            First: SuperBass100.FirstRail);
        Assert.Equal((SuperBass100.InverterRail, SuperBass100.SecondRail), (rails.Inverter, rails.Second));

        // The idle currents at given rails: the inverter's two plates; V2A and the V2B follower on its plate; V1's
        // two triodes, each on 100k, sharing the 820 Ω.
        (double Pair, double Second, double First) Currents(double a, double b, double c) {
            var tables = PhaseInverter.For(inverter with { Supply = a });
            var pair = (a - tables.RestPlateA) / inverter.PlateResistorA
                + (a - tables.RestPlateB) / inverter.PlateResistorB;
            var (v2a, plate, _) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, b, 100e3, 820);
            var (v2b, _) = OperatingPoint.CathodeFollower(TriodeModel.Ecc83, plate, b, 100e3);
            var (v1, _, _) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, c, 100e3, 2 * 820);
            return (pair, v2a + v2b, 2 * v1);
        }

        var power = new AmpChannel(Design, 96_000).Power;
        var screens = power.ScreenSupply;
        var solved = rails;
        for (var i = 0; i < 12; i++) {
            var (pair, second, first) = Currents(solved.Inverter, solved.Second, solved.First);
            var a = screens - 20e3 * (pair + second + first);
            var b = a - 10e3 * (second + first);
            solved = (a, b, b - 10e3 * first);
        }

        var drawn = Currents(rails.Inverter, rails.Second, rails.First);
        var all = drawn.Pair + drawn.Second + drawn.First;
        output.WriteLine(
            $"screens {screens:0.0} V; the droppers leave {solved.Inverter:0.0} / {solved.Second:0.0} / "
            + $"{solved.First:0.0} V (the model: {rails.Inverter:0} / {rails.Second:0} / {rails.First:0} V), drawing "
            + $"{all * 1e3:0.00} mA through the 20k, {drawn.Second * 1e3:0.00} mA for V2 and {drawn.First * 1e3:0.00} "
            + $"mA for V1; preamp node {power.PreampVoltage:0.0} V"
        );
        Assert.Equal(solved.Inverter, rails.Inverter, 0.01 * solved.Inverter);
        Assert.Equal(solved.Second, rails.Second, 0.01 * solved.Second);
        Assert.Equal(solved.First, rails.First, 0.01 * solved.First);
        Assert.Equal(all, supply.PreampCurrent, 0.03 * all);
        Assert.Equal(drawn.Second, nodes[0].Current, 0.03 * drawn.Second);
        Assert.Equal(drawn.First, nodes[1].Current, 0.03 * drawn.First);
        Assert.Equal(rails.Inverter, power.PreampVoltage, 0.01 * rails.Inverter);
    }

    /// <summary>
    ///     No bright cap, 0.022 µF coupling and a bypassed cathode on the first channel, and 250 pF on the stack: at
    ///     the same settings the first channel has more bass against its treble than the Super Lead's bright channel,
    ///     and turning its volume down does not thin the sound (the Super Lead's 5 nF across the volume does).
    /// </summary>
    [Fact]
    public void It_is_darker_than_the_super_lead_at_the_same_settings() {
        static double Tilt(string model, float volume) {
            var tones = Signals.Sine(100, Rate, 24_000, 0.002f)
                .Zip(Signals.Sine(3_000, Rate, 24_000, 0.002f), static (a, b) => a + b)
                .ToArray();
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == model));
            block.Params.SetTarget(CircuitAmpBlock.Gain, volume);
            block.Prepare(new(Rate, 64));
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered, 3_000, Rate))
                - Analysis.ToDb(Analysis.MagnitudeAt(rendered, 100, Rate));
        }

        var (bass, lead) = (Tilt(Model, 5), Tilt("Plexi 100W", 5));
        var (bassLow, leadLow) = (Tilt(Model, 2), Tilt("Plexi 100W", 2));
        output.WriteLine(
            $"3 kHz against 100 Hz: Super Bass {bass:+0.0;-0.0} dB at 5 and {bassLow:+0.0;-0.0} dB at 2, Super Lead "
            + $"{lead:+0.0;-0.0} dB and {leadLow:+0.0;-0.0} dB"
        );
        Assert.True(bass < lead - 6, $"Super Bass {bass:0.0} dB, Super Lead {lead:0.0} dB");
        Assert.True(Math.Abs(bassLow - bass) < 3, $"the volume moves the tilt by {bassLow - bass:0.0} dB");
        Assert.True(leadLow > lead + 3, "the Super Lead's bright cap should brighten it at low volume");
    }

    [Fact]
    public void Its_knobs_and_switches_behave_as_the_other_marshalls() {
        var family = new MarshallModelTests(output);
        family.Gain_and_master_raise_the_level_monotonically(Model);
        family.Panel_switches_are_audible(Model);
        family.Full_knob_sweeps_stay_finite_and_bounded(Model);
        family.The_tone_knobs_all_at_0_still_pass_signal(Model);
    }

    static string Label(string id) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[AmpDesign.All.ToList().IndexOf(Design)] : parameter.Name;
    }
}
