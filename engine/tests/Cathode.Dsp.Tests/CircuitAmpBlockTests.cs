using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp block as a whole: levels, operating points of the output stages, Newton's health under abuse, and
///     the expert controls doing what they say. The stages themselves are validated in <see cref="AmpStageTests" />.
/// </summary>
public class CircuitAmpBlockTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    public static TheoryData<int> Types => [..Enumerable.Range(0, AmpDesign.All.Count)];

    [Theory]
    [MemberData(nameof(Types))]
    public void Typical_guitar_input_comes_out_at_sane_levels(int type) {
        // A strummed chord peaking at -12 dBFS (0.25 V at the jack).
        foreach (var gain in new[] { 3f, 5f, 8f }) {
            using var block = Create(type, (CircuitAmpBlock.Gain, gain));
            var rendered = BlockRender.Mono(block, Chord(96_000, 0.25f), 64).AsSpan(9_600);
            var peak = Analysis.ToDb(Analysis.Peak(rendered));
            var rms = Analysis.ToDb(Analysis.Rms(rendered));
            output.WriteLine($"{AmpDesign.All[type].Name} gain {gain}: peak {peak:0.0} dBFS, rms {rms:0.0} dBFS");
            Assert.InRange(peak, gain == 3f ? -30 : -12, 0.5);
        }
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Output_tubes_idle_inside_their_ratings(int type) {
        var channel = new AmpChannel(AmpDesign.All[type], 96_000);
        var power = channel.Power;
        var design = power.Design;
        var (plate, _) = power.PlateCurrents;
        var (screen, _) = power.ScreenCurrents;
        var dissipation = plate * (power.Supply - power.CathodeVoltage);
        var rating = design.Tube == Wdf.PentodeModel.EL84 ? 12
            : design.Tube == Wdf.PentodeModel.EL34 ? 25
            : design.Tube == Wdf.PentodeModel.SixV6GT ? 14
            : 30;
        output.WriteLine(
            $"{design.Name}: B+ {power.Supply:0.0} V, screen node {power.ScreenSupply:0.0} V, preamp node " +
            $"{power.PreampVoltage:0.0} V, grid bias {power.GridBias:0.00} V, cathode {power.CathodeVoltage:0.00} V, " +
            $"{plate * 1e3:0.0} mA plate + {screen * 1e3:0.00} mA screen per tube, {dissipation:0.0} W of {rating} W"
        );
        Assert.InRange(dissipation, 0.4 * rating, 0.9 * rating);
        Assert.InRange(power.Supply, 0.97 * design.Supply, 1.03 * design.Supply);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Newton_converges_under_full_scale_drive_at_full_gain(int type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Bias, 100f), (CircuitAmpBlock.Sag, 10f));
        var square = Signals.Sine(110, Rate, 48_000).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var rendered = BlockRender.Mono(block, Signals.Concat(square, Signals.Noise(48_000, 4)), 64);
        var channel = block.Channel;
        var stages = channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!)
            .Append(channel.Power.Newton);
        foreach (var newton in stages) {
            output.WriteLine(
                $"{newton.MeanIterations:0.00} iterations per sample (most {newton.MostIterations}), unconverged " +
                $"{newton.Unconverged}, non-finite {newton.NonFinite}"
            );
            Assert.Equal(0, newton.NonFinite);
            Assert.True(newton.Unconverged <= newton.Samples / 10_000, $"{newton.Unconverged} unconverged samples");
        }

        Assert.True(Analysis.AllFinite(rendered));
        Assert.True(Analysis.ToDb(Analysis.Peak(rendered)) < 6, $"{Analysis.ToDb(Analysis.Peak(rendered)):0.0} dBFS");
    }

    [Fact]
    public void Negative_feedback_lowers_the_gain_and_presence_lifts_the_treble() {
        // The Plexi below clipping: master low, a quiet 1 kHz and 6 kHz.
        double Level(float feedback, float presence, double frequency) {
            using var block = Create(
                1,
                (CircuitAmpBlock.Gain, 2f),
                (CircuitAmpBlock.Master, 2f),
                (CircuitAmpBlock.Feedback, feedback),
                (CircuitAmpBlock.Presence, presence)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, 0.01f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        var open = Level(0, 5, 1_000);
        var stock = Level(5, 5, 1_000);
        var doubled = Level(10, 5, 1_000);
        output.WriteLine($"1 kHz: no feedback {open:0.0} dB, stock {stock:0.0} dB, twice {doubled:0.0} dB");
        Assert.InRange(open - stock, 3, 12);
        Assert.True(stock - doubled > 1.5);

        var tilt0 = Level(5, 0, 6_000) - Level(5, 0, 1_000);
        var tilt10 = Level(5, 10, 6_000) - Level(5, 10, 1_000);
        output.WriteLine($"6 kHz over 1 kHz: presence 0 {tilt0:0.0} dB, presence 10 {tilt10:0.0} dB");
        Assert.True(tilt10 - tilt0 > 3, $"presence adds {tilt10 - tilt0:0.0} dB");
    }

    [Fact]
    public void Sag_and_bias_move_the_supply_and_the_idle_current() {
        double Idle(float bias) {
            using var block = Create(1, (CircuitAmpBlock.Bias, bias));
            return block.Channel.Power.PlateCurrents.A;
        }

        var (cold, stock, hot) = (Idle(0), Idle(50), Idle(100));
        output.WriteLine($"Plexi idle: cold {cold * 1e3:0.0} mA, stock {stock * 1e3:0.0} mA, hot {hot * 1e3:0.0} mA");
        Assert.True(cold < 0.5 * stock && hot > 1.5 * stock);

        double Sagged(float sag) {
            using var block = Create(2, (CircuitAmpBlock.Sag, sag), (CircuitAmpBlock.Gain, 8f));
            BlockRender.Mono(block, Chord(24_000, 0.5f), 64);
            return block.Channel.Power.Supply;
        }

        var (stiff, soft) = (Sagged(0), Sagged(10));
        output.WriteLine($"AC30 B+ after a loud chord: sag 0 {stiff:0.0} V, sag 10 {soft:0.0} V");
        Assert.True(stiff - soft > 10, $"{stiff - soft:0.0} V");
    }

    [Fact]
    public void Tone_stack_block_is_about_unity_at_1_khz_with_the_knobs_at_noon() {
        for (var type = 0; type < ToneStackDesign.All.Count; type++) {
            using var block = new ToneStackBlock();
            block.Params.SetTarget(ToneStackBlock.Type, type);
            block.Prepare(new(Rate, 64));
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 0.5f), 64);
            var gain = Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)) / (0.5 / Math.Sqrt(2)));
            output.WriteLine($"{ToneStackDesign.All[type].Name}: {gain:0.00} dB at 1 kHz");
            Assert.InRange(gain, -0.5, 0.5);
        }
    }

    static CircuitAmpBlock Create(int type, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, type);
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    /// <summary>
    ///     A strummed E major chord (decaying harmonic tones), restruck every half second, peaking at
    ///     <paramref name="peak" />.
    /// </summary>
    internal static float[] Chord(int length, float peak) {
        double[] notes = [82.4, 123.5, 164.8, 207.7, 246.9, 329.6];
        var y = new double[length];
        var max = 0.0;
        for (var n = 0; n < length; n++) {
            var t = n % 24_000 / Rate;
            var s = 0.0;
            foreach (var f in notes) {
                for (var h = 1; h <= 6; h++) {
                    s += Math.Sin(2 * Math.PI * f * h * t) / (h * h) * Math.Exp(-t * (2 + h));
                }
            }

            y[n] = s;
            max = Math.Max(max, Math.Abs(s));
        }

        return [..y.Select(v => (float)(v / max * peak))];
    }
}
