using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Marshall-family stages (docs/06 §3.2, stream F2) against the reference simulator: the JCM800's input with
///     its Low input jack, the two-channel first tubes with their jumper (two triodes; one for a shared cathode), the
///     cathode follower on a stack without a master, the Laney's first stage; and a network stage's output resistance
///     following its knobs.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, bool, bool, double, double> MarshallStages =>
        new() {
            {
                "jcm800 input",
                false,
                false,
                0.5,
                -40
            },
            {
                "jcm800 input",
                false,
                false,
                3.0,
                -35
            },
            {
                "jcm800 input",
                true,
                false,
                3.0,
                -40
            },
            {
                "jtm45 input",
                false,
                false,
                0.5,
                -40
            },
            {
                "jtm45 input",
                true,
                false,
                3.0,
                -35
            },
            {
                "jtm45 input",
                false,
                true,
                3.0,
                -35
            },
            {
                "plexi 100w input",
                false,
                false,
                3.0,
                -35
            },
            {
                "plexi 100w input",
                true,
                false,
                3.0,
                -35
            },
            {
                "follower stack",
                false,
                false,
                30.0,
                -40
            },
            {
                "laney input",
                false,
                false,
                2.0,
                -35
            },
            {
                "laney input",
                true,
                false,
                2.0,
                -35
            }
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
            $"{name} switches {switch1}/{switch2}: rest output {stage.RestOutput:0.000} V (reference "
            + $"{reference.Voltage(node):0.000} V), output resistance {stage.OutputResistance / 1e3:0.0}k"
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
                     new(0.8, 0.5, 0.5, 0.5, 1, false), new(0.8, 0.5, 0.5, 0.5, 1, false, Switch1: true)
                 }) {
            moved.Update(controls);
            var fresh = design.Create(Rate);
            fresh.Update(controls);
            fresh.Reset(0);
            output.WriteLine(
                $"gain {controls.Gain} low {controls.Switch1}: {moved.OutputResistance / 1e3:0.00}k after the knob, "
                + $"{fresh.OutputResistance / 1e3:0.00}k from a reset (gain 0.2: {before / 1e3:0.00}k)"
            );
            Assert.Equal(fresh.OutputResistance, moved.OutputResistance, 1e-9 * fresh.OutputResistance);
        }

        Assert.True(Math.Abs(moved.OutputResistance - before) > 0.1 * before, "the volume moves the output resistance");
    }
}

/// <summary>
///     The Marshall-family models in the Circuit Amp block (docs/06 §5.4 step 3): each knob does what the circuit's
///     does over its whole travel, the panel's switches are audible, and full sweeps stay finite and bounded.
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

            output.WriteLine(
                $"{model} {CircuitAmpBlock.Info.Params[knob].Id}: "
                + $"{string.Join(", ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            for (var i = 1; i < levels.Count; i++) {
                Assert.True(
                    levels[i] > levels[i - 1] - 0.1,
                    $"{model}: level fell from {levels[i - 1]:0.0} to {levels[i]:0.0} dB"
                );
            }

            Assert.True(
                levels[^1] - levels[0] > 6,
                $"{model}: the knob moves the level by {levels[^1] - levels[0]:0.0} dB"
            );
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
                     (CircuitAmpBlock.Switch1, design.Panel.Switch1), (CircuitAmpBlock.Switch2, design.Panel.Switch2),
                     (CircuitAmpBlock.Switch3, design.Panel.Switch3)
                 }) {
            if (label is null) {
                continue;
            }

            var switched = Render(type, chord, (index, 1f));
            var difference = Analysis.NullResidualDb(stock, switched) - Analysis.ToDb(Analysis.Rms(stock));
            output.WriteLine(
                $"{model} {label}: {difference:0.0} dB from stock, peak "
                + $"{Analysis.ToDb(Analysis.Peak(switched)):0.0} dBFS"
            );
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

    /// <summary>
    ///     JTM45.DGM feeds both screens through one 1k 2 W above the two 470 Ω: it leaves the idle point alone (a
    ///     few volts of screen drop at idle) and, carrying both tubes' screen current, holds the screens down at
    ///     full power, where 470 Ω alone lets them draw more.
    /// </summary>
    [Fact]
    public void The_jtm45s_shared_screen_dropper_limits_the_screen_current_at_full_power() {
        var drawn = JTM45.Design.Power;
        Assert.Equal(1e3, drawn.SharedScreenResistor);
        Assert.Equal(470, drawn.ScreenResistor);
        Assert.Equal(0, Bluesbreaker.Design.Power.SharedScreenResistor);

        (double Idle, double IdleScreen, double Driven) Run(PowerAmpDesign design) {
            var power = new PowerAmp(design, 96_000);
            power.SetSourceResistance(30e3);
            power.Reset();
            var idle = power.PlateCurrents.A;
            var idleScreen = power.ScreenCurrents.A;
            var driven = 0.0;
            const int Length = 19_200;
            for (var n = 0; n < 2 * Length; n++) {
                power.Tick(40 * Math.Sin(2 * Math.PI * 200 * n / 96_000.0));
                Assert.True(double.IsFinite(power.Supply));
                if (n >= Length) {
                    var (a, b) = power.ScreenCurrents;
                    driven += (a + b) / Length;
                }
            }

            return (idle, idleScreen, driven);
        }

        var shared = Run(drawn);
        var direct = Run(drawn with { SharedScreenResistor = 0 });
        output.WriteLine(
            $"idle {shared.Idle * 1e3:0.0} / {direct.Idle * 1e3:0.0} mA plate, {shared.IdleScreen * 1e3:0.00} / "
            + $"{direct.IdleScreen * 1e3:0.00} mA screen; driven {shared.Driven * 1e3:0.0} / "
            + $"{direct.Driven * 1e3:0.0} mA of screen current (1k shared / none)"
        );
        // The bias is set for the design's idle current with the dropper in place (the supply rests a little under
        // the design's B+, so the idle current does too).
        Assert.Equal(drawn.IdleCurrent, shared.Idle, 0.05 * drawn.IdleCurrent);
        Assert.Equal(direct.Idle, shared.Idle, 0.01 * direct.Idle);
        Assert.True(shared.Driven > 2 * 2 * shared.IdleScreen, "the drive must raise the screen current");
        Assert.True(shared.Driven < 0.95 * direct.Driven, "the shared 1k must hold the screen current down");
    }

    /// <summary>
    ///     Drawing 70-6-11 (#197): "47K (100K)" of feedback into the tail's foot, the 5k presence pot from there to
    ///     ground with 0.1 µF on its wiper, no 4k7 under the tail; 47k is the 100 W amp's value. The 1959T and the
    ///     1959SLP model the same power amp, so the three have one network.
    /// </summary>
    [Fact]
    public void The_plexi_100w_has_the_drawings_47k_into_the_presence_pot_as_the_shunt() {
        var feedback = SuperLead100.Design.Power.Feedback;
        Assert.Equal(47e3, feedback.FeedbackResistor);
        Assert.Equal(5e3, feedback.ShuntResistor);
        Assert.Equal(5e3, feedback.Presence.Ohms);
        Assert.True(feedback.PresenceIsShunt);
        Assert.Equal(5.0 / 52, feedback.Beta, 1e-12);
        Assert.Equal(Marshall1959T.Design.Power.Feedback, feedback);
        Assert.Equal(Marshall1959Slp.Design.Power.Feedback, feedback);
        // Presence at 0 leaves the feedback flat; at 10 the cap shorts the whole shunt at high frequencies.
        Assert.Equal(feedback.PresenceTimes(0).Tau1, feedback.PresenceTimes(0).Tau2, 1e-12);
        Assert.Equal(0, feedback.PresenceTimes(1).Tau1, 1e-12);
        Assert.True(feedback.PresenceTimes(1).Tau2 > 0);
    }

    /// <summary>
    ///     The Marshall-family stacks (the 5F6-A they come from, the Laney GH100L and the BE-100, whose drawings wire
    ///     it the same way) keep the whole middle track under the bass pot,
    ///     with the middle cap on its wiper (#196): Bass, Middle and Treble all at 0 still play, a few decibels under
    ///     noon. Wired as a variable resistor the middle pot at 0 grounded the stack, and these models went silent.
    /// </summary>
    [Theory]
    [InlineData("Plexi")]
    [InlineData("JCM800 2203")]
    [InlineData("JCM800 2204")]
    [InlineData("JTM45")]
    [InlineData("Plexi 100W")]
    [InlineData("1959SLP")]
    [InlineData("1987X")]
    [InlineData("1959T Tremolo")]
    [InlineData("Bluesbreaker 1962")]
    [InlineData("Bassman 5F6-A")]
    [InlineData("Laney GH100L")]
    [InlineData("BE-100")]
    [InlineData("BE-100 HBE")]
    public void The_tone_knobs_all_at_0_still_pass_signal(string model) {
        var type = TypeOf(model);
        var input = Signals.Sine(440, Rate, 24_000, 0.01f);

        double Level(float knobs) =>
            Analysis.ToDb(
                Analysis.Rms(
                    Render(
                            type,
                            input,
                            (CircuitAmpBlock.Bass, knobs),
                            (CircuitAmpBlock.Middle, knobs),
                            (CircuitAmpBlock.Treble, knobs)
                        )
                        .AsSpan(12_000)
                )
            );

        var (noon, closed) = (Level(5), Level(0));
        output.WriteLine($"{model}: {noon:0.0} dB at noon, {closed:0.0} dB with the three at 0");
        Assert.True(closed > noon - 30, $"{closed:0.0} dB with the tone knobs at 0, {noon:0.0} dB at noon");
    }

    /// <summary>
    ///     2203 STD (output stage issue 6, preamp issue 4) feeds node X (the inverter's 82k and 100k, V2a's 100k and
    ///     V2b's plate) from the screens' node through R29 10k alone, and V1 from X through R8 10k (#198). So the
    ///     models' rails are the ones those droppers leave with the stages' own idle currents, the supply's preamp
    ///     draw is that current, and the power amp's preamp node rests at the inverter's rail.
    /// </summary>
    [Theory]
    [InlineData("JCM800 2203")]
    [InlineData("JCM800 2204")]
    public void The_jcm800s_preamp_rails_follow_from_the_drawings_10k_droppers(string model) {
        var design = AmpDesign.All[TypeOf(model)];
        var supply = design.Power.PowerSupply;
        var inverter = (LongTailPairDesign)design.Power.PhaseInverter;
        var railX = inverter.Supply;
        var railV1 = ((TriodeStageDesign)design.Preamp[1]).Supply;
        Assert.Equal(railX, ((TriodeStageDesign)design.Preamp[2]).Supply);
        Assert.Equal(railX, ((ToneStackStageDesign)design.Preamp[3]).Supply);
        Assert.Equal(10e3, supply.PreampResistance);
        var chain = Assert.Single(design.PreampSupply!.Nodes);
        Assert.Equal((10e3, 50e-6, 0), (chain.Resistance, chain.Cap, chain.Parent)); // R8 into half of C21
        Assert.Equal([1, 1, 0, 0], design.Preamp.Select(static s => s.SupplyNode ?? 0));

        // The idle currents at given rails: the inverter's two plates, V2a, the V2b follower on V2a's plate, V1a and
        // the V1b cold clipper.
        (double X, double V1) Currents(double x, double v1) {
            var tables = PhaseInverter.For(inverter with { Supply = x });
            var pair = (x - tables.RestPlateA) / inverter.PlateResistorA
                + (x - tables.RestPlateB) / inverter.PlateResistorB;
            var (v2a, plate, _) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, x, 100e3, 820);
            var (v2b, _) = OperatingPoint.CathodeFollower(TriodeModel.Ecc83, plate, x, 100e3);
            var (v1a, _, _) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, v1, 100e3, 2.7e3);
            var (v1b, _, _) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, v1, 100e3, 10e3);
            return (pair + v2a + v2b + v1a + v1b, v1a + v1b);
        }

        var power = new AmpChannel(design, 96_000).Power;
        var screens = power.ScreenSupply;
        var (solvedX, solvedV1) = (railX, railV1);
        for (var i = 0; i < 50; i++) {
            var (ix, iv1) = Currents(solvedX, solvedV1);
            (solvedX, solvedV1) = (screens - 10e3 * ix, solvedX - 10e3 * iv1);
        }

        var (drawX, drawV1) = Currents(railX, railV1);
        output.WriteLine(
            $"{model}: screens {screens:0.0} V; the droppers leave {solvedX:0.0} V at X and {solvedV1:0.0} V at V1 "
            + $"(the model: {railX:0} and {railV1:0} V), drawing {drawX * 1e3:0.00} mA through R29 and "
            + $"{drawV1 * 1e3:0.00} mA through R8; preamp node {power.PreampVoltage:0.0} V"
        );
        Assert.Equal(solvedX, railX, 0.01 * solvedX);
        Assert.Equal(solvedV1, railV1, 0.01 * solvedV1);
        Assert.Equal(drawX, supply.PreampCurrent, 0.03 * drawX);
        Assert.Equal(drawV1, chain.Current, 0.03 * drawV1);
        Assert.Equal(railX, power.PreampVoltage, 0.01 * railX);
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
