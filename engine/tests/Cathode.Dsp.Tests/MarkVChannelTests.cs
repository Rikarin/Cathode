using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Mark V's three channels as three types (Mark V, Mark V Clean, Mark V Crunch), from the factory drawing set
///     the model file cites: the modes each channel's relays make, the loop's recovery stage V6B with the Output
///     control after the graphic EQ in all three, and the driver's feedback with the playing channel's presence
///     rheostat in series.
/// </summary>
public class MarkVChannelTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Types = ["Mark V", "Mark V Clean", "Mark V Crunch"];

    [Fact]
    public void Panels_name_each_channels_modes() {
        Assert.Equal(["Fat", "Tweed", "Bold"], Switches("Mark V Clean"));
        Assert.Equal(["Edge", "Mark I", string.Empty], Switches("Mark V Crunch"));
        foreach (var type in Types) {
            Assert.Equal("Output", Label("master", type));
            Assert.Equal("Master", Label("control1", type));
            Assert.Equal("Presence", Label("presence", type));
            Assert.Equal("80", Label("eq1", type));
            Assert.Empty(Label("bright", type));
        }
    }

    /// <summary>
    ///     Every channel ends in the graphic EQ and V6B: the Send Level pot's 25k as two fixed parts over 1.5k, 470 Ω
    ///     and 47k at the grid, 120k from 380 V, 1k, then 100k, 0.68 µF and 220k into the 1M Output control.
    /// </summary>
    [Theory]
    [InlineData("Mark V")]
    [InlineData("Mark V Clean")]
    [InlineData("Mark V Crunch")]
    public void Loop_recovery_stage_follows_the_equalizer(string type) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        Assert.IsType<EqualizerStageDesign>(design.Preamp[^2]);
        var recovery = Assert.IsType<NetworkStageDesign>(design.Preamp[^1]);
        Assert.Equal("V6B", recovery.Name);
        var parts = recovery.Netlist.Elements;
        var fixedResistors = parts
            .Where(static e => e.Kind == ElementKind.Resistor && e.Pot.Ohms == 0)
            .Select(static e => e.Value)
            .ToArray();
        // An audio taper at 12:00: a tenth of the 25k under the wiper.
        Assert.Contains(22.5e3, fixedResistors);
        Assert.Contains(2.5e3, fixedResistors);
        foreach (var ohms in (double[])[1.5e3, 470, 47e3, 120e3, 1e3, 100e3, 220e3]) {
            Assert.Contains(ohms, fixedResistors);
        }

        Assert.Contains(parts, static e => e is { Kind: ElementKind.Supply, Value: 380 });
        Assert.Contains(parts, static e => e is { Kind: ElementKind.Capacitor, Value: 0.68e-6 });
        Assert.Contains(parts, static e => e.Role == PotRole.Master && e.Pot.Ohms == 1e6);
    }

    /// <summary>
    ///     The driver, from the Loop/Driver sheet: the 22k tail on 3.3k over 1.5k, and the Mark IV's series presence
    ///     network behind 4.7k from the tap (2.7k more on channel 2). Channel 3 alone has the 1 nF in series with its
    ///     rheostat, and its relay puts it in in Extreme, whatever Presence Shift says.
    /// </summary>
    [Fact]
    public void Driver_has_the_drawn_tail_and_each_channels_presence() {
        foreach (var type in Types) {
            var power = AmpDesign.All.Single(a => a.Name == type).Power;
            var pair = Assert.IsType<LongTailPairDesign>(power.PhaseInverter);
            Assert.Equal(26.8e3, pair.TailResistor);
            Assert.Equal(250e3, power.Feedback.Presence.Ohms);
            Assert.Equal(type == "Mark V Crunch" ? 10.7e3 : 8e3, power.Feedback.FeedbackResistor);
            var series = Assert.NotNull(power.Feedback.Series);
            Assert.Equal((56e3, 3.3e3, 47e-9), (series.LegResistor, series.ShuntLegResistor, series.ShuntLegCap));
            Assert.Equal(type == "Mark V" ? 1e-9 : 0, series.ShiftCap);
            Assert.Equal(type == "Mark V", power.ShiftUnless is not null);
        }

        static float[] Render(float iic, float shift) {
            using var block = Create(
                "Mark V",
                (CircuitAmpBlock.Switch1, iic),
                (CircuitAmpBlock.PresenceShift, shift),
                (CircuitAmpBlock.Gain, 3f)
            );
            return BlockRender.Mono(block, Signals.Sine(196, Rate, 12_000, 0.02f), 64);
        }

        Assert.Equal(Render(0, 0), Render(0, 1));
        Assert.Equal(Render(1, 0), Render(1, 1));
    }

    /// <summary>The presence rheostat is in series with the feedback: turning it up lifts the treble.</summary>
    [Theory]
    [InlineData("Mark V", 1f)]
    [InlineData("Mark V Clean", 0f)]
    [InlineData("Mark V Crunch", 0f)]
    public void Presence_lifts_the_treble(string type, float switch1) {
        double At(float presence) =>
            Level(
                type,
                5_000,
                (CircuitAmpBlock.Switch1, switch1),
                (CircuitAmpBlock.Gain, 2f),
                (CircuitAmpBlock.Control1, 2f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Presence, presence)
            );

        var (dark, bright) = (At(0f), At(10f));
        output.WriteLine($"{type}: 5 kHz with Presence 0 / 10: {dark:0.0} / {bright:0.0} dB");
        Assert.True(bright > dark + 1);
    }

    /// <summary>
    ///     Channel 1's modes at the same knobs. Tweed feeds V3A through 475k where Clean has 3.3M and takes the gain
    ///     pot's wiper straight to V1B: louder. Fat puts 15 µF across V1A's cathode: more of the lows, against the
    ///     mids, than Clean. Bold halves the 150k ahead of the master: louder.
    /// </summary>
    [Fact]
    public void Clean_channel_modes_follow_the_relays() {
        static double At(double hertz, float fat, float tweed, float bold) =>
            Level(
                "Mark V Clean",
                hertz,
                (CircuitAmpBlock.Switch1, fat),
                (CircuitAmpBlock.Switch2, tweed),
                (CircuitAmpBlock.Switch3, bold),
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Control1, 3f),
                (CircuitAmpBlock.Master, 2f)
            );

        var (clean, tweed, bold) = (At(1_000, 0, 0, 0), At(1_000, 0, 1, 0), At(1_000, 0, 0, 1));
        var (cleanLows, fatLows) = (At(60, 0, 0, 0) - clean, At(60, 1, 0, 0) - At(1_000, 1, 0, 0));
        output.WriteLine(
            $"1 kHz: Clean {clean:0.0} dB, Tweed {tweed:0.0} dB, Bold {bold:0.0} dB; 60 Hz against 1 kHz: Clean "
            + $"{cleanLows:0.0} dB, Fat {fatLows:0.0} dB"
        );
        Assert.True(tweed > clean + 3);
        Assert.True(bold > clean + 2);
        Assert.True(fatLows > cleanLows + 1.5);
    }

    /// <summary>
    ///     Channel 2's modes at the same knobs. Edge, behind 475k ahead of its stack and with V1A's smaller plate
    ///     load, has the least gain. Mark I has 15 µF across V2A's cathode, where Crunch has only 0.22 µF through
    ///     1.5k, and a 250k slope resistor: more of the lows, against the mids, than Crunch.
    /// </summary>
    [Fact]
    public void Crunch_channel_modes_follow_the_relays() {
        static double At(double hertz, float edge, float markI) =>
            Level(
                "Mark V Crunch",
                hertz,
                (CircuitAmpBlock.Switch1, edge),
                (CircuitAmpBlock.Switch2, markI),
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Control1, 2f),
                (CircuitAmpBlock.Master, 2f)
            );

        var (edge, crunch, markI) = (At(1_000, 1, 0), At(1_000, 0, 0), At(1_000, 0, 1));
        var (crunchLows, markLows) = (At(100, 0, 0) - crunch, At(100, 0, 1) - markI);
        output.WriteLine(
            $"1 kHz: Edge {edge:0.0} dB, Crunch {crunch:0.0} dB, Mark I {markI:0.0} dB; 100 Hz against 1 kHz: "
            + $"Crunch {crunchLows:0.0} dB, Mark I {markLows:0.0} dB"
        );
        Assert.True(edge < crunch - 3);
        Assert.True(edge < markI - 3);
        Assert.True(markLows > crunchLows + 1.5);
    }

    /// <summary>
    ///     Channel 2 has a gain stage more than channel 1 (V2A), so it has far more gain at the same knobs.
    /// </summary>
    [Fact]
    public void Crunch_channel_has_more_gain_than_the_clean_channel() {
        static double At(string type) =>
            Level(
                type,
                1_000,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Control1, 2f),
                (CircuitAmpBlock.Master, 1f)
            );

        var (clean, crunch) = (At("Mark V Clean"), At("Mark V Crunch"));
        output.WriteLine($"1 kHz: Mark V Clean {clean:0.0} dB, Mark V Crunch {crunch:0.0} dB");
        Assert.True(crunch > clean + 10);
        Assert.Equal(1, Triodes("Mark V Clean") - Triodes("Mark V Crunch") + 1);
    }

    /// <summary>The triodes in a type's preamp netlists and triode stages.</summary>
    static int Triodes(string type) =>
        AmpDesign.All.Single(a => a.Name == type)
            .Preamp.Sum(static s => s switch {
                    NetworkStageDesign n => n.Netlist.Elements.Count(static e => e.Kind == ElementKind.Triode),
                    TriodeStageDesign => 1,
                    _ => 0
                }
            );

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

    static double Level(string type, double hertz, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(hertz, Rate, 24_000, 1e-5f), 64);
        return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hertz, Rate));
    }

    static string Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }

    static string[] Switches(string type) =>
        [..new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, type))];
}
