using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The high-gain front ends (stream F4) against the reference MNA simulator as whole chains: every stage from the
///     input to the stage after the cold clipper joined in one circuit, with no Thévenin cut, so the cuts' error, the
///     cold clipper's grid current from the previous stage and Newton on cascaded clipping stages are all checked.
/// </summary>
/// <remarks>
///     What the limits record (measured, with a few dB of margin): in the linear range two stages agree to -29 dB where
///     the
///     first is a <see cref="TriodeStage" />, whose Thévenin resistance treats a partly bypassed cathode (1.8k ‖ 1 µF)
///     as grounded, and to -60 dB where it is a network stage, which uses the 1 kHz impedance. Driven hard, the cut's
///     linearised plate is the main error, worst behind a cold clipper with a plate bypass cap (SLO-100, 5150III) whose
///     next stage draws grid current: -17 and -9 dB there (-26 and -19 dB with the cap removed). That SLO-100 figure
///     is from while its overdrive path left out the 330k at the node the normal channel joins (#199); with it the
///     cold clipper works into 254k instead of 2.4M, so the cut's one output resistance (the plate's 100k ‖ 1 nF
///     taken at 1 kHz) costs more: -15 dB in the linear range (it was under -22) and -9 dB driven hard. The
///     Rectifier's chain
///     was -60 dB while it followed a redraw without the plate cap; the factory drawing has 1 nF there, and it is -8
///     dB. The rhythm and clean channels have no cold clipper and are here for the same check of their first four
///     stages: the 5150's rhythm channel -31 dB, the 5150III's channels one and two -26 dB (-23 dB on channel two
///     driven hard).
/// </remarks>
public partial class AmpStageTests {
    public static TheoryData<string, bool, double, double> ColdClipperChains =>
        new() {
            { "SLO-100", false, 0.002, -12 },
            { "SLO-100", false, 0.05, -6 },
            { "SLO-100", true, 0.002, -28 },
            { "SLO-100", true, 0.05, -28 },
            { "5150", false, 0.002, -25 },
            { "5150", false, 0.05, -20 },
            { "5150", true, 0.002, -27 },
            { "5150", true, 0.05, -27 },
            { "Dual Rectifier", false, 0.05, -5 },
            { "5150III", false, 0.05, -6 },
            { "5150III Clean/Crunch", false, 0.002, -23 },
            { "5150III Clean/Crunch", false, 0.05, -23 },
            { "5150III Clean/Crunch", true, 0.002, -23 },
            { "5150III Clean/Crunch", true, 0.05, -20 },
            { "Legacy VL100", false, 0.002, -21 },
            { "Legacy VL100", false, 0.05, -14 },
            { "Legacy VL100", true, 0.05, -45 }
        };

    /// <summary>
    ///     The first four stages of the channel <paramref name="switched" /> selects (Switch 1 on: a channel switch's
    ///     other channel), or as many as it has.
    /// </summary>
    [Theory]
    [MemberData(nameof(ColdClipperChains))]
    public void Cold_clipper_chains_match_the_reference(string type, bool switched, double amplitude, double limitDb) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var designs = design.Preamp
            .SelectMany(s => s is ChannelSwitchStageDesign c ? switched ? c.On : c.Off : [s])
            .Take(4)
            .ToArray();
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false, Switch1: switched);

        var stages = designs.Select(static d => d.Create(Rate)).ToArray();
        var (rest, resistance) = (0.0, 0.0);
        foreach (var stage in stages) {
            stage.Update(controls);
            stage.SetSourceResistance(resistance);
            stage.Reset(rest);
            (rest, resistance) = (stage.RestOutput, stage.OutputResistance);
        }

        var circuit = new ReferenceCircuit();
        var node = circuit.Node();
        circuit.Source(node, 0, t => Drive(amplitude, t));
        foreach (var d in designs) {
            var network = d switch {
                TriodeStageDesign triode => triode.ToNetwork(),
                NetworkStageDesign net => net,
                _ => throw new InvalidOperationException(d.Name)
            };
            node = AmpReferences.Append(circuit, network, controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            $"{type}{(switched ? " (switched)" : string.Empty)} "
            + $"{string.Join(" > ", designs.Select(static d => d.Name))}",
            x => {
                foreach (var stage in stages) {
                    x = stage.Tick(x);
                }

                return x;
            },
            stages[^1].Newton,
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"{type}: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The high-gain models (stream F4) on the Circuit Amp block: their panels, knob sweeps without instability, the
///     knobs that must move the sound monotonically, their switches, and the Dual Rectifier's supply switches.
/// </summary>
public class HighGainAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models =
        ["SLO-100", "Mark IIC+", "5150", "Dual Rectifier", "5150III", "Mark IV", "5150III Clean/Crunch"];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Volume", Label("gain", "Mark IIC+"));
        Assert.Equal("Lead Drive", Label("control1", "Mark IIC+"));
        Assert.Equal("Lead Master", Label("control2", "Mark IIC+"));
        Assert.Equal(["Treble Shift", "Deep", "Bass Shift"], Switches("Mark IIC+"));
        Assert.Equal(["Fat", "Bright", "Mid Gain"], Switches("Mark IV"));
        Assert.Equal(
            ["80", "240", "750", "2200", "6600"],
            Enumerable.Range(1, 5).Select(static b => Label($"eq{b}", "Mark IIC+"))
        );
        Assert.Equal("Pre Gain", Label("gain", "5150"));
        Assert.Equal("Post Gain", Label("master", "5150"));
        Assert.Equal("Resonance", Label("depth", "5150"));
        Assert.Equal(["Modern", "Tube Rectifier", "Spongy"], Switches("Dual Rectifier"));
        foreach (var type in Models) {
            Assert.Empty(Label("cut", type));
            Assert.Equal("Treble", Label("treble", type));
        }

        Assert.Equal("Normal Ch", Label("switch1", "SLO-100"));
        Assert.Empty(Label("eq1", "5150"));
        Assert.Equal(["Rhythm Ch", "Crunch", string.Empty], Switches("5150"));
        Assert.Equal("Bright", Label("bright", "5150"));
        Assert.Equal(["Crunch", string.Empty, string.Empty], Switches("5150III Clean/Crunch"));
        Assert.Equal("Volume", Label("master", "5150III Clean/Crunch"));
        Assert.Equal("Resonance", Label("depth", "5150III Clean/Crunch"));
        Assert.Empty(Label("bright", "5150III Clean/Crunch"));
    }

    /// <summary>
    ///     Issue #306: Fender's service diagram for the 5150-III prints the signal at test points along channels one
    ///     and two for a 1 kHz sine at the jack (10.7 mV on channel one, 17.0 mV on channel two), every control at half
    ///     rotation but Two Gain at 20 %, and allows 20 %: TP3 (V1B's plate), TP5 (V2A's), TP7 (V2B's), TP9 (V3A's),
    ///     and 1.10 V at the top of the playing channel's volume (TP11, TP12), which at half rotation leaves 30 % of
    ///     the track, loaded by the 10M after the relays, below the wiper. The channel's netlists are joined in one
    ///     reference circuit (so each plate carries the next stage's load, as the test point does) and taken
    ///     small-signal at its operating point; the WDF stages against the same circuit are
    ///     <see cref="AmpStageTests.Cold_clipper_chains_match_the_reference" />'s.
    /// </summary>
    /// <remarks>
    ///     Held stage by stage (each test point over the one before it), which is what the netlist between the two
    ///     decides. Every stage is within the sheet's 20 % but two. V1B's gain is 50 where the sheet has 41 on both
    ///     channels (a 12AX7 of 100 and 62k into about 200k gives 50; the sheet's figure is a weaker tube's), so it is
    ///     held to 25 %. And channel two's gain pot at 20 % of its rotation: the model's audio law leaves 9 % of the
    ///     track below the wiper there, the sheet's TP5 is what 3.4 % would give (the model at 0.8 of 10), so the
    ///     model is 2.2 times the sheet at that one point. A real 30 % pot is not exponential near the bottom of its
    ///     travel, and the sheet's "9 o'clock" is a nominal setting; from TP5 on, channel two's stages agree with the
    ///     sheet to 3 %. With the knob at noon channel one lands within 7 % of every printed voltage after TP3.
    /// </remarks>
    [Theory]
    [InlineData(false, 0.0107, 0.5, 0.442, 0.737, 2.39, 22.3)]
    [InlineData(true, 0.0170, 0.2, 0.709, 0.408, 3.72, 34.5)]
    public void The_5150III_clean_and_crunch_channels_reach_the_service_diagrams_test_voltages(
        bool two,
        double input,
        double gain,
        double tp3,
        double tp5,
        double tp7,
        double tp9
    ) {
        var channel = Assert.IsType<ChannelSwitchStageDesign>(Evh5150III.CleanCrunch.Preamp[1]);
        AmpStageDesign[] designs = [Evh5150III.CleanCrunch.Preamp[0], ..two ? channel.On : channel.Off];
        var controls = new AmpControls(gain, 0.5, 0.5, 0.5, 0.5, false, Switch1: two);
        var circuit = new ReferenceCircuit();
        var jack = circuit.Node();
        circuit.Source(jack, 0, static _ => 0);
        var nodes = new int[designs.Length];
        var node = jack;
        for (var i = 0; i < designs.Length; i++) {
            var network = designs[i] is TriodeStageDesign triode ? triode.ToNetwork() : (NetworkStageDesign)designs[i];
            nodes[i] = node = AmpReferences.Append(circuit, network, controls, PreampSettings.Stock, node);
        }

        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        var below = 1 / (1 / 0.3e6 + 1 / (two ? 10e6 : 5e6));
        double[] printed = [tp3, tp5, tp7, tp9, 1.10 * below / (0.7e6 + below)];
        var (before, printedBefore) = (input, input);
        for (var i = 0; i < designs.Length; i++) {
            var volts = input * reference.Ac(new(0, 2 * Math.PI * 1_000), 0, nodes[i], linearised: true).Magnitude;
            var (gain1k, printedGain) = (volts / before, printed[i] / printedBefore);
            output.WriteLine(
                $"{designs[i].Name}: {volts:0.000} V for the sheet's {printed[i]:0.000} V; the stage's gain "
                + $"{gain1k:0.000} for {printedGain:0.000} ({100 * (gain1k / printedGain - 1):+0;-0} %)"
            );
            var (low, high) = i == 0 ? (1, 1.25) : two && i == 1 ? (1.8, 2.6) : (0.8, 1.2);
            Assert.InRange(gain1k / printedGain, low, high);
            (before, printedBefore) = (volts, printed[i]);
        }
    }

    /// <summary>
    ///     Issue #306: what tells the service diagram's channel two from channel one, and the type from the red one.
    ///     K2B grounds 68k at V2B's grid on channel one and 330k ahead of its 390k on channel two; channel one's stack
    ///     has 250 pF and a 100k slope, channel two's 470 pF and 47k behind 43k; each channel's Gain is its own 250k
    ///     pot with the other one a fixed load. The power amp is the red type's with the sheet's 20 mA bias and its
    ///     rails: X (V3A, V3B) on node 1 and W (V1B, V2A, V2B) on node 2.
    /// </summary>
    [Fact]
    public void The_5150III_clean_and_crunch_channels_have_the_service_diagrams_parts() {
        var design = Evh5150III.CleanCrunch;
        var channel = Assert.IsType<ChannelSwitchStageDesign>(design.Preamp[1]);
        Assert.Equal(SwitchRole.Switch1, channel.Switch);
        foreach (var chain in new[] { channel.Off, channel.On }) {
            Assert.Equal([2, 2, 1, 1], chain.Select(static s => s.SupplyNode));
            var gain = Elements(chain[0]);
            Assert.Equal(
                2,
                gain.Count(static e => e is { Role: PotRole.Gain, Value: 250e3 } && e.Part != PotPart.None)
            );
            Assert.Contains(gain, static e => e is { Kind: ElementKind.Resistor, Value: 250e3, Part: PotPart.None });
            Assert.Contains(gain, static e => e is { Kind: ElementKind.Capacitor, Value: 750e-12 });
            Assert.Contains(gain, static e => e is { Kind: ElementKind.Capacitor, Value: 4.7e-9 });
        }

        Assert.True(Has(channel.Off[1], ElementKind.Resistor, 68e3));
        Assert.False(Has(channel.Off[1], ElementKind.Resistor, 330e3));
        Assert.True(Has(channel.On[1], ElementKind.Resistor, 330e3));
        Assert.False(Has(channel.On[1], ElementKind.Resistor, 68e3));
        Assert.True(Has(channel.Off[3], ElementKind.Capacitor, 250e-12));
        Assert.True(Has(channel.Off[3], ElementKind.Resistor, 100e3));
        Assert.False(Has(channel.Off[3], ElementKind.Resistor, 43e3));
        Assert.True(Has(channel.On[3], ElementKind.Capacitor, 470e-12));
        Assert.True(Has(channel.On[3], ElementKind.Resistor, 47e3));
        Assert.Equal(2, Elements(channel.On[3]).Count(static e => e is { Kind: ElementKind.Resistor, Value: 43e3 }));

        var red = Evh5150III.Design.Power;
        Assert.Equal(0.020, design.Power.IdleCurrent);
        Assert.Equal(red with { IdleCurrent = 0.020, PowerSupply = design.Power.PowerSupply }, design.Power);
        var (x, w) = (design.PreampSupply!.Nodes[0], design.PreampSupply.Nodes[1]);
        Assert.Equal((5e3, 47e-6, 0), (x.Resistance, x.Cap, x.Parent));
        Assert.Equal((10e3, 22e-6, 1), (w.Resistance, w.Cap, w.Parent));
        Assert.Equal(7.8e-3, x.Current, 1e-9);
        Assert.Equal(4.4e-3, w.Current, 1e-9);

        static IReadOnlyList<StageElement> Elements(AmpStageDesign stage) =>
            Assert.IsType<NetworkStageDesign>(stage).Netlist.Elements;

        static bool Has(AmpStageDesign stage, ElementKind kind, double value) =>
            Elements(stage).Any(e => e.Kind == kind && e.Value == value && e.Part == PotPart.None);
    }

    /// <summary>
    ///     Issue #306, on the block: small-signal at 1 kHz with the knobs alike, channel two has more gain than channel
    ///     one and the red type far more than either; at a guitar's level with the gain up, channel two distorts where
    ///     channel one stays nearly clean.
    /// </summary>
    [Fact]
    public void The_5150III_crunch_channel_has_more_gain_than_the_clean_and_less_than_the_red() {
        const string Type = "5150III Clean/Crunch";
        (int, float) gainAt5 = (CircuitAmpBlock.Gain, 5f);
        var clean = Small(Type, 1_000, gainAt5);
        var crunch = Small(Type, 1_000, gainAt5, (CircuitAmpBlock.Switch1, 1f));
        var red = Small("5150III", 1_000, gainAt5);
        output.WriteLine($"1 kHz, gain 5: clean {clean:0.0} dB, crunch {crunch:0.0} dB, red {red:0.0} dB");
        Assert.True(crunch - clean > 6, $"channel two is only {crunch - clean:0.0} dB over channel one");
        Assert.True(red - crunch > 10, $"the red channel is only {red - crunch:0.0} dB over channel two");

        var (cleanThd, crunchThd) = (Distortion(0f), Distortion(1f));
        output.WriteLine($"THD at 20 mV, gain 5: clean {100 * cleanThd:0.0} %, crunch {100 * crunchThd:0.0} %");
        Assert.True(crunchThd > 3 * cleanThd, $"crunch {100 * crunchThd:0.0} %, clean {100 * cleanThd:0.0} %");

        // A coherent sine (bin 171 of 8192: 1002 Hz, above channel two's 750 pF coupling) with the volume low, so
        // that the power amp stays clean.
        static double Distortion(float crunchOn) {
            using var block = Create(
                Type,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Switch1, crunchOn)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(171 * Rate / 8_192, Rate, 32_768, 0.02f), 64);
            return Analysis.Thd(rendered.AsSpan(rendered.Length - 8_192), 171);
        }
    }

    /// <summary>
    ///     Issue #201: the 5150's rhythm channel as the factory pages draw it with their relays at rest. V1B's cathode
    ///     has C26's 22 µF beside the 1 µF and V2A's grid R20's 220k beside the 1M (K1), which the lead channel has
    ///     neither of; Crunch (K4) swaps 470k ‖ 270 pF for 2.2M ‖ 470 pF and lifts the 39k shunt; Bright closes 470
    ///     pF across the 1M.
    /// </summary>
    [Fact]
    public void The_5150_rhythm_channel_has_the_scans_parts() {
        var channel = Assert.IsType<ChannelSwitchStageDesign>(Peavey5150.Design.Preamp[1]);
        Assert.Equal(SwitchRole.Switch1, channel.Switch);
        var lead = Assert.IsType<NetworkStageDesign>(channel.Off[0]).Netlist.Elements;
        var rhythm = Assert.IsType<NetworkStageDesign>(channel.On[0]).Netlist.Elements;
        Assert.Contains(rhythm, static e => e is { Kind: ElementKind.Capacitor, Value: 22e-6, B: StageNetlist.Ground });
        Assert.DoesNotContain(lead, static e => e is { Kind: ElementKind.Capacitor, Value: 22e-6 });
        Assert.Contains(rhythm, static e => e is { Value: 2.2e6, OffValue: 470e3, Switch: SwitchRole.Switch2 });
        Assert.Contains(rhythm, static e => e is { Value: 470e-12, OffValue: 270e-12, Switch: SwitchRole.Switch2 });
        Assert.Contains(rhythm, static e => e is { OffValue: 39e3, Switch: SwitchRole.Switch2 });
        Assert.Contains(rhythm, static e => e is { Kind: ElementKind.Resistor, Switch: SwitchRole.Bright });
        Assert.DoesNotContain(lead, static e => e.Switch is not null);

        Assert.Equal(1e6, Assert.IsType<TriodeStageDesign>(channel.Off[1]).GridLeak);
        Assert.Equal(1e6 * 220e3 / (1e6 + 220e3), Assert.IsType<TriodeStageDesign>(channel.On[1]).GridLeak, 1e-6);
    }

    /// <summary>
    ///     Issue #201, on the block: small-signal at 1 kHz the rhythm channel is far below the lead (its pre gain
    ///     sits behind 470k and 1M with 39k to ground), Crunch gives most of that back, and Crunch and Bright do
    ///     nothing while the lead channel plays. Bright lifts the treble of the rhythm channel at a low gain setting
    ///     and leaves its lows within 1 dB (at 100 Hz the closed cap's current leads the 39k + 0.1 µF shunt's, and
    ///     the level falls by half a dB).
    /// </summary>
    [Fact]
    public void The_5150_rhythm_channel_is_clean_until_crunch_and_bright_lifts_its_treble() {
        static double Level(double frequency, params (int Index, float Value)[] settings) =>
            Small("5150", frequency, [(CircuitAmpBlock.Gain, 3f), ..settings]);

        (int, float) rhythmOn = (CircuitAmpBlock.Switch1, 1f);
        (int, float) crunchOn = (CircuitAmpBlock.Switch2, 1f);
        (int, float) brightOn = (CircuitAmpBlock.Bright, 1f);
        var (lead, rhythm, crunch) = (Level(1_000), Level(1_000, rhythmOn), Level(1_000, rhythmOn, crunchOn));
        output.WriteLine($"1 kHz: lead {lead:0.0} dB, rhythm {rhythm:0.0} dB, with Crunch {crunch:0.0} dB");
        Assert.True(lead - rhythm > 20, $"the lead is only {lead - rhythm:0.0} dB over the rhythm channel");
        Assert.InRange(crunch - rhythm, 10, 35);
        Assert.True(lead > crunch, "Crunch has more gain than the lead");
        Assert.Equal(lead, Level(1_000, crunchOn, brightOn), 6);

        var high = Level(5_000, rhythmOn, brightOn) - Level(5_000, rhythmOn);
        var low = Level(100, rhythmOn, brightOn) - Level(100, rhythmOn);
        output.WriteLine($"Bright on the rhythm channel: {high:+0.0;-0.0} dB at 5 kHz, {low:+0.0;-0.0} dB at 100 Hz");
        Assert.True(high > 3, "Bright does not lift the treble");
        Assert.InRange(low, -1, 0.5);
    }

    /// <summary>
    ///     Issue #199: on the overdrive channel the stage after the cold clipper sits behind the node the normal
    ///     channel joins, so the cold clipper (about 100k of output resistance) works into the 330k to ground there
    ///     (254k with the two 2.2M) and loses 2.9 dB into it at 1 kHz. Without the 330k it lost 0.35 dB into 2.4M.
    /// </summary>
    [Fact]
    public void Slo_overdrive_loads_the_cold_clipper_with_the_shared_nodes_330k() {
        var channel = Assert.IsType<ChannelSwitchStageDesign>(Slo100.Design.Preamp[1]);
        var design = channel.Off.Single(static s => s.Name == "V2B");

        double Level(double sourceResistance) {
            var stage = design.Create(Rate);
            stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false));
            stage.SetSourceResistance(sourceResistance);
            stage.Reset(0);
            var (low, high) = (double.MaxValue, double.MinValue);
            for (var n = 0; n < 48_000; n++) {
                var y = stage.Tick(0.01 * Math.Sin(2 * Math.PI * 1_000 * n / Rate));
                if (n >= 24_000) {
                    (low, high) = (Math.Min(low, y), Math.Max(high, y));
                }
            }

            return high - low;
        }

        var lossDb = 20 * Math.Log10(Level(1) / Level(100e3));
        output.WriteLine($"loss into the join node from 100k: {lossDb:0.00} dB");
        Assert.InRange(lossDb, 2.6, 3.2);
    }

    /// <summary>
    ///     Issue #200: <see cref="LongTailPairDesign.TailResistor" /> is the whole DC path from the bias junction to
    ///     ground, so on the amps whose tail resistor stands on the feedback shunt it is the drawing's tail resistor
    ///     (<paramref name="drawnTail" />) plus the shunt (and the leg under it on the Marks' series presence). Eight
    ///     of these once gave the tail resistor alone and idled their inverters 15 to 30 % hot.
    /// </summary>
    [Theory]
    [InlineData("SLO-100", 10e3)]
    [InlineData("5150", 10e3)]
    [InlineData("5150III", 10e3)]
    [InlineData("Dual Rectifier", 10e3)]
    [InlineData("Dual Rectifier 3-Ch", 10e3)]
    [InlineData("Triple Rectifier", 10e3)]
    [InlineData("Rect-o-Verb", 10e3)]
    [InlineData("Powerball", 22e3)]
    [InlineData("Savage 120", 22e3)]
    [InlineData("Fireball 60", 22e3)]
    [InlineData("Mark IIC+", 22e3)]
    [InlineData("Mark IV", 22e3)]
    [InlineData("Blues Junior", 33e3)]
    [InlineData("Liverpool", 39e3)]
    public void Long_tail_pairs_rest_on_the_tail_resistor_and_the_feedback_shunt(string name, double drawnTail) {
        var power = AmpDesign.All.Single(a => a.Name == name).Power;
        var pair = Assert.IsType<LongTailPairDesign>(power.PhaseInverter);
        var foot = power.Feedback.ShuntResistor + (power.Feedback.Series?.ShuntLegResistor ?? 0);
        Assert.Equal(drawnTail + foot, pair.TailResistor, 1e-6);

        // The rest point is the one that tail gives: the junction sits at the tail current times the whole tail.
        var inverter = LongTailPair.For(pair);
        var current = inverter.RestCurrents.A + inverter.RestCurrents.B;
        Assert.Equal(inverter.RestGrid, current * (drawnTail + foot), 1e-3);
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, and Newton converging.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Depth, CircuitAmpBlock.Master, CircuitAmpBlock.Control1,
            CircuitAmpBlock.Control2
        ];
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        int[] toggles =
            [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3, CircuitAmpBlock.Bright];
        foreach (var toggle in toggles) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        var solvers = channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!)
            .Append(channel.Power.Newton);
        foreach (var newton in solvers) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>Below clipping, the gain and master knobs raise the level monotonically, as their pots do.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Gain_and_master_raise_the_level_monotonically(string type) {
        double Level(int knob, float value) {
            var gain = knob == CircuitAmpBlock.Gain ? value : 3f;
            var master = knob == CircuitAmpBlock.Master ? value : 3f;
            using var block = Create(type, (CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, master));
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v)).ToArray();
            output.WriteLine($"{type} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    /// <summary>
    ///     The 5150III's presence network as the 100S redraw draws it: 39k into a 4.7k shunt, and across the shunt a
    ///     10k audio pot in series with two 68 nF in parallel (136 nF).
    /// </summary>
    [Fact]
    public void The_5150III_presence_cap_is_the_redraws_two_68_nF_in_parallel() {
        var feedback = Evh5150III.Design.Power.Feedback;
        Assert.Equal((39e3, 4.7e3), (feedback.FeedbackResistor, feedback.ShuntResistor));
        Assert.Equal(new(10e3, PotTaper.Audio), feedback.Presence);
        Assert.Equal(2 * 68e-9, feedback.PresenceCap, 1e-15);
    }

    /// <summary>The Rectifier's tube rectifier and Spongy each lower B+ under load; together, more.</summary>
    [Fact]
    public void Rectifier_switches_lower_the_supply() {
        static double Supply(bool tube, bool spongy) {
            using var block = Create(
                "Dual Rectifier",
                (CircuitAmpBlock.Gain, 8f),
                (CircuitAmpBlock.Master, 10f),
                (CircuitAmpBlock.Switch2, tube ? 1f : 0f),
                (CircuitAmpBlock.Switch3, spongy ? 1f : 0f)
            );
            BlockRender.Mono(block, Signals.Sine(110, Rate, 24_000, 0.5f), 64);
            return block.Channel.Power.Supply;
        }

        var (silicon, tube) = (Supply(false, false), Supply(true, false));
        var (spongy, both) = (Supply(false, true), Supply(true, true));
        output.WriteLine(
            $"B+ under drive: silicon {silicon:0.0} V, 5U4 {tube:0.0} V, Spongy {spongy:0.0} V, both {both:0.0} V"
        );
        Assert.True(tube < silicon - 10);
        Assert.True(spongy < silicon - 20);
        Assert.True(both < Math.Min(tube, spongy));
    }

    /// <summary>
    ///     The Mark IIC+'s two Shift pulls are independent, as on the amp: Treble Shift (the 750 pF) lifts the stack's
    ///     upper mids and leaves the lows, Bass Shift (V1A's 15 µF bypass) lifts the lows; with both on, both act.
    /// </summary>
    [Fact]
    public void Mark_IIC_treble_and_bass_shift_are_separate_switches() {
        static (double Low, double Mid) Levels(float treble, float bass) {
            var tones = Signals.Sine(60, Rate, 24_000, 2e-4f)
                .Zip(Signals.Sine(700, Rate, 24_000, 2e-4f), static (a, b) => a + b)
                .ToArray();
            using var block = Create(
                "Mark IIC+",
                (CircuitAmpBlock.Gain, 1f),
                (CircuitAmpBlock.Control1, 2f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Switch1, treble),
                (CircuitAmpBlock.Switch3, bass)
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return (
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 60, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 700, Rate))
            );
        }

        var off = Levels(0, 0);
        var treble = Levels(1, 0);
        var bass = Levels(0, 1);
        var both = Levels(1, 1);
        output.WriteLine(
            "60 Hz / 700 Hz against off: "
            + $"Treble Shift {treble.Low - off.Low:+0.0;-0.0} / {treble.Mid - off.Mid:+0.0;-0.0}"
            + $" dB, Bass Shift {bass.Low - off.Low:+0.0;-0.0} / {bass.Mid - off.Mid:+0.0;-0.0}"
            + $" dB, both {both.Low - off.Low:+0.0;-0.0} / {both.Mid - off.Mid:+0.0;-0.0} dB"
        );
        Assert.True(treble.Mid - off.Mid > 1, "Treble Shift does not lift the mids");
        Assert.True(bass.Low - off.Low > 1, "Bass Shift does not lift the lows");
        Assert.True(Math.Abs(bass.Mid - off.Mid) < Math.Abs(treble.Mid - off.Mid), "Bass Shift moves the mids");
        Assert.True(both.Low - treble.Low > 1, "Bass Shift does nothing with Treble Shift on");
        Assert.True(both.Mid - bass.Mid > 1, "Treble Shift does nothing with Bass Shift on");
    }

    /// <summary>The Mark's graphic EQ: a band at +12 dB lifts its frequency, at 0 dB the path is untouched.</summary>
    [Fact]
    public void Mark_graphic_equaliser_lifts_its_band() {
        static double Level(float band, double frequency) {
            using var block = Create(
                "Mark IIC+",
                (CircuitAmpBlock.Gain, 1f),
                (CircuitAmpBlock.Control1, 2f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Eq1 + 2, band)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        var lift = Level(12, 750) - Level(0, 750);
        var off = Level(12, 6_000) - Level(0, 6_000);
        output.WriteLine($"750 Hz band at +12 dB: {lift:0.0} dB at 750 Hz, {off:0.0} dB at 6 kHz");
        Assert.InRange(lift, 8, 13);
        Assert.InRange(Math.Abs(off), 0, 2);
    }

    /// <summary>
    ///     The Marks' presence is a 250k rheostat in series with the feedback
    ///     (<see cref="FeedbackDesign.Series" />): at 0 the network feeds back nearly a third of the tap's treble, the
    ///     most of any model. Flat out (every gain and
    ///     master at 10) at both ends of the knob, and at 0 with the Feedback expert parameter at 10 (twice the stock
    ///     feedback, 62 % of the treble), a loud chord renders finite and within 12 dB of the knob at noon, and two
    ///     seconds after it stops the amp is silent: the loop does not oscillate, small-signal or driven (what is left
    ///     half a second after the stop, -60 to -70 dB, is the preamp's slow recovery, falling alike at every setting).
    ///     Small-signal, turning the knob up is brighter, as on the amp: more series resistance is less feedback, and
    ///     less of the 4.7 nF's treble feedback.
    /// </summary>
    [Theory]
    [InlineData("Mark IIC+")]
    [InlineData("Mark IV")]
    public void Mark_series_presence_is_stable_at_its_extremes_and_brighter_turned_up(string type) {
        var chord = new float[(int)(2.5 * Rate)];
        for (var n = 0; n < (int)(0.5 * Rate); n++) {
            var t = n / Rate;
            chord[n] = (float)(0.4
                * (Math.Sin(2 * Math.PI * 82.4 * t)
                    + Math.Sin(2 * Math.PI * 123.5 * t)
                    + Math.Sin(2 * Math.PI * 164.8 * t))
                / 3);
        }

        (float Presence, float Feedback)[] cases = [(5, 5), (0, 5), (10, 5), (0, 10)];
        var peaks = new double[cases.Length];
        for (var i = 0; i < cases.Length; i++) {
            var (presence, feedback) = cases[i];
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 10f),
                (CircuitAmpBlock.Control1, 10f),
                (CircuitAmpBlock.Control2, 10f),
                (CircuitAmpBlock.Master, 10f),
                (CircuitAmpBlock.Presence, presence),
                (CircuitAmpBlock.Feedback, feedback)
            );
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type} at presence {presence}: not finite");
            peaks[i] = Analysis.Peak(rendered);
            var tail = Analysis.ToDb(Analysis.Rms(rendered.AsSpan(rendered.Length - (int)(0.1 * Rate))));
            output.WriteLine($"presence {presence}, feedback {feedback}: peak {peaks[i]:0.000}, tail {tail:0.0} dB");
            Assert.True(tail < -100, $"{type} at presence {presence}: {tail:0.0} dB two seconds after the chord");
            Assert.InRange(Analysis.ToDb(peaks[i] / peaks[0]), -12, 12);
        }

        var dark = Level(type, 0, 5_000) - Level(type, 0, 500);
        var bright = Level(type, 10, 5_000) - Level(type, 10, 500);
        output.WriteLine($"5 kHz against 500 Hz: {dark:0.0} dB at presence 0, {bright:0.0} dB at 10");
        Assert.True(bright - dark > 6, $"{type}: presence 10 is only {bright - dark:0.0} dB brighter than 0");

        static double Level(string type, float presence, double frequency) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 1f),
                (CircuitAmpBlock.Control1, 1f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Presence, presence)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }
    }

    /// <summary>The level in dB of a 0.1 mV sine through the block, with the master at 3.</summary>
    static double Small(string type, double frequency, params (int Index, float Value)[] settings) {
        using var block = Create(type, [(CircuitAmpBlock.Master, 3f), ..settings]);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 1e-4f), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }

    static int TypeOf(string name) => AmpDesign.All.ToList().FindIndex(a => a.Name == name);

    static CircuitAmpBlock Create(string type, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, TypeOf(type));
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static string Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }

    static IEnumerable<string> Switches(string type) =>
        new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, type));
}
