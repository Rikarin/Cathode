using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The British vintage and boutique stream's preamps against the reference MNA simulator as whole chains (no Thévenin
///     cut): the tremolo-loaded and the shared-cathode, unequal-plate first stages, the Tiny Terror's dual gain pot and
///     the Cornford's channels (a channel switch's chain is checked through its stages).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, bool, int, double, double> BritishVintageChains =>
        new() {
            {
                "1959SLP",
                false,
                3,
                0.002,
                -30
            },
            {
                "1959T Tremolo",
                false,
                3,
                0.002,
                -30
            },
            {
                "Bluesbreaker 1962",
                false,
                3,
                0.002,
                -30
            },
            {
                "Bluesbreaker 1962",
                false,
                3,
                0.05,
                -20
            },
            {
                "Tiny Terror",
                false,
                2,
                0.002,
                -30
            },
            {
                "Tiny Terror",
                false,
                2,
                0.05,
                -20
            },
            {
                "Cornford MK50H II",
                false,
                5,
                0.002,
                -20
            },
            {
                "Cornford MK50H II",
                true,
                3,
                0.002,
                -30
            }
        };

    [Theory]
    [MemberData(nameof(BritishVintageChains))]
    public void British_vintage_chains_match_the_reference(
        string type,
        bool on,
        int count,
        double amplitude,
        double limitDb
    ) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var all = design.Preamp[0] is ChannelSwitchStageDesign channel ? on ? channel.On : channel.Off : design.Preamp;
        var designs = all.Take(count).ToArray();
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false);

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
            $"{type} {string.Join(" > ", designs.Select(static d => d.Name))}",
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
///     The British vintage and boutique models on the Circuit Amp block: their panels, knob sweeps without instability,
///     every control audible, and gain and master monotonic.
/// </summary>
public class BritishVintageAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = [
        "1959SLP", "1987X", "1959T Tremolo", "Bluesbreaker 1962", "Hiwatt DR504", "Cornford MK50H II", "Tiny Terror"
    ];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Loudness 1", Label("gain", "1959SLP"));
        Assert.Equal("Loudness 2", Label("control1", "1987X"));
        Assert.Equal("Volume II", Label("control1", "Bluesbreaker 1962"));
        Assert.Equal("Channel II", Label("switch2", "Bluesbreaker 1962"));
        Assert.Equal("Brilliant Vol", Label("gain", "Hiwatt DR504"));
        Assert.Equal("Overdrive", Label("control1", "Cornford MK50H II"));
        Assert.Equal("Resonance", Label("depth", "Cornford MK50H II"));
        Assert.Equal("Clean Ch", Label("switch1", "Cornford MK50H II"));
        Assert.Equal("Volume", Label("master", "Tiny Terror"));
        Assert.Empty(Label("treble", "Tiny Terror"));
        foreach (var type in Models.Where(static m => m != "Tiny Terror")) {
            Assert.Empty(Label("cut", type));
            Assert.NotEmpty(Label("treble", type));
        }

        foreach (var type in Models.Where(static m => m.StartsWith('1') || m.StartsWith('B'))) {
            Assert.Empty(Label("master", type));
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
    ///     the level or the spectrum of a two-tone signal by more than a fraction of a decibel somewhere. The Cornford's
    ///     Gain and Overdrive are checked on its Overdrive channel, its tone knobs on both.
    /// </summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Every_control_is_audible(string type) {
        var tones = Signals.Sine(110, Rate, 24_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 24_000, 0.02f), static (a, b) => a + b)
            .ToArray();

        double[] Bands(int control, float value, float channel) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 4f),
                (CircuitAmpBlock.Master, 4f),
                (CircuitAmpBlock.Presence, 5f),
                (CircuitAmpBlock.Switch1, channel),
                (control, value)
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return [
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 110, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 2_500, Rate))
            ];
        }

        float[] channels = type == "Cornford MK50H II" ? [0f, 1f] : [0f];
        foreach (var channel in channels) {
            foreach (var control in Controls(type)) {
                if (channel > 0
                    && control is CircuitAmpBlock.Gain or CircuitAmpBlock.Control1 or CircuitAmpBlock.Switch1) {
                    continue;
                }

                var (low, high) = IsSwitch(control) ? (0f, 1f) : (0f, 10f);
                var (a, b) = control == CircuitAmpBlock.Switch1
                    ? (Bands(control, low, low), Bands(control, high, high))
                    : (Bands(control, low, channel), Bands(control, high, channel));
                var change = Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));
                output.WriteLine(
                    $"{type} channel {channel} control {control}: 110 Hz {b[0] - a[0]:+0.0;-0.0} dB, 2.5 kHz {b[1] - a[1]:+0.0;-0.0} dB"
                );
                Assert.True(change > 0.2, $"{type}: control {control} moves the sound by only {change:0.00} dB");
            }
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

    /// <summary>
    ///     The 1959T's tremolo load on its normal channel: its first stage, against the same stage without the load (the
    ///     1959 Super Lead's), is about 6 dB quieter with the normal volume at noon.
    /// </summary>
    [Fact]
    public void The_tremolo_load_lowers_the_1959Ts_normal_channel() {
        double Level(AmpStageDesign design) {
            var stage = design.Create(Rate);
            stage.Update(new(0.5, 0.5, 0.5, 0.5, 1, false, Switch2: true));
            stage.SetSourceResistance(0);
            stage.Reset(0);
            var sine = Signals.Sine(1_000, Rate, 24_000, 1e-3f);
            var rendered = sine.Select(x => (float)(stage.Tick(x) - stage.RestOutput)).ToArray();
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        var tremolo = Level(AmpDesign.All.Single(static a => a.Name == "1959T Tremolo").Preamp[0]);
        var superLead = Level(AmpDesign.All.Single(static a => a.Name == "Plexi 100W").Preamp[0]);
        output.WriteLine($"1959T {tremolo:0.0} dB, Plexi 100W {superLead:0.0} dB");
        Assert.InRange(superLead - tremolo, 4, 8);
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
}
