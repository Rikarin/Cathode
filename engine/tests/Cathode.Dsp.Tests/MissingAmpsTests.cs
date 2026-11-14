using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The amps added from the 1.0.0-rc.5 audit's missing-amp inventory (the Blues Deluxe and DeVille, the Mesa Mark
///     I, the JCM900 SL-X, the Soldano Hot Rod and the Laney VH100R) against the reference MNA simulator as whole
///     chains: the stages each setting of the channel switch plays, joined in one circuit with no Thévenin cut. A
///     model whose last stage is a tone-stack stage is compared up to it (the chain takes the stages the reference
///     can build, which is every netlist and every triode stage).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, int, int, double, double> MissingAmpChains =>
        new() {
            // Switches as bits: 1 = Switch 1; then how many of the playing stages the chain takes. Two models are
            // cut short: the Soldano's chain stops at the cold clipper's plate, whose 1 nF bypass cap makes a poor
            // Thevenin source for the stage after it (-17 dB with V3A in, as the AFD100's five-triode chain), and
            // the Laney's before its own cold clipper, where the reference simulator's Newton fails ten times in
            // the run (this model's own converges on every sample, and the three-stage chain is -52 dB). The
            // Soldano's Normal channel takes -28 dB: its 1M feed resistor leaves V2A a source the cut matches
            // less well than the Overdrive network's.
            {
                "Blues Deluxe",
                0,
                9,
                0.002,
                -30
            },
            {
                "Blues Deluxe",
                1,
                9,
                0.01,
                -30
            },
            {
                "Blues DeVille",
                0,
                9,
                0.002,
                -30
            },
            {
                "Mark I",
                0,
                9,
                0.002,
                -30
            },
            {
                "Mark I",
                1,
                9,
                0.002,
                -30
            },
            {
                "JCM900 SL-X 2100",
                0,
                9,
                0.0005,
                -30
            },
            {
                "JCM900 SL-X 2100",
                1,
                9,
                0.0005,
                -30
            },
            {
                "Hot Rod 100",
                0,
                3,
                0.0005,
                -30
            },
            {
                "Hot Rod 100",
                1,
                3,
                0.002,
                -28
            },
            {
                "VH100R",
                0,
                2,
                0.0005,
                -30
            },
            {
                "VH100R",
                1,
                9,
                0.005,
                -30
            }
        };

    /// <summary>
    ///     The stages of <paramref name="designs" /> the reference can build, in order, each as a netlist: the
    ///     network stages as they are and the triode stages as the same circuit
    ///     (<see cref="TriodeStageDesign.ToNetwork" />), stopping at the first stage that is neither.
    /// </summary>
    static NetworkStageDesign[] Netlists(IEnumerable<AmpStageDesign> designs) => [
        ..designs.TakeWhile(static d => d is NetworkStageDesign or TriodeStageDesign)
            .Select(static d => d is TriodeStageDesign triode ? triode.ToNetwork() : (NetworkStageDesign)d)
    ];

    [Theory]
    [MemberData(nameof(MissingAmpChains))]
    public void Missing_amp_chains_match_the_reference(
        string type,
        int switches,
        int take,
        double amplitude,
        double limitDb
    ) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var controls = new AmpControls(
            0.5,
            0.5,
            0.5,
            0.5,
            0.5,
            false,
            Switch1: (switches & 1) != 0,
            Switch2: (switches & 2) != 0,
            Switch3: (switches & 4) != 0
        );
        var playing = Playing(design.Preamp, controls).ToArray();
        var designs = playing.TakeWhile(static d => d is NetworkStageDesign or TriodeStageDesign)
            .Take(take)
            .ToArray();
        var netlists = Netlists(designs);

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
        foreach (var netlist in netlists) {
            node = AmpReferences.Append(circuit, netlist, controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            $"{type} [{switches}] {string.Join(" > ", designs.Select(static d => d.Name))}",
            x => {
                foreach (var stage in stages) {
                    x = stage.Tick(x);
                }

                return x;
            },
            stages.Last(static s => s.Newton is not null).Newton,
            circuit,
            node,
            amplitude
        );
        Assert.True(errorDb < limitDb, $"{type} [{switches}]: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The same amps on the Circuit Amp block: their panels, and what each model's channel switch and its own
///     controls do.
/// </summary>
public class MissingAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Types = [
        "Blues Deluxe", "Blues DeVille", "Mark I", "JCM900 SL-X 2100", "JCM900 SL-X 2500", "Hot Rod 100",
        "Hot Rod 50", "VH100R"
    ];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        foreach (var type in new[] { "Blues Deluxe", "Blues DeVille" }) {
            Assert.Equal("Drive", Label("gain", type));
            Assert.Equal("Volume", Label("control1", type));
            Assert.Equal("Normal Ch", Label("switch1", type));
            Assert.Equal("Master", Label("master", type));
            foreach (var id in new[] { "depth", "cut", "switch2", "switch3", "control2" }) {
                Assert.Empty(Label(id, type));
            }
        }

        Assert.Equal(
            ["Volume 1", "Volume 2", "Gain Boost"],
            new[] { "gain", "control1", "switch1" }.Select(static id => Label(id, "Mark I"))
        );
        foreach (var id in new[] { "bright", "depth", "cut", "switch2", "control2" }) {
            Assert.Empty(Label(id, "Mark I"));
        }

        foreach (var type in new[] { "JCM900 SL-X 2100", "JCM900 SL-X 2500" }) {
            Assert.Equal(
                ["Master A", "Volume", "Master B", "Master B"],
                new[] { "master", "control1", "control2", "switch1" }.Select(id => Label(id, type))
            );
            foreach (var id in new[] { "bright", "depth", "cut", "switch2", "switch3" }) {
                Assert.Empty(Label(id, type));
            }
        }

        foreach (var type in new[] { "Hot Rod 100", "Hot Rod 50" }) {
            Assert.Equal("Normal Ch", Label("switch1", type));
            Assert.Equal("Master", Label("master", type));
            foreach (var id in new[] { "bright", "depth", "cut", "control1", "control2" }) {
                Assert.Empty(Label(id, type));
            }
        }

        Assert.Equal(
            ["Drive", "Clean Volume", "Clean Ch", "Bright"],
            new[] { "gain", "control1", "switch1", "bright" }.Select(static id => Label(id, "VH100R"))
        );
        foreach (var id in new[] { "depth", "cut", "switch2", "control2" }) {
            Assert.Empty(Label(id, "VH100R"));
        }
    }

    /// <summary>
    ///     The Blues Deluxe's two channels: the Drive channel has more gain than the Normal one at the same
    ///     settings, the Volume knob moves the Normal channel and not the Drive one (its relay lifts the pot), and
    ///     the Master moves the Drive channel and not the Normal one, as the relays on the drawing wire them.
    /// </summary>
    [Fact]
    public void Blues_deluxe_relays_give_each_channel_its_own_controls() {
        static double Level(bool normal, float volume, float master) =>
            LevelAt(
                "Blues Deluxe",
                440,
                0.0005f,
                (CircuitAmpBlock.Switch1, normal ? 1f : 0f),
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Control1, volume),
                (CircuitAmpBlock.Master, master)
            );

        var drive = Level(false, 5f, 5f);
        var normal = Level(true, 5f, 5f);
        output.WriteLine($"drive {drive:0.0}, normal {normal:0.0} dB");
        Assert.True(drive > normal + 6);

        var normalQuiet = Level(true, 1f, 5f);
        var driveVolume = Level(false, 1f, 5f);
        output.WriteLine($"volume at 1: normal {normalQuiet:0.0}, drive {driveVolume:0.0} dB");
        Assert.True(normal > normalQuiet + 6);
        Assert.True(Math.Abs(drive - driveVolume) < 0.5);

        var driveQuiet = Level(false, 5f, 1f);
        var normalMaster = Level(true, 5f, 1f);
        output.WriteLine($"master at 1: drive {driveQuiet:0.0}, normal {normalMaster:0.0} dB");
        Assert.True(drive > driveQuiet + 6);
        Assert.True(Math.Abs(normal - normalMaster) < 0.5);
    }

    /// <summary>
    ///     The Mark I's Gain Boost lifts the middle pot's foot off ground, which takes the stack's shunt leg out:
    ///     a large lift everywhere, and more of it at the bottom, where the shunt leg took the most (110 Hz against
    ///     750 Hz).
    /// </summary>
    [Fact]
    public void Mark_i_gain_boost_lifts_the_stack_and_its_bottom() {
        static double Level(float hz, bool boost) =>
            LevelAt(
                "Mark I",
                hz,
                0.0005f,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Master, 5f),
                (CircuitAmpBlock.Switch1, boost ? 1f : 0f)
            );

        var (plain, boosted) = (Level(750, false), Level(750, true));
        var tilt = boosted - plain - (Level(110, true) - Level(110, false));
        output.WriteLine($"750 Hz: {plain:0.0} to {boosted:0.0} dB; against 110 Hz {tilt:+0.0;-0.0} dB");
        Assert.True(boosted > plain + 3);
        Assert.True(tilt < -1);
    }

    /// <summary>
    ///     The SL-X's two master stages: with the Master B switch on, Control 2 is the master in circuit and the
    ///     Master knob does nothing; with it off, the other way round.
    /// </summary>
    [Fact]
    public void Slx_switch_picks_which_master_stage_plays() {
        static double Level(bool b, float master, float masterB) =>
            LevelAt(
                "JCM900 SL-X 2100",
                440,
                0.0002f,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Switch1, b ? 1f : 0f),
                (CircuitAmpBlock.Master, master),
                (CircuitAmpBlock.Control2, masterB)
            );

        var a = Level(false, 5f, 5f);
        var aQuiet = Level(false, 1f, 5f);
        var b = Level(true, 5f, 5f);
        var bQuiet = Level(true, 5f, 1f);
        output.WriteLine($"A {a:0.0} to {aQuiet:0.0} dB, B {b:0.0} to {bQuiet:0.0} dB");
        Assert.True(a > aQuiet + 6);
        Assert.True(b > bQuiet + 6);
        Assert.True(Math.Abs(Level(true, 1f, 5f) - b) < 0.5);
        Assert.True(Math.Abs(Level(false, 5f, 1f) - a) < 0.5);
    }

    /// <summary>
    ///     The Soldano Hot Rod's SSB switch: the Normal channel's 1M in series with 250 pF to ground is quieter and
    ///     darker than the Overdrive network (1 kHz against 110 Hz).
    /// </summary>
    [Fact]
    public void Soldano_hot_rod_normal_channel_is_quieter_and_darker() {
        static double Level(float hz, bool normal) =>
            LevelAt(
                "Hot Rod 100",
                hz,
                0.0005f,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Master, 5f),
                (CircuitAmpBlock.Switch1, normal ? 1f : 0f)
            );

        var (overdrive, normal) = (Level(1_000, false), Level(1_000, true));
        var tilt = Level(1_000, true) - Level(110, true) - (Level(1_000, false) - Level(110, false));
        output.WriteLine($"1 kHz: overdrive {overdrive:0.0}, normal {normal:0.0} dB; tilt {tilt:+0.0;-0.0} dB");
        Assert.True(overdrive > normal + 6);
        Assert.True(tilt < 0);
    }

    /// <summary>
    ///     The VH100R's channels: the Overdrive channel has far more gain than the Clean one, the Clean Volume
    ///     moves the Clean channel only, and its Bright switch lifts the treble there (4 kHz against 440 Hz).
    /// </summary>
    [Fact]
    public void Laney_vh100r_channels_and_bright_switch() {
        static double Level(float hz, bool clean, float volume, bool bright) =>
            LevelAt(
                "VH100R",
                hz,
                0.0002f,
                (CircuitAmpBlock.Gain, 5f),
                (CircuitAmpBlock.Master, 5f),
                (CircuitAmpBlock.Switch1, clean ? 1f : 0f),
                (CircuitAmpBlock.Control1, volume),
                (CircuitAmpBlock.Bright, bright ? 1f : 0f)
            );

        var drive = Level(440, false, 5f, false);
        var clean = Level(440, true, 5f, false);
        var cleanQuiet = Level(440, true, 1f, false);
        output.WriteLine($"drive {drive:0.0}, clean {clean:0.0}, clean at volume 1 {cleanQuiet:0.0} dB");
        Assert.True(drive > clean + 6);
        Assert.True(clean > cleanQuiet + 6);
        Assert.True(Math.Abs(Level(440, false, 1f, false) - drive) < 0.5);

        var lift = Level(4_000, true, 5f, true) - Level(4_000, true, 5f, false);
        var lows = Level(440, true, 5f, true) - clean;
        output.WriteLine($"bright at 4 kHz {lift:+0.0;-0.0} dB, at 440 Hz {lows:+0.0;-0.0} dB");
        Assert.True(lift > lows + 1);
    }

    /// <summary>Every new model passes signal at its defaults and its gain knob is monotonic.</summary>
    [Theory]
    [MemberData(nameof(TypeNames))]
    public void Gain_and_master_are_monotonic(string type) {
        var levels = new[] { 1f, 4f, 7f, 10f }
            .Select(g => LevelAt(type, 440, 0.0005f, (CircuitAmpBlock.Gain, g), (CircuitAmpBlock.Master, 5f)))
            .ToArray();
        output.WriteLine($"{type}: {string.Join(", ", levels.Select(static l => $"{l:0.0}"))} dB");
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] >= levels[i - 1] - 0.5, $"{type}: gain {i} is not monotonic");
        }

        Assert.True(levels[^1] > levels[0] + 3, $"{type}: the gain knob does little");
    }

    public static TheoryData<string> TypeNames => [..Types];

    static double LevelAt(string type, float hz, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hz, Rate));
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
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] ?? string.Empty : parameter.Name;
    }
}
