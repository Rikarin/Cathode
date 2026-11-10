using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Globalization;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Fender family (docs/06 §3.1): knob sweeps on each model through the block: every knob and switch on its
///     panel audible, the volume raising the level, treble and bass doing what they say, the 5E3's channel interaction,
///     levels bounded, no allocation on the audio thread.
/// </summary>
public class FenderAmpTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Names = [
        "Deluxe Reverb",
        "Twin Reverb",
        "Super Reverb",
        "Princeton Reverb",
        "Bassman 5F6-A",
        "Bassman 5F6-A Jumped",
        "Tweed Deluxe 5E3",
        "Tweed Deluxe 5E3 Jumped"
    ];

    public static TheoryData<string> Fenders => [..Names];

    [Theory]
    [MemberData(nameof(Fenders))]
    public void Every_panel_control_is_audible_and_bounded(string model) {
        var design = AmpDesign.All.Single(a => a.Name == model);
        var panel = design.Panel;
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

        // A Super's middle knob is on the Vibrato channel only.
        var channel = model == "Super Reverb" ? (CircuitAmpBlock.Switch1, 1f) : (CircuitAmpBlock.Level, 0f);
        foreach (var (index, label) in knobs.Where(static k => k.Label is not null)) {
            var low = Render(model, channel, (index, 2f));
            var high = Render(model, channel, (index, 8f));
            Check(model, label!, low, high);
        }

        foreach (var (index, label) in switches.Where(static s => s.Label is not null)) {
            Check(model, label!, Render(model, channel, (index, 0f)), Render(model, channel, (index, 1f)));
        }
    }

    [Theory]
    [MemberData(nameof(Fenders))]
    public void Volume_raises_the_level_and_the_tone_knobs_shape_it(string model) {
        // A soft pluck (30 mV), so that the tweeds are not already saturated at 1.
        var design = AmpDesign.All.Single(a => a.Name == model);
        var rms = new[] { 1f, 3f, 5f, 8f }
            .Select(g => Analysis.ToDb(Analysis.Rms(Render(model, 0.03f, (CircuitAmpBlock.Gain, g)).AsSpan(4_800))))
            .ToArray();
        output.WriteLine(
            $"{model}: rms "
            + $"{string.Join(", ", rms.Select(static r => r.ToString("0.0", CultureInfo.InvariantCulture)))} dBFS "
            + "at volume 1, 3, 5, 8"
        );
        for (var i = 1; i < rms.Length; i++) {
            Assert.True(rms[i] > rms[i - 1] + 0.5, $"volume {i}: {rms[i]:0.0} dB after {rms[i - 1]:0.0} dB");
        }

        // Small signals, so that the power amp's compression does not hide the stack.
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

    [Fact]
    public void Tweed_deluxe_unused_volume_loads_the_one_played() {
        // Robinette: the unused channel's volume loads the one played from about its V1 plate at 10 to 1M at 0, so
        // turning it down gives the most gain.
        var down = Analysis.Rms(
            Render("Tweed Deluxe 5E3", 0.02f, (CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Control1, 0f))
        );
        var up = Analysis.Rms(
            Render("Tweed Deluxe 5E3", 0.02f, (CircuitAmpBlock.Gain, 3f), (CircuitAmpBlock.Control1, 10f))
        );
        output.WriteLine(
            $"Instrument channel: {Analysis.ToDb(down):0.0} dBFS with the Mic volume at 0, "
            + $"{Analysis.ToDb(up):0.0} at 10"
        );
        Assert.True(down > 1.5 * up);
    }

    [Theory]
    [InlineData("Bassman 5F6-A")]
    [InlineData("Bassman 5F6-A Jumped")]
    public void Bassman_has_no_master_so_the_hidden_master_knob_does_nothing(string model) {
        // The treble wiper drives the inverter directly: no pot left at a hidden Master's default between them.
        var design = AmpDesign.All.Single(a => a.Name == model);
        Assert.Null(design.Panel.Master);
        Assert.False(AmpFeatures.Has(design, "mvloc"));
        Assert.Equal(
            Render(model, 0.05f, (CircuitAmpBlock.Gain, 6f), (CircuitAmpBlock.Master, 2f)),
            Render(model, 0.05f, (CircuitAmpBlock.Gain, 6f), (CircuitAmpBlock.Master, 10f))
        );
    }

    [Fact]
    public void Blackface_is_built_from_the_ab763_parts() {
        // The AB763 drawings: 34k at the input (68k ‖ 68k), a 10K-A mid pot, 0.047 µF into the 220k mixing resistors,
        // 1 nF into a 12AT7 pair with 470 Ω between the cathodes and the tail.
        var design = Amp.Models.Blackface.Design;
        var twin = Amp.Models.TwinReverb.Design;
        var first = (ToneStackStageDesign)design.Preamp[0];
        var twinFirst = (ToneStackStageDesign)twin.Preamp[0];
        Assert.Equal(twinFirst.InputResistance, first.InputResistance);
        Assert.Equal(34e3, first.InputResistance);
        Assert.Equal(twinFirst.Stack with { Name = first.Stack.Name }, first.Stack);
        Assert.Equal(new Pot(10e3, PotTaper.Audio), first.Stack.Middle);
        Assert.Equal(47e-9, design.Preamp.OfType<CouplingStageDesign>().Single().CouplingCap);
        Assert.Equal(twin.Power.PhaseInverter, design.Power.PhaseInverter);
        Assert.Equal(470, ((LongTailPairDesign)design.Power.PhaseInverter).BiasResistor);
        Assert.Equal(twin.Power.InverterInput, design.Power.InverterInput);
    }

    [Theory]
    [InlineData("Blackface", 270)] // a Super Reverb-style channel
    [InlineData("Deluxe Reverb", 180)]
    [InlineData("Twin Reverb", 270)]
    [InlineData("Super Reverb", 270)]
    [InlineData("Vibroverb AB763", 280)]
    [InlineData("Bandmaster AB763", 275)]
    public void Ab763_first_stage_plates_rest_near_the_drawings_plate_voltage(string model, double drawn) {
        // Robinette's "AB763 model differences" lists the voltage the drawings print at the 7025 plates, not the
        // preamp's supply node: a 270 V plate on 100k and 1.5k needs a node near 410 V.
        var stage = AmpDesign.All.Single(a => a.Name == model).Preamp[0];
        var supply = stage switch {
            ToneStackStageDesign stack => stack.Supply,
            NetworkStageDesign network => network.Netlist.Elements.First(static e => e.Kind == ElementKind.Supply).Value,
            _ => throw new InvalidOperationException(stage.Name)
        };
        var plate = OperatingPoint.CommonCathode(Wdf.TriodeModel.Ecc83, 0, supply, 100e3, 1.5e3).Plate;
        output.WriteLine($"{model}: V1 on {supply:0} V rests at {plate:0} V at the plate (drawing {drawn:0} V)");
        Assert.Equal(drawn, plate, 0.15 * drawn);
    }

    [Theory]
    [InlineData("Bassman 5F6-A")]
    [InlineData("Bassman 5F6-A Jumped")]
    public void Bassman_inverter_takes_the_stack_through_its_input_cap_and_draws_grid_current(string model) {
        // The 5F6-A drawing: 0.02 µF from the treble wiper to the inverter's grid, 1M from the grid to the junction of
        // the 470 Ω and the tail. The Tweed Twin 5F8-A's drawing has the same.
        var design = AmpDesign.All.Single(a => a.Name == model);
        Assert.Equal(new InverterInputDesign(20e-9, 1e6), design.Power.InverterInput);
        Assert.Equal(Amp.Models.TweedTwin.Design.Power.InverterInput, design.Power.InverterInput);

        // Overdriven, the grid conducts on the peaks and charges the cap: the grid's mean falls below its rest.
        var power = new PowerAmp(design.Power, 96_000);
        power.SetSourceResistance(40e3);
        power.Configure(new(0.5, 0, 0, 0.5, 0.5, 0.5));
        power.Reset();
        var rest = power.InverterGrid;
        var mean = 0.0;
        for (var n = 0; n < 19_200; n++) {
            power.Tick(60 * Math.Sin(2 * Math.PI * 220 * n / 96_000.0));
            if (n >= 9_600) {
                mean += power.InverterGrid / 9_600;
            }
        }

        output.WriteLine($"{model}: driven grid rests at {rest:0.00} V, mean {mean:0.00} V under a 60 V sine");
        Assert.True(mean < rest - 1, $"mean {mean:0.00} V against {rest:0.00} V at rest");

        // And the whole amp still converges at full gain, hot bias and full sag under a full-scale square wave.
        using var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == model));
        block.Params.SetTarget(CircuitAmpBlock.Gain, 10);
        block.Params.SetTarget(CircuitAmpBlock.Control1, 10);
        block.Params.SetTarget(CircuitAmpBlock.Bias, 100);
        block.Params.SetTarget(CircuitAmpBlock.Sag, 10);
        block.Prepare(new(Rate, 64));
        var square = Signals.Sine(110, Rate, 48_000).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var rendered = BlockRender.Mono(block, Signals.Concat(square, Signals.Noise(48_000, 4)), 64);
        Assert.True(Analysis.AllFinite(rendered));
        var channel = block.Channel;
        var solvers = channel.Stages
            .Where(static s => s.Newton is not null)
            .Select(static s => s.Newton!)
            .Append(channel.Power.Newton);
        foreach (var newton in solvers) {
            Assert.Equal(0, newton.NonFinite);
            Assert.True(
                newton.Unconverged <= newton.Samples / 10_000,
                $"{model}: {newton.Unconverged} unconverged of {newton.Samples}"
            );
        }
    }

    [Fact]
    public void Tweed_deluxe_power_tubes_idle_on_the_drawings_250_ohm_cathode_resistor() {
        // The 5E3's two 6V6s share 250 Ω (Robinette: about 38 mA a tube); the resistor sets the idle current, not a
        // current the model guesses.
        var power = new AmpChannel(Amp.Models.TweedDeluxe.Design, 96_000).Power;
        var (plate, _) = power.PlateCurrents;
        var (screen, _) = power.ScreenCurrents;
        output.WriteLine(
            $"{power.CathodeResistor:0} Ω, cathode {power.CathodeVoltage:0.0} V, {plate * 1e3:0.0} mA plate"
        );
        Assert.Equal(250, power.CathodeResistor, 6);
        Assert.Equal(power.CathodeVoltage, 250 * 2 * (plate + screen), 0.05 * power.CathodeVoltage);
        Assert.InRange(plate, 0.030, 0.040);
    }

    [Fact]
    public void Tweed_deluxe_bright_cap_fades_out_as_tone_is_turned_down() {
        // The drawing's tone pot has the 500 pF on its far end: at 10 it bypasses the Instrument volume's upper part,
        // and turned down the pot's track comes in series with it. The Microphone channel is the same circuit without
        // the cap, so the Instrument channel's extra treble over it is the bright bypass.
        double Lift(float tone) {
            return Channel(0f) - Channel(1f);

            double Channel(float mic) => Analysis.ToDb(Level(4_000, mic) / Level(400, mic));

            double Level(double frequency, float mic) {
                using var block = new CircuitAmpBlock();
                block.Params.SetTarget(
                    CircuitAmpBlock.Type,
                    AmpDesign.All.ToList().FindIndex(static a => a.Name == "Tweed Deluxe 5E3")
                );
                block.Params.SetTarget(CircuitAmpBlock.Gain, 3);
                block.Params.SetTarget(CircuitAmpBlock.Control1, 3);
                block.Params.SetTarget(CircuitAmpBlock.Treble, tone);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, mic);
                block.Prepare(new(Rate, 64));
                var rendered = BlockRender.Mono(block, Signals.Sine(frequency, Rate, 19_200, 0.01f), 64);
                return Analysis.Rms(rendered.AsSpan(9_600));
            }
        }

        var (full, half, low) = (Lift(10f), Lift(5f), Lift(2f));
        output.WriteLine(
            $"Instrument over Microphone, 4 kHz over 400 Hz: {full:0.0} dB at tone 10, {half:0.0} dB at 5, "
            + $"{low:0.0} dB at 2"
        );
        Assert.True(full > 10, $"bright cap at tone 10: {full:0.0} dB");
        Assert.True(Math.Abs(half) < 3, $"bright cap at tone 5: {half:0.0} dB");
        Assert.True(Math.Abs(low) < 3, $"bright cap at tone 2: {low:0.0} dB");
    }

    [Fact]
    public void Tweed_deluxe_supply_has_the_drawings_droppers_and_its_nodes_follow_from_them() {
        // The 5E3 drawing: 5k from the reservoir to the screen node, 22k on to the preamp node. It prints no voltages,
        // so the preamp node is where the 22k and the draw of the stages on it meet.
        var design = Amp.Models.TweedDeluxe.Design;
        var supply = design.Power.PowerSupply;
        Assert.Equal(5e3, supply.ChokeResistance);
        Assert.Equal(22e3, supply.PreampResistance);

        var rail = design.Power.PhaseInverter.Supply;
        var draw = 2 * OperatingPoint.CommonCathode(Wdf.TriodeModel.TwelveAY7, 0, rail, 100e3, 2 * 820).Current
            + OperatingPoint.CommonCathode(Wdf.TriodeModel.Ecc83, 0, rail, 100e3, 1.5e3).Current
            + OperatingPoint.CommonCathode(Wdf.TriodeModel.Ecc83, 0, rail, 56e3 + 56e3, 1.5e3).Current;
        var power = new AmpChannel(design, 96_000).Power;
        output.WriteLine(
            $"B+ {power.Supply:0} V, screens {power.ScreenSupply:0} V, preamp {power.PreampVoltage:0} V "
            + $"(stages on {rail:0} V drawing {draw * 1e3:0.00} mA)"
        );
        Assert.Equal(draw, supply.PreampCurrent, 0.03 * draw);
        Assert.Equal(rail, power.PreampVoltage, 0.02 * rail);
        Assert.InRange(power.ScreenSupply, 310, 330);
    }

    [Fact]
    public void Switching_to_a_fender_model_and_playing_it_does_not_allocate() {
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
                block.Params.SetTarget(CircuitAmpBlock.Gain, 5);
                Play(8);
                block.Params.SetTarget(CircuitAmpBlock.Gain, 8);
                block.Params.SetTarget(CircuitAmpBlock.Switch1, 0);
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
        Assert.True(difference > -30, $"{control}: only {difference:0.0} dB apart");
    }
}
