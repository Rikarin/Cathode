using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Marshall-family stages (docs/06 §3.2, stream F2) against the reference simulator: the JCM800's input with its
///     Low input jack, the two-channel first tubes with their jumper (two triodes; one for a shared cathode), the
///     cathode follower on a stack without a master, the Laney's first stage; and a network stage's output resistance
///     following its knobs.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, bool, bool, double, double> MarshallStages => new() {
        { "jcm800 input", false, false, 0.5, -40 },
        { "jcm800 input", false, false, 3.0, -35 },
        { "jcm800 input", true, false, 3.0, -40 },
        { "jtm45 input", false, false, 0.5, -40 },
        { "jtm45 input", true, false, 3.0, -35 },
        { "jtm45 input", false, true, 3.0, -35 },
        { "plexi 100w input", false, false, 3.0, -35 },
        { "plexi 100w input", true, false, 3.0, -35 },
        { "follower stack", false, false, 30.0, -40 },
        { "laney input", false, false, 2.0, -35 },
        { "laney input", true, false, 2.0, -35 }
    };

    [Theory]
    [MemberData(nameof(MarshallStages))]
    public void Marshall_stages_match_the_reference_in_the_time_domain(
        string name,
        bool switch1,
        bool switch2,
        double amplitude,
        double limitDb
    ) {
        var (design, rest) = name switch {
            "jcm800 input" => (MarshallParts.JCM800Input(290), 0.0),
            "jtm45 input" => ((NetworkStageDesign)JTM45.Design.Preamp[0], 0.0),
            "plexi 100w input" => ((NetworkStageDesign)SuperLead100.Design.Preamp[0], 0.0),
            "follower stack" => (MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallParts.JTM45Stack, 1e6), 175.0),
            _ => ((NetworkStageDesign)LaneyGH100L.Design.Preamp[0], 0.0)
        };
        const double SourceResistance = 30e3;
        var controls = new AmpControls(0.7, 0.3, 0.6, 0.8, 1, false, 0.6, Switch1: switch1, Switch2: switch2);
        var stage = design.Create(Rate);
        stage.SetSourceResistance(SourceResistance);
        stage.Update(controls);
        stage.Reset(rest);
        var (circuit, node) = AmpReferences.Network(
            design,
            controls,
            PreampSettings.Stock,
            SourceResistance,
            t => rest + Drive(amplitude, t)
        );
        var reference = new ReferenceSimulator(circuit);
        reference.OperatingPoint(40);
        output.WriteLine(
            $"{name} switches {switch1}/{switch2}: rest output {stage.RestOutput:0.000} V (reference " +
            $"{reference.Voltage(node):0.000} V), output resistance {stage.OutputResistance / 1e3:0.0}k"
        );
        Assert.Equal(reference.Voltage(node), stage.RestOutput, 0.002 * Math.Abs(reference.Voltage(node)) + 1e-3);

        var errorDb = CompareInTime(name, x => stage.Tick(rest + x), stage.Newton, circuit, node, amplitude);
        Assert.True(errorDb < limitDb, $"{name}: {errorDb:0.0} dB from the reference");
    }

    [Fact]
    public void Network_output_resistance_follows_its_knobs_and_switches() {
        var design = MarshallParts.JCM800Input(290);
        var moved = design.Create(Rate);
        moved.Update(new(0.2, 0.5, 0.5, 0.5, 1, false));
        moved.Reset(0);
        var before = moved.OutputResistance;
        foreach (var controls in new AmpControls[] {
                     new(0.8, 0.5, 0.5, 0.5, 1, false),
                     new(0.8, 0.5, 0.5, 0.5, 1, false, Switch1: true)
                 }) {
            moved.Update(controls);
            var fresh = design.Create(Rate);
            fresh.Update(controls);
            fresh.Reset(0);
            output.WriteLine(
                $"gain {controls.Gain} low {controls.Switch1}: {moved.OutputResistance / 1e3:0.00}k after the knob, " +
                $"{fresh.OutputResistance / 1e3:0.00}k from a reset (gain 0.2: {before / 1e3:0.00}k)"
            );
            Assert.Equal(fresh.OutputResistance, moved.OutputResistance, 1e-9 * fresh.OutputResistance);
        }

        Assert.True(Math.Abs(moved.OutputResistance - before) > 0.1 * before, "the volume moves the output resistance");
    }
}

/// <summary>
///     The Marshall-family models in the Circuit Amp block (docs/06 §5.4 step 3): each knob does what the circuit's does
///     over its whole travel, the panel's switches are audible, and full sweeps stay finite and bounded.
/// </summary>
public class MarshallModelTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    public static TheoryData<string> Models => [
        "JCM800 2203",
        "JCM800 2204",
        "JTM45",
        "Plexi 100W",
        "Laney GH100L"
    ];

    [Theory]
    [MemberData(nameof(Models))]
    public void Gain_and_master_raise_the_level_monotonically(string model) {
        var type = TypeOf(model);
        var design = AmpDesign.All[type];
        var knobs = new List<int> { CircuitAmpBlock.Gain };
        if (design.Panel.Master is not null) {
            knobs.Add(CircuitAmpBlock.Master);
        }

        if (design.Panel.Control1 is not null) {
            knobs.Add(CircuitAmpBlock.Control1);
        }

        foreach (var knob in knobs) {
            var levels = new List<double>();
            foreach (var value in new[] { 1f, 3f, 5f, 7f, 10f }) {
                // A quiet 440 Hz, below most of the clipping, with the other level knobs low where they exist.
                var settings = new List<(int, float)> { (knob, value) };
                if (knob != CircuitAmpBlock.Master && design.Panel.Master is not null) {
                    settings.Add((CircuitAmpBlock.Master, 3f));
                }

                // A second channel's volume does nothing audible until the guitar plays into that channel.
                if (knob == CircuitAmpBlock.Control1 && design.Panel.Switch2 == "Normal Input") {
                    settings.Add((CircuitAmpBlock.Switch2, 1f));
                }

                var rendered = Render(type, Signals.Sine(440, Rate, 24_000, 0.01f), [..settings]);
                levels.Add(Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000))));
            }

            output.WriteLine($"{model} {CircuitAmpBlock.Info.Params[knob].Id}: {string.Join(", ", levels.Select(static l => $"{l:0.0}"))} dB");
            for (var i = 1; i < levels.Count; i++) {
                Assert.True(levels[i] > levels[i - 1] - 0.1, $"{model}: level fell from {levels[i - 1]:0.0} to {levels[i]:0.0} dB");
            }

            Assert.True(levels[^1] - levels[0] > 6, $"{model}: the knob moves the level by {levels[^1] - levels[0]:0.0} dB");
        }
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Panel_switches_are_audible(string model) {
        var type = TypeOf(model);
        var design = AmpDesign.All[type];
        var chord = CircuitAmpBlockTests.Chord(14_400, 0.1f);
        var stock = Render(type, chord);
        foreach (var (index, label) in new[] {
                     (CircuitAmpBlock.Switch1, design.Panel.Switch1),
                     (CircuitAmpBlock.Switch2, design.Panel.Switch2),
                     (CircuitAmpBlock.Switch3, design.Panel.Switch3)
                 }) {
            if (label is null) {
                continue;
            }

            var switched = Render(type, chord, (index, 1f));
            var difference = Analysis.NullResidualDb(stock, switched) - Analysis.ToDb(Analysis.Rms(stock));
            output.WriteLine($"{model} {label}: {difference:0.0} dB from stock, peak {Analysis.ToDb(Analysis.Peak(switched)):0.0} dBFS");
            Assert.True(Analysis.AllFinite(switched));
            Assert.True(difference > -30, $"{model} {label}: {difference:0.0} dB from stock");
        }
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Full_knob_sweeps_stay_finite_and_bounded(string model) {
        // Every knob swept end to end while a chord plays, switches toggling, as a player would: no blow-up, no clicks
        // beyond the amp's own clipping level.
        var type = TypeOf(model);
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, type);
        block.Prepare(new(Rate, 64));
        var input = Signals.Concat(CircuitAmpBlockTests.Chord(48_000, 0.3f), Signals.Noise(48_000, 3, 0.3f));
        var left = (float[])input.Clone();
        var right = new float[left.Length];
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Master, CircuitAmpBlock.Control1
        ];
        var blocks = left.Length / 64;
        for (var b = 0; b < blocks; b++) {
            var phase = (double)b / blocks;
            foreach (var knob in knobs) {
                block.Params.SetTarget(knob, (float)(5 + 5 * Math.Sin(2 * Math.PI * (phase * 3 + knob * 0.1))));
            }

            block.Params.SetTarget(CircuitAmpBlock.Switch1, b / 250 % 2);
            block.Params.SetTarget(CircuitAmpBlock.Switch2, b / 400 % 2);
            block.Process(new(left.AsSpan(b * 64, 64), right.AsSpan(b * 64, 64)), new(Rate, 120, b * 64));
        }

        var peak = Analysis.ToDb(Analysis.Peak(left));
        output.WriteLine($"{model}: peak {peak:0.0} dBFS");
        Assert.True(Analysis.AllFinite(left));
        Assert.True(peak < 6, $"peak {peak:0.0} dBFS");
    }

    static int TypeOf(string model) => AmpDesign.All.ToList().FindIndex(a => a.Name == model);

    static float[] Render(int type, float[] input, params (int Index, float Value)[] settings) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, type);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return BlockRender.Mono(block, input, 64);
    }
}
