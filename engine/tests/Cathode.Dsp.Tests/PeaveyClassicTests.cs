using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Numerics;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Peavey Classic 30 (issue #363) against its drawing, 81501580: the DC voltages printed at the plates, the
///     −14 V bias, the feedback network's shelf, the two channels' gain and the Boost switch. The knob sweeps are in
///     <see cref="FenderUsAmpTests" /> and the stages against the reference simulator in <see cref="AmpStageTests" />.
/// </summary>
public class PeaveyClassicTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly int Type = AmpDesign.All.ToList().IndexOf(PeaveyClassic30.Design);

    /// <summary>
    ///     The drawing prints each plate's voltage: V1A 160 V, V2A 150 V, V2B 175 V, V1B 165 V (the stages, by their
    ///     plate's node), V3A 195 V (the loop stage) and V3B 225 V (the cathodyne). The model's 12AX7 draws a little
    ///     more than Peavey's did and sits 4 to 11 % under each of them: all within 12 %.
    /// </summary>
    [Fact]
    public void Plates_rest_where_the_drawing_prints_them() {
        var design = PeaveyClassic30.Design;
        var lead = ((ChannelSwitchStageDesign)design.Preamp[1]).Off;
        (AmpStageDesign Stage, int Plate, double Volts)[] stages = [
            (design.Preamp[0], 3, 160),
            (lead[0], 2, 150),
            (lead[1], 2, 175),
            (lead[2], 5, 165)
        ];
        var points = new List<(string Name, double Model, double Drawing)>();
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 0.5, false);
        var (rest, resistance) = (0.0, 0.0);
        foreach (var (stage, plate, volts) in stages) {
            var built = Assert.IsType<NetworkStage<FastTriode>>(stage.Create(96_000));
            built.Update(controls);
            built.SetSourceResistance(resistance);
            built.Reset(rest);
            (rest, resistance) = (built.RestOutput, built.OutputResistance);
            points.Add((stage.Name, built.RestVoltage(plate), volts));
        }

        points.Add(("V3A", new LoopStage(design.Power.Loop!, Rate).Table.RestPlateA, 195));
        points.Add(("V3B", PhaseInverter.For(design.Power.PhaseInverter).RestPlateA, 225));
        foreach (var (name, model, drawing) in points) {
            output.WriteLine($"{name}: {model:0.0} V, drawing {drawing:0.0} V");
        }

        Assert.All(points, static p => Assert.InRange(p.Model, 0.88 * p.Drawing, 1.12 * p.Drawing));
    }

    /// <summary>
    ///     The idle current is what the drawing's −14 V bias gives on the EL84 at its 332 V plates and 328 V screens.
    /// </summary>
    [Fact]
    public void Idle_current_is_the_drawings_bias_on_the_el84() {
        var power = PeaveyClassic30.Design.Power;
        var current = KorenPentode.Plate(power.Tube, -14, 328, power.Supply);
        output.WriteLine($"{current * 1e3:0.0} mA at -14 V; the design idles at {power.IdleCurrent * 1e3:0.0} mA");
        Assert.InRange(power.IdleCurrent, 0.9 * current, 1.1 * current);
    }

    /// <summary>
    ///     The feedback from the 8 Ω tap, as the power amp's filters carry it, against the drawing's network worked out
    ///     here: 220k ‖ 0.0047 µF and 100k into V3A's 1.5k with 10k + 0.2 µF across it. Within half a decibel from 40
    ///     Hz to 5 kHz, and 9 dB less at 40 Hz than at 2 kHz.
    /// </summary>
    [Fact]
    public void Feedback_follows_the_drawings_network() {
        var feedback = PeaveyClassic30.Design.Power.Feedback;
        var low = feedback.LowFeedback;
        var corner = feedback.DepthFrequency;

        double Model(double hz) {
            var shelf = new Complex(low * corner, hz) / new Complex(corner, hz);
            return (feedback.Response(hz, 0.5) * shelf).Magnitude;
        }

        static double Drawing(double hz) {
            var s = new Complex(0, 2 * Math.PI * hz);
            var series = 100e3 + 220e3 / (1 + s * 220e3 * 4.7e-9);
            var shunt = 1 / (1 / 1.5e3 + 1 / (10e3 + 1 / (s * 0.2e-6)));
            return (shunt / (series + shunt)).Magnitude;
        }

        foreach (var hz in new[] { 40.0, 100, 250, 500, 1_000, 2_000, 5_000 }) {
            var error = Analysis.ToDb(Model(hz) / Drawing(hz));
            output.WriteLine(
                $"{hz:0} Hz: {Analysis.ToDb(Drawing(hz)):0.0} dB on the drawing, model {error:+0.00;-0.00} dB"
            );
            Assert.InRange(error, -0.5, 0.5);
        }

        Assert.InRange(Analysis.ToDb(Drawing(2_000) / Drawing(40)), 8, 10);
    }

    /// <summary>
    ///     The loop stage's cathode: 1.5k at DC, and for the signal the 1.5k, the branch's 10k and the 100k in
    ///     parallel.
    /// </summary>
    [Fact]
    public void Loop_stage_cathode_is_the_feedback_shunt() {
        var loop = PeaveyClassic30.Design.Power.Loop!;
        Assert.Equal(1.5e3, loop.BypassedCathode + loop.CathodeResistor, 1e-9);
        Assert.Equal(1 / (1 / 1.5e3 + 1 / 10e3 + 1 / 100e3), loop.CathodeResistor, 1e-9);
    }

    /// <summary>
    ///     A quiet 1 kHz sine: with every knob at noon the Lead channel (Pre, V2A, V2B, Post) is about 6 dB louder than
    ///     the Normal channel, and with the channels' own knobs at 10 more than 12 dB. Boost (0.047 µF across the
    ///     stack's 270 pF) lifts 400 Hz by more than 6 dB.
    /// </summary>
    [Fact]
    public void Lead_channel_has_more_gain_and_boost_lifts_the_mids() {
        foreach (var (knobs, least) in new[] { (5f, 1.5), (10f, 4.0) }) {
            var normal = Level(1_000, knobs, (CircuitAmpBlock.Switch1, 1f));
            var lead = Level(1_000, knobs, (CircuitAmpBlock.Switch1, 0f));
            output.WriteLine($"1 kHz, knobs at {knobs:0}: Lead {Analysis.ToDb(lead / normal):0.0} dB over Normal");
            Assert.True(lead > least * normal);
        }

        var flat = Level(400, 5, (CircuitAmpBlock.Switch1, 1f));
        var boosted = Level(400, 5, (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f));
        output.WriteLine($"400 Hz: Boost {Analysis.ToDb(boosted / flat):0.0} dB");
        Assert.True(boosted > 2 * flat);
    }

    /// <summary>
    ///     Both channels with every knob at 10, Boost on and a loud low chord: finite, bounded, and at rest again
    ///     afterwards.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Full_drive_stays_bounded_and_settles(float channel) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Type);
        block.Params.SetTarget(CircuitAmpBlock.Switch1, channel);
        block.Params.SetTarget(CircuitAmpBlock.Switch2, 1);
        foreach (var knob in new[] {
                     CircuitAmpBlock.Gain, CircuitAmpBlock.Master, CircuitAmpBlock.Control1, CircuitAmpBlock.Bass,
                     CircuitAmpBlock.Middle, CircuitAmpBlock.Treble
                 }) {
            block.Params.SetTarget(knob, 10);
        }

        block.Prepare(new(Rate, 64));
        var chord = Signals.Sine(82.4, Rate, 24_000, 0.5f)
            .Zip(Signals.Sine(123.5, Rate, 24_000, 0.5f), static (a, b) => a + b)
            .ToArray();
        var played = BlockRender.Mono(block, Signals.Concat(chord, new float[48_000]), 64);
        var peak = Analysis.Peak(played);
        var tail = Analysis.Rms(played.AsSpan(played.Length - 4_800));
        output.WriteLine($"peak {Analysis.ToDb(peak):0.0} dBFS, tail {Analysis.ToDb(tail):0.0} dBFS");
        Assert.True(Analysis.AllFinite(played));
        Assert.True(Analysis.ToDb(peak) < 6);
        Assert.True(Analysis.ToDb(tail) < -80);
    }

    /// <summary>
    ///     The block's output level for a very quiet sine at <paramref name="hz" />, with Pre, Post and Volume at
    ///     <paramref name="knobs" /> and the tone knobs at noon.
    /// </summary>
    static double Level(double hz, float knobs, params (int Index, float Value)[] settings) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, Type);
        foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master, CircuitAmpBlock.Control1 }) {
            block.Params.SetTarget(knob, knobs);
        }

        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        var rendered = BlockRender.Mono(block, Signals.Sine(hz, Rate, 19_200, 0.0002f), 64);
        return Analysis.Rms(rendered.AsSpan(9_600));
    }
}
