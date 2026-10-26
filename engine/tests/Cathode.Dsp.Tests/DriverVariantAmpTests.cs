using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>Stream M2's preamps against the reference MNA simulator as whole chains, every stage in one circuit.</summary>
public partial class AmpStageTests {
    public static TheoryData<string, double> DriverVariantChains =>
        new() { { "Major 1967", 0.01 }, { "Bass 400", 0.01 } };

    [Theory]
    [MemberData(nameof(DriverVariantChains))]
    public void Driver_variant_preamps_match_the_reference(string type, double amplitude) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var designs = design.Preamp.Cast<NetworkStageDesign>().ToArray();
        var controls = new AmpControls(
            0.6,
            0.7,
            0.4,
            0.6,
            0.8,
            true,
            0.5,
            0.7,
            false,
            true,
            true,
            new(6, -6, 3, 0, -3)
        );
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
            node = AmpReferences.Append(circuit, d, controls, PreampSettings.Stock, node);
        }

        var errorDb = CompareInTime(
            type,
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
        Assert.True(errorDb < -30, $"{type}: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The Marshall Major 1967 and the Mesa/Boogie Bass 400 on the Circuit Amp block: panels, knob sweeps at full drive
///     (finite, converging, below +6 dBFS), every control audible, gain and master monotonic, the tone stack inside the
///     Major's feedback loop, the Bass 400's graphic equaliser, and their CPU.
/// </summary>
public class DriverVariantAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = ["Major 1967", "Bass 400"];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal("Treble Vol", Label("gain", "Major 1967"));
        Assert.Equal("Normal Vol", Label("control1", "Major 1967"));
        Assert.Equal(
            ["Jumper", "Normal Input"],
            new[] { "switch1", "switch2" }.Select(static id => Label(id, "Major 1967"))
        );
        Assert.Empty(Label("master", "Major 1967"));
        Assert.Equal("Presence", Label("presence", "Major 1967"));

        Assert.Equal("Volume 1", Label("gain", "Bass 400"));
        Assert.Equal("Volume 2", Label("control1", "Bass 400"));
        Assert.Equal("4 kHz", Label("control2", "Bass 400"));
        Assert.Equal(
            ["Input 2", "Bass Shift", "Treble Shift"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "Bass 400"))
        );
        Assert.Equal(
            ["34 Hz", "107 Hz", "269 Hz", "723 Hz", "1.6 kHz"],
            new[] { "eq1", "eq2", "eq3", "eq4", "eq5" }.Select(static id => Label(id, "Bass 400"))
        );
        Assert.Empty(Label("presence", "Bass 400"));
        Assert.Equal("Master", Label("master", "Bass 400"));
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Control1, CircuitAmpBlock.Control2, CircuitAmpBlock.Eq1,
            CircuitAmpBlock.Eq1 + 4
        ];
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.5f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.5f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in knobs) {
            var range = (knob is >= CircuitAmpBlock.Eq1 and < CircuitAmpBlock.Eq1 + 5) ? 12f : 10f;
            foreach (var value in new[] {
                         (knob is >= CircuitAmpBlock.Eq1 and < CircuitAmpBlock.Eq1 + 5) ? -12f : 0f, range, 5f
                     }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        foreach (var toggle in new[] {
                     CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3, CircuitAmpBlock.Bright
                 }) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        foreach (var newton in channel.Stages.Where(static s => s.Newton is not null).Select(static s => s.Newton!)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} of {newton.Samples}"
            );
        }

        var power = channel.Power.Newton;
        output.WriteLine(
            $"{type}: peak {Analysis.ToDb(peak):0.0} dBFS; power amp {power.MeanIterations:0.00} iterations a sample, "
            + $"{power.Unconverged} unconverged of {power.Samples}"
        );
        Assert.Equal(0, power.NonFinite);
        Assert.True(power.Unconverged <= power.Samples / 10_000);
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    [Theory]
    [InlineData("Major 1967", CircuitAmpBlock.Gain)]
    [InlineData("Major 1967", CircuitAmpBlock.Control1)]
    [InlineData("Bass 400", CircuitAmpBlock.Gain)]
    [InlineData("Bass 400", CircuitAmpBlock.Control1)]
    [InlineData("Bass 400", CircuitAmpBlock.Master)]
    public void Volumes_raise_the_level_monotonically(string type, int knob) {
        (int, float)[] extra = knob switch {
            CircuitAmpBlock.Control1 => [(CircuitAmpBlock.Switch2, 1f)],
            _ => []
        };
        if (type == "Bass 400" && knob == CircuitAmpBlock.Control1) {
            extra = [(CircuitAmpBlock.Switch1, 1f)];
        }

        var levels = new[] { 0.5f, 2.5f, 5f, 7.5f, 10f }
            .Select(v => Level(type, 1_000, 1e-3f, [..extra, (knob, v)]))
            .ToArray();
        output.WriteLine($"{type} knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: not monotonic at step {i}");
        }

        Assert.True(levels[^1] - levels[0] > 6, $"{type}: spans only {levels[^1] - levels[0]:0.0} dB");
    }

    /// <summary>Every knob and switch the panel names changes the output (at 1 kHz or where it acts).</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Every_control_is_audible(string type) {
        var controls = new List<(string, int, float, float, double)> {
            ("bass", CircuitAmpBlock.Bass, 0, 10, 100),
            ("middle", CircuitAmpBlock.Middle, 0, 10, 600),
            ("treble", CircuitAmpBlock.Treble, 0, 10, 3_000),
            ("switch1", CircuitAmpBlock.Switch1, 0, 1, 1_000),
            ("switch2", CircuitAmpBlock.Switch2, 0, 1, 100)
        };
        if (type == "Major 1967") {
            controls.Add(("presence", CircuitAmpBlock.Presence, 0, 10, 4_000));
            controls.Add(("feedback", CircuitAmpBlock.Feedback, 0, 10, 1_000));
        } else {
            controls.Add(("bright", CircuitAmpBlock.Bright, 0, 1, 4_000));
            controls.Add(("switch3", CircuitAmpBlock.Switch3, 0, 1, 3_000));
            controls.Add(("eq1", CircuitAmpBlock.Eq1, -12, 12, 34));
            controls.Add(("eq2", CircuitAmpBlock.Eq1 + 1, -12, 12, 107));
            controls.Add(("eq3", CircuitAmpBlock.Eq1 + 2, -12, 12, 269));
            controls.Add(("eq4", CircuitAmpBlock.Eq1 + 3, -12, 12, 723));
            controls.Add(("eq5", CircuitAmpBlock.Eq1 + 4, -12, 12, 1_570));
            controls.Add(("control2", CircuitAmpBlock.Control2, 0, 10, 4_040));
        }

        foreach (var (name, index, low, high, frequency) in controls) {
            var a = Level(type, frequency, 1e-3f, (CircuitAmpBlock.Gain, 3f), (index, low));
            var b = Level(type, frequency, 1e-3f, (CircuitAmpBlock.Gain, 3f), (index, high));
            output.WriteLine($"{type} {name}: {a:0.0} -> {b:0.0} dB at {frequency} Hz");
            Assert.True(Math.Abs(b - a) > 1, $"{type}: {name} moves {b - a:0.0} dB");
        }
    }

    /// <summary>
    ///     The Major's tone stack sits inside the feedback loop, so the loop flattens it: the bass and treble knobs move
    ///     the output less with the feedback in than with it out (the feedback knob at 0).
    /// </summary>
    [Fact]
    public void Major_feedback_flattens_its_tone_stack() {
        static double Swing(float feedback, int knob, double frequency) =>
            Level("Major 1967", frequency, 1e-3f, (CircuitAmpBlock.Feedback, feedback), (knob, 10f))
            - Level("Major 1967", frequency, 1e-3f, (CircuitAmpBlock.Feedback, feedback), (knob, 0f));

        var closed = Swing(5, CircuitAmpBlock.Treble, 3_000);
        var open = Swing(0, CircuitAmpBlock.Treble, 3_000);
        output.WriteLine($"treble 0 -> 10 at 3 kHz: {closed:0.0} dB in the loop, {open:0.0} dB without feedback");
        Assert.True(open > closed + 1, $"{open:0.0} / {closed:0.0} dB");
    }

    /// <summary>
    ///     The Bass 400's graphic equaliser: flat with the sliders centred against the extremes, each slider boosting and
    ///     cutting around its own band.
    /// </summary>
    [Fact]
    public void Bass_400_sliders_boost_and_cut_their_bands() {
        foreach (var (index, frequency) in new[] {
                     (CircuitAmpBlock.Eq1, 34.0), (CircuitAmpBlock.Eq1 + 2, 269), (CircuitAmpBlock.Eq1 + 4, 1_570)
                 }) {
            var flat = Level("Bass 400", frequency, 1e-3f, (CircuitAmpBlock.Gain, 3f));
            var boost = Level("Bass 400", frequency, 1e-3f, (CircuitAmpBlock.Gain, 3f), (index, 12f)) - flat;
            var cut = Level("Bass 400", frequency, 1e-3f, (CircuitAmpBlock.Gain, 3f), (index, -12f)) - flat;
            output.WriteLine($"{frequency} Hz: boost {boost:0.0} dB, cut {cut:0.0} dB");
            Assert.True(boost > 4, $"{frequency} Hz boost {boost:0.0} dB");
            Assert.True(cut < -4, $"{frequency} Hz cut {cut:0.0} dB");
        }
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

    static double Level(string type, double frequency, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }
}
