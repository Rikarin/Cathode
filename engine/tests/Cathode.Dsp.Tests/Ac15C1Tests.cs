using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Vox AC15C1's stages: each input's chain against the reference simulator as one circuit, the preamp's rails
///     against the drawing's dropping resistors (the drawing gives no voltages), and the two op-amps' gains and rails.
/// </summary>
public partial class AmpStageTests {
    /// <summary>
    ///     Each input's chain as one circuit with no Thévenin cut (the reference's op-amps are ideal, so the drive
    ///     stays below their rails): the Top Boost input, then (Switch 1) the Normal input.
    /// </summary>
    [Theory]
    [InlineData(0, 0.02, -38)]
    [InlineData(0, 0.2, -38)]
    [InlineData(1, 0.02, -40)]
    [InlineData(1, 0.2, -40)]
    public void Ac15c1_chains_match_the_reference(int switches, double amplitude, double limitDb) =>
        Remaining_marshall_chains_match_the_reference("AC15C1", switches, amplitude, limitDb); // any model's chain

    /// <summary>
    ///     The model's rails are the ones its own stages' currents leave on the drawing's droppers: R75 22k from the
    ///     screen node to the inverter's node, R18 10k from there to V2's and R7 22k to V1's (both halves of V1 draw).
    /// </summary>
    [Fact]
    public void Ac15c1_rails_follow_from_the_droppers_at_the_stages_currents() {
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 1, false);
        var chain = Chain(AC15C1.Design, false);
        var v1 = (NetworkStage<FastTriode>)chain[0].Create(Rate);
        v1.Update(controls);
        v1.Reset(0);
        var v2 = (NetworkStage<Devices<FastTriode, FastTriode>>)chain[1].Create(Rate);
        v2.Update(controls);
        v2.SetSourceResistance(v1.OutputResistance);
        v2.Reset(v1.RestOutput);

        // Nodes as the netlists number them: V1's plate 4; V2's plate 5 and its follower's cathode 7.
        var first = 2 * (AC15C1.FirstSupply - v1.RestVoltage(4)) / 100e3;
        var second = (AC15C1.SecondSupply - v2.RestVoltage(5)) / 100e3 + v2.RestVoltage(7) / 56e3;
        var pair = LongTailPair.For((LongTailPairDesign)AC15C1.Design.Power.PhaseInverter);
        var inverter = (2 * AC15C1.InverterSupply - pair.RestPlateA - pair.RestPlateB) / 100e3;

        var power = new PowerAmp(AC15C1.Design.Power, Rate);
        power.Configure(new(0.5, 0, 0, 0.5, 0.5, 0.5, 1));
        power.Reset();
        var inverterNode = power.ScreenSupply - 22e3 * (first + second + inverter);
        output.WriteLine(
            $"V1 {first * 1e3:0.00} mA, V2 {second * 1e3:0.00} mA, V3 {inverter * 1e3:0.00} mA; screens "
            + $"{power.ScreenSupply:0.0} V, inverter node {inverterNode:0.0} V, "
            + $"V2's {inverterNode - 10e3 * second:0.0} V, V1's {inverterNode - 22e3 * first:0.0} V; "
            + $"follower grid {v2.RestVoltage(5):0.0} V, cathode "
            + $"{v2.RestVoltage(7):0.0} V; EL84 cathodes {power.CathodeVoltage:0.0} V, "
            + $"{power.PlateCurrents.A * 1e3:0.0} mA each"
        );
        Assert.Equal(AC15C1.InverterSupply, inverterNode, 0.02 * AC15C1.InverterSupply);
        Assert.Equal(AC15C1.SecondSupply, inverterNode - 10e3 * second, 0.02 * AC15C1.SecondSupply);
        Assert.Equal(AC15C1.FirstSupply, inverterNode - 22e3 * first, 0.02 * AC15C1.FirstSupply);
        Assert.Equal(first + second + inverter, AC15C1.Design.Power.PowerSupply.PreampCurrent, 0.2e-3);
        Assert.Equal(second, AC15C1.Design.PreampSupply!.Nodes[0].Current, 0.2e-3);
        Assert.Equal(first, AC15C1.Design.PreampSupply!.Nodes[1].Current, 0.2e-3);
        // Owners measure 320 to 335 V from plate to cathode on a 120 Ω shared cathode resistor.
        Assert.InRange(power.Supply - power.CathodeVoltage, 315, 340);
    }

    /// <summary>
    ///     U1A and U5A by their resistors. Top Boost: 1 + 820k/470k from U1A's non-inverting input, then R57 56k into
    ///     R60 56k beside R54 22k, then 1 + 3.9k/1k: 2.96. Normal with the volume at 10: C5 and R1 330k into VR1 500k
    ///     beside R22 470k, then 820k/470k inverting and the same second half; and neither swings past its ± 27 V
    ///     rails.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ac15c1_op_amps_have_the_drawing_s_gain_and_stop_at_their_rails(bool normal) {
        var stage = Chain(AC15C1.Design, normal)[^1].Create(Rate);
        stage.SetSourceResistance(1);
        stage.Update(new(1, 0.5, 0.5, 0.5, 1, false));
        stage.Reset(0);
        var second = 1 / (1 + 56e3 * (1 / 56e3 + 1 / 22e3)) * 4.9;
        var loaded = 1 / (1 / 500e3 + 1 / 470e3);
        var expected = normal ? loaded / (330e3 + loaded) * (820e3 / 470e3) * second : (1 + 820e3 / 470e3) * second;

        var small = Peak(0.1) / 0.1;
        var driven = Peak(100);
        output.WriteLine($"{(normal ? "Normal" : "Top Boost")}: gain {small:0.000} ({expected:0.000}), {driven:0.0} V");
        Assert.Equal(expected, small, 0.03 * expected); // at 200 Hz, C5 and C4 still move the Normal input's by 1 %
        Assert.InRange(driven, 20, 27);

        double Peak(double amplitude) {
            var peak = 0.0;
            for (var n = 0; n < 9_600; n++) {
                var y = stage.Tick(amplitude * Math.Sin(2 * Math.PI * 200 * n / Rate));
                peak = n > 4_800 ? Math.Max(peak, Math.Abs(y)) : peak;
            }

            return peak;
        }
    }
}

/// <summary>
///     The AC15C1 on the Circuit Amp block: its panel, and the controls only one input has. The sweeps it shares with
///     the other Vox-style models are in <see cref="ClassAAmpSweepTests" />.
/// </summary>
public class Ac15C1Tests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static int Type => AmpDesign.All.ToList().IndexOf(AC15C1.Design);

    [Fact]
    public void Panel_names_the_real_controls_and_hides_the_rest() {
        Assert.Equal(
            ["Volume", "Bass", "Treble", "Cut", "Master", "Normal Ch"],
            new[] { "gain", "bass", "treble", "cut", "master", "switch1" }.Select(Label)
        );
        foreach (var id in new[] { "middle", "presence", "depth", "bright", "switch2", "control1" }) {
            Assert.Empty(Label(id));
        }
    }

    /// <summary>The Treble and Bass are the Top Boost input's: on the Normal input they move nothing.</summary>
    [Theory]
    [InlineData(CircuitAmpBlock.Treble, 4_000)]
    [InlineData(CircuitAmpBlock.Bass, 80)]
    public void Tone_controls_work_on_the_top_boost_input_only(int knob, double frequency) {
        double Span(bool normal) => Level(normal, knob, 10, frequency) - Level(normal, knob, 0, frequency);
        var (topBoost, normal) = (Span(false), Span(true));
        output.WriteLine($"{frequency} Hz, knob 0 to 10: Top Boost {topBoost:0.0} dB, Normal {normal:0.0} dB");
        Assert.True(topBoost > 6, $"{topBoost:0.0} dB");
        Assert.Equal(0, normal, 0.01);
    }

    static double Level(bool normal, int knob, float value, double frequency) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Type);
        block.Params.SetTarget(CircuitAmpBlock.Switch1, normal ? 1 : 0);
        block.Params.SetTarget(CircuitAmpBlock.Gain, 3f);
        block.Params.SetTarget(knob, value);
        block.Prepare(new(Rate, 64));
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 19_200, 0.003f), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(9_600)));
    }

    static string Label(string id) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[Type] ?? string.Empty : parameter.Name;
    }
}
