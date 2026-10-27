using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream C (Fender brownface, the Bassman 135 and the Gibson Scout) on the Circuit Amp block: panels, knob sweeps at
///     full drive (finite, bounded, Newton converging), every control audible, volume and master monotonic; the harmonic
///     vibrato (its rate from the oscillator's ladder, its depth from the Intensity knob, the two bands alternating, the
///     pedal stopping it); the Bassman 135's gain staging against the AC test points on CBS's drawing; the 6EU7 and 6AQ5
///     fits against their datasheets and the Scout's operating points against Gibson's drawing.
/// </summary>
public class BrownfaceAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Models = [
        "Concert 6G12", "Super 6G4", "Deluxe 6G3", "Vibrolux 6G11", "Bassman 135", "Scout GA-17RVT"
    ];

    public static TheoryData<string> Types => [..Models];

    [Fact]
    public void Panels_name_the_real_controls_and_hide_the_rest() {
        Assert.Equal(["Vibrato Ch", "Vibrato", string.Empty], Switches("Concert 6G12"));
        // The harmonic vibrato is the amp's one tremolo: on the tremolo controls, with no generic bias tremolo beside it.
        Assert.Equal("Speed", Label("tremrate", "Super 6G4"));
        Assert.Equal("Intensity", Label("tremdepth", "Concert 6G12"));
        Assert.Empty(Label("control1", "Super 6G4"));
        Assert.Empty(Label("control2", "Concert 6G12"));
        Assert.True(new AmpChannel(Concert6G12.Design, 96_000).HasTremolo);
        Assert.True(new AmpChannel(Super6G4.Design, 96_000).HasTremolo);
        Assert.Equal("Presence", Label("presence", "Concert 6G12"));
        Assert.Equal("Tone", Label("treble", "Deluxe 6G3"));
        Assert.Empty(Label("bass", "Deluxe 6G3"));
        Assert.Equal(["Bright Ch", string.Empty, string.Empty], Switches("Vibrolux 6G11"));
        Assert.Equal(["Normal Ch", "Deep", string.Empty], Switches("Bassman 135"));
        Assert.Equal("Master", Label("master", "Bassman 135"));
        Assert.Equal(
            ["gain"],
            new[] { "gain", "bass", "middle", "treble", "master", "presence", "bright" }
                .Where(static id => !string.IsNullOrEmpty(Label(id, "Scout GA-17RVT")))
        );
        foreach (var type in Models) {
            Assert.Empty(Label("cut", type));
            Assert.Empty(Label("depth", type));
        }
    }

    [Fact]
    public void The_brownface_vibrolux_is_a_one_by_twelve_with_its_feedback_from_the_8_ohm_winding() {
        // One 8 Ω speaker (the 2 × 10 is the blackface Vibrolux Reverb): the feedback tap is the 8 Ω winding itself.
        var power = Vibrolux6G11.Design.Power;
        Assert.Same(SpeakerDesign.OpenBack1x12, power.Speaker);
        Assert.Equal(1, power.Feedback.TapGain);
    }

    /// <summary>Every knob swept end to end and every switch flipped while a loud chord plays: finite, bounded, converging.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Knob_sweeps_at_full_drive_stay_finite_and_converge(string type) {
        using var block = Create(type, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.Master, 10f));
        var chord = Signals.Sine(82.4, Rate, 9_600, 0.8f)
            .Zip(Signals.Sine(123.5, Rate, 9_600, 0.8f), static (a, b) => a + b)
            .ToArray();
        var peak = 0.0;
        foreach (var control in Controls(type)) {
            float[] values = IsSwitch(control) ? [1f, 0f, 1f] : [0f, 10f, 5f];
            foreach (var value in values) {
                block.Params.SetTarget(control, value);
                var rendered = BlockRender.Mono(block, chord, 64);
                Assert.True(Analysis.AllFinite(rendered), $"{type}: control {control} at {value}");
                peak = Math.Max(peak, Analysis.Peak(rendered));
            }
        }

        var channel = block.Channel;
        var solvers = channel.Stages
            .SelectMany(static s => s is ChannelSwitchStage c ? c.Active : [s])
            .SelectMany(static s => s is HarmonicVibratoStage v ? new[] { v.Newton, v.TrebleNewton } : [s.Newton])
            .OfType<NewtonSolver>()
            .Append(channel.Power.Newton);
        foreach (var newton in solvers) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{type}: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }

        output.WriteLine($"{type}: peak {Analysis.ToDb(peak):0.0} dBFS over every sweep");
        Assert.True(Analysis.ToDb(peak) < 6);
    }

    /// <summary>
    ///     Every control on the panel changes the sound: a knob from 0 to 10 or a switch on moves the level or the spectrum
    ///     of a two-tone signal somewhere, or (the vibrato's knobs) the depth or rate of its modulation.
    /// </summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Every_control_is_audible(string type) {
        var tones = Signals.Sine(110, Rate, 48_000, 0.02f)
            .Zip(Signals.Sine(2_500, Rate, 48_000, 0.02f), static (a, b) => a + b)
            .ToArray();

        float[] Render(int control, float value) {
            (int, float)[] settings = [
                (CircuitAmpBlock.Gain, 4f),
                (CircuitAmpBlock.Master, 4f),
                (CircuitAmpBlock.Presence, 5f),
                ..ChannelFor(type, control),
                (control, value)
            ];
            using var block = Create(type, settings);
            return BlockRender.Mono(block, tones, 64).AsSpan(12_000).ToArray();
        }

        foreach (var control in Controls(type)) {
            var (low, high) = IsSwitch(control) ? (0f, 1f) : (0f, 10f);
            var (a, b) = (Render(control, low), Render(control, high));
            var change = Math.Max(
                Math.Abs(Analysis.ToDb(Analysis.MagnitudeAt(a, 110, Rate) / Analysis.MagnitudeAt(b, 110, Rate))),
                Math.Abs(Analysis.ToDb(Analysis.MagnitudeAt(a, 2_500, Rate) / Analysis.MagnitudeAt(b, 2_500, Rate)))
            );
            var difference = Analysis.NullResidualDb(a, b) - Analysis.ToDb(Analysis.Rms(a));
            output.WriteLine($"{type} control {control}: {change:0.00} dB at a tone, {difference:0.0} dB apart");
            Assert.True(
                change > 0.2 || difference > -20,
                $"{type}: control {control} moves the sound by only {change:0.00} dB"
            );
        }
    }

    /// <summary>Below clipping, the volume and master knobs raise the level monotonically, as their pots do.</summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void Volume_and_master_raise_the_level_monotonically(string type) {
        double Level(int knob, float value, bool second) {
            var gain = knob == CircuitAmpBlock.Gain ? value : 3f;
            var master = knob == CircuitAmpBlock.Master ? value : 3f;
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, gain),
                (CircuitAmpBlock.Master, master),
                (CircuitAmpBlock.Switch1, second ? 1f : 0f)
            );
            var rendered = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, 1e-4f), 64);
            return Analysis.ToDb(Analysis.Rms(rendered.AsSpan(12_000)));
        }

        foreach (var second in Controls(type).Contains(CircuitAmpBlock.Switch1) ? new[] { false, true } : [false]) {
            foreach (var knob in new[] { CircuitAmpBlock.Gain, CircuitAmpBlock.Master }.Where(k =>
                         Controls(type).Contains(k)
                     )) {
                var levels = new[] { 0f, 2.5f, 5f, 7.5f, 10f }.Select(v => Level(knob, v, second)).ToArray();
                var text = string.Join(" ", levels.Select(static l => $"{l:0.0}"));
                output.WriteLine($"{type} channel {(second ? 2 : 1)} knob {knob}: {text} dB");
                for (var i = 1; i < levels.Length; i++) {
                    if (levels[i - 1] > -100) {
                        Assert.True(levels[i] > levels[i - 1] - 0.1, $"{type}: knob {knob} not monotonic at step {i}");
                    }
                }

                Assert.True(
                    levels[^1] - levels[0] > 10,
                    $"{type}: knob {knob} spans only {levels[^1] - levels[0]:0.0} dB"
                );
            }
        }
    }

    /// <summary>
    ///     The oscillator's rate comes from its phase-shift ladder: 2.6 Hz at the slowest to 9.4 Hz at the fastest, rising
    ///     with the Speed knob; the Intensity knob's depth rises from nothing, and falls a little at faster rates (the
    ///     network's caps).
    /// </summary>
    [Fact]
    public void The_vibrato_rate_follows_the_ladder_and_its_depth_the_intensity() {
        var lfo = new VibratoOscillatorDesign();
        var rates = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }.Select(lfo.Rate).ToArray();
        output.WriteLine(
            $"rate {string.Join(", ", rates.Select(static r => $"{r:0.00}"))} Hz at speed 0, 2.5, 5, 7.5, 10"
        );
        Assert.InRange(rates[0], 2.4, 2.8);
        Assert.InRange(rates[^1], 9.0, 9.8);
        for (var i = 1; i < rates.Length; i++) {
            Assert.True(rates[i] > rates[i - 1]);
        }

        var depths = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }.Select(i => lfo.Depth(rates[2], i)).ToArray();
        output.WriteLine(
            $"depth {string.Join(", ", depths.Select(static d => $"{d:0.00}"))} V at intensity 0, 2.5, 5, 7.5, 10"
        );
        Assert.Equal(0, depths[0]);
        for (var i = 1; i < depths.Length; i++) {
            Assert.True(depths[i] > depths[i - 1]);
        }

        Assert.True(lfo.Depth(rates[^1], 1) < lfo.Depth(rates[0], 1));
    }

    /// <summary>
    ///     On the Concert's Vibrato channel the bass and treble swell in turn at the oscillator's rate: a low and a high tone
    ///     are modulated, in antiphase, by several decibels at full intensity; with the pedal off neither moves (Speed and
    ///     Intensity are the tremolo controls, so no bias tremolo runs beside the vibrato).
    /// </summary>
    [Theory]
    [InlineData("Concert 6G12")]
    [InlineData("Super 6G4")]
    public void The_harmonic_vibrato_swells_the_bass_and_treble_in_turn(string type) {
        const int Length = 96_000;
        var low = Signals.Sine(100, Rate, Length, 0.01f);
        var high = Signals.Sine(3_000, Rate, Length, 0.01f);
        var both = low.Zip(high, static (a, b) => a + b).ToArray();

        (double[] Low, double[] High) Envelopes(bool pedal) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 3f),
                (CircuitAmpBlock.Switch1, 1f),
                (CircuitAmpBlock.Switch2, pedal ? 1f : 0f),
                (CircuitAmpBlock.TremoloRate, 5f),
                (CircuitAmpBlock.TremoloDepth, 100f)
            );
            var rendered = BlockRender.Mono(block, both, 64);

            // Each band's level per 20 ms window (one 100 Hz period is 10 ms).
            const int Window = 960;
            var count = (Length - 24_000) / Window;
            var (l, h) = (new double[count], new double[count]);
            for (var w = 0; w < count; w++) {
                var span = rendered.AsSpan(24_000 + w * Window, Window).ToArray();
                l[w] = Analysis.ToDb(Analysis.MagnitudeAt(span, 100, Rate));
                h[w] = Analysis.ToDb(Analysis.MagnitudeAt(span, 3_000, Rate));
            }

            return (l, h);
        }

        static double Swing(double[] x) => x.Max() - x.Min();

        static double Correlation(double[] a, double[] b) {
            var (ma, mb) = (a.Average(), b.Average());
            var cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
            return cov / Math.Sqrt(a.Sum(x => (x - ma) * (x - ma)) * b.Sum(y => (y - mb) * (y - mb)));
        }

        var (onLow, onHigh) = Envelopes(true);
        var (offLow, offHigh) = Envelopes(false);
        var correlation = Correlation(onLow, onHigh);
        output.WriteLine(
            $"{type}: pedal on, 100 Hz swings {Swing(onLow):0.0} dB and 3 kHz {Swing(onHigh):0.0} dB, correlation "
            + $"{correlation:0.00}; pedal off, {Swing(offLow):0.00} dB and {Swing(offHigh):0.00} dB"
        );
        Assert.True(Swing(onLow) > 3, "the bass band hardly moves");
        Assert.True(Swing(onHigh) > 3, "the treble band hardly moves");
        Assert.True(correlation < -0.3, "the bands do not alternate");
        Assert.True(Swing(offLow) < 0.3 && Swing(offHigh) < 0.3, "the pedal does not stop the vibrato");
    }

    /// <summary>
    ///     CBS's Bassman 135 drawing lists the AC signal at test points for a 100 Hz sine at the input with every control at
    ///     maximum (6 mV in; 10 W out): V1B's grid 80 mV, its plate 4 V, the mix 1.8 V. Here the Bass channel's stages are
    ///     run alone and the signal read at the matching nodes.
    /// </summary>
    [Fact]
    public void The_bassman_follows_the_test_points_on_the_drawing() {
        const double rate = 96_000;
        var controls = new AmpControls(1, 1, 1, 1, 1, false);
        var chain = ((ChannelSwitchStageDesign)Bassman135.Design.Preamp[0]).Off;
        var stages = chain.Select(s => (NetworkStage<FastTriode>)s.Create(rate)).ToArray();
        var rest = 0.0;
        var source = 1e3;
        foreach (var stage in stages) {
            stage.SetSourceResistance(source);
            stage.Update(controls);
            stage.Reset(rest);
            rest = stage.RestOutput;
            source = stage.OutputResistance;
        }

        // V1B's plate and the mix node in the second stage's netlist (plate first, then the mix, as it is built).
        var second = stages[1];
        var plateNode = second.Design.Netlist.Elements.First(static e => e.Kind == ElementKind.Triode).A;
        var mixNode = plateNode + 4;
        var n = (int)rate;
        var (grid, plate, mix) = (new List<double>(), new List<double>(), new List<double>());
        for (var i = 0; i < n; i++) {
            var x = 0.006 * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 100 * i / rate);
            var g = stages[0].Tick(x);
            second.Tick(g);
            if (i >= n / 2) {
                grid.Add(g);
                plate.Add(second.Voltage(plateNode));
                mix.Add(second.Voltage(mixNode));
            }
        }

        static double Rms(List<double> x) {
            var mean = x.Average();
            return Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / x.Count);
        }

        (string Name, double Value, double Expected)[] points = [
            ("V1B grid", Rms(grid), 0.080), ("V1B plate", Rms(plate), 4.0), ("mix", Rms(mix), 1.8)
        ];
        foreach (var (name, value, expected) in points) {
            var error = Analysis.ToDb(value / expected);
            output.WriteLine($"{name}: {value * 1e3:0.0} mV (drawing {expected * 1e3:0} mV, {error:+0.0;-0.0} dB)");
        }

        Assert.All(points, static p => Assert.InRange(Analysis.ToDb(p.Value / p.Expected), -6, 6));
    }

    /// <summary>The 6EU7 and 6AQ5 fits reproduce their datasheets' tabulated characteristics.</summary>
    [Fact]
    public void The_new_tube_fits_follow_their_datasheets() {
        // RCA 6EU7: (plate V, grid V, mA, mS).
        (double Vp, double Vg, double Ip, double Gm)[] triode = [(100, -1, 0.5, 1.25), (250, -2, 1.2, 1.6)];
        var eu7 = new Triode(TriodeModel.SixEU7);
        foreach (var (vp, vg, ip, gm) in triode) {
            var i = OperatingPoint.PlateCurrent(eu7, vg, vp) * 1e3;
            var g = (OperatingPoint.PlateCurrent(eu7, vg + 1e-3, vp) - OperatingPoint.PlateCurrent(eu7, vg - 1e-3, vp))
                / 2e-3
                * 1e3;
            output.WriteLine($"6EU7 {vp} V {vg} V: {i:0.000} mA ({ip}), {g:0.000} mS ({gm})");
            Assert.InRange(i / ip, 0.8, 1.25);
            Assert.InRange(g / gm, 0.85, 1.15);
        }

        // GE 6AQ5: (plate = screen V, grid V, mA, screen mA).
        (double V, double Vg, double Ip, double Ig2)[] pentode = [(180, -8.5, 29, 3.0), (250, -12.5, 45, 4.5)];
        var tables = PentodeTables.For(PentodeModel.SixAQ5);
        foreach (var (v, vg, ip, ig2) in pentode) {
            var i = tables.PlateCurrent(tables.ScreenFactor(v), vg, v, v, out _, out _) * 1e3;
            var s = tables.ScreenCurrent(vg, v) * 1e3;
            output.WriteLine($"6AQ5 {v} V {vg} V: {i:0.0} mA ({ip}), screen {s:0.00} mA ({ig2})");
            Assert.InRange(i / ip, 0.95, 1.05);
            Assert.InRange(s / ig2, 0.95, 1.05);
        }

        // The Scout's 6EU7 stages as drawn: 470k from 257 V and 263 V, 4.7k: 120 V and 130 V on the plates.
        var (_, a, _) = OperatingPoint.CommonCathode(TriodeModel.SixEU7, 0, 257, 470e3, 4.7e3);
        var (_, b, _) = OperatingPoint.CommonCathode(TriodeModel.SixEU7, 0, 263, 470e3, 4.7e3);
        output.WriteLine($"Scout V2A plate {a:0.0} V (drawing 120), V2B {b:0.0} V (130)");
        Assert.InRange(a, 110, 130);
        Assert.InRange(b, 115, 140);
    }

    [Fact]
    public void Switching_to_these_models_and_playing_them_does_not_allocate() {
        using var block = new CircuitAmpBlock();
        block.Prepare(new(Rate, 64));
        var left = new float[64];
        var right = new float[64];
        var chord = CircuitAmpBlockTests.Chord(64 * 40, 0.25f);

        void Play(int blocks) {
            for (var b = 0; b < blocks; b++) {
                chord.AsSpan(b % 40 * 64, 64).CopyTo(left);
                block.Process(new(left, right), new(Rate, 120, b * 64));
            }
        }

        int[] types = [..Models.Select(TypeOf)];

        void Round() {
            foreach (var type in types) {
                block.Params.SetTarget(CircuitAmpBlock.Type, type);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 1);
                block.Params.SetTarget(CircuitAmpBlock.Switch2, 1);
                block.Params.SetTarget(CircuitAmpBlock.TremoloRate, 3);
                block.Params.SetTarget(CircuitAmpBlock.TremoloDepth, 70);
                block.Params.SetTarget(CircuitAmpBlock.Gain, 5);
                Play(8);
                block.Params.SetTarget(CircuitAmpBlock.Gain, 8);
                block.Params.SetTarget(CircuitAmpBlock.TremoloRate, 7);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 0);
                block.Params.SetTarget(CircuitAmpBlock.Switch2, 0);
                Play(8);
            }
        }

        Round(); // the first rounds warm up the runtime (tiered compilation)
        Round();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Round();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{allocated} bytes allocated");
        Assert.Equal(0, allocated);
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

    /// <summary>The knobs and switches a model's panel shows, as block parameter indices.</summary>
    static IEnumerable<int> Controls(string type) {
        (string Id, int Index)[] all = [
            ("gain", CircuitAmpBlock.Gain), ("bass", CircuitAmpBlock.Bass), ("middle", CircuitAmpBlock.Middle),
            ("treble", CircuitAmpBlock.Treble), ("presence", CircuitAmpBlock.Presence),
            ("depth", CircuitAmpBlock.Depth),
            ("cut", CircuitAmpBlock.Cut), ("master", CircuitAmpBlock.Master), ("bright", CircuitAmpBlock.Bright),
            ("switch1", CircuitAmpBlock.Switch1), ("switch2", CircuitAmpBlock.Switch2),
            ("switch3", CircuitAmpBlock.Switch3),
            ("control1", CircuitAmpBlock.Control1), ("control2", CircuitAmpBlock.Control2)
        ];
        return all.Where(c => !string.IsNullOrEmpty(Label(c.Id, type))).Select(static c => c.Index);
    }

    static bool IsSwitch(int index) =>
        index is CircuitAmpBlock.Bright
            or CircuitAmpBlock.Switch1
            or CircuitAmpBlock.Switch2
            or CircuitAmpBlock.Switch3;

    /// <summary>
    ///     The channel a control belongs to: the vibrato's controls play on the brownface Vibrato channel with the pedal
    ///     on, the Bassman 135's Bright on its Normal channel; everything else on the default channel.
    /// </summary>
    static (int Index, float Value)[] ChannelFor(string type, int control) =>
        (type, control) switch {
            ("Concert 6G12" or "Super 6G4", CircuitAmpBlock.Switch2) => [
                (CircuitAmpBlock.Switch1, 1), (CircuitAmpBlock.TremoloDepth, 100)
            ],
            ("Bassman 135", CircuitAmpBlock.Bright) => [(CircuitAmpBlock.Switch1, 1)],
            _ => []
        };

    static IEnumerable<string> Switches(string type) =>
        new[] { "switch1", "switch2", "switch3" }.Select(id => Label(id, type));
}
