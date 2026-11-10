using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Ampeg SVT-CL on the Circuit Amp block, against SLM's drawings (preamp 07S519-11, power amp 07S419, tube
///     board 07S419-41) and Ampeg's owner's manual: its panel, the tubes' DC voltages and the first stage's signal
///     voltage, the driver's rest point, the supply nodes, the two inputs, the Ultra switches, the five midrange
///     frequencies, and knob sweeps without instability.
/// </summary>
public class SvtClassicTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const string Type = "SVT-CL";

    [Fact]
    public void Panel_names_the_real_controls_and_hides_the_rest() {
        Assert.Equal(
            ["Ultra Hi", "Ultra Lo", "-15 dB Input"],
            new[] { "switch1", "switch2", "switch3" }.Select(Label)
        );
        Assert.Equal(
            ["Gain", "Bass", "Middle", "Treble", "Master", "Frequency"],
            new[] { "gain", "bass", "middle", "treble", "master", "control1" }.Select(Label)
        );
        foreach (var id in new[] { "presence", "depth", "cut", "bright", "control2" }) {
            Assert.Empty(Label(id));
        }
    }

    /// <summary>
    ///     The preamp drawing's DC voltages: V1A's plate 235 V, V1B's and V2A's 240 V, V2B's cathode 100 V; and its
    ///     signal voltage after V1A, 0.9 V rms for 15 mV rms into the 0 dB input (a gain of 60). The model's 12AX7
    ///     draws a little more than the drawing's at these points, so its plates sit some volts lower.
    /// </summary>
    [Fact]
    public void Preamp_rests_at_the_drawings_voltages_and_v1a_has_its_gain() {
        var channel = Channel();
        var stages = channel.Stages;
        output.WriteLine(
            $"V1A plate {stages[0].RestOutput:0.0} V (235), V1B plate {stages[1].RestOutput:0.0} V (240), V2B "
            + $"cathode {stages[3].RestOutput:0.0} V (100)"
        );
        Assert.InRange(stages[0].RestOutput, 235 - 25, 235 + 25);
        Assert.InRange(stages[1].RestOutput, 240 - 25, 240 + 25);
        Assert.InRange(stages[3].RestOutput, 100 - 30, 100 + 30);

        const int Samples = 48_000;
        var squares = 0.0;
        for (var n = 0; n < Samples; n++) {
            var y = stages[0].Tick(0.015 * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 1_000 * n / Rate))
                - stages[0].RestOutput;
            if (n >= Samples / 2) {
                squares += y * y;
            }
        }

        var rms = Math.Sqrt(2 * squares / Samples);
        output.WriteLine($"V1A: {rms:0.000} V rms for 15 mV in (0.9 V on the drawing)");
        Assert.InRange(20 * Math.Log10(rms / 0.9), -1.5, 1.5);
    }

    /// <summary>
    ///     The power amp at rest: 660 V on the plates and 24 mA a tube, the screens' stand-in node at 380 V and the
    ///     preamp's at 345 V, and the driver's followers at the drawing's "approx. −45 V" with balanced, antiphase
    ///     outputs that swing far enough to cut the 6550s off and to drive their grids positive.
    /// </summary>
    [Fact]
    public void Power_amp_rests_at_the_drawings_voltages() {
        var channel = Channel();
        var power = channel.Power;
        output.WriteLine(
            $"B+ {power.Supply:0.0} V, screens {power.ScreenSupply:0.0} V, preamp {power.PreampVoltage:0.0} V, grid "
            + $"bias {power.GridBias:0.0} V, idle {power.PlateCurrents.A * 1e3:0.0} mA per tube"
        );
        Assert.InRange(power.Supply, 655, 665);
        Assert.InRange(power.ScreenSupply, 375, 385);
        Assert.InRange(power.PreampVoltage, 340, 350);
        Assert.InRange(power.PlateCurrents.A, 0.022, 0.026);

        var driver = (DriverChain)PhaseInverter.For(SvtClassic.Design.Power.PhaseInverter);
        var (a, b) = driver.Evaluate(0.05, out _, out _);
        var (high, _) = driver.Evaluate(-driver.Range, out _, out _);
        var (low, _) = driver.Evaluate(driver.Range, out _, out _);
        output.WriteLine(
            $"Followers rest at {driver.RestFollower:0.0} V; +50 mV gives {a:0.00} / {b:0.00} V; swing {low:0} .. "
            + $"{high:0} V"
        );
        Assert.InRange(driver.RestFollower, -47, -43);
        Assert.True(a < 0 && b > 0, "the outputs are in antiphase");
        Assert.InRange(-b / a, 0.9, 1.1);
        Assert.True(high > 45 && low < -60, $"follower swing {low:0} .. {high:0} V");
    }

    /// <summary>The −15 dB input: 100k in series and 22k to ground, against 18k in series.</summary>
    [Fact]
    public void Padded_input_is_fifteen_decibels_down() {
        var pad = Level(1_000, 1e-3f, (CircuitAmpBlock.Switch3, 1f)) - Level(1_000, 1e-3f);
        output.WriteLine($"-15 dB input: {pad:0.0} dB");
        Assert.InRange(pad, -16, -14);
    }

    /// <summary>
    ///     Ampeg's manual: Ultra Lo +2 dB at 40 Hz and −10 dB at 500 Hz, Ultra Hi +9 dB at 8 kHz. Read with the tone
    ///     controls and the gain at 5.
    /// </summary>
    [Fact]
    public void Ultra_switches_shape_as_the_manual_says() {
        var notch = Level(500, 1e-3f, (CircuitAmpBlock.Switch2, 1f)) - Level(500, 1e-3f);
        var lows = Level(40, 1e-3f, (CircuitAmpBlock.Switch2, 1f)) - Level(40, 1e-3f);
        var hi = Level(8_000, 1e-3f, (CircuitAmpBlock.Switch1, 1f)) - Level(8_000, 1e-3f);
        var hiLows = Level(100, 1e-3f, (CircuitAmpBlock.Switch1, 1f)) - Level(100, 1e-3f);
        output.WriteLine(
            $"Ultra Lo: {lows:0.0} dB at 40 Hz, {notch:0.0} dB at 500 Hz; Ultra Hi: {hi:0.0} dB at 8 kHz, "
            + $"{hiLows:0.0} dB at 100 Hz"
        );
        Assert.InRange(lows, 1, 3.5);
        Assert.InRange(notch, -13, -7);
        Assert.InRange(hi, 6, 12);
        Assert.InRange(hiLows, -0.5, 0.5);
    }

    /// <summary>
    ///     The Midrange pot boosts and cuts around each of the Frequency switch's five centres, most within a third of
    ///     an octave of the manual's figure, and moves a frequency far from it little.
    /// </summary>
    [Theory]
    [InlineData(0f, 220.0)]
    [InlineData(2.5f, 450.0)]
    [InlineData(5f, 800.0)]
    [InlineData(7.5f, 1_600.0)]
    [InlineData(10f, 3_000.0)]
    public void Midrange_boosts_and_cuts_at_the_selected_frequency(float position, double frequency) {
        double Swing(double hz) => At(hz, 10) - At(hz, 0);

        double At(double hz, float middle) =>
            Level(hz, 1e-3f, (CircuitAmpBlock.Control1, position), (CircuitAmpBlock.Middle, middle));

        var (boost, cut) = (At(frequency, 10) - At(frequency, 5), At(frequency, 0) - At(frequency, 5));
        var ratios = new[] { 0.5, 0.63, 0.79, 1, 1.26, 1.59, 2 };
        var peak = ratios.MaxBy(r => Swing(frequency * r)) * frequency;
        var far = frequency < 1_000 ? 8_000.0 : 60.0;
        output.WriteLine(
            $"{frequency} Hz: boost {boost:0.0} dB, cut {cut:0.0} dB, widest swing near {peak:0} Hz; {far} Hz moves "
            + $"{Swing(far):0.0} dB"
        );
        Assert.True(boost > 3, $"{frequency} Hz boost {boost:0.0} dB");
        Assert.True(cut < -6, $"{frequency} Hz cut {cut:0.0} dB");
        Assert.InRange(peak / frequency, 0.75, 1.3);
        Assert.InRange(Math.Abs(Swing(far)), 0, 3);
    }

    /// <summary>Below clipping, Gain and Master each raise the level monotonically.</summary>
    [Theory]
    [InlineData(CircuitAmpBlock.Gain)]
    [InlineData(CircuitAmpBlock.Master)]
    public void Gain_and_master_raise_the_level_monotonically(int knob) {
        var levels = new[] { 0.5f, 2.5f, 5f, 7.5f, 10f }
            .Select(v => Level(1_000, 1e-4f, (CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Master, 3f), (knob, v)))
            .ToArray();
        output.WriteLine($"knob {knob}: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] > levels[i - 1] - 0.1, $"not monotonic at step {i}");
        }

        Assert.True(levels[^1] - levels[0] > 10, $"spans only {levels[^1] - levels[0]:0.0} dB");
    }

    /// <summary>
    ///     Every knob swept end to end and every switch thrown while a loud low chord plays: finite, bounded, and
    ///     every solver converging.
    /// </summary>
    [Fact]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge() {
        using var block = Create((CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Control1, CircuitAmpBlock.Master
        ];
        var chord = Signals.Sine(41.2, Rate, 9_600, 0.5f)
            .Zip(Signals.Sine(61.7, Rate, 9_600, 0.5f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in knobs) {
            foreach (var value in new[] { 0f, 10f, 2.5f, 7.5f, 5f }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        foreach (var toggle in new[] { CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3 }) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        foreach (var newton in channel.Stages.Where(static s => s.Newton is not null).Select(static s => s.Newton!)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(newton.Unconverged <= newton.Samples / 10_000, $"{newton.Unconverged} of {newton.Samples}");
        }

        Assert.Equal(0, channel.Power.Newton.NonFinite);
        Assert.True(channel.Power.Newton.Unconverged <= channel.Power.Newton.Samples / 10_000);
        output.WriteLine($"peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    static int TypeIndex => AmpDesign.All.ToList().FindIndex(static a => a.Name == Type);

    /// <summary>The channel at rest with every knob at 5.</summary>
    static AmpChannel Channel() {
        var channel = new AmpChannel(SvtClassic.Design, Rate);
        channel.Configure(new(0.5, 0.5, 0.5, 0.5, 0.5, false), new(0.5, 0, 0, 0.5, 0.5, 0.5));
        channel.Reset();
        return channel;
    }

    static CircuitAmpBlock Create(params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, TypeIndex);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static string Label(string id) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels ? labels[TypeIndex] : parameter.Name;
    }

    /// <summary>The block's output level (dB) for a sine at <paramref name="frequency" />.</summary>
    static double Level(double frequency, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }
}
