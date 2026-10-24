using Cathode.Dsp.Amp;
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
///     first is a <see cref="TriodeStage" />, whose Thévenin resistance treats a partly bypassed cathode (1.8k ‖ 1 µF) as
///     grounded, and to -60 dB where it is a network stage, which uses the 1 kHz impedance. Driven hard, the cut's
///     linearised plate is the main error, worst behind a cold clipper with a plate bypass cap (SLO-100, 5150III) whose
///     next stage draws grid current: -17 and -9 dB there (-26 and -19 dB with the cap removed). The Rectifier's chain
///     was -60 dB while it followed a redraw without the plate cap; the factory drawing has 1 nF there, and it is -8 dB.
/// </remarks>
public partial class AmpStageTests {
    public static TheoryData<string, bool, double, double> ColdClipperChains =>
        new() {
            { "SLO-100", false, 0.002, -22 },
            { "SLO-100", false, 0.05, -14 },
            { "SLO-100", true, 0.002, -28 },
            { "SLO-100", true, 0.05, -28 },
            { "5150", false, 0.002, -25 },
            { "5150", false, 0.05, -20 },
            { "Dual Rectifier", false, 0.05, -5 },
            { "5150III", false, 0.05, -6 },
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
            $"{type}{(switched ? " (switched)" : string.Empty)} {string.Join(" > ", designs.Select(static d => d.Name))}",
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

    static readonly string[] Models = ["SLO-100", "Mark IIC+", "5150", "Dual Rectifier", "5150III", "Mark IV"];

    public static TheoryData<string> Types => [..Models];

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

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Volume", Label("gain", "Mark IIC+"));
        Assert.Equal("Lead Drive", Label("control1", "Mark IIC+"));
        Assert.Equal("Lead Master", Label("control2", "Mark IIC+"));
        Assert.Equal(["Shift", "Deep", "Lead Bright"], Switches("Mark IIC+"));
        Assert.Equal(["Fat", "Bright", "Mid Boost"], Switches("Mark IV"));
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

    /// <summary>The Rectifier's tube rectifier and Spongy each lower B+ under load; together, more.</summary>
    [Fact]
    public void Rectifier_switches_lower_the_supply() {
        double Supply(bool tube, bool spongy) {
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

    /// <summary>The Mark's graphic EQ: a band at +12 dB lifts its frequency, at 0 dB the path is untouched.</summary>
    [Fact]
    public void Mark_graphic_equaliser_lifts_its_band() {
        double Level(float band, double frequency) {
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
}
