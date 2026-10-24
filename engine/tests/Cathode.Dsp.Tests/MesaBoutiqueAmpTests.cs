using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Mesa and US boutique stream's preamps against the reference MNA simulator as whole chains (the first stages
///     of the playing channel joined in one circuit, no Thévenin cut): the new combinations (switched Rock/Jazz and Deep
///     networks, plate-to-cathode caps, LDR-switched Mesa voicings, the op-amp Dynamic Voice) checked in one go.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, bool, int, double, double> MesaBoutiqueChains =>
        new() {
            {
                "Liverpool",
                false,
                3,
                0.002,
                -30
            },
            {
                "Liverpool",
                false,
                3,
                0.05,
                -20
            },
            {
                "ODS 70s",
                false,
                2,
                0.01,
                -30
            },
            {
                "ODS 70s",
                true,
                4,
                0.002,
                -20
            },
            {
                "Dual Rectifier",
                false,
                4,
                0.002,
                -15
            },
            {
                "Dual Rectifier 3-Ch",
                false,
                4,
                0.002,
                -15
            },
            {
                "Subway Blues",
                false,
                3,
                0.002,
                -30
            },
            {
                "Subway Blues",
                false,
                3,
                0.05,
                -20
            },
            {
                "TriAxis Rhythm",
                false,
                3,
                0.002,
                -30
            },
            {
                "TriAxis Lead 1",
                false,
                4,
                0.002,
                -25
            },
            {
                "TriAxis Lead 2",
                false,
                5,
                0.002,
                -15
            },
            {
                "TriAxis Lead 2",
                false,
                5,
                0.05,
                -10
            }
        };

    /// <summary>
    ///     The first <paramref name="count" /> stages of the channel <paramref name="switched" /> selects (Switch 1 on: a
    ///     channel switch's other channel).
    /// </summary>
    [Theory]
    [MemberData(nameof(MesaBoutiqueChains))]
    public void Mesa_and_boutique_chains_match_the_reference(
        string type,
        bool switched,
        int count,
        double amplitude,
        double limitDb
    ) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var designs = design.Preamp
            .SelectMany(s => s is ChannelSwitchStageDesign c ? switched ? c.On : c.Off : [s])
            .Take(count)
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
///     The Mesa and US boutique models on the Circuit Amp block: their panels, knob sweeps without instability, every
///     control audible, gain and master monotonic, and levels below +6 dBFS.
/// </summary>
public class MesaBoutiqueAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = [
        "Liverpool", "ODS 70s", "Dual Rectifier", "Dual Rectifier 3-Ch", "Subway Blues", "TriAxis Rhythm",
        "TriAxis Lead 1", "TriAxis Lead 2"
    ];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal(["Bright", "Clarity", string.Empty, string.Empty], Switches("Liverpool"));
        Assert.Empty(Label("master", "Liverpool"));
        Assert.Equal(["Bright", "Overdrive", "Jazz", "Deep"], Switches("ODS 70s"));
        Assert.Equal("Accent", Label("presence", "ODS 70s"));
        Assert.Equal([string.Empty, "Modern", "Tube Rectifier", "Spongy"], Switches("Dual Rectifier"));
        Assert.Equal(["Raw", "Modern", "Tube Rectifier", "Spongy"], Switches("Dual Rectifier 3-Ch"));
        Assert.Equal("Presence", Label("control2", "Dual Rectifier"));
        foreach (var type in Models) {
            Assert.Empty(Label("cut", type));
            Assert.Empty(Label("depth", type));
        }
    }

    /// <summary>Every knob swept end to end and every switch flipped while a loud chord plays: finite, bounded, converging.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var control in Controls(type)) {
            float[] values = IsSwitch(control) ? [1f, 0f, 1f] : [0f, 10f, 5f];
            foreach (var value in values) {
                block.Params.SetTarget(control, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: control {control} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
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

    /// <summary>
    ///     Every control on the panel changes the sound: at a moderate setting, a knob from 0 to 10 or a switch on moves
    ///     the level or the spectrum of a three-tone signal by more than a fraction of a decibel somewhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Every_control_is_audible(string type) {
        var tones = Signals.Sine(110, Rate, 24_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 24_000, 0.02f), static (a, b) => a + b)
            .Zip(Signals.Sine(6_000, Rate, 24_000, 0.01f), static (a, b) => a + b)
            .ToArray();

        double[] Bands(int control, float value) {
            using var block = Create(
                type,
                [
                    (CircuitAmpBlock.Gain, 4f),
                    (CircuitAmpBlock.Master, 4f),
                    (CircuitAmpBlock.Presence, 5f),
                    ..Context(type, control),
                    (control, value)
                ]
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return [
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 110, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 2_500, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 6_000, Rate))
            ];
        }

        foreach (var control in Controls(type)) {
            var (low, high) = IsSwitch(control) ? (0f, 1f) : (0f, 10f);
            var (a, b) = (Bands(control, low), Bands(control, high));
            var change = a.Zip(b, static (x, y) => Math.Abs(x - y)).Max();
            output.WriteLine(
                $"{type} control {control}: 110 Hz {b[0] - a[0]:+0.0;-0.0} dB, 2.5 kHz {b[1] - a[1]:+0.0;-0.0} dB, "
                + $"6 kHz {b[2] - a[2]:+0.0;-0.0} dB"
            );
            Assert.True(change > 0.2, $"{type}: control {control} moves the sound by only {change:0.00} dB");
        }
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

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }.Where(k =>
                     Controls(type).Contains(k)
                 )) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v)).ToArray();
            output.WriteLine($"{type} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Length; i++) {
                if (levels[i - 1] > -100) {
                    Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
                }
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    /// <summary>The Rectifiers' Modern switch takes the global feedback out: the open-loop power amp is louder.</summary>
    [Theory]
    [InlineData("Dual Rectifier")]
    [InlineData("Dual Rectifier 3-Ch")]
    public void Modern_takes_the_feedback_out(string type) {
        double Level(bool modern) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 1f),
                (CircuitAmpBlock.Master, 3f),
                (CircuitAmpBlock.Switch1, modern ? 1f : 0f)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(400, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), 400, Rate));
        }

        var (vintage, modern) = (Level(false), Level(true));
        output.WriteLine($"{type}: Vintage {vintage:0.0} dB, Modern {modern:0.0} dB at 400 Hz");
        Assert.True(modern > vintage + 3);
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

    /// <summary>The knobs and switches a model's panel shows, as block parameter indices.</summary>
    static IEnumerable<int> Controls(string type) {
        (string Id, int Index)[] all = [
            ("gain", CircuitAmpBlock.Gain), ("bass", CircuitAmpBlock.Bass), ("middle", CircuitAmpBlock.Middle),
            ("treble", CircuitAmpBlock.Treble), ("presence", CircuitAmpBlock.Presence),
            ("depth", CircuitAmpBlock.Depth),
            ("cut", CircuitAmpBlock.Cut), ("master", CircuitAmpBlock.Master), ("bright", CircuitAmpBlock.Bright),
            ("switch1", CircuitAmpBlock.Switch1), ("switch2", CircuitAmpBlock.Switch2),
            ("switch3", CircuitAmpBlock.Switch3),
            ("control1", CircuitAmpBlock.Control1), ("control2", CircuitAmpBlock.Control2)
        ];
        return all.Where(c => !string.IsNullOrEmpty(Label(c.Id, type))).Select(static c => c.Index);
    }

    static bool IsSwitch(int index) =>
        index is CircuitAmpBlock.Bright
            or CircuitAmpBlock.Switch1
            or CircuitAmpBlock.Switch2
            or CircuitAmpBlock.Switch3;

    /// <summary>
    ///     The settings at which a control is checked for being audible: the channel or mode it belongs to (the
    ///     overdrive's knobs do nothing while the clean channel plays).
    /// </summary>
    static (int Index, float Value)[] Context(string type, int control) =>
        (type, control) switch {
            ("ODS 70s", CircuitAmpBlock.Control1 or CircuitAmpBlock.Control2) => [(CircuitAmpBlock.Switch1, 1f)],
            _ => []
        };

    static IEnumerable<string> Switches(string type) =>
        new[] { "bright", "switch1", "switch2", "switch3" }.Select(id => Label(id, type));
}
