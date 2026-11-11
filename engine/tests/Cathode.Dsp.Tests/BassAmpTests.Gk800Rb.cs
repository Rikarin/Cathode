using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Gallien-Krueger 800RB against its service manual: the 1982 drawings' signal voltages and FET operating
///     point, the operator's manual's bands, filters, boost and ratings, and its panel.
/// </summary>
public partial class BassAmpTests {
    const string Gk = "GK 800RB";

    [Fact]
    public void Gk800Rb_panel_names_the_real_controls() {
        Assert.Equal(
            ["Volume", "Bass", "Hi Mid", "Treble", "Master", "Hi Boost", "Lo Mid", "Boost"],
            new[] { "gain", "bass", "middle", "treble", "master", "bright", "control1", "control2" }
                .Select(static id => Label(id, Gk))
        );
        Assert.Equal(
            ["Contour", "Lo Cut", "-10 dB"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, Gk))
        );
        foreach (var id in new[] { "presence", "depth", "cut", "bias", "powertube", "speaker", "pretube" }) {
            Assert.Empty(Label(id, Gk));
        }

        foreach (var id in new[] { "sag", "variac", "btc", "poweramp" }) {
            Assert.NotEmpty(Label(id, Gk));
        }
    }

    /// <summary>
    ///     The preamp drawing's annotations (2 mV at 200 Hz into the input, every control at 10, the filters out):
    ///     19 mV after U1a, 13 mV at the contour filter's output, 107 mV after U2a, 115 mV after the mid stages,
    ///     150 mV after U3b, 0.67 V into the power amp; and the power amp drawing's 41 V at the speaker jack for
    ///     0.65 V in. The FET rests with its drain at +6.4 V and its source at +1.5 V, 13 V after the supply's 1k.
    /// </summary>
    [Fact]
    public void Gk800Rb_matches_the_factory_signal_voltages_and_fet_operating_point() {
        const double SampleRate = 96_000;
        var channel = new AmpChannel(AmpDesign.All[TypeOf(Gk)], SampleRate);
        channel.Configure(new(1, 1, 1, 1, 1, false, 1, 1), new(0.5, 0, 0, 0.5, 0.5, 0.5));
        channel.Reset();

        var boost = Assert.IsType<NetworkStage<Jfet>>(channel.Stages[4]);
        var fet = boost.Design.Netlist.Elements.Single(static e => e.Kind == ElementKind.Jfet);
        var (drain, source) = (boost.RestVoltage(fet.A), boost.RestVoltage(fet.C));
        var decoupled = drain + 4.7e3 * (source + 15) / 12e3; // the drain's 4.7k carries the source's current
        output.WriteLine($"J113 at rest: drain {drain:0.00} V, source {source:0.00} V, supply node {decoupled:0.0} V");
        Assert.InRange(drain, 5.8, 7.4);
        Assert.InRange(source, 1.3, 1.7);
        Assert.InRange(decoupled, 12.6, 13.9);

        var voicing = Assert.IsType<NetworkStage<OpAmp>>(channel.Stages[1]);
        var contour = voicing.Design.Netlist.Elements.First(static e => e.Kind == ElementKind.Amplifier).C;
        // Half the drawing's 2 mV: the 300 W amp, loaded with 4 Ω here, would clip at the 41 V the drawing has unloaded.
        const double Drive = 0.001;
        const int Samples = 96_000;
        const int Settled = Samples / 2;
        var squares = new double[channel.Stages.Count + 2];
        for (var n = 0; n < Samples; n++) {
            var x = Drive * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 200 * n / SampleRate);
            for (var k = 0; k <= channel.Stages.Count; k++) {
                x = k < channel.Stages.Count ? channel.Stages[k].Tick(x) : channel.SolidState!.Tick(x);
                if (n >= Settled) {
                    squares[k] += x * x;
                }
            }

            if (n >= Settled) {
                squares[^1] += voicing.Voltage(contour) * voicing.Voltage(contour);
            }
        }

        var rms = squares.Select(static s => Math.Sqrt(s / Settled)).ToArray();
        output.WriteLine(
            $"U1a {rms[0] * 1e3:0.0} mV, contour {rms[6] * 1e3:0.0} mV, U2a {rms[1] * 1e3:0} mV, mids "
            + $"{rms[2] * 1e3:0} mV, U3b {rms[3] * 1e3:0} mV, masters {rms[4]:0.000} V, speaker {rms[5]:0.0} V"
        );
        Assert.InRange(Db(rms[0] / Drive / (19.0 / 2)), -1.5, 1.5); // 11 × 2 mV is 22 mV: the figures are rounded
        Assert.InRange(Db(rms[6] / rms[0] / (13.0 / 19)), -1.5, 1.5);
        Assert.InRange(Db(rms[1] / rms[0] / (107.0 / 19)), -1, 1);
        Assert.InRange(Db(rms[2] / rms[1] / (115.0 / 107)), -1, 1);
        Assert.InRange(Db(rms[3] / rms[2] / (150.0 / 115)), -1, 1);
        Assert.InRange(Db(rms[4] / rms[3] / (0.67 / 0.150)), -1.5, 1.5);
        Assert.InRange(Db(rms[5] / rms[4] / (41 / 0.65)), -1, 1);
    }

    static double Db(double ratio) => 20 * Math.Log10(ratio);

    /// <summary>The amplitude of <paramref name="y" />'s component on bin <paramref name="bin" /> of its length.</summary>
    static double Harmonic(double[] y, int bin) {
        var (re, im) = (0.0, 0.0);
        for (var n = 0; n < y.Length; n++) {
            re += y[n] * Math.Cos(2 * Math.PI * bin * n / y.Length);
            im += y[n] * Math.Sin(2 * Math.PI * bin * n / y.Length);
        }

        return 2 * Math.Sqrt(re * re + im * im) / y.Length;
    }

    /// <summary>
    ///     The operator's manual's bands: treble at 4 kHz, high mid at 1 kHz, low mid at 250 Hz, bass at 60 Hz. As
    ///     drawn (and as the factory voltages have it) the two mid controls are within 1 dB of flat at 10 and cut
    ///     below it, deepest at their band; the bass and treble boost and cut.
    /// </summary>
    [Fact]
    public void Gk800Rb_tone_controls_act_on_the_manuals_bands() {
        double[] frequencies = [60, 125, 250, 500, 1_000, 2_000, 4_000, 8_000];

        double[] Sweep(int knob, float value) =>
            [..frequencies.Select(f => Level(Gk, f, 1e-3f, (CircuitAmpBlock.Gain, 3f), (knob, value)))];

        foreach (var (name, knob, centre) in new[] {
                     ("Hi Mid", CircuitAmpBlock.Middle, 1_000.0), ("Lo Mid", CircuitAmpBlock.Control1, 250.0)
                 }) {
            var (full, noon, off) = (Sweep(knob, 10), Sweep(knob, 5), Sweep(knob, 0));
            var cut = full.Zip(off, static (a, b) => b - a).ToArray();
            var half = full.Zip(noon, static (a, b) => b - a).ToArray();
            output.WriteLine($"{name} 0 against 10: {string.Join(" ", cut.Select(static c => $"{c:0.0}"))} dB");
            output.WriteLine($"{name} 5 against 10: {string.Join(" ", half.Select(static c => $"{c:0.0}"))} dB");
            var deepest = frequencies[Array.IndexOf(cut, cut.Min())];
            Assert.Equal(centre, deepest);
            Assert.InRange(cut.Min(), -20, -12);
            Assert.InRange(half[Array.IndexOf(cut, cut.Min())], -8, -4);
        }

        var bass = Sweep(CircuitAmpBlock.Bass, 10)
            .Zip(Sweep(CircuitAmpBlock.Bass, 0), static (a, b) => a - b)
            .ToArray();
        var treble = Sweep(CircuitAmpBlock.Treble, 10)
            .Zip(Sweep(CircuitAmpBlock.Treble, 0), static (a, b) => a - b)
            .ToArray();
        output.WriteLine($"Bass 10 against 0: {string.Join(" ", bass.Select(static c => $"{c:0.0}"))} dB");
        output.WriteLine($"Treble 10 against 0: {string.Join(" ", treble.Select(static c => $"{c:0.0}"))} dB");
        Assert.True(bass[0] > 10 && bass[0] > bass[2] + 6 && bass[^1] < 1, "the bass control works the bottom end");
        Assert.True(
            treble[^2] > 15 && treble[^2] > treble[4] + 6 && treble[0] < 1,
            "the treble control works at 4 kHz"
        );
    }

    /// <summary>
    ///     The voicing filters and the input attenuator: Mid Contour "puts a notch at about 500 Hz", Lo Cut rolls
    ///     the bass off (0.22 µF against 3.3k: 220 Hz), Hi Boost lifts the top at a low volume setting (22 nF and
    ///     3.3k past the volume pot), and −10 dB takes the input stage from 11× to 2.25×.
    /// </summary>
    [Fact]
    public void Gk800Rb_voicing_filters_do_what_the_manual_says() {
        static double At(double frequency, params (int Index, float Value)[] settings) =>
            Level(Gk, frequency, 1e-3f, [(CircuitAmpBlock.Gain, 3f), ..settings]);

        double[] frequencies = [125, 250, 500, 1_000, 2_000];
        var contour = frequencies.Select(static f => At(f, (CircuitAmpBlock.Switch1, 1f)) - At(f)).ToArray();
        var loCut = At(60, (CircuitAmpBlock.Switch2, 1f)) - At(60);
        var loCutMid = At(2_000, (CircuitAmpBlock.Switch2, 1f)) - At(2_000);
        var hiBoost = At(6_000, (CircuitAmpBlock.Bright, 1f)) - At(6_000);
        var hiBoostLow = At(100, (CircuitAmpBlock.Bright, 1f)) - At(100);
        var pad = At(1_000, (CircuitAmpBlock.Switch3, 1f)) - At(1_000);
        output.WriteLine(
            $"Contour {string.Join(" ", contour.Select(static c => $"{c:0.0}"))} dB at 125 Hz to 2 kHz; Lo Cut "
            + $"{loCut:0.0} dB at 60 Hz, {loCutMid:0.0} dB at 2 kHz; Hi Boost {hiBoost:0.0} dB at 6 kHz, "
            + $"{hiBoostLow:0.0} dB at 100 Hz; attenuator {pad:0.0} dB"
        );
        Assert.Equal(500, frequencies[Array.IndexOf(contour, contour.Min())]);
        Assert.True(contour.Min() < contour[0] - 6 && contour.Min() < contour[^1] - 6, "a notch, not a tilt");
        Assert.InRange(loCut, -14, -6);
        Assert.InRange(loCutMid, -1, 0.1);
        Assert.InRange(hiBoost, 6, 20);
        Assert.InRange(hiBoostLow, -0.5, 1.5);
        Assert.InRange(pad, -14.5, -13); // 20 log (11 / 2.25) = 13.8 dB: "10 dB" on the panel
    }

    /// <summary>
    ///     The Boost knob spans the manual's 15 dB (the 25k pot over 3.3k: 18.7 dB at the FET's gate, less what the
    ///     wiper's loading takes), and the Master fades the 300 W amp to nothing.
    /// </summary>
    [Fact]
    public void Gk800Rb_boost_spans_fifteen_decibels_and_the_master_fades_out() {
        static double At(params (int Index, float Value)[] settings) =>
            Level(Gk, 400, 1e-4f, [(CircuitAmpBlock.Gain, 5f), ..settings]);

        var boost = At((CircuitAmpBlock.Control2, 10f)) - At((CircuitAmpBlock.Control2, 0f));
        var master = At((CircuitAmpBlock.Master, 10f)) - At((CircuitAmpBlock.Master, 1f));
        output.WriteLine($"Boost 0 to 10: {boost:0.0} dB; Master 1 to 10: {master:0.0} dB");
        Assert.InRange(boost, 14, 19.5);
        Assert.InRange(master, 17, 23); // a linear pot: 20 dB from 1 to 10
    }

    /// <summary>
    ///     The boost stage is where the amp's overdrive comes from. At a moderate level the J113's square law puts a
    ///     second harmonic on the note, well above the third, and more of it the further the Boost is turned up; a
    ///     hot signal with the Boost at 10 drives the FET between cut-off and its ohmic region, and it clips.
    /// </summary>
    [Fact]
    public void Gk800Rb_boost_drives_the_fet_into_distortion() {
        var low = BoostStageHarmonics(0.02, 0);
        var high = BoostStageHarmonics(0.02, 1);
        var hot = BoostStageHarmonics(0.1, 1);
        output.WriteLine(
            $"20 mV: Boost 0 H2 {low.Second:0.0} dB, H3 {low.Third:0.0} dB; Boost 10 H2 {high.Second:0.0} dB, H3 "
            + $"{high.Third:0.0} dB. 100 mV, Boost 10: H2 {hot.Second:0.0} dB, H3 {hot.Third:0.0} dB"
        );
        Assert.True(low.Second < -40, $"Boost 0: H2 {low.Second:0.0} dB");
        Assert.True(high.Second > -32, $"Boost 10: H2 {high.Second:0.0} dB");
        Assert.True(high.Second > high.Third + 10, "a square law: the second harmonic leads");
        Assert.True(hot.Third > -20, $"hot, Boost 10: H3 {hot.Third:0.0} dB");
    }

    /// <summary>
    ///     The second and third harmonics (dB below the fundamental) at the masters for <paramref name="amplitude" />
    ///     volts at 108 Hz into the input, the volume at 7 and the tone controls at 10, with the Boost at
    ///     <paramref name="boost" />.
    /// </summary>
    static (double Second, double Third) BoostStageHarmonics(double amplitude, double boost) {
        const int N = 16_384;
        const int Bin = 37;
        var channel = new AmpChannel(AmpDesign.All[TypeOf(Gk)], Rate);
        channel.Configure(new(0.7, 1, 1, 1, 1, false, 1, boost), new(0.5, 0, 0, 0.5, 0.5, 0.5));
        channel.Reset();
        var y = new double[N];
        for (var n = 0; n < 3 * N; n++) {
            var x = amplitude * Math.Sin(2 * Math.PI * Bin * n / N);
            foreach (var stage in channel.Stages) {
                x = stage.Tick(x);
            }

            if (n >= 2 * N) {
                y[n - 2 * N] = x;
            }
        }

        var fundamental = Harmonic(y, Bin);
        return (Db(Harmonic(y, 2 * Bin) / fundamental), Db(Harmonic(y, 3 * Bin) / fundamental));
    }

    /// <summary>
    ///     The 300 W amp alone, a 100 Hz sine into its input: clean at 250 W into its 4 Ω load, clipping by 350 W (the
    ///     manual rates it 300 W into 4 Ω), with the ±85 V rails pulled down by the load.
    /// </summary>
    [Fact]
    public void Gk800Rb_power_amp_clips_at_its_rated_power() {
        var design = AmpDesign.All[TypeOf(Gk)].SolidState!;

        (double Watts, double Third, double Rail) Drive(double watts) {
            const int N = 48_000;
            var amp = new SolidStatePowerAmp(design, Rate);
            var peak = Math.Sqrt(2 * watts * design.Load) / design.Gain;
            var y = new double[N];
            for (var n = 0; n < 2 * N; n++) {
                var v = amp.Tick(peak * Math.Sin(2 * Math.PI * 100 * n / Rate));
                if (n >= N) {
                    y[n - N] = v;
                }
            }

            var (h1, h3) = (Harmonic(y, 100), Harmonic(y, 300)); // a one-second window: the bin is the frequency
            return (h1 * h1 / (2 * design.Load), Db(h3 / h1), amp.Rails.Positive);
        }

        var (below, above) = (Drive(250), Drive(350));
        output.WriteLine(
            $"Asked 250 W: {below.Watts:0} W, H3 {below.Third:0.0} dB, +rail {below.Rail:0.0} V; asked 350 W: "
            + $"{above.Watts:0} W, H3 {above.Third:0.0} dB, +rail {above.Rail:0.0} V"
        );
        Assert.Equal(61.7, design.Gain, 0.1);
        Assert.InRange(below.Watts, 230, 255);
        Assert.True(below.Third < -50, $"250 W: H3 {below.Third:0.0} dB");
        Assert.True(above.Third > -35, $"350 W: H3 {above.Third:0.0} dB");
        Assert.InRange(above.Watts, 280, 345);
        Assert.InRange(below.Rail, 50, 70);
    }
}
