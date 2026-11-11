using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Supro Thunderbolt S6420 on the Circuit Amp block: its panel, its operating points against the DC voltages on
///     Jim Mitchell's trace, the fixed paraphase inverter (balance, antiphase, the driving plate's resistance), what
///     Volume and Tone do, the input's 100 Hz high-pass, and full-drive sweeps that stay finite and converge.
/// </summary>
public class ThunderboltAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const string Name = "Thunderbolt S6420";

    static readonly int Type = AmpDesign.All.ToList().IndexOf(Thunderbolt.Design);

    [Fact]
    public void The_panel_is_volume_and_tone() {
        string[] ids = ["gain", "bass", "middle", "treble", "presence", "depth", "cut", "master", "bright", "switch1"];
        var shown = ids.ToDictionary(
            static id => id,
            static id => CircuitAmpBlock.Info.Params.Single(p => p.Id == id).TypeLabels![Type]
        );
        Assert.Equal(Name, Thunderbolt.Design.Name);
        Assert.Equal("Volume", shown["gain"]);
        Assert.Equal("Tone", shown["treble"]);
        Assert.All(ids.Except(["gain", "treble"]), id => Assert.Empty(shown[id]));
        Assert.Same(SpeakerDesign.OpenBack1x15, Thunderbolt.Design.Power.Speaker);
        Assert.False(Thunderbolt.Design.Power.HasFeedback);
    }

    /// <summary>
    ///     Mitchell's DC voltages: V1 197 V and 2.2 V, V2A 138 V and 1.4 V, V2B 167 V and 2.0 V, the 6L6s' shared
    ///     cathode 30 V with 397 V on the reservoir and 390 V on the screens. V2's plate readings do not agree with
    ///     its cathode readings through the drawing's own resistors (1.4 V on 2.2k is 0.64 mA, which leaves 165 V under
    ///     270k from 337 V, not 138 V; 2.0 V on 3.9k leaves 198 V, not 167 V), so the cathodes are held closely and
    ///     those two plates loosely.
    /// </summary>
    [Fact]
    public void The_operating_points_follow_the_voltages_on_the_drawing() {
        var channel = new AmpChannel(Thunderbolt.Design, Rate);
        var stages = channel.Stages.Cast<NetworkStage<FastTriode>>().ToArray();
        var inverter = Assert.IsType<Paraphase>(channel.Power.PhaseInverter);
        var power = channel.Power;
        var tubes = stages.Select(static s => {
                    var triode = s.Design.Netlist.Elements.Single(static e => e.Kind == ElementKind.Triode);
                    return (Plate: s.RestVoltage(triode.A), Cathode: s.RestVoltage(triode.C));
                }
            )
            .ToArray();

        (string Point, double Value, double Drawn, double Tolerance)[] points = [
            ("V1 plate", tubes[0].Plate, 197, 0.1),
            ("V1 cathode", tubes[0].Cathode, 2.2, 0.1),
            ("V2A plate", tubes[1].Plate, 138, 0.25),
            ("V2A cathode", tubes[1].Cathode, 1.4, 0.1),
            ("V2B plate", inverter.RestPlateB, 167, 0.25),
            ("V2B cathode", inverter.RestCathode, 2.0, 0.1),
            ("6L6 cathode", power.CathodeVoltage, 30, 0.15),
            ("reservoir", power.Supply, 397, 0.01),
            ("screens", power.ScreenSupply, 390, 0.02)
        ];
        foreach (var (point, value, drawn, _) in points) {
            output.WriteLine($"{point}: {value:0.00} V (drawing {drawn})");
        }

        Assert.All(points, static p => Assert.InRange(p.Value / p.Drawn, 1 - p.Tolerance, 1 + p.Tolerance));
    }

    /// <summary>
    ///     The inverter passes the driving plate on as output A and inverts a divided part of it for output B. Nothing
    ///     trims the balance: with the unbypassed 3.9k under V2B its gain is in the thirties against a divider of 1 in
    ///     23.5, so the inverted side is the hotter one, by about a quarter open-circuit. Output A's impedance is V2A's
    ///     plate, as the stage itself reports it.
    /// </summary>
    [Fact]
    public void The_paraphase_inverts_and_roughly_balances() {
        var design = Assert.IsType<ParaphaseDesign>(Thunderbolt.Design.Power.PhaseInverter);
        var inverter = PhaseInverter.For(design);
        var (a, b) = inverter.Evaluate(5, out var slopeA, out _);
        var (a2, b2) = inverter.Evaluate(-5, out _, out _);
        output.WriteLine($"+5 V -> {a:0.00} / {b:0.00} V, -5 V -> {a2:0.00} / {b2:0.00} V; tap {design.Tap:0.0000}");
        Assert.Equal(5, a, 9);
        Assert.Equal(1, slopeA, 6);
        Assert.True(b < 0 && b2 > 0, "output B is not inverted");
        Assert.InRange(-b / a, 1.0, 1.5);
        Assert.InRange(-b / (a * design.Tap), 30, 40);

        // Driven far past the second triode's range, output A goes on and output B stops: cut off at the supply one
        // way, bottomed above its cathode the other.
        var (far, cutOff) = inverter.Evaluate(-200, out _, out var flat);
        var (_, bottomed) = inverter.Evaluate(200, out _, out _);
        output.WriteLine($"output B from {bottomed:0.0} V to {cutOff:0.0} V about {inverter.RestPlateB:0.0} V");
        Assert.Equal(-200, far, 6);
        Assert.Equal(design.Supply - inverter.RestPlateB, cutOff, 0);
        Assert.Equal(0, flat, 2);
        Assert.InRange(inverter.RestPlateB + bottomed, 5, 60);

        var stage = new AmpChannel(Thunderbolt.Design, Rate).Stages[1];
        output.WriteLine($"V2A plate: {stage.OutputResistance:0} Ω as the stage has it, {design.SourceResistance:0} Ω");
        Assert.InRange(design.SourceResistance / stage.OutputResistance, 0.9, 1.1);
        Assert.Equal(design.SourceResistance, inverter.OutputImpedance.Zaa);
        Assert.True(inverter.OutputImpedance.Zbb > inverter.OutputImpedance.Zaa, "V2B's cathode is not bypassed");
    }

    [Fact]
    public void Volume_is_monotonic_and_tone_brightens() {
        double[] levels = [..new[] { 1f, 3f, 5f, 7f, 9f }.Select(static v => Level(110, (CircuitAmpBlock.Gain, v)))];
        output.WriteLine($"Volume 1..9: {string.Join(", ", levels.Select(static l => $"{l:0.0} dB"))}");
        Assert.All(levels.Zip(levels.Skip(1)), static pair => Assert.True(pair.Second > pair.First + 1));

        static double Tilt(float tone) =>
            Level(3_000, (CircuitAmpBlock.Treble, tone)) - Level(150, (CircuitAmpBlock.Treble, tone));

        var (dark, noon, bright) = (Tilt(0), Tilt(5), Tilt(10));
        output.WriteLine($"3 kHz over 150 Hz with Tone at 0, 5, 10: {dark:0.0}, {noon:0.0}, {bright:0.0} dB");
        Assert.True(bright > noon && noon > dark, "Tone up is not brighter");
        Assert.True(bright - dark > 6, $"Tone moves the tilt by only {bright - dark:0.0} dB");
    }

    /// <summary>
    ///     The 0.005 µF into 47k + 270k is a 100 Hz high-pass ahead of V1: the low E of a bass (41 Hz) comes through
    ///     well below a note two octaves up.
    /// </summary>
    [Fact]
    public void The_input_cap_rolls_the_bass_off() {
        var (low, high) = (Level(41.2, (CircuitAmpBlock.Treble, 0f)), Level(164.8, (CircuitAmpBlock.Treble, 0f)));
        output.WriteLine($"41 Hz {low:0.0} dB, 165 Hz {high:0.0} dB");
        Assert.InRange(high - low, 3, 14);
    }

    [Fact]
    public void Full_drive_sweeps_stay_finite_and_converge() {
        using var block = Create((CircuitAmpBlock.Gain, 10f));
        var chord = CircuitAmpBlockTests.Chord(9_600, 0.8f);
        var peak = 0.0;
        foreach (var (control, value) in new[] {
                     (CircuitAmpBlock.Gain, 0f), (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Treble, 0f),
                     (CircuitAmpBlock.Treble, 10f), (CircuitAmpBlock.Treble, 5f)
                 }) {
            block.Params.SetTarget(control, value);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"control {control} at {value}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        foreach (var newton in channel.Stages.Select(static s => s.Newton).Append(channel.Power.Newton)) {
            Assert.Equal(0, newton!.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine(
            $"peak {Analysis.ToDb(peak):0.0} dBFS; cathode {channel.Power.CathodeVoltage:0.0} V, "
            + $"reservoir {channel.Power.Supply:0} V at full drive"
        );
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>The level in dB of a small sine at <paramref name="hz" /> through the amp.</summary>
    static double Level(double hz, (int Index, float Value) setting) {
        using var block = Create((CircuitAmpBlock.Gain, 4f), setting);
        var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 48_000, 0.01f), 64).AsSpan(24_000);
        return Analysis.ToDb(Analysis.MagnitudeAt(rendered, hz, Rate));
    }

    static CircuitAmpBlock Create(params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Type);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }
}
