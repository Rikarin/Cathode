using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The JCM800 2210 and 2205 against the reference MNA simulator: each channel's playing chain (the channel's
///     own stages and V4A) as one circuit with no Thévenin cut.
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, bool, double, double> Jcm2210Chains =>
        new() {
            // Boost: below the bridge's threshold and into it (the error has a floor of about 30 µV at V4A's plate,
            // so a quieter drive reads worse). Normal (Switch 1): clean, and V2 driven hard.
            { "JCM800 2210", false, 0.02, -45 },
            { "JCM800 2210", false, 0.2, -45 },
            { "JCM800 2210", true, 0.01, -50 },
            { "JCM800 2210", true, 0.3, -50 },
            { "JCM800 2205", false, 0.02, -45 },
            { "JCM800 2205", true, 0.01, -50 }
        };

    [Theory]
    [MemberData(nameof(Jcm2210Chains))]
    public void Jcm2210_chains_match_the_reference(string type, bool normal, double amplitude, double limitDb) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 0.5, false, Switch1: normal, Control1: 0.5);
        var designs = Playing(design.Preamp, controls).Cast<NetworkStageDesign>().ToArray();

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
            $"{type} [{(normal ? "Normal" : "Boost")}] {string.Join(" > ", designs.Select(static d => d.Name))}",
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
        Assert.True(errorDb < limitDb, $"{type} [{normal}]: {errorDb:0.0} dB from the reference");
    }
}

/// <summary>
///     The JCM800 2210 and 2205 on the Circuit Amp block: the panel, the rest points the drawing's parts give, the
///     gain structure (the Boost channel's gain, its bridge clipper, the Normal channel's level), every knob doing
///     what its label says, and knob sweeps at full drive without instability.
/// </summary>
public class Jcm2210Tests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly int[] Knobs = [
        CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
        CircuitAmpBlock.Presence, CircuitAmpBlock.Master, CircuitAmpBlock.Control1
    ];

    public static TheoryData<string, bool> Channels =>
        new() { { "JCM800 2210", false }, { "JCM800 2210", true }, { "JCM800 2205", false }, { "JCM800 2205", true } };

    [Theory]
    [InlineData("JCM800 2210")]
    [InlineData("JCM800 2205")]
    public void Panel_names_the_real_controls_and_hides_the_rest(string type) {
        Assert.Equal(
            ["Gain", "Bass", "Middle", "Treble", "Presence", "Master", "Normal", "Boost Volume"],
            new[] { "gain", "bass", "middle", "treble", "presence", "master", "switch1", "control1" }
                .Select(id => Label(id, type))
        );
        foreach (var id in new[] { "depth", "cut", "bright", "switch2", "switch3", "control2" }) {
            Assert.Empty(Label(id, type));
        }
    }

    /// <summary>
    ///     A new 2210 or 2205 starts with the Boost Volume full up (the block's default is 5): at 5 the Master at 10
    ///     leaves the power amp far from driven, a strummed chord more than 10 dB quieter than with it at 10.
    /// </summary>
    [Theory]
    [InlineData("JCM800 2210")]
    [InlineData("JCM800 2205")]
    public void A_new_amp_has_the_boost_volume_full_up(string type) {
        Assert.Equal(10, CircuitAmpBlock.Info.Params[CircuitAmpBlock.Control1].DefaultFor(TypeOf(type)));
        Assert.Equal(5, CircuitAmpBlock.Info.Params[CircuitAmpBlock.Control1].DefaultFor(0));

        double Peak(float volume) {
            using var block = Create(type, (CircuitAmpBlock.Master, 10f), (CircuitAmpBlock.Control1, volume));
            var rendered = BlockRender.Mono(block, CircuitAmpBlockTests.Chord(48_000, 0.25f), 64).AsSpan(9_600);
            return Analysis.ToDb(Analysis.Peak(rendered));
        }

        var (half, full) = (Peak(5), Peak(10));
        output.WriteLine($"{type}: Master 10, Boost Volume 5 {half:0.0} dBFS, 10 {full:0.0} dBFS");
        Assert.True(full > half + 10);
        Assert.InRange(full, -12, 0.5);
    }

    /// <summary>
    ///     The two heads differ where their drawings do: four EL34 on 1.7k against two on 3.2k, the 100 µF (50 + 50)
    ///     cans against single 50 µF ones, and 220 pF against 47 pF from V3A's grid to its cathode. The inverter is
    ///     the drawings' on both: 82k and 100k, 470 and the 22k tail on the 4k7 shunt, 0.022 µF in with 330k.
    /// </summary>
    [Fact]
    public void The_2205_differs_from_the_2210_where_its_drawing_does() {
        var (big, small) = (JCM2210.Design, JCM2210.Head50);
        Assert.Equal(
            (2, 100.0, 1.7e3),
            (big.Power.TubesPerSide, big.Power.RatedPower, big.Power.Transformer.PlateToPlate)
        );
        Assert.Equal(
            (1, 50.0, 3.2e3),
            (small.Power.TubesPerSide, small.Power.RatedPower, small.Power.Transformer.PlateToPlate)
        );
        Assert.Equal((100e-6, 100e-6), (big.Power.PowerSupply.Reservoir, big.Power.PowerSupply.ScreenCap));
        Assert.Equal((50e-6, 50e-6), (small.Power.PowerSupply.Reservoir, small.Power.PowerSupply.ScreenCap));
        foreach (var design in new[] { big, small }) {
            var pair = Assert.IsType<LongTailPairDesign>(design.Power.PhaseInverter);
            Assert.Equal(
                (82e3, 100e3, 26.7e3, 470.0),
                (pair.PlateResistorA, pair.PlateResistorB, pair.TailResistor, pair.BiasResistor)
            );
            Assert.Equal(new InverterInputDesign(22e-9, 330e3), design.Power.InverterInput);
            Assert.Equal(
                (100e3, 4.7e3, 0.1e-6),
                (design.Power.Feedback.FeedbackResistor, design.Power.Feedback.ShuntResistor,
                    design.Power.Feedback.PresenceCap)
            );
            Assert.Equal(new Pot(22e3, PotTaper.Audio), design.Power.Feedback.Presence);
        }

        static double GridCap(AmpDesign design) {
            var channel = (ChannelSwitchStageDesign)design.Preamp[0];
            var net = ((NetworkStageDesign)channel.Off[1]).Netlist;
            return net.Elements.Single(e => e.Kind == ElementKind.Capacitor && e.A == net.Input).Value;
        }

        Assert.Equal(220e-12, GridCap(big));
        Assert.Equal(47e-12, GridCap(small));
    }

    /// <summary>
    ///     The drawings give no voltages, so the preamp node is computed: every preamp triode, V4 and the inverter
    ///     hang from it, and it hangs from the screens' node through one 10k. The model's rail and the current it
    ///     tells the supply are that computation: the triodes' rest currents at the rail (V1B's with its cathode on
    ///     the diode) add up to the stated draw, and the screens' node less 10k times the draw is the rail.
    /// </summary>
    [Theory]
    [InlineData("JCM800 2210", 469)]
    [InlineData("JCM800 2205", 459)]
    public void The_preamp_rail_follows_from_the_dropper_and_the_tubes_currents(string type, double screens) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var pair = (LongTailPairDesign)design.Power.PhaseInverter;
        var rail = pair.Supply;

        static double Stage(double rail, double plate, double cathode) =>
            OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, rail, plate, cathode).Current;

        // V1B: the 1N4007 across its 10k holds the cathode at the diode's drop at the stage's own current.
        var (diode, v1b) = (DiodeModel.Rectifier, 0.5e-3);
        for (var i = 0; i < 50; i++) {
            var drop = diode.EmissionVoltage(1) * Math.Log(1 + v1b / diode.SaturationCurrent);
            v1b = OperatingPoint.CommonCathode(TriodeModel.Ecc83, -drop, rail, 470e3, 0).Current;
        }

        var inverter = PhaseInverter.For(pair);
        var tail = inverter.RestCathode / (pair.TailResistor + pair.BiasResistor);
        // V1A; V2a, V2b; V1B; V3A; V4A and V4B (the reverb recovery, 100k and 1k5 like V4A).
        var triodes = Stage(rail, 100e3, 2.7e3)
            + Stage(rail, 150e3, 10e3)
            + Stage(rail, 100e3, 1.5e3)
            + v1b
            + (3 * Stage(rail, 100e3, 1.5e3));
        var draw = triodes + tail;
        output.WriteLine(
            $"{type}: triodes {triodes * 1e3:0.00} mA (V1B {v1b * 1e3:0.00}), inverter {tail * 1e3:0.00} mA, "
            + $"in all {draw * 1e3:0.00} mA; node {screens - (10e3 * draw):0.0} V against {rail} V"
        );
        Assert.Equal(design.Power.PowerSupply.PreampCurrent, draw, 0.3e-3);
        Assert.Equal(rail, screens - (10e3 * draw), 4.0);
    }

    /// <summary>
    ///     V1B rests on its cathode diode, not on its 10k: the cathode sits at a silicon junction's drop (about half a
    ///     volt) where 10k alone would put it at several volts, and the plate, on 470k, well under half the rail.
    /// </summary>
    [Fact]
    public void V1b_rests_on_its_cathode_diode() {
        var channel = (ChannelSwitchStageDesign)JCM2210.Design.Preamp[0];
        var net = ((NetworkStageDesign)channel.Off[0]).Netlist;
        var triode = net.Elements.Last(static e => e.Kind == ElementKind.Triode); // V1A comes first
        var diode = net.Elements.Single(e => e.Kind == ElementKind.Diodes && e.A == triode.C);
        Assert.Equal((1, 0, StageNetlist.Ground), (diode.Forward, diode.Reverse, diode.B));
        var bridge = net.Elements.Single(e => e.Kind == ElementKind.Diodes && e.A != triode.C);
        Assert.Equal((3, 3, StageNetlist.Ground), (bridge.Forward, bridge.Reverse, bridge.B));

        var rail = ((LongTailPairDesign)JCM2210.Design.Power.PhaseInverter).Supply;
        var resistorAlone = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, rail, 470e3, 10e3);
        var onDiode = OperatingPoint.CommonCathode(TriodeModel.Ecc83, -0.5, rail, 470e3, 0);
        output.WriteLine(
            $"V1B on 10k alone: cathode {resistorAlone.Cathode:0.00} V, plate {resistorAlone.Plate:0} V; "
            + $"on the diode: plate {onDiode.Plate:0} V at {onDiode.Current * 1e3:0.00} mA"
        );
        Assert.True(resistorAlone.Cathode > 1.5, "10k alone would bias V1B colder than a junction's drop");
        Assert.True(onDiode.Plate < rail / 2);
    }

    /// <summary>
    ///     The gain structure, below clipping at 1 kHz: with each channel's own knobs full up the Boost channel has far
    ///     more gain than the Normal one, and its Gain knob, two log gangs in series, spans far more than one pot.
    /// </summary>
    [Theory]
    [InlineData("JCM800 2210")]
    [InlineData("JCM800 2205")]
    public void The_boost_channel_has_the_gain(string type) {
        double Level(float gain, bool normal) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, gain),
                (CircuitAmpBlock.Control1, 10f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Switch1, normal ? 1f : 0f)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 2e-5f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), 1_000, Rate));
        }

        var (boost, normal) = (Level(10, false), Level(10, true));
        var (half, normalHalf) = (Level(5, false), Level(5, true));
        output.WriteLine(
            $"{type}: full up Boost {boost:0.0}, Normal {normal:0.0} dB; "
            + $"at 5 Boost {half:0.0}, Normal {normalHalf:0.0} dB"
        );
        Assert.True(boost > normal + 10, "the Boost channel louder than the Normal one below clipping");
        Assert.True(boost - half > normal - normalHalf + 10, "two gangs span more than the Normal channel's one pot");
    }

    /// <summary>
    ///     The bridge clipper: with the Master low (the power amp clean) the Boost channel's output stops rising with
    ///     the input once the bridge conducts, and it clips both ways alike: the third harmonic leads the second. The
    ///     Boost Volume, after the clipper, still sets the level.
    /// </summary>
    [Fact]
    public void The_bridge_clips_the_boost_channel_symmetrically() {
        static float[] Render(float amplitude, float volume) {
            using var block = Create(
                "JCM800 2210",
                (CircuitAmpBlock.Gain, 7f),
                (CircuitAmpBlock.Control1, volume),
                (CircuitAmpBlock.Master, 1f)
            );
            return BlockRender.Mono(block, Signals.Sine(440, Rate, 24_000, amplitude), 64).AsSpan(12_000).ToArray();
        }

        static double Db(float[] x, double hz) => Analysis.ToDb(Analysis.MagnitudeAt(x, hz, Rate));
        var (soft, loud, louder) = (Render(0.0005f, 3f), Render(0.1f, 3f), Render(0.4f, 3f));
        var linear = Db(loud, 440) - Db(soft, 440);
        var held = Db(louder, 440) - Db(loud, 440);
        var (second, third) = (Db(loud, 880) - Db(loud, 440), Db(loud, 1_320) - Db(loud, 440));
        var volume = Db(Render(0.1f, 6f), 440) - Db(loud, 440);
        output.WriteLine(
            $"0.5 mV to 100 mV (+46 dB in): {linear:+0.0} dB out; to 400 mV (+12 dB in): {held:+0.0} dB; "
            + $"H2 {second:0.0}, H3 {third:0.0} dB; Boost Volume 3 to 6: {volume:+0.0} dB"
        );
        Assert.True(linear < 40, "the clipper holds the level");
        Assert.True(held < 3);
        Assert.True(third > -20, "a clipped wave has a strong third harmonic");
        Assert.True(second < third - 6, "three junctions each way: the odd harmonics lead");
        Assert.True(volume > 6);
    }

    /// <summary>
    ///     The tone knobs do what their labels say, below clipping: each channel's Bass lifts 80 Hz against 1 kHz and
    ///     its Treble 5 kHz against 500 Hz; the Boost channel's Middle lifts 600 Hz; the Presence lifts 5 kHz.
    /// </summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void The_tone_knobs_move_their_bands(string type, bool normal) {
        double Level(float hz, int knob, float value) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Switch1, normal ? 1f : 0f),
                (knob, value)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 24_000, normal ? 2e-3f : 5e-5f), 64);
            return Analysis.ToDb(Analysis.MagnitudeAt(rendered.AsSpan(12_000).ToArray(), hz, Rate));
        }

        double Lift(float hz, float reference, int knob) =>
            Level(hz, knob, 10) - Level(reference, knob, 10) - (Level(hz, knob, 0) - Level(reference, knob, 0));

        var bass = Lift(80, 1_000, CircuitAmpBlock.Bass);
        var treble = Lift(5_000, 500, CircuitAmpBlock.Treble);
        var presence = Level(5_000, CircuitAmpBlock.Presence, 10) - Level(5_000, CircuitAmpBlock.Presence, 0);
        output.WriteLine($"{type} [{normal}]: Bass {bass:+0.0}, Treble {treble:+0.0}, Presence {presence:+0.0} dB");
        Assert.True(bass > 3);
        Assert.True(treble > 3);
        Assert.True(presence > 2);
        if (!normal) {
            var middle = Level(600, CircuitAmpBlock.Middle, 10) - Level(600, CircuitAmpBlock.Middle, 0);
            output.WriteLine($"{type}: Middle {middle:+0.0} dB at 600 Hz");
            Assert.True(middle > 3);
        }
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, converging.</summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type, bool normal) {
        using var block = Create(
            type,
            (CircuitAmpBlock.Gain, 10f),
            (CircuitAmpBlock.Master, 10f),
            (CircuitAmpBlock.Switch1, normal ? 1f : 0f)
        );
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var control in Knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(control, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type} [{normal}]: control {control} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        var channel = block.Channel;
        foreach (var newton in Solvers(channel.Stages).Append(channel.Power.Newton)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type} [{normal}]: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type} [{normal}]: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>
    ///     Every knob changes the sound in the given channel, but the Boost channel's own (Middle, Boost Volume) on
    ///     the Normal channel, where they only move the idle stack's load or are out of circuit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Every_knob_is_audible(string type, bool normal) {
        var tones = Signals.Sine(110, Rate, 24_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 24_000, 0.02f), static (a, b) => a + b)
            .ToArray();

        double[] Bands(int control, float value) {
            using var block = Create(
                type,
                [
                    (CircuitAmpBlock.Gain, 4f), (CircuitAmpBlock.Master, 4f),
                    (CircuitAmpBlock.Switch1, normal ? 1f : 0f), (control, value)
                ]
            );
            var rendered = BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
            return [
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 110, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(rendered, 2_500, Rate))
            ];
        }

        foreach (var control in Knobs) {
            if (normal && control is CircuitAmpBlock.Middle or CircuitAmpBlock.Control1) {
                continue;
            }

            var (a, b) = (Bands(control, 0f), Bands(control, 10f));
            var change = Math.Max(Math.Abs(a[0] - b[0]), Math.Abs(a[1] - b[1]));
            output.WriteLine(
                $"{type} [{normal}] control {control}: 110 Hz {b[0] - a[0]:+0.0;-0.0} dB, 2.5 kHz "
                + $"{b[1] - a[1]:+0.0;-0.0} dB"
            );
            Assert.True(change > 0.2, $"{type} [{normal}]: control {control} moves the sound by {change:0.00} dB");
        }
    }

    /// <summary>
    ///     Below clipping, Gain (the Normal channel's Volume), the Boost Volume and Master raise the level
    ///     monotonically.
    /// </summary>
    [Theory]
    [MemberData(nameof(Channels))]
    public void Gain_volume_and_master_raise_the_level_monotonically(string type, bool normal) {
        double Level(int knob, float value) {
            using var block = Create(
                type,
                [
                    (CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Master, 3f),
                    (CircuitAmpBlock.Switch1, normal ? 1f : 0f), (knob, value)
                ]
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        int[] knobs = normal
            ? [CircuitAmpBlock.Gain, CircuitAmpBlock.Master]
            : [CircuitAmpBlock.Gain, CircuitAmpBlock.Master, CircuitAmpBlock.Control1];
        foreach (var knob in knobs) {
            var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v)).ToArray();
            output.WriteLine(
                $"{type} [{normal}] knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB"
            );
            for (var i = 1; i < levels.Length; i++) {
                if (levels[i - 1] > -100) {
                    Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type} [{normal}]: knob {knob} falls at {i}");
                }
            }

            Assert.True(levels[^1] - levels[0] > 10, $"knob {knob} spans only {levels[^1] - levels[0]:0.0} dB");
        }
    }

    static IEnumerable<NewtonSolver> Solvers(IEnumerable<AmpStage> stages) =>
        stages.SelectMany(static s => s is ChannelSwitchStage c ? Solvers(c.Active) : s.Newton is { } n ? [n] : []);

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
