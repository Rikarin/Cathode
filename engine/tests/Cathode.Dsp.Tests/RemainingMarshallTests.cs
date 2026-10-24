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
            { "JVM410H Clean/Crunch", 5, 0.0005, -30 },
            { "DSL100", 0, 0.01, -30 },
            { "DSL100", 2, 0.002, -30 },
            { "DSL100", 1, 0.001, -30 },
            { "DSL100", 3, 0.0005, -30 },
            { "DSL100", 7, 0.0005, -30 },
            { "JMP-1", 0, 0.005, -30 },
            { "JMP-1", 2, 0.002, -30 },
            { "JMP-1", 4, 0.005, -30 },
            { "JMP-1", 1, 0.0001, -20 },
            { "JMP-1", 3, 0.0001, -20 },
            // The AFD100's cuts: #34 at V6A's and V6B's plates and at the send, AFD at the send only (its five triodes
            // up to V8A are one circuit: the bypassed cathodes of V5 and V6A make a poor Thévenin resistor).
            { "AFD100", 0, 0.002, -41 },
            { "AFD100", 2, 0.002, -41 },
            { "AFD100", 0, 0.05, -37 },
            { "AFD100", 1, 0.002, -42 },
            { "AFD100", 3, 0.002, -42 },
            { "AFD100", 1, 0.05, -39 }
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
        ("JVM410H Clean/Crunch", [0, 2, 4, 1, 3, 5]),
        ("DSL100", [0, 2, 1, 3, 4]),
        ("JMP-1", [0, 2, 1, 3, 4]),
        ("AFD100", [0, 1, 2, 3])
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

        Assert.Equal(
            ["Ultra Gain", "Crunch / Lead 2", "Tone Shift"],
            new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "DSL100"))
        );
        Assert.Equal("Volume", Label("master", "DSL100"));
        Assert.Equal("Deep", Label("depth", "DSL100"));
        Assert.Empty(Label("control1", "DSL100"));

        Assert.Equal(
            ["OD", "Clean II / OD II", "Bass Shift"],
            new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "JMP-1"))
        );
        Assert.Equal(["Output", "Presence"], new[] { "control1", "control2" }.Select(id => Label(id, "JMP-1")));
        Assert.Empty(Label("presence", "JMP-1"));
        Assert.Empty(Label("depth", "JMP-1"));

        Assert.Equal(
            ["AFD", "Low Input", ""],
            new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, "AFD100"))
        );
        Assert.Equal(
            ["Gain", "Bass", "Middle", "Treble", "Presence", "Master"],
            new[] { "gain", "bass", "middle", "treble", "presence", "master" }.Select(id => Label(id, "AFD100"))
        );
        foreach (var id in new[] { "depth", "cut", "bright", "control1", "control2" }) {
            Assert.Empty(Label(id, "AFD100"));
        }
    }

    /// <summary>
    ///     The AFD100's modes and inputs, below clipping: #34 (V6B in the chain, V6A unbypassed) has more gain than AFD
    ///     (V6B skipped, V6A's cathode bypassed above 160 Hz, 150k on the Gain's wiper), and AFD's partial bypass tilts it
    ///     toward the treble (1 kHz against 110 Hz); the Low input is about 6 dB down in both.
    /// </summary>
    [Fact]
    public void Afd100_modes_and_inputs_set_the_gain() {
        double Level(float hz, params (int, float)[] switches) {
            using var block = Create("AFD100", [(CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Master, 3f), ..switches]);
            var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 24_000, 0.0002f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hz, Rate));
        }

        var number34 = Level(1_000);
        var afd = Level(1_000, (CircuitAmpBlock.Switch1, 1f));
        var low34 = Level(1_000, (CircuitAmpBlock.Switch2, 1f));
        var lowAfd = Level(1_000, (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f));
        var tilt34 = number34 - Level(110);
        var tiltAfd = afd - Level(110, (CircuitAmpBlock.Switch1, 1f));
        output.WriteLine(
            $"#34 {number34:0.0}, AFD {afd:0.0}, Low #34 {low34:0.0}, Low AFD {lowAfd:0.0} dB; "
            + $"1 kHz over 110 Hz: #34 {tilt34:0.0}, AFD {tiltAfd:0.0} dB"
        );
        Assert.True(number34 > afd + 2);
        Assert.InRange(number34 - low34, 4, 8);
        Assert.InRange(afd - lowAfd, 4, 8);
        Assert.True(tiltAfd > tilt34);
    }

    /// <summary>
    ///     The JMP-1's switches: OD has more gain than Clean, Clean II than Clean I, OD II's voicing moves the level, and
    ///     Bass Shift moves the low end on both channels (110 Hz against 1 kHz).
    /// </summary>
    [Fact]
    public void Jmp1_switches_change_the_gain_and_the_bass() {
        double Level(float hz, params (int, float)[] switches) {
            using var block = Create("JMP-1", [(CircuitAmpBlock.Gain, 5f), (CircuitAmpBlock.Master, 5f), ..switches]);
            var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 24_000, 0.0005f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hz, Rate));
        }

        var clean = Level(1_000);
        var cleanTwo = Level(1_000, (CircuitAmpBlock.Switch2, 1f));
        var od = Level(1_000, (CircuitAmpBlock.Switch1, 1f));
        var odTwo = Level(1_000, (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f));
        output.WriteLine($"clean I {clean:0.0}, clean II {cleanTwo:0.0}, OD I {od:0.0}, OD II {odTwo:0.0} dB");
        Assert.True(cleanTwo > clean + 3);
        Assert.True(od > clean + 10);
        Assert.True(Math.Abs(odTwo - od) > 1);

        foreach (var channel in new[] { 0f, 1f }) {
            var tilt = Level(110, (CircuitAmpBlock.Switch1, channel)) - Level(1_000, (CircuitAmpBlock.Switch1, channel));
            var shifted = Level(110, (CircuitAmpBlock.Switch1, channel), (CircuitAmpBlock.Switch3, 1f))
                - Level(1_000, (CircuitAmpBlock.Switch1, channel), (CircuitAmpBlock.Switch3, 1f));
            output.WriteLine($"channel {channel}: 110 Hz against 1 kHz {tilt:0.0} dB, with Bass Shift {shifted:0.0} dB");
            Assert.True(Math.Abs(shifted - tilt) > 1);
        }
    }

    /// <summary>
    ///     The JMP-1's active tone circuit is flat with every band at the centre (the bands' boost and cut currents
    ///     cancel), and each band boosts its own region: Bass at 100 Hz, Middle at 700 Hz, Treble and Presence above.
    /// </summary>
    [Fact]
    public void Jmp1_active_tone_is_flat_at_the_centre_and_each_band_boosts_its_region() {
        var design = AmpDesign.All.Single(a => a.Name == "JMP-1");
        var tone = (NetworkStageDesign)design.Preamp.Single(d => d.Name == "Active tone");

        double Gain(double hz, AmpControls controls) {
            var stage = tone.Create(96_000);
            stage.Update(controls);
            stage.SetSourceResistance(0);
            stage.Reset(0);
            var input = Signals.Sine(hz, 96_000, 38_400, 0.01f);
            var rendered = input.Select(x => (float)(stage.Tick(x) - stage.RestOutput)).ToArray();
            return Analysis.ToDb(
                Analysis.MagnitudeAt(rendered.AsSpan(19_200).ToArray(), hz, 96_000)
                / Analysis.MagnitudeAt(input.AsSpan(19_200).ToArray(), hz, 96_000)
            );
        }

        var centre = new AmpControls(0.5, 0.5, 0.5, 0.5, 0.5, false, Control2: 0.5);
        foreach (var hz in new[] { 100.0, 700, 3_000, 8_000 }) {
            var flat = Gain(hz, centre);
            output.WriteLine($"{hz} Hz at the centre: {flat:+0.0;-0.0} dB");
            Assert.True(Math.Abs(flat) < 1);
        }

        (double Hz, AmpControls Up)[] bands = [
            (100, centre with { Bass = 1 }), (700, centre with { Middle = 1 }), (8_000, centre with { Treble = 1 }),
            (3_000, centre with { Control2 = 1 })
        ];
        foreach (var (hz, up) in bands) {
            var boost = Gain(hz, up) - Gain(hz, centre);
            output.WriteLine($"{hz} Hz boost: {boost:+0.0;-0.0} dB");
            Assert.True(boost > 3);
        }
    }

    /// <summary>
    ///     The DSL100's mode switches raise the gain below clipping (Classic Clean to Crunch, Lead 1 to Lead 2), and the
    ///     Ultra Gain channel has more than the Classic Gain one; Tone Shift scoops the middle (750 Hz) against the
    ///     treble (3 kHz).
    /// </summary>
    [Fact]
    public void Dsl100_modes_and_tone_shift_do_what_the_handbook_says() {
        double Level(float hz, params (int, float)[] switches) {
            using var block = Create("DSL100", [(CircuitAmpBlock.Gain, 5f), (CircuitAmpBlock.Master, 3f), ..switches]);
            var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 24_000, 0.0005f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hz, Rate));
        }

        var clean = Level(440);
        var crunch = Level(440, (CircuitAmpBlock.Switch2, 1f));
        var lead1 = Level(440, (CircuitAmpBlock.Switch1, 1f));
        var lead2 = Level(440, (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f));
        output.WriteLine($"clean {clean:0.0}, crunch {crunch:0.0}, lead 1 {lead1:0.0}, lead 2 {lead2:0.0} dB");
        Assert.True(crunch > clean + 3);
        Assert.True(lead1 > crunch);
        Assert.True(lead2 > lead1);

        var scoop = Level(750) - Level(3_000);
        var shifted = Level(750, (CircuitAmpBlock.Switch3, 1f)) - Level(3_000, (CircuitAmpBlock.Switch3, 1f));
        output.WriteLine($"750 Hz against 3 kHz: {scoop:0.0} dB, with Tone Shift {shifted:0.0} dB");
        Assert.True(shifted < scoop - 2);
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
