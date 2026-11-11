using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Globalization;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Fender and US vintage stream (docs/06 §3.1): knob sweeps on each model through the block (every control on
///     its panel audible on the channel it belongs to, the volume and master raising the level, treble and bass doing
///     what they say, Fat and More Drive adding gain, levels bounded), the Hot Rod Deluxe's and Blues Junior's gain
///     staging against the AC test points on Fender's drawings, and no allocation on the audio thread.
/// </summary>
public class FenderUsAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Names = [
        "Hot Rod Deluxe",
        "Champ 5F1",
        "Vibroverb AB763",
        "Vibrolux Reverb AA964",
        "Bandmaster AB763",
        "Tweed Twin 5F8-A",
        "Tweed Twin 5F8-A Jumped",
        "Tremolux AA763",
        "Princeton 5F2-A",
        "Princeton AA964",
        "Vibro-King",
        "Blues Junior",
        "Pro Junior",
        "Champ AA764",
        "Classic 30"
    ];

    public static TheoryData<string> Models => [..Names];

    [Theory]
    [MemberData(nameof(Models))]
    public void Every_panel_control_is_audible_and_bounded(string model) {
        var panel = Design(model).Panel;
        (int Index, string? Label)[] knobs = [
            (CircuitAmpBlock.Gain, panel.Gain),
            (CircuitAmpBlock.Bass, panel.Bass),
            (CircuitAmpBlock.Middle, panel.Middle),
            (CircuitAmpBlock.Treble, panel.Treble),
            (CircuitAmpBlock.Presence, panel.Presence),
            (CircuitAmpBlock.Master, panel.Master),
            (CircuitAmpBlock.Control1, panel.Control1)
        ];
        (int Index, string? Label)[] switches = [
            (CircuitAmpBlock.Bright, panel.Bright),
            (CircuitAmpBlock.Switch1, panel.Switch1),
            (CircuitAmpBlock.Switch2, panel.Switch2)
        ];

        // The Hot Rod Deluxe's Volume and Bright and the Classic 30's Volume belong to their Normal channels;
        // everything else plays on the default one.
        (int, float) Channel(int index) =>
            model is "Hot Rod Deluxe" or "Classic 30" && index is CircuitAmpBlock.Control1 or CircuitAmpBlock.Bright
                ? (CircuitAmpBlock.Switch1, 1f)
                : (CircuitAmpBlock.Level, 0f);

        foreach (var (index, label) in knobs.Where(static k => k.Label is not null)) {
            Check(
                model,
                label!,
                Render(model, Channel(index), (index, 2f)),
                Render(model, Channel(index), (index, 8f))
            );
        }

        foreach (var (index, label) in switches.Where(static s => s.Label is not null)) {
            Check(
                model,
                label!,
                Render(model, Channel(index), (index, 0f)),
                Render(model, Channel(index), (index, 1f))
            );
        }
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Volume_raises_the_level_and_the_tone_knobs_shape_it(string model) {
        var design = Design(model);
        var rms = new[] { 1f, 3f, 5f, 8f }
            .Select(g => Analysis.ToDb(Analysis.Rms(Render(model, 0.03f, (CircuitAmpBlock.Gain, g)).AsSpan(4_800))))
            .ToArray();
        output.WriteLine(
            $"{model}: rms "
            + $"{string.Join(", ", rms.Select(static r => r.ToString("0.0", CultureInfo.InvariantCulture)))} "
            + "dBFS at gain 1, 3, 5, 8"
        );
        for (var i = 1; i < rms.Length; i++) {
            Assert.True(rms[i] > rms[i - 1] + 0.5, $"gain step {i}: {rms[i]:0.0} dB after {rms[i - 1]:0.0} dB");
        }

        if (design.Panel.Master is not null) {
            var masters = new[] { 1f, 3f, 5f, 8f }
                .Select(m => Analysis.ToDb(
                        Analysis.Rms(
                            Render(model, 0.03f, (CircuitAmpBlock.Gain, 5f), (CircuitAmpBlock.Master, m)).AsSpan(4_800)
                        )
                    )
                )
                .ToArray();
            output.WriteLine(
                $"{model}: rms "
                + $"{string.Join(", ", masters.Select(static r => r.ToString("0.0", CultureInfo.InvariantCulture)))} "
                + "dBFS at master 1, 3, 5, 8"
            );
            for (var i = 1; i < masters.Length; i++) {
                Assert.True(
                    masters[i] > masters[i - 1] + 0.5,
                    $"master step {i}: {masters[i]:0.0} dB after {masters[i - 1]:0.0} dB"
                );
            }
        }

        if (design.Panel.Treble is not null) {
            var dull = Tilt(model, 3_000, 300, (CircuitAmpBlock.Treble, 1f));
            var bright = Tilt(model, 3_000, 300, (CircuitAmpBlock.Treble, 9f));
            output.WriteLine($"{model}: 3 kHz over 300 Hz {dull:0.0} dB at treble 1, {bright:0.0} dB at 9");
            Assert.True(bright > dull + 3);
        }

        if (design.Panel.Bass is not null) {
            var thin = Tilt(model, 100, 1_000, (CircuitAmpBlock.Bass, 1f));
            var full = Tilt(model, 100, 1_000, (CircuitAmpBlock.Bass, 9f));
            output.WriteLine($"{model}: 100 Hz over 1 kHz {thin:0.0} dB at bass 1, {full:0.0} dB at 9");
            Assert.True(full > thin + 3);
        }
    }

    /// <summary>
    ///     The push-pull Princetons' 125A10B is 8.5k plate to plate (Hammond's figure for its replacement, the 1750E),
    ///     not the Deluxe Reverb's 6.6k 125A1A.
    /// </summary>
    [Theory]
    [InlineData("Princeton Reverb")]
    [InlineData("Princeton AA964")]
    public void The_princetons_output_transformer_is_the_125A10Bs_8500_ohms(string model) {
        Assert.Equal(8.5e3, Design(model).Power.Transformer.PlateToPlate);
        Assert.Equal(6.6e3, Design("Deluxe Reverb").Power.Transformer.PlateToPlate);
    }

    /// <summary>
    ///     The Champ AA764 against the voltages on Fender's drawing, which gives them to within 20 %: V1A's plate 200 V
    ///     and cathode 1.8 V, V1B's plate 205 V, the 12AX7's node 330 V, and the 6V6 with 350 V on its screen and 19 V
    ///     on its 470 Ω cathode resistor (40 mA).
    /// </summary>
    [Fact]
    public void The_champ_aa764_rests_at_the_drawings_voltages() {
        var channel = new AmpChannel(Design("Champ AA764"), 96_000);
        var first = (ToneStackStage)channel.Stages[0];
        var power = channel.Power;
        output.WriteLine(
            $"V1A plate {first.RestDriven:0.0} V, cathode {first.RestCathode:0.00} V; V1B plate "
            + $"{channel.Stages[1].RestOutput:0.0} V; B+ {power.Supply:0.0} V, screen {power.ScreenSupply:0.0} V, "
            + $"preamp {power.PreampVoltage:0.0} V; 6V6 cathode {power.CathodeVoltage:0.0} V across "
            + $"{power.CathodeResistor:0} Ω, {power.PlateCurrents.A * 1e3:0.0} mA plate"
        );
        Assert.Equal(200, first.RestDriven, 0.2 * 200);
        Assert.Equal(1.8, first.RestCathode, 0.2 * 1.8);
        Assert.Equal(205, channel.Stages[1].RestOutput, 0.2 * 205);
        Assert.Equal(330, power.PreampVoltage, 0.1 * 330);
        Assert.Equal(350, power.ScreenSupply, 0.1 * 350);
        Assert.Equal(470, power.CathodeResistor, 6);
        Assert.Equal(19, power.CathodeVoltage, 0.2 * 19);
    }

    /// <summary>
    ///     The Champ AA764's stack is the blackface one with 15k under the bass pot where the Princeton has 6.8k, so
    ///     its mid scoop is shallower: with the tone knobs at noon, 500 Hz sits closer to 100 Hz than on the Princeton
    ///     AA964.
    /// </summary>
    [Fact]
    public void The_champ_aa764s_15k_fills_the_mids_the_princetons_6k8_scoops() {
        Assert.Equal(30e3, ((ToneStackStageDesign)Design("Champ AA764").Preamp[0]).Stack.Middle.Ohms);
        Assert.Equal(13.6e3, ((ToneStackStageDesign)Design("Princeton AA964").Preamp[0]).Stack.Middle.Ohms);
        var champ = Tilt("Champ AA764", 500, 100, (CircuitAmpBlock.Bass, 5f));
        var princeton = Tilt("Princeton AA964", 500, 100, (CircuitAmpBlock.Bass, 5f));
        output.WriteLine($"500 Hz over 100 Hz: Champ AA764 {champ:0.0} dB, Princeton AA964 {princeton:0.0} dB");
        Assert.True(champ > princeton + 1);
    }

    /// <summary>
    ///     The Champ AA764's power amp is the 5F1's 6V6 stage on the AA764 drawing's supply (360 V on 20 µF, 1k, 10k)
    ///     and feedback (2.7k from the speaker into the 47 Ω under V1B's cathode); the panel is Volume, Treble and
    ///     Bass, with nothing else on it.
    /// </summary>
    [Fact]
    public void The_champ_aa764_has_the_drawings_supply_feedback_and_panel() {
        var design = Design("Champ AA764");
        var (power, tweed) = (design.Power, Design("Champ 5F1").Power);
        Assert.Equal(PowerTopology.SingleEnded, power.Topology);
        Assert.Equal(360, power.Supply);
        Assert.Equal((20e-6, 1e3, 20e-6, 10e3, 20e-6), Chain(power.PowerSupply));
        Assert.Equal((16e-6, 10e3, 8e-6, 22e3, 8e-6), Chain(tweed.PowerSupply));
        Assert.Same(RectifierModel.FiveY3, power.PowerSupply.Rectifier);
        Assert.Equal((2.7e3, 47), (power.Feedback.FeedbackResistor, power.Feedback.ShuntResistor));
        Assert.Equal(
            (tweed.CathodeResistor, tweed.CathodeBypass, tweed.CouplingCap, tweed.GridLeak),
            (power.CathodeResistor, power.CathodeBypass, power.CouplingCap, power.GridLeak)
        );

        var panel = design.Panel;
        Assert.Equal(("Volume", "Bass", "Treble"), (panel.Gain, panel.Bass, panel.Treble));
        Assert.Null(panel.Middle ?? panel.Presence ?? panel.Master ?? panel.Bright ?? panel.TremoloRate);

        static (double, double, double, double, double) Chain(PowerSupplyDesign supply) =>
            (supply.Reservoir, supply.ChokeResistance, supply.ScreenCap, supply.PreampResistance, supply.PreampCap);
    }

    /// <summary>
    ///     The Hot Rod Deluxe's Presence is the drawing's 25k30C, a reverse audio rheostat under the 0.1 µF: at noon
    ///     7.5k of the track is left in series with the cap (a linear pot leaves 12.5k), all of it at 0 and none at 10.
    /// </summary>
    [Fact]
    public void The_hot_rod_deluxes_presence_pot_is_the_drawings_reverse_audio_taper() {
        var feedback = Design("Hot Rod Deluxe").Power.Feedback;
        Assert.Equal(new(25e3, PotTaper.ReverseAudio, 0.3), feedback.Presence);
        Assert.Equal(25e3 * 0.1e-6, feedback.PresenceTimes(0).Tau1, 12);
        Assert.Equal(7.5e3 * 0.1e-6, feedback.PresenceTimes(0.5).Tau1, 12);
        Assert.Equal(0, feedback.PresenceTimes(1).Tau1, 12);
    }

    [Theory]
    [InlineData("Hot Rod Deluxe", CircuitAmpBlock.Switch2)]
    [InlineData("Vibro-King", CircuitAmpBlock.Switch1)]
    [InlineData("Blues Junior", CircuitAmpBlock.Switch1)]
    public void Fat_and_more_drive_add_gain(string model, int index) {
        // Quiet enough that the boost is not compressed away.
        var off = Analysis.Rms(Render(model, 0.005f, (CircuitAmpBlock.Gain, 3f), (index, 0f)));
        var on = Analysis.Rms(Render(model, 0.005f, (CircuitAmpBlock.Gain, 3f), (index, 1f)));
        output.WriteLine($"{model}: {Analysis.ToDb(on / off):0.0} dB more with the switch on");
        Assert.True(on > 1.4 * off);
    }

    /// <summary>
    ///     Fender's drawings list the AC signal at test points for a 1 kHz sine at the input with the knobs at noon:
    ///     the Hot Rod Deluxe III in Drive mode at 4 mV (V1B's grid 13 mV, V2A's grid 58 mV, its plate 1.49 V; the
    ///     loop's send, 1.45 V, is inside the channel stage here), the Blues Junior III at 10 mV (after the volume 42
    ///     mV, the treble wiper 162 mV, the master's wiper 114 mV). The Blues Junior's are within 2.9 dB. The Hot Rod
    ///     Deluxe's are within 6.6 dB: its stack's loss between V1B's plate and V2A's grid (15.7 dB on the drawing) is
    ///     what the stack's values give (16.5 dB), but the drawing's V1B gain (27 from grid to plate) is lower than a
    ///     12AX7 with a bypassed 1.5k into that load gives, so our V2A grid is 4.7 dB hotter and the error carries on
    ///     down the chain (the 12AX7 refitted to RCA's points, a little steeper at these stages' idle, added 0.6 dB).
    /// </summary>
    [Theory]
    [InlineData("Hot Rod Deluxe", 0.004, "V1A=0.013,V1B=0.058,V2A=1.49")]
    [InlineData("Blues Junior", 0.010, "V1B=0.042,V1A=0.162,V2B=0.114")]
    [InlineData("Pro Junior", 0.020, "V1B=0.008,V1A=0.238")]
    public void Gain_staging_follows_the_test_points_on_the_drawing(string model, double input, string points) {
        var expected = points.Split(',')
            .Select(static p => p.Split('='))
            .ToDictionary(
                static p => p[0],
                static p => double.Parse(p[1], CultureInfo.InvariantCulture)
            );
        var controls = new AmpControls(0.5, 0.5, 0.5, 0.5, 0.5, false);
        const double rate = 96_000;
        var stages = Design(model).Preamp.Select(s => s.Create(rate)).ToArray();
        var rest = 0.0;
        var source = 1e3;
        foreach (var stage in stages) {
            stage.SetSourceResistance(source);
            stage.Update(controls);
            stage.Reset(rest);
            rest = stage.RestOutput;
            source = stage.OutputResistance;
        }

        var n = (int)rate;
        var settled = n / 2; // the second half, measured
        var sums = new double[stages.Length];
        var means = new double[stages.Length];
        for (var i = 0; i < n; i++) {
            var x = input * Math.Sqrt(2) * Math.Sin(2 * Math.PI * 1_000 * i / rate);
            for (var s = 0; s < stages.Length; s++) {
                x = stages[s].Tick(x + (s == 0 ? 0 : stages[s - 1].RestOutput)) - stages[s].RestOutput;
                if (i >= settled) {
                    sums[s] += x * x;
                    means[s] += x;
                }
            }
        }

        var errors = new List<double>();
        for (var s = 0; s < stages.Length; s++) {
            var mean = means[s] / settled;
            var rms = Math.Sqrt(sums[s] / settled - mean * mean);
            var name = Design(model).Preamp[s].Name;
            if (expected.TryGetValue(name, out var target)) {
                var error = Analysis.ToDb(rms / target);
                output.WriteLine(
                    $"{model} {name}: {rms * 1e3:0.0} mV (drawing {target * 1e3:0.0} mV, {error:+0.0;-0.0} dB)"
                );
                errors.Add(error);
            } else {
                output.WriteLine($"{model} {name}: {rms * 1e3:0.0} mV");
            }
        }

        Assert.All(errors, static e => Assert.InRange(e, -7, 7));
    }

    /// <summary>
    ///     The Pro Junior against the DC test points of Fender's drawing 047420 (its voltages "may vary ± 20 %"): 152
    ///     V on both V1 plates and 1.2 V on their cathodes from the 232 V node, 172 V on the inverter's plates and 41 V
    ///     on its cathodes. The inverter's tail is the 33k alone (the feedback enters at the second grid), and the
    ///     feedback is 100k into 1k with 0.1 µF straight across it, so it falls above 1.6 kHz wherever the hidden
    ///     Presence knob stands.
    /// </summary>
    [Fact]
    public void The_pro_junior_rests_where_the_drawing_measures_it() {
        var design = Design("Pro Junior");
        foreach (var stage in design.Preamp.Cast<NetworkStageDesign>()) {
            var triode = stage.Netlist.Elements.Single(static e => e.Kind == ElementKind.Triode);
            var built = Assert.IsType<NetworkStage<FastTriode>>(stage.Create(Rate));
            built.SetSourceResistance(1e3);
            built.Update(new(0.5, 0.5, 0.5, 0.5, 0.5, false));
            built.Reset(0);
            var (plate, cathode) = (built.RestVoltage(triode.A), built.RestVoltage(triode.C));
            output.WriteLine($"{stage.Name}: plate {plate:0.0} V (152), cathode {cathode:0.00} V (1.2)");
            Assert.InRange(plate, 152 * 0.9, 152 * 1.1);
            Assert.InRange(cathode, 1.2 * 0.8, 1.2 * 1.2);
        }

        var pair = Assert.IsType<LongTailPairDesign>(design.Power.PhaseInverter);
        Assert.Equal(33e3, pair.TailResistor);
        var inverter = LongTailPair.For(pair);
        output.WriteLine(
            $"inverter: cathodes {inverter.RestCathode:0.0} V (41), plates {inverter.RestPlateA:0.0} / "
            + $"{inverter.RestPlateB:0.0} V (172)"
        );
        Assert.InRange(inverter.RestCathode, 41 * 0.85, 41 * 1.15);
        Assert.InRange(inverter.RestPlateB, 172 * 0.9, 172 * 1.1);

        var feedback = design.Power.Feedback;
        Assert.Equal(1.0 / 101, feedback.Beta, 12);
        foreach (var presence in new[] { 0.0, 0.5, 1.0 }) {
            var (zero, pole) = feedback.PresenceTimes(presence);
            Assert.Equal(0, zero);
            Assert.InRange(1 / (2 * Math.PI * pole), 1_550, 1_650);
        }
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

        int[] types = [..Names.Select(static n => AmpDesign.All.ToList().FindIndex(a => a.Name == n))];

        void Round() {
            foreach (var type in types) {
                block.Params.SetTarget(CircuitAmpBlock.Type, type);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 1);
                block.Params.SetTarget(CircuitAmpBlock.Switch2, 1);
                block.Params.SetTarget(CircuitAmpBlock.Gain, 5);
                Play(8);
                block.Params.SetTarget(CircuitAmpBlock.Gain, 8);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 0);
                block.Params.SetTarget(CircuitAmpBlock.Switch2, 0);
                Play(8);
            }
        }

        Round(); // the first round warms up the runtime (tiered compilation)
        var before = GC.GetAllocatedBytesForCurrentThread();
        Round();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{allocated} bytes allocated");
        Assert.Equal(0, allocated);
    }

    static AmpDesign Design(string model) => AmpDesign.All.Single(a => a.Name == model);

    static float[] Render(string model, params (int Index, float Value)[] settings) => Render(model, 0.25f, settings);

    static float[] Render(string model, float peak, params (int Index, float Value)[] settings) {
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == model));
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return BlockRender.Mono(block, CircuitAmpBlockTests.Chord(14_400, peak), 64);
    }

    /// <summary>
    ///     The level of a quiet sine at <paramref name="high" /> hertz over one at <paramref name="low" />, in dB.
    /// </summary>
    static double Tilt(string model, double high, double low, (int Index, float Value) knob) {
        return Analysis.ToDb(Level(high) / Level(low));

        double Level(double frequency) {
            using var block = new CircuitAmpBlock();
            block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == model));
            block.Params.SetTarget(CircuitAmpBlock.Gain, 3);

            // The Hot Rod Deluxe's Drive channel saturates even a quiet sine at Drive 3: measure on its Normal channel.
            if (model == "Hot Rod Deluxe") {
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 1);
                block.Params.SetTarget(CircuitAmpBlock.Control1, 3);
            }

            block.Params.SetTarget(knob.Index, knob.Value);
            block.Prepare(new(Rate, 64));
            var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 19_200, 0.01f), 64);
            return Analysis.Rms(rendered.AsSpan(9_600));
        }
    }

    void Check(string model, string control, float[] low, float[] high) {
        var difference = Analysis.NullResidualDb(low, high) - Analysis.ToDb(Analysis.Rms(low));
        var peak = Math.Max(Analysis.Peak(low), Analysis.Peak(high));
        output.WriteLine($"{model} {control}: {difference:0.0} dB apart, peak {Analysis.ToDb(peak):0.0} dBFS");
        Assert.True(Analysis.AllFinite(low) && Analysis.AllFinite(high));
        Assert.True(Analysis.ToDb(peak) < 6, $"{control}: peak {Analysis.ToDb(peak):0.0} dBFS");
        // Without reverb (Bandmaster, Tremolux) the AB763's two channels differ only in their 0.047 / 0.1 µF coupling
        // caps into the mixer: the Vibrato Ch switch moves the sound by about -31 dB of residual.
        Assert.True(difference > (control == "Vibrato Ch" ? -35 : -30), $"{control}: only {difference:0.0} dB apart");
    }
}
