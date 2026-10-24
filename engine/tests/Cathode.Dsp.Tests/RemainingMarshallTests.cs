using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The remaining Marshalls (stream M1) against the reference MNA simulator as whole chains: the stages each setting
///     of the channel and mode switches plays (nested <see cref="ChannelSwitchStageDesign" />s resolved), joined in one
///     circuit with no Thévenin cut; ideal buffers as unity sources.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, int, double, double> RemainingMarshallChains =>
        new() {
            // Switches as bits: 1 = Switch 1, 2 = Switch 2, 4 = Switch 3.
            { "JVM410H OD", 0, 0.0005, -30 },
            { "JVM410H OD", 2, 0.0001, -30 },
            { "JVM410H OD", 4, 0.0001, -30 },
            { "JVM410H OD", 5, 0.0001, -30 },
            { "JVM410H Clean/Crunch", 0, 0.005, -30 },
            { "JVM410H Clean/Crunch", 2, 0.0005, -30 },
            { "JVM410H Clean/Crunch", 4, 0.0005, -30 },
            { "JVM410H Clean/Crunch", 1, 0.005, -30 },
            { "JVM410H Clean/Crunch", 3, 0.001, -30 },
            { "JVM410H Clean/Crunch", 5, 0.0005, -30 }
        };

    /// <summary>The stages <paramref name="controls" /> play, nested channel switches resolved.</summary>
    internal static IEnumerable<AmpStageDesign> Playing(IEnumerable<AmpStageDesign> stages, AmpControls controls) =>
        stages.SelectMany(d => d is ChannelSwitchStageDesign channel
            ? Playing(controls.IsOn(channel.Switch) ? channel.On : channel.Off, controls)
            : [d]
        );

    [Theory]
    [MemberData(nameof(RemainingMarshallChains))]
    public void Remaining_marshall_chains_match_the_reference(
        string type,
        int switches,
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
        var designs = Playing(design.Preamp, controls).ToArray();

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
            if (d is NetworkStageDesign network) {
                node = AmpReferences.Append(circuit, network, controls, PreampSettings.Stock, node);
            } else {
                var buffered = circuit.Node();
                circuit.Vcvs(buffered, 0, node, 0, 1);
                node = buffered;
            }
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
///     The remaining Marshalls on the Circuit Amp block: their panels, knob sweeps at full drive in every channel and
///     mode without instability, every control audible, gain and master monotonic, levels below +6 dBFS.
/// </summary>
public class RemainingMarshallTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>Each type with the switch settings (bits: Switch 1, 2, 3) that select its channels and modes.</summary>
    static readonly (string Type, int[] Settings)[] Models = [
        ("JVM410H OD", [0, 2, 4, 1, 3, 5]),
        ("JVM410H Clean/Crunch", [0, 2, 4, 1, 3, 5])
    ];

    public static TheoryData<string, int> Settings {
        get {
            var data = new TheoryData<string, int>();
            foreach (var (type, settings) in Models) {
                foreach (var setting in settings) {
                    data.Add(type, setting);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> Types => [..Models.Select(static m => m.Type)];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal(
            ["OD2", "Orange", "Red"],
            new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "JVM410H OD"))
        );
        Assert.Equal(
            ["Crunch", "Orange", "Red"],
            new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "JVM410H Clean/Crunch"))
        );
        foreach (var type in new[] { "JVM410H OD", "JVM410H Clean/Crunch" }) {
            Assert.Equal("Resonance", Label("depth", type));
            Assert.Equal("Volume", Label("control1", type));
            Assert.Empty(Label("cut", type));
            Assert.Empty(Label("bright", type));
        }
    }

    /// <summary>
    ///     Each mode steps up the gain, as the handbook says: below clipping, with the Volume full up (the Clean Green mode
    ///     has none), Orange is louder than Green and Red than Orange.
    /// </summary>
    [Theory]
    [InlineData("JVM410H OD", 0)]
    [InlineData("JVM410H OD", 1)]
    [InlineData("JVM410H Clean/Crunch", 0)]
    [InlineData("JVM410H Clean/Crunch", 1)]
    public void The_modes_raise_the_gain_in_order(string type, int channel) {
        var tone = Signals.Sine(440, Rate, 24_000, 0.001f);
        double Level(int mode) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Control1, 10f),
                (CircuitAmpBlock.Switch1, channel),
                (CircuitAmpBlock.Switch2, mode == 1 ? 1f : 0f),
                (CircuitAmpBlock.Switch3, mode == 2 ? 1f : 0f)
            );
            var rendered = BlockRender.Mono(block, tone, 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), 440, Rate));
        }

        var levels = new[] { 0, 1, 2 }.Select(Level).ToArray();
        output.WriteLine($"{type} channel {channel}: green {levels[0]:0.0}, orange {levels[1]:0.0}, red {levels[2]:0.0} dB");
        Assert.True(levels[1] > levels[0] + 1, "orange louder than green below clipping");
        Assert.True(levels[2] > levels[1] + 1, "red louder than orange below clipping");
    }

    /// <summary>Every knob swept end to end and every switch flipped while a loud chord plays: finite, bounded, converging.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, int setting) {
        using var block = Create(type, [(CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f), ..Switches(setting)]);
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var control in Controls(type).Where(static c => !IsSwitch(c))) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(control, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type} [{setting}]: control {control} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        var channel = block.Channel;
        foreach (var newton in Solvers(channel.Stages).Append(channel.Power.Newton)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type} [{setting}]: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type} [{setting}]: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>
    ///     Every control on the panel changes the sound in the given channel and mode: a knob from 0 to 10 or a switch on
    ///     moves the level or the spectrum of a two-tone signal by more than a fraction of a decibel somewhere. The Clean
    ///     Green mode's Volume is out of circuit, as on the amp, and is skipped there.
    /// </summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Every_control_is_audible(string type, int setting) {
        var tones = Signals.Sine(110, Rate, 24_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 24_000, 0.02f), static (a, b) => a + b)
            .ToArray();

        double[] Bands(int control, float value) {
            using var block = Create(
                type,
                [(CircuitAmpBlock.Gain, 4f), (CircuitAmpBlock.Master, 4f), ..Switches(setting), (control, value)]
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return [
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 110, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 2_500, Rate))
            ];
        }

        foreach (var control in Controls(type)) {
            if (IsSwitch(control) || (type == "JVM410H Clean/Crunch" && setting == 0 && control == CircuitAmpBlock.Control1)) {
                continue;
            }

            var (a, b) = (Bands(control, 0f), Bands(control, 10f));
            var change = Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));
            output.WriteLine(
                $"{type} [{setting}] control {control}: 110 Hz {b[0] - a[0]:+0.0;-0.0} dB, 2.5 kHz {b[1] - a[1]:+0.0;-0.0} dB"
            );
            Assert.True(change > 0.2, $"{type} [{setting}]: control {control} moves the sound by only {change:0.00} dB");
        }
    }

    /// <summary>Below clipping, the gain and master knobs raise the level monotonically, in every channel and mode.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Gain_and_master_raise_the_level_monotonically(string type, int setting) {
        double Level(int knob, float value) {
            var gain = knob == CircuitAmpBlock.Gain ? value : 3f;
            var master = knob == CircuitAmpBlock.Master ? value : 3f;
            using var block = Create(
                type,
                [(CircuitAmpBlock.Gain, gain), (CircuitAmpBlock.Master, master), ..Switches(setting)]
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v)).ToArray();
            output.WriteLine(
                $"{type} [{setting}] knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            for (var i = 1; i < levels.Length; i++) {
                if (levels[i - 1] > -100) {
                    Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type} [{setting}]: knob {knob} not monotonic at {i}");
                }
            }

            Assert.True(levels[^1] - levels[0] > 10, $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    static IEnumerable<NewtonSolver> Solvers(IEnumerable<AmpStage> stages) =>
        stages.SelectMany(static s => s is ChannelSwitchStage c ? Solvers(c.Active) : s.Newton is { } n ? [n] : []);

    static (int, float)[] Switches(int setting) => [
        (CircuitAmpBlock.Switch1, setting & 1),
        (CircuitAmpBlock.Switch2, (setting >> 1) & 1),
        (CircuitAmpBlock.Switch3, (setting >> 2) & 1)
    ];

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
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] ?? "" : parameter.Name;
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
