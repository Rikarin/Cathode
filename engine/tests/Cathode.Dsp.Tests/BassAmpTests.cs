using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Tests.Circuits;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The bass and solid-state preamps against the reference MNA simulator as whole chains, every stage joined in one
///     circuit with no Thévenin cut: the SVT's three-triode midrange loop with its feedback and resonant circuit, the
///     B-15N's two triodes around its volume network, and the JC-120's op-amp stages (the reference's op-amps are ideal
///     but for a gain of 10⁵, so the comparison stays below clipping).
/// </summary>
public partial class AmpStageTests {
    public static TheoryData<string, double, double> BassChains =>
        new() { { "SVT", 0.001, -30 }, { "Portaflex B-15N", 0.01, -30 }, { "JC-120", 0.02, -40 } };

    [Theory]
    [MemberData(nameof(BassChains))]
    public void Bass_and_solid_state_chains_match_the_reference(string type, double amplitude, double limitDb) {
        var design = AmpDesign.All.Single(a => a.Name == type);
        var designs = design.Preamp.ToArray();
        var controls = new AmpControls(0.5, 0.5, 0.7, 0.5, 1, false);

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
        foreach (var d in designs.SelectMany(s => s is ChannelSwitchStageDesign c
                         ? controls.IsOn(c.Switch) ? c.On : c.Off
                         : [s]
                 )) {
            if (d is NetworkStageDesign network) {
                node = AmpReferences.Append(circuit, network, controls, PreampSettings.Stock, node);
            } else {
                // An ideal buffer.
                var buffered = circuit.Node();
                circuit.Vcvs(buffered, 0, node, 0, 1);
                node = buffered;
            }
        }

        var errorDb = CompareInTime(
            $"{type} {string.Join(" > ", designs.Select(static d => d.Name))}",
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
        Assert.True(errorDb < limitDb, $"{type}: {errorDb:0.0} dB from the reference");
    }

    [Fact]
    public void Driver_chain_and_paraphase_tables_are_balanced() {
        var svt = (DriverChain)PhaseInverter.For(AmpDesign.All.Single(static a => a.Name == "SVT").Power.PhaseInverter);
        var b15 = (FloatingParaphase)PhaseInverter.For(
            AmpDesign.All.Single(static a => a.Name == "Portaflex B-15N").Power.PhaseInverter
        );
        foreach (var (name, inverter, vd) in new (string, PhaseInverter, double)[] {
                     ("SVT", svt, 0.05), ("B-15N", b15, 0.2)
                 }) {
            var (a, b) = inverter.Evaluate(vd, out _, out _);
            var (a2, _) = inverter.Evaluate(-vd, out _, out _);
            output.WriteLine(
                $"{name}: +{vd} V -> {a:0.00} / {b:0.00} V, -{vd} V -> {a2:0.00} V, rest outputs "
                + $"{inverter.RestPlateA:0.0} / {inverter.RestPlateB:0.0} V"
            );
            Assert.True(a < 0 && b > 0, $"{name}: the outputs are in antiphase");
            Assert.InRange(-b / a, 0.9, 1.1);
            Assert.InRange(-a2 / a, 0.9, 1.1);
        }

        // The SVT's followers swing far enough to cut the 6550s off and to drive their grids positive.
        var (high, _) = svt.Evaluate(-svt.Range, out _, out _);
        var (low, _) = svt.Evaluate(svt.Range, out _, out _);
        output.WriteLine($"SVT follower swing {low:0} .. {high:0} V");
        Assert.True(high > 60 && low < -60, $"follower swing {low:0} .. {high:0} V");
    }
}

/// <summary>
///     The bass and solid-state models (Ampeg SVT and Portaflex B-15N, Roland JC-120) on the Circuit Amp block: their
///     panels, knob sweeps without instability, monotonic volume, their switches doing what the manuals say, the JC-120
///     against Roland's signal-voltage annotations, and the solid-state power amp.
/// </summary>
public class BassAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = ["SVT", "Portaflex B-15N", "JC-120"];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal(
            ["Ultra Hi", "Ultra Lo", "Bass Cut"],
            new[] { "switch1", "switch2", "switch3" }.Select(static id => Label(id, "SVT"))
        );
        Assert.Equal("Mid Freq", Label("control1", "SVT"));
        Assert.Equal("Bright Input", Label("bright", "SVT"));
        Assert.Equal(
            ["Ultra Hi", "Ultra Lo"],
            new[] { "switch1", "switch2" }.Select(static id => Label(id, "Portaflex B-15N"))
        );
        Assert.Equal("Hi Treble", Label("control1", "JC-120"));
        Assert.Equal("Distortion", Label("control2", "JC-120"));
        Assert.Equal("Distortion", Label("switch1", "JC-120"));
        Assert.Empty(Label("switch2", "JC-120"));
        foreach (var type in Models) {
            Assert.Equal("Volume", Label("gain", type));
            Assert.Empty(Label("master", type));
            Assert.Empty(Label("presence", type));
            Assert.Empty(Label("cut", type));
        }

        // A solid-state amp has none of the tube parameters, and keeps the ones its rails answer to.
        foreach (var id in new[] { "bias", "feedback", "powertube", "xfmrmatch", "speaker", "rectifier", "pretube" }) {
            Assert.Empty(Label(id, "JC-120"));
            Assert.NotEmpty(Label(id, "SVT"));
        }

        foreach (var id in new[] { "sag", "variac", "btc", "poweramp", "lowcut", "intrim" }) {
            Assert.NotEmpty(Label(id, "JC-120"));
        }
    }

    /// <summary>Every knob swept end to end while a loud chord plays: finite, bounded, and every solver converging.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f));
        int[] knobs = [
            CircuitAmpBlock.Gain, CircuitAmpBlock.Bass, CircuitAmpBlock.Middle, CircuitAmpBlock.Treble,
            CircuitAmpBlock.Control1, CircuitAmpBlock.Control2
        ];
        var chord = Signals.Sine(41.2, Rate, 9_600, 0.5f)
            .Zip(Signals.Sine(61.7, Rate, 9_600, 0.5f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var knob in knobs) {
            foreach (var value in new[] { 0f, 10f, 5f }) {
                block.Params.SetTarget(knob, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: knob {knob} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        int[] toggles =
            [CircuitAmpBlock.Switch1, CircuitAmpBlock.Switch2, CircuitAmpBlock.Switch3, CircuitAmpBlock.Bright];
        foreach (var toggle in toggles) {
            block.Params.SetTarget(toggle, 1f);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switch {toggle}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        // With every switch on: a knob that only acts behind one (the JC-120's Distortion).
        foreach (var value in new[] { 0f, 10f, 5f }) {
            block.Params.SetTarget(CircuitAmpBlock.Control2, value);
            var rendered = BlockRender.Mono(block, chord, 64);
            Assert.True(Analysis.AllFinite(rendered), $"{type}: switched knob at {value}");
            peak = Math.Max(peak, Analysis.Peak(rendered));
        }

        var channel = block.Channel;
        foreach (var newton in channel.Stages.Where(static s => s.Newton is not null).Select(static s => s.Newton!)) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} of {newton.Samples}"
            );
        }

        if (channel.SolidState is { } solid) {
            output.WriteLine(
                $"{type}: power amp {solid.MeanIterations:0.00} iterations per sample (most {solid.MostIterations})"
            );
            Assert.Equal(0, solid.NonFinite);
            Assert.True(solid.Unconverged <= solid.Samples / 10_000, $"{type}: {solid.Unconverged} of {solid.Samples}");
        } else {
            Assert.Equal(0, channel.Power.Newton.NonFinite);
            Assert.True(channel.Power.Newton.Unconverged <= channel.Power.Newton.Samples / 10_000);
        }

        output.WriteLine($"{type}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>Below clipping, the volume raises the level monotonically.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Volume_raises_the_level_monotonically(string type) {
        var levels = new[] { 0.5f, 2.5f, 5f, 7.5f, 10f }
            .Select(v => Level(type, 1_000, 1e-4f, (CircuitAmpBlock.Gain, v)))
            .ToArray();
        output.WriteLine($"{type} volume: {string.Join(" ", levels.Select(static l => $"{l:0.0}"))} dB");
        for (var i = 1; i < levels.Length; i++) {
            Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: not monotonic at step {i}");
        }

        Assert.True(levels[^1] - levels[0] > 10, $"{type}: spans only {levels[^1] - levels[0]:0.0} dB");
    }

    /// <summary>
    ///     Ampeg's manual: Ultra Lo about +2 dB at 40 Hz and −10 dB at 500 Hz, Ultra Hi +9 dB at 8 kHz; Bass Cut a
    ///     high-pass. Read with the volume at 3 (the Ultra Hi cap's lift depends on it). The model's twin-T (150k / 2 nF
    ///     / 68k, the 820k shorted) notches deeper than the manual's 10 dB, about 18 dB, and its 40 Hz falls 2 dB where
    ///     the manual has it rise: the switch's wiring is the redraw's least certain part (see <see cref="Amp.Models.Svt" />).
    /// </summary>
    [Fact]
    public void Svt_ultra_switches_shape_as_the_manual_says() {
        static double At(double frequency, params (int Index, float Value)[] settings) =>
            Level("SVT", frequency, 1e-3f, [(CircuitAmpBlock.Gain, 3f), ..settings]);

        var notch = At(500, (CircuitAmpBlock.Switch2, 1f)) - At(500);
        var lows = At(40, (CircuitAmpBlock.Switch2, 1f)) - At(40);
        var hi = At(8_000, (CircuitAmpBlock.Switch1, 1f)) - At(8_000);
        var cut = At(50, (CircuitAmpBlock.Switch3, 1f)) - At(50);
        output.WriteLine(
            $"Ultra Lo: {notch:0.0} dB at 500 Hz, {lows:0.0} dB at 40 Hz; Ultra Hi {hi:0.0} dB at 8 kHz; Bass Cut {cut:0.0} dB at 50 Hz"
        );
        Assert.InRange(notch, -20, -8);
        Assert.InRange(lows - notch, 8, 20);
        Assert.InRange(hi, 4, 14);
        Assert.True(cut < -6, $"Bass Cut {cut:0.0} dB");
    }

    /// <summary>The Mid-Range pot boosts and cuts around the Mid Freq selector's frequency, and little elsewhere.</summary>
    [Fact]
    public void Svt_midrange_boosts_and_cuts_at_the_selected_frequency() {
        foreach (var (position, frequency) in new[] { (0f, 220.0), (5f, 800.0), (10f, 3_000.0) }) {
            double At(double hz, float middle) =>
                Level(
                    "SVT",
                    hz,
                    1e-3f,
                    (CircuitAmpBlock.Gain, 3f),
                    (CircuitAmpBlock.Control1, position),
                    (CircuitAmpBlock.Middle, middle)
                );

            var (boost, cut) = (At(frequency, 10) - At(frequency, 5), At(frequency, 0) - At(frequency, 5));
            var far = frequency < 1_000 ? 8_000.0 : 60.0;
            var away = At(far, 10) - At(far, 0);
            output.WriteLine($"{frequency} Hz: boost {boost:0.0} dB, cut {cut:0.0} dB; {far} Hz moves {away:0.0} dB");
            Assert.True(boost > 4, $"{frequency} Hz boost {boost:0.0} dB");
            Assert.True(cut < -6, $"{frequency} Hz cut {cut:0.0} dB");
            Assert.InRange(Math.Abs(away), 0, 3);
        }
    }

    [Fact]
    public void Portaflex_ultra_switches_lift_the_lows_and_highs() {
        static double At(double frequency, params (int Index, float Value)[] settings) =>
            Level("Portaflex B-15N", frequency, 1e-3f, [(CircuitAmpBlock.Gain, 3f), ..settings]);

        var low = At(60, (CircuitAmpBlock.Switch2, 1f))
            - At(60)
            - (At(1_000, (CircuitAmpBlock.Switch2, 1f)) - At(1_000));
        var high = At(8_000, (CircuitAmpBlock.Switch1, 1f)) - At(8_000);
        output.WriteLine($"Ultra Lo: lows {low:0.0} dB over 1 kHz; Ultra Hi {high:0.0} dB at 8 kHz");
        Assert.InRange(low, 3, 15);
        Assert.InRange(high, 3, 15);
    }

    /// <summary>
    ///     Roland's annotations on the circuit diagram (1 kHz, every tone control and the volume full up): 21 mV into the
    ///     High input, 72 mV after IC2b, 130 mV after IC2a, 710 mV after IC3a, 290 mV after IC3b, 800 mV into the power
    ///     amp, 22 V at the speaker. The stage gains reproduce them, all but the tone stack's: the drawn Fender-type stack
    ///     (its 10k middle pot against the 100k slope resistor) loses about 15 dB at 1 kHz with everything up, where the
    ///     annotations imply 7 dB; so the stages after it are checked by their own gains.
    /// </summary>
    [Fact]
    public void Jc120_stage_gains_match_the_factory_signal_voltages() {
        var channel = new AmpChannel(AmpDesign.All[TypeOf("JC-120")], 96_000);
        channel.Configure(new(1, 1, 1, 1, 1, false, 1), new(0.5, 0, 0, 0.5, 0.5, 0.5));
        channel.Reset();
        const int Samples = 96_000;
        const int Settled = Samples / 2; // the second half, measured
        var squares = new double[channel.Stages.Count + 1];
        for (var n = 0; n < Samples; n++) {
            var x = 0.021 * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 1_000 * n / 96_000.0);
            for (var k = 0; k <= channel.Stages.Count; k++) {
                x = k < channel.Stages.Count ? channel.Stages[k].Tick(x) : channel.SolidState!.Tick(x);
                if (n >= Settled) {
                    squares[k] += x * x;
                }
            }
        }

        var rms = squares.Select(static s => Math.Sqrt(s / Settled)).ToArray();
        output.WriteLine(
            $"IC2b {rms[0] * 1e3:0} mV, IC2a {rms[1] * 1e3:0} mV, stack {rms[2] * 1e3:0.0} mV, IC3a {rms[3] * 1e3:0} mV, "
            + $"IC3b {rms[5] * 1e3:0} mV, IC1a {rms[6] * 1e3:0} mV, speaker {rms[7]:0.00} V"
        );
        Assert.InRange(Db(rms[0] / 0.072), -1, 1);
        Assert.InRange(Db(rms[1] / 0.130), -1, 1);
        Assert.InRange(Db(rms[3] / rms[2] / 13.3), -0.5, 0.5); // IC3a: 1 + 270k / 22k
        Assert.InRange(Db(rms[5] / rms[3] / (0.290 / 0.710)), -1, 1);
        Assert.InRange(Db(rms[6] / rms[5] / (0.800 / 0.290)), -1, 1);
        Assert.InRange(Db(rms[7] / rms[6] / (22 / 0.800)), -1, 1);

        static double Db(double ratio) => 20 * Math.Log10(ratio);
    }

    /// <summary>
    ///     The JC-120's distortion, read after the tone stage (IC3a's input), a 108 Hz sine into the High input: off, the
    ///     Distortion knob does nothing; on, it raises IC2a's gain (from 1.9× to 48×), and the diodes after the two 3.9k
    ///     clip: the output's peak barely follows the drive and its third harmonic rises, where the same IC2a output with
    ///     the distortion off (more input instead) stays clean.
    /// </summary>
    [Fact]
    public void Jc120_distortion_knob_adds_gain_and_its_diodes_clip_only_when_on() {
        var off0 = ThroughStack(0.005, false, 0);
        var off10 = ThroughStack(0.005, false, 1);
        Assert.Equal(off0.Fundamental, off10.Fundamental);

        // Below the diodes' knee: the knob is IC2a's gain.
        var on0 = ThroughStack(0.005, true, 0);
        var on10 = ThroughStack(0.005, true, 1);
        var knob = Db(on10.Fundamental / on0.Fundamental);
        output.WriteLine(
            $"5 mV in: on at 0 {Db(on0.Fundamental / off0.Fundamental):0.00} dB from off, at 10 +{knob:0.0} dB"
        );
        Assert.InRange(Db(on0.Fundamental / off0.Fundamental), -0.5, 0.5);
        Assert.True(knob > 15, $"Distortion at 10 adds {knob:0.0} dB");

        // 50 mV in: IC2a's output 8.6 V peak at 10, below its rails, clipped by the diodes.
        var clipped = ThroughStack(0.05, true, 1);
        var moderate = ThroughStack(0.05, true, 0.7);
        var clean = ThroughStack(0.05 * 48 / 1.92, false, 0);
        output.WriteLine(
            $"On at 10: H3 {Db(clipped.Third / clipped.Fundamental):0.0} dB, peak {clipped.Peak:0.000} V; at 7: H3 "
            + $"{Db(moderate.Third / moderate.Fundamental):0.0} dB, peak {moderate.Peak:0.000} V; off, same IC2a output: "
            + $"H3 {Db(clean.Third / clean.Fundamental):0.0} dB, peak {clean.Peak:0.000} V"
        );
        Assert.True(Db(clipped.Third / clipped.Fundamental) > -20, "the diodes clip");
        Assert.True(Db(clean.Third / clean.Fundamental) < -40, "no clipping with the distortion off");
        Assert.True(
            Db(clipped.Third / clipped.Fundamental) > Db(moderate.Third / moderate.Fundamental),
            "more distortion, more third harmonic"
        );
        Assert.True(Db(clipped.Peak / moderate.Peak) < 6, "the diodes bound the peak"); // 48× over 3.9×: 22 dB linear
        Assert.True(clean.Peak > 3 * clipped.Peak, "unclipped, the same drive goes far higher");

        static double Db(double ratio) => 20 * Math.Log10(ratio);
    }

    /// <summary>
    ///     A sine of <paramref name="amplitude" /> volts on bin 37 of 16384 (108 Hz at 48 kHz) through the JC-120's
    ///     stages to the tone stage's output, volume and tone controls at 5: its fundamental, third harmonic and peak.
    /// </summary>
    static (double Fundamental, double Third, double Peak) ThroughStack(double amplitude, bool on, double distortion) {
        const int N = 16_384;
        const int Bin = 37;
        var channel = new AmpChannel(AmpDesign.All[TypeOf("JC-120")], Rate);
        channel.Configure(
            new(0.5, 0.5, 0.5, 0.5, 1, false, 1, distortion, on),
            new(0.5, 0, 0, 0.5, 0.5, 0.5)
        );
        channel.Reset();
        var y = new double[N];
        for (var n = 0; n < 2 * N; n++) {
            var x = amplitude * Math.Sin(2 * Math.PI * Bin * n / N);
            for (var k = 0; k < 3; k++) {
                x = channel.Stages[k].Tick(x);
            }

            if (n >= N) {
                y[n - N] = x;
            }
        }

        return (Amplitude(Bin), Amplitude(3 * Bin), y.Max(Math.Abs));

        double Amplitude(int bin) {
            var (re, im) = (0.0, 0.0);
            for (var n = 0; n < N; n++) {
                re += y[n] * Math.Cos(2 * Math.PI * bin * n / N);
                im += y[n] * Math.Sin(2 * Math.PI * bin * n / N);
            }

            return 2 * Math.Sqrt(re * re + im * im) / N;
        }
    }

    /// <summary>
    ///     The solid-state power amp: its output impedance is so low that the speaker settings change nothing; its rails
    ///     sag under a loud note (more with Sag up), and the output clips just under them.
    /// </summary>
    [Fact]
    public void Solid_state_power_amp_holds_the_load_and_sags_on_its_rails() {
        var stock = Level("JC-120", 100, 0.05f, (CircuitAmpBlock.Gain, 5f));
        var speaker = Level(
            "JC-120",
            100,
            0.05f,
            (CircuitAmpBlock.Gain, 5f),
            (CircuitAmpBlock.SpeakerCurve, 5f),
            (CircuitAmpBlock.SpeakerImpedance, 50f)
        );
        Assert.Equal(stock, speaker, 6);

        static double Rail(float sag) {
            using var block = Create("JC-120", (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Sag, sag));
            BlockRender.Mono(block, Signals.Sine(110, Rate, 24_000, 0.5f), 64);
            return block.Channel.SolidState!.Rails.Positive;
        }

        var (stiff, soft) = (Rail(0), Rail(10));
        output.WriteLine($"+rail after a loud note: sag 0 {stiff:0.0} V, sag 10 {soft:0.0} V");
        Assert.True(stiff < 40 && soft < stiff - 2, $"{stiff:0.0} / {soft:0.0} V");

        using var loud = Create("JC-120", (CircuitAmpBlock.Gain, 10f));
        var rendered = BlockRender.Mono(loud, Signals.Sine(110, Rate, 24_000), 64);
        var peak = Analysis.ToDb(Analysis.Peak(rendered.AsSpan(12_000)));
        output.WriteLine($"Full drive: peak {peak:0.0} dBFS");
        Assert.InRange(peak, -6, 0.5);
    }

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
        return parameter.TypeLabels is { } labels ? labels[TypeOf(type)] : parameter.Name;
    }

    /// <summary>The block's output level (dB) for a sine at <paramref name="frequency" />.</summary>
    static double Level(string type, double frequency, float amplitude, params (int Index, float Value)[] settings) {
        using var block = Create(type, settings);
        var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 24_000, amplitude), 64);
        return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
    }
}
