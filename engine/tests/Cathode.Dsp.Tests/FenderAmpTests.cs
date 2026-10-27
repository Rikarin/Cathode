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
            $"{model}: rms {string.Join(", ", rms.Select(static r => r.ToString("0.0", CultureInfo.InvariantCulture)))} dBFS at volume 1, 3, 5, 8"
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
            $"Instrument channel: {Analysis.ToDb(down):0.0} dBFS with the Mic volume at 0, {Analysis.ToDb(up):0.0} at 10"
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
