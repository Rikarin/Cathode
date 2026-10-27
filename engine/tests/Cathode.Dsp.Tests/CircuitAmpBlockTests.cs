using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Resampling;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp block as a whole: levels, operating points of the output stages, Newton's health under abuse,
///     and the expert controls doing what they say. The stages themselves are validated in
///     <see cref="AmpStageTests" />.
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
            // The Princeton AA964 has two stages, the second inside the feedback loop: at 5 it is still clean and
            // quiet (13 dB under the Princeton Reverb, which has a third), and its trim is held down by full drive. So
            // is the Gibson Scout's (two stages after an audio-taper volume, no feedback, a class-A pair).
            var quiet = AmpDesign.All[type] == PrincetonAA964.Design
                || AmpDesign.All[type] == GibsonScout.Design;
            var floor = gain == 3f ? -30 : quiet && gain == 5f ? -18 : -12;
            Assert.InRange(peak, floor, 0.5);
        }
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Output_tubes_idle_inside_their_ratings(int type) {
        if (AmpDesign.All[type].SolidState is not null) {
            return; // no output tubes: the solid-state power amp's own tests cover it (BassAmpTests)
        }

        var channel = new AmpChannel(AmpDesign.All[type], 96_000);
        var power = channel.Power;
        var design = power.Design;
        var (plate, _) = power.PlateCurrents;
        var (screen, _) = power.ScreenCurrents;
        var dissipation = plate * (power.Supply - power.CathodeVoltage);
        var rating = design.Tube == PentodeModel.EL84 ? 12
            : design.Tube == PentodeModel.EL34 ? 25
            : design.Tube == PentodeModel.SixV6GT ? 14
            : design.Tube == PentodeModel.SixAQ5 ? 12
            : 30;
        output.WriteLine(
            $"{design.Name}: B+ {power.Supply:0.0} V, screen node {power.ScreenSupply:0.0} V, preamp node "
            + $"{power.PreampVoltage:0.0} V, grid bias {power.GridBias:0.00} V, cathode {power.CathodeVoltage:0.00} V, "
            + $"{plate * 1e3:0.0} mA plate + {screen * 1e3:0.00} mA screen per tube, {dissipation:0.0} W of {rating} W"
        );
        // Vox-style class-A amps idle their EL84s past the rating: the AC30's drawing has 10 V across 50 Î© for four
        // (AmpBooks: about 17 W of plate dissipation each), the DC-30 runs as hot. An amp biased cold by design (the
        // 5150's fixed bias: 11-14 mA a tube) idles well under half its rating.
        // So do the class-A EL34 pairs on a resistor each: the Chieftain's and the Hot Cat's drawings both measure 24 V
        // across 270 Î© (89 mA a tube, about 31 W of plate dissipation at 390 V).
        var hot = design.CathodeBias && design.Tube == PentodeModel.EL84 ? 1.45
            : design.CathodeBias && design.Tube == PentodeModel.EL34 ? 1.5
            : 0.9;
        var coldest = design.IdleCurrent < 0.02 ? 0.15 : 0.4;
        Assert.InRange(dissipation, coldest * rating, hot * rating);
        Assert.InRange(power.Supply, 0.97 * design.Supply, 1.03 * design.Supply);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void Newton_converges_under_full_scale_drive_at_full_gain(int type) {
        using var block = Create(
            type,
            (CircuitAmpBlock.Gain, 10f),
            (CircuitAmpBlock.Bias, 100f),
            (CircuitAmpBlock.Sag, 10f)
        );
        var square = Signals.Sine(110, Rate, 48_000).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var rendered = BlockRender.Mono(block, Signals.Concat(square, Signals.Noise(48_000, 4)), 64);
        var channel = block.Channel;
        var stages = channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!);
        if (channel.SolidState is { } solid) {
            Assert.Equal(0, solid.NonFinite);
            Assert.True(
                solid.Unconverged <= solid.Samples / 10_000,
                $"{solid.Unconverged} unconverged power amp samples"
            );
        } else {
            stages = stages.Append(channel.Power.Newton);
        }

        foreach (var newton in stages) {
            output.WriteLine(
                $"{newton.MeanIterations:0.00} iterations per sample (most {newton.MostIterations}), unconverged "
                + $"{newton.Unconverged}, non-finite {newton.NonFinite}"
            );
            Assert.Equal(0, newton.NonFinite);
            Assert.True(newton.Unconverged <= newton.Samples / 10_000, $"{newton.Unconverged} unconverged samples");
        }

        Assert.True(Analysis.AllFinite(rendered));
        Assert.True(Analysis.ToDb(Analysis.Peak(rendered)) < 6, $"{Analysis.ToDb(Analysis.Peak(rendered)):0.0} dBFS");
    }

    /// <summary>
    ///     Signal to in-band (under 20 kHz) aliases of a 2 kHz, 0.3 V sine at gain 8 on the models that alias most
    ///     (docs/06 §3.4): Standard oversampling may not fall below today's floor, and High (4 x) buys at least 6 dB
    ///     more. Measured 23.8 / 33.0 (SLO-100), 19.7 / 26.3 (5150), 24.8 / 34.7 (Dual Rectifier), 20.9 / 27.7 dB
    ///     (5150III).
    /// </summary>
    [Theory]
    [InlineData("SLO-100", 20.5)]
    [InlineData("5150", 17.0)]
    [InlineData("Dual Rectifier", 23.0)]
    [InlineData("5150III", 18.0)]
    public void High_oversampling_lowers_the_aliasing_of_the_high_gain_models(string name, double floor) {
        const int Length = 32_768;
        const int Settle = 12_000;
        var type = AmpDesign.All.ToList().FindIndex(a => a.Name == name);
        var bin = (int)Math.Round(2_000.0 * Length / Rate) | 1; // odd: no alias lands on a harmonic
        var sine = new float[Settle + Length];
        for (var i = 0; i < sine.Length; i++) {
            sine[i] = (float)(0.3 * Math.Sin(2 * Math.PI * bin * i / Length));
        }

        double Measure(float oversampling) {
            using var block = Create(
                type,
                (CircuitAmpBlock.Gain, 8f),
                (CircuitAmpBlock.Master, 5f),
                (CircuitAmpBlock.OversamplingSetting, oversampling)
            );
            var magnitudes = Fft.Magnitudes(BlockRender.Mono(block, sine, 64).AsSpan(Settle, Length));
            double harmonic = 0, total = 0;
            for (var k = 1; k <= 20_000 * Length / (int)Rate; k++) {
                var energy = magnitudes[k] * magnitudes[k];
                total += energy;
                harmonic += k % bin == 0 ? energy : 0;
            }

            return 10 * Math.Log10(harmonic / (total - harmonic));
        }

        var (standard, high) = (Measure(0), Measure(1));
        output.WriteLine($"{name}: signal to aliases {standard:0.0} dB standard, {high:0.0} dB high");
        Assert.True(standard >= floor, $"{standard:0.0} dB standard (floor {floor} dB)");
        Assert.True(high >= standard + 6, $"{high:0.0} dB high against {standard:0.0} dB standard");
    }

    [Fact]
    public void Latency_follows_the_oversampling_setting() {
        static CircuitAmpBlock Linear(float oversampling) {
            var block = new CircuitAmpBlock { OversamplingPhase = OversamplingPhase.Linear };
            block.Params.SetTarget(CircuitAmpBlock.OversamplingSetting, oversampling);
            block.Prepare(new(Rate, 64));
            return block;
        }

        using var standard = Linear(0);
        using var high = Linear(1);
        Assert.Equal(2, standard.Oversampling);
        Assert.Equal(4, high.Oversampling);
        Assert.True(CircuitAmpBlock.Info.Params[CircuitAmpBlock.OversamplingSetting].Recompile);
        output.WriteLine($"linear phase: latency {standard.LatencySamples} / {high.LatencySamples} samples");
        Assert.True(standard.LatencySamples > 0);
        Assert.True(high.LatencySamples > standard.LatencySamples);
        var rendered = BlockRender.Mono(high, Chord(48_000, 0.25f), 64);
        Assert.True(Analysis.AllFinite(rendered));
        Assert.True(Analysis.Rms(rendered.AsSpan(24_000)) > 1e-3);
    }

    /// <summary>
    ///     A channel built at one rate and moved to another (its stages' <c>SetSampleRate</c>: every capacitor and
    ///     inductor reconfigured, keeping its stored wave, then the block's reset) plays as one built at the new rate.
    ///     At 48 kHz the channel runs at 96 kHz; at 44.1 kHz, at 88.2 kHz.
    /// </summary>
    [Theory]
    [MemberData(nameof(Types))]
    public void A_channel_moved_to_a_new_rate_plays_as_one_built_there(int type) {
        var chord = Chord(9_600, 0.25f);
        using var moved = Create(type);
        BlockRender.Mono(moved, chord, 64);
        moved.Prepare(new(44_100, 64));

        using var fresh = new CircuitAmpBlock();
        fresh.Params.SetTarget(CircuitAmpBlock.Type, type);
        fresh.Params.SetTarget(CircuitAmpBlock.Master, 10);
        fresh.Prepare(new(44_100, 64));

        var expected = BlockRender.Mono(fresh, chord, 64);
        var actual = BlockRender.Mono(moved, chord, 64);
        var residual = Analysis.NullResidualDb(expected, actual);
        Assert.True(residual < -140, $"{AmpDesign.All[type].Name}: {residual:0.0} dB from a channel built at 44.1 kHz");
    }

    [Fact]
    public void Negative_feedback_lowers_the_gain_and_presence_lifts_the_treble() {
        // The Plexi below clipping: master low, a quiet 1 kHz and 6 kHz.
        static double Level(float feedback, float presence, double frequency) {
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
        static double Idle(float bias) {
            using var block = Create(1, (CircuitAmpBlock.Bias, bias));
            return block.Channel.Power.PlateCurrents.A;
        }

        var (cold, stock, hot) = (Idle(0), Idle(50), Idle(100));
        output.WriteLine($"Plexi idle: cold {cold * 1e3:0.0} mA, stock {stock * 1e3:0.0} mA, hot {hot * 1e3:0.0} mA");
        Assert.True(cold < 0.5 * stock && hot > 1.5 * stock);

        static double Sagged(float sag) {
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
        block.Params.SetTarget(CircuitAmpBlock.Master, 10); // the levels and limits here are a fully driven power amp's
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    [Fact]
    public void A_type_change_takes_effect_once_the_control_thread_has_built_its_channel() {
        var plexi = AmpDesign.All.ToList().IndexOf(Plexi.Design);
        var afd = AmpDesign.All.ToList().IndexOf(MarshallAfd100.Design);
        using var block = Create(plexi);
        block.Params.SetTarget(CircuitAmpBlock.Type, afd);
        var left = new float[64];
        var right = new float[64];
        block.Process(new(left, right), new(Rate, 120, 0)); // warm the path up
        var before = GC.GetAllocatedBytesForCurrentThread();
        block.Process(new(left, right), new(Rate, 120, 0));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread()); // the audio thread builds nothing
        Assert.Same(Plexi.Design, block.Channel.Design); // and plays the channel it has meanwhile

        block.Refresh(); // what the host does on the control thread after setting parameters
        block.Process(new(left, right), new(Rate, 120, 0));
        Assert.Same(MarshallAfd100.Design, block.Channel.Design);
    }

    [Fact]
    public void An_expert_setting_finds_cached_tube_tables_while_another_model_is_being_built() {
        // The control thread builds a new channel's tables under the caches' build lock; an expert setting on the
        // audio thread (a new Preamp or Power Tube) looks up a cached model meanwhile and must not wait for it.
        var eq = TriodeModel.Ecc83;
        var el34 = PentodeModel.EL34;
        var cached = (TriodeTables.For(eq), PentodeTables.For(el34));

        static object BuildLock(Type type) =>
            type.GetField(
                "BuildLock",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!
                .GetValue(null)!;

        lock (BuildLock(typeof(TriodeTables))) {
            lock (BuildLock(typeof(PentodeTables))) {
                (TriodeTables, PentodeTables)? found = null;
                var lookup = new Thread(() => found = (TriodeTables.For(eq), PentodeTables.For(el34)));
                lookup.Start();
                Assert.True(lookup.Join(TimeSpan.FromSeconds(10)), "the lookup waited for the build lock");
                Assert.Equal(cached, found);
            }
        }
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
