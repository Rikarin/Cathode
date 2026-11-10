using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Sunn Model T (#365) against drawing D-1029 revision A: the rest points of its voltage chart, the supply
///     nodes and their fall at 150 W, the panel, and on the Circuit Amp block the volumes, the tone controls, the
///     inputs and full-drive knob sweeps.
/// </summary>
public class SunnModelTTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const string Model = "Sunn Model T";

    static readonly AmpDesign Design = SunnModelT.Design;

    /// <summary>
    ///     The chart's "no signal" column: V1's plates 227 and 232 V on 1.71 V (points 1, 3, 2), V2A's plate 203 V on
    ///     1.3 V (4, 7), the follower's cathode 205 V (5), the inverter's plates 271 and 251 V on 38 V (8, 10, 9). The
    ///     triode law is a generic 12AX7, so the plates are held to 6 % and the cathodes to 15 %.
    /// </summary>
    [Fact]
    public void Rest_points_match_the_drawings_voltage_chart() {
        var second = (TriodeStageDesign)Design.Preamp[1];
        var follower = (ToneStackStageDesign)Design.Preamp[2];
        Assert.Equal(351, second.Supply);
        Assert.Equal(351, follower.Supply);

        // Each V1 triode on the shared 680 Ω sees both currents: one triode on twice the resistor.
        var (_, firstPlate, firstCathode) = OperatingPoint.CommonCathode(TriodeModel.Ecc83, 0, 351, 100e3, 2 * 680);
        var (_, secondPlate, secondCathode) = OperatingPoint.CommonCathode(
            second.Tube,
            0,
            second.Supply,
            second.PlateResistor,
            second.CathodeResistor
        );
        var (_, followerCathode) = OperatingPoint.CathodeFollower(
            follower.Tube,
            secondPlate,
            follower.Supply,
            follower.CathodeResistor
        );
        var inverter = PhaseInverter.For(Design.Power.PhaseInverter);
        output.WriteLine(
            $"V1 {firstPlate:0.0} V on {firstCathode:0.00} V (227 and 232 on 1.71); V2A {secondPlate:0.0} V on "
            + $"{secondCathode:0.00} V (203 on 1.3); follower {followerCathode:0.0} V (205); inverter "
            + $"{inverter.RestPlateA:0.0} and {inverter.RestPlateB:0.0} V on {inverter.RestCathode:0.0} V "
            + "(271 and 251 on 38)"
        );
        Assert.Equal(229.5, firstPlate, 0.06 * 229.5);
        Assert.Equal(1.71, firstCathode, 0.15 * 1.71);
        Assert.Equal(203, secondPlate, 0.06 * 203);
        Assert.Equal(1.3, secondCathode, 0.15 * 1.3);
        Assert.Equal(205, followerCathode, 0.06 * 205);
        Assert.Equal(271, inverter.RestPlateA, 0.06 * 271);
        Assert.Equal(251, inverter.RestPlateB, 0.06 * 251);
        Assert.Equal(38, inverter.RestCathode, 0.15 * 38);
    }

    /// <summary>
    ///     The supply as drawn: A 519 V at the plates, 10k to B (419 V, the inverter), 10k to C (351 V, V1 and V2),
    ///     80 µF on each; the currents through the two 10k are the chart's drops. At 150 W into the load (34.6 V RMS
    ///     across the nominal 8 Ω) the chart has A at 481 V.
    /// </summary>
    [Fact]
    public void Supply_nodes_rest_at_the_charts_voltages_and_fall_towards_its_full_power_column() {
        var supply = Design.Power.PowerSupply;
        var chain = Assert.Single(Design.PreampSupply!.Nodes);
        Assert.Equal((10e3, 80e-6), (supply.PreampResistance, supply.PreampCap));
        Assert.Equal((10e3, 80e-6, 0), (chain.Resistance, chain.Cap, chain.Parent));
        Assert.Equal(10e-3, supply.PreampCurrent, 1e-9);
        Assert.Equal(6.8e-3, chain.Current, 1e-9);
        Assert.Equal([1, 1, 1], Design.Preamp.Select(static s => s.SupplyNode ?? 0));

        var power = new PowerAmp(Design.Power, 96_000);
        power.SetSourceResistance(30e3);
        power.Reset();
        var (idle, idlePreamp, idlePlate) = (power.Supply, power.PreampVoltage, power.PlateCurrents.A);
        Assert.Equal(519, idle, 2.0);
        Assert.Equal(419, idlePreamp, 3.0);
        Assert.Equal(Design.Power.IdleCurrent, idlePlate, 0.05 * idlePlate);

        // Find the drive that gives 150 W, then read node A once the supply has settled there.
        var (low, high) = (0.0, 60.0);
        var (watts, plates) = (0.0, 0.0);
        for (var i = 0; i < 12; i++) {
            var drive = 0.5 * (low + high);
            (watts, plates) = Drive(drive);
            if (watts > 150) {
                high = drive;
            } else {
                low = drive;
            }
        }

        output.WriteLine(
            $"idle: A {idle:0.0} V, B {idlePreamp:0.0} V, {idlePlate * 1e3:0.0} mA a tube, bias "
            + $"{power.GridBias:0.0} V; at {watts:0.0} W: A {plates:0.0} V (the chart: 481 V)"
        );
        Assert.Equal(150, watts, 5.0);
        Assert.Equal(481, plates, 10.0);

        (double Watts, double Plates) Drive(double amplitude) {
            power.Reset();
            const int Length = 96_000;
            var (square, node) = (0.0, 0.0);
            for (var n = 0; n < 2 * Length; n++) {
                var y = power.Tick(amplitude * Math.Sin(2 * Math.PI * 1_000 * n / 96_000.0));
                if (n >= Length) {
                    square += y * y / Length;
                    node += power.Supply / Length;
                }
            }

            return (square / SpeakerDesign.Nominal, node);
        }
    }

    [Fact]
    public void Panel_names_the_amps_controls() {
        Assert.Equal("Brite Vol", Label("gain"));
        Assert.Equal("Normal Vol", Label("control1"));
        Assert.Equal("Master", Label("master"));
        Assert.Equal("Presence", Label("presence"));
        Assert.Equal(["Both In", "Normal Input"], new[] { "switch1", "switch2" }.Select(Label));
        Assert.Equal(["", "", ""], new[] { "cut", "depth", "bright" }.Select(Label));
    }

    [Theory]
    [InlineData(CircuitAmpBlock.Gain)]
    [InlineData(CircuitAmpBlock.Control1)]
    [InlineData(CircuitAmpBlock.Master)]
    public void Volumes_raise_the_level_monotonically(int knob) {
        // The Normal volume does nothing until the guitar plays into that channel.
        (int, float)[] extra = knob == CircuitAmpBlock.Control1 ? [(CircuitAmpBlock.Switch2, 1f)] : [];
        var levels = new[] { 0.5f, 2.5f, 5f, 7.5f, 10f }
            .Select(v => Level(1_000, 1e-3f, [(CircuitAmpBlock.Master, 3f), ..extra, (knob, v)]))
            .ToArray();
        output.WriteLine($"knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] > levels[i - 1] - 0.1, $"not monotonic at step {i}");
        }

        Assert.True(levels[^1] - levels[0] > 6, $"spans only {levels[^1] - levels[0]:0.0} dB");
    }

    /// <summary>Every knob and switch the panel names changes the output where it acts.</summary>
    [Theory]
    [InlineData("bass", CircuitAmpBlock.Bass, 10f, 100)]
    [InlineData("middle", CircuitAmpBlock.Middle, 10f, 600)]
    [InlineData("treble", CircuitAmpBlock.Treble, 10f, 3_000)]
    [InlineData("presence", CircuitAmpBlock.Presence, 10f, 4_000)]
    [InlineData("both in", CircuitAmpBlock.Switch1, 1f, 1_000)]
    [InlineData("normal input", CircuitAmpBlock.Switch2, 1f, 5_000)]
    public void Every_control_is_audible(string name, int index, float high, double frequency) {
        // Normal Vol down, so that the Brite channel's 470 pF is not shunted by the other channel's mixing resistor.
        (int, float)[] common = [(CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Master, 3f)];
        var a = Level(frequency, 1e-3f, [..common, (index, 0f)]);
        var b = Level(frequency, 1e-3f, [..common, (index, high)]);
        output.WriteLine($"{name}: {a:0.0} -> {b:0.0} dB at {frequency} Hz");
        Assert.True(Math.Abs(b - a) > 1, $"{name} moves {b - a:0.0} dB");
    }

    /// <summary>
    ///     The Brite channel's 470 pF across its 470k mixing resistor: with the volumes equal, the Brite input is
    ///     brighter than the Normal one, by more at 5 kHz than at 200 Hz.
    /// </summary>
    [Fact]
    public void The_brite_channel_is_brighter_than_the_normal_one() {
        static double Tilt(float normalInput) {
            (int, float)[] settings = [
                (CircuitAmpBlock.Gain, 5f), (CircuitAmpBlock.Control1, 5f), (CircuitAmpBlock.Master, 3f),
                (CircuitAmpBlock.Switch2, normalInput)
            ];
            return Level(5_000, 1e-3f, settings) - Level(200, 1e-3f, settings);
        }

        var (brite, normal) = (Tilt(0), Tilt(1));
        output.WriteLine($"5 kHz over 200 Hz: Brite {brite:0.0} dB, Normal {normal:0.0} dB");
        Assert.True(brite > normal + 2, $"Brite {brite:0.0} dB, Normal {normal:0.0} dB");
    }

    [Fact]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge() {
        using var block = Create((CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Presence, CircuitAmpBlock.Control1, CircuitAmpBlock.Master
        ];
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.5f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.5f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var (index, value) in knobs
                     .SelectMany(static knob => new[] { (knob, 0f), (knob, 10f), (knob, 5f) })
                     .Concat([(CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.Switch2, 1f)])) {
            block.Params.SetTarget(index, value);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"control {index} at {value}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        foreach (var newton in channel.Stages.Where(static s => s.Newton is not null).Select(static s => s.Newton!)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(newton.Unconverged <= newton.Samples / 10_000, $"{newton.Unconverged} of {newton.Samples}");
        }

        var power = channel.Power.Newton;
        output.WriteLine(
            $"peak {Analysis.ToDb(peak):0.0} dBFS; power amp {power.MeanIterations:0.00} iterations a sample, "
            + $"{power.Unconverged} unconverged of {power.Samples}"
        );
        Assert.Equal(0, power.NonFinite);
        Assert.True(power.Unconverged <= power.Samples / 10_000);
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    static int Type => AmpDesign.All.ToList().FindIndex(static a => a.Name == Model);

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

    static string Label(string id) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[Type] : parameter.Name;
    }

    static double Level(double frequency, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }
}
