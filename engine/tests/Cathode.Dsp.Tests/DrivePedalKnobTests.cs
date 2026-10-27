using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Drive block's last per-pedal knobs (docs/08 §6.5, docs/09 stream DK): each is shown only on its own pedal,
///     under the pedal's name for it, and moves the response the way the pedal's control does. That each is stock at
///     its default is <see cref="AmpDefaultsTests" />' job.
/// </summary>
public class DrivePedalKnobTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Length = 8_192;

    [Theory]
    [InlineData(DriveBlock.HiMid, "Hi Mids", "Blackglass 7K")]
    [InlineData(DriveBlock.Blend, "Blend", "Blackglass 7K")]
    [InlineData(DriveBlock.KingMode, "Mode", "Tone of Kings")]
    [InlineData(DriveBlock.FullMode, "Mode", "Full OD")]
    [InlineData(DriveBlock.BoostSwitch, "Boost Switch", "Full OD")]
    [InlineData(DriveBlock.BoostLevel, "Boost", "Full OD")]
    [InlineData(DriveBlock.Glow, "Glow", "Eternal Love")]
    [InlineData(DriveBlock.PedalAttack, "Attack", "Horizon Precision Drive")]
    [InlineData(DriveBlock.Gate, "Gate", "Horizon Precision Drive")]
    [InlineData(DriveBlock.Tight, "Tight", "Bosom Boost")]
    [InlineData(DriveBlock.StackBass, "Bass", "Bosom Boost")]
    [InlineData(DriveBlock.DipSwitches, "DIP Switches", "Esoteric Bass RCB")]
    [InlineData(DriveBlock.ToneSwitch, "Tone", "Super Fuzz")]
    public void Each_knob_shows_on_its_own_pedal_only_on_the_basic_page(int index, string label, string pedal) {
        var descriptor = DriveBlock.Info.Params[index];
        var labels = descriptor.TypeLabels!;
        Assert.Equal(label, labels[TypeOf(pedal)]);
        Assert.Equal("Basic", descriptor.Page);
        if (index != DriveBlock.StackBass) {
            // Shown on its own pedal and nowhere else.
            Assert.Equal([TypeOf(pedal)], Enumerable.Range(0, labels.Count).Where(t => labels[t].Length > 0));
        }
    }

    [Fact]
    public void The_new_knobs_are_appended_after_the_earlier_ones() {
        string[] ids = [
            "hiMid", "blend", "kingMode", "fullMode", "boostOn", "boostLevel", "glow", "pedalAttack", "gate", "tight",
            "dip", "toneSwitch"
        ];
        Assert.Equal(ids, DriveBlock.Info.Params.Skip(44).Select(static p => p.Id));
        Assert.Equal(DriveBlock.HiMid, 44);
        Assert.Equal(DriveBlock.ToneSwitch, 55);
        // The Super-Fuzz has a switch where the others have a Tone knob.
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.Tone].TypeLabels![TypeOf("Super Fuzz")]);
        Assert.Equal("Tone", DriveBlock.Info.Params[DriveBlock.Tone].TypeLabels![TypeOf("Pi Fuzz")]);
    }

    [Fact]
    public void B7k_hi_mids_boosts_and_cuts_its_band() {
        var type = TypeOf("Blackglass 7K");

        double Level(double hz, float hiMid) =>
            SteadyDb(Render(type, Sine(hz, 0.0005f), (DriveBlock.Drive, 0f), (DriveBlock.HiMid, hiMid)));

        var (cut, flat, boost) = (Level(1_500, 0f), Level(1_500, 5f), Level(1_500, 10f));
        output.WriteLine($"1.5 kHz: {cut:0.0} / {flat:0.0} / {boost:0.0} dB");
        Assert.True(boost > flat + 6 && cut < flat - 6, $"{cut:0.0} / {flat:0.0} / {boost:0.0}");
        // Its own band: the bass barely moves.
        Assert.InRange(Level(60, 10f) - Level(60, 5f), -1.5, 1.5);
    }

    [Fact]
    public void B7k_blend_mixes_the_clean_path_with_the_distorted_one() {
        var type = TypeOf("Blackglass 7K");
        double Distortion(float blend) => Thd(type, 8f, 0.05f, (DriveBlock.Blend, blend));
        var (clean, noon, full) = (Distortion(0f), Distortion(5f), Distortion(10f));
        output.WriteLine($"THD at drive 8: {clean:P2} Blend 0, {noon:P1} at noon, {full:P1} at 10");
        Assert.True(clean < 0.01, $"Blend 0 is the clean path alone: {clean:P2}");
        Assert.True(full > noon && noon > 10 * clean, $"{clean:P2} / {noon:P1} / {full:P1}");
    }

    [Fact]
    public void King_of_tone_distortion_clips_harder_and_boost_is_louder_and_cleaner() {
        var type = TypeOf("Tone of Kings");
        var (od, distortion, boost) = (Thd(type, 5f, 0.3f), Thd(type, 5f, 0.3f, (DriveBlock.KingMode, 1f)),
            Thd(type, 5f, 0.3f, (DriveBlock.KingMode, 2f)));

        double Level(float mode) =>
            SteadyDb(Render(type, Sine(1_000, 0.3f), (DriveBlock.Drive, 5f), (DriveBlock.KingMode, mode)));

        var (odLevel, boostLevel) = (Level(0f), Level(2f));
        output.WriteLine(
            $"300 mV at drive 5: THD OD {od:P1}, Distortion {distortion:P1}, Boost {boost:P1}; level OD {odLevel:0.0}, "
            + $"Boost {boostLevel:0.0} dB"
        );
        Assert.True(distortion > od, $"{distortion:P1} against {od:P1}");
        Assert.True(boost < od / 2, $"{boost:P1} against {od:P1}");
        Assert.True(boostLevel > odLevel + 6, $"{boostLevel:0.0} against {odLevel:0.0} dB");
    }

    [Fact]
    public void Full_drive_comp_cut_is_louder_and_vintage_humps_the_mids() {
        var type = TypeOf("Full OD");

        double Level(double hz, float amplitude, float mode) =>
            SteadyDb(Render(type, Sine(hz, amplitude), (DriveBlock.Drive, 5f), (DriveBlock.FullMode, mode)));

        var (flatMids, compCut) = (Level(1_000, 0.2f, 0f), Level(1_000, 0.2f, 1f));
        output.WriteLine($"200 mV: Flat-Mids {flatMids:0.0}, Comp-Cut {compCut:0.0} dB");
        Assert.True(compCut > flatMids + 3, $"{compCut:0.0} against {flatMids:0.0} dB");

        // Small signals: the vintage leg's 720 Hz corner keeps more of the bass out of the gain.
        double Bass(float mode) => Level(100, 0.0005f, mode) - Level(1_000, 0.0005f, mode);
        output.WriteLine($"100 Hz under 1 kHz: Flat-Mids {Bass(0f):0.0}, Vintage {Bass(2f):0.0} dB");
        Assert.True(Bass(2f) < Bass(0f) - 3, $"{Bass(2f):0.0} against {Bass(0f):0.0} dB");
    }

    [Fact]
    public void Full_drive_boost_adds_gain_and_clipping_only_when_switched_on() {
        var type = TypeOf("Full OD");
        var input = Sine(1_000, 0.01f);
        var off = Render(type, input, (DriveBlock.Drive, 2f), (DriveBlock.BoostLevel, 10f));
        var on = Render(
            type,
            input,
            (DriveBlock.Drive, 2f),
            (DriveBlock.BoostSwitch, 1f),
            (DriveBlock.BoostLevel, 10f)
        );
        var none = Render(
            type,
            input,
            (DriveBlock.Drive, 2f),
            (DriveBlock.BoostSwitch, 1f),
            (DriveBlock.BoostLevel, 0f)
        );
        output.WriteLine($"10 mV at drive 2: {SteadyDb(off):0.0} dB off, {SteadyDb(on):0.0} dB boosted");
        Assert.True(SteadyDb(on) > SteadyDb(off) + 6);
        Assert.True(
            Thd(type, 2f, 0.01f, (DriveBlock.BoostSwitch, 1f), (DriveBlock.BoostLevel, 10f)) > 2 * Thd(type, 2f, 0.01f)
        );
        Assert.Equal(off, none); // the knob at 0 adds nothing
    }

    [Fact]
    public void Eternity_glow_compresses_with_diodes_across_its_gain() {
        var type = TypeOf("Eternal Love");

        double Swing(float glow) {
            return Level(0.3f) - Level(0.003f);

            double Level(float amplitude) =>
                SteadyDb(Render(type, Sine(1_000, amplitude), (DriveBlock.Drive, 5f), (DriveBlock.Glow, glow)));
        }

        var (glass, glow) = (Swing(0f), Swing(1f));
        output.WriteLine($"output from 3 mV to 300 mV (40 dB in): Glass {glass:0.0} dB, Glow {glow:0.0} dB");
        Assert.True(glow < glass - 1.5, $"{glow:0.0} against {glass:0.0} dB");
        Assert.Equal(
            SteadyDb(Render(type, Sine(1_000, 0.003f), (DriveBlock.Drive, 5f))),
            SteadyDb(Render(type, Sine(1_000, 0.003f), (DriveBlock.Drive, 5f), (DriveBlock.Glow, 1f))),
            0.5
        ); // quiet: the same
    }

    [Fact]
    public void Precision_attack_tightens_the_lows_into_the_gain() {
        var type = TypeOf("Horizon Precision Drive");

        double Bass(float attack) =>
            SteadyDb(Render(type, Sine(80, 0.0003f), (DriveBlock.Drive, 3f), (DriveBlock.PedalAttack, attack)))
            - SteadyDb(Render(type, Sine(1_000, 0.0003f), (DriveBlock.Drive, 3f), (DriveBlock.PedalAttack, attack)));

        var (loose, noon, tight) = (Bass(0f), Bass(5f), Bass(10f));
        output.WriteLine($"80 Hz under 1 kHz: {loose:0.0} / {noon:0.0} / {tight:0.0} dB");
        Assert.True(loose > noon + 3 && tight < noon - 3, $"{loose:0.0} / {noon:0.0} / {tight:0.0}");
    }

    [Fact]
    public void Precision_gate_closes_on_a_quiet_tail_and_passes_the_note() {
        var type = TypeOf("Horizon Precision Drive");
        // A note, then a quiet tail (hum and hiss at about −66 dBV) the gate should shut.
        var input = Signals.Concat(Sine(220, 0.1f, 24_000), Sine(220, 0.0005f, 24_000));
        float[] Gated(float gate) => Render(type, input, (DriveBlock.Drive, 5f), (DriveBlock.Gate, gate));
        var (open, shut) = (Gated(0f), Gated(6f));
        static double Db(float[] s, int from, int to) => Analysis.ToDb(Analysis.Rms(s.AsSpan(from, to - from)));
        var note = (Db(open, 12_000, 24_000), Db(shut, 12_000, 24_000));
        var tail = (Db(open, 36_000, 48_000), Db(shut, 36_000, 48_000));
        output.WriteLine(
            $"note {note.Item1:0.0} / {note.Item2:0.0} dB, tail {tail.Item1:0.0} / {tail.Item2:0.0} dB (off / Gate 6)"
        );
        Assert.InRange(note.Item2 - note.Item1, -1, 1);
        Assert.True(tail.Item2 < tail.Item1 - 20, $"{tail.Item2:0.0} against {tail.Item1:0.0} dB");
    }

    [Fact]
    public void Bosom_bass_shelves_the_lows_and_tight_cuts_them_ahead_of_the_boost() {
        var type = TypeOf("Bosom Boost");

        double Level(double hz, params (int, float)[] settings) =>
            SteadyDb(Render(type, Sine(hz, 0.001f), [(DriveBlock.Drive, 3f), ..settings]));

        var (cut, flat, boost) = (Level(50, (DriveBlock.StackBass, 0f)), Level(50),
            Level(50, (DriveBlock.StackBass, 10f)));
        output.WriteLine($"50 Hz: Bass {cut:0.0} / {flat:0.0} / {boost:0.0} dB");
        Assert.True(boost > flat + 8 && cut < flat - 8, $"{cut:0.0} / {flat:0.0} / {boost:0.0}");
        Assert.InRange(Level(3_000, (DriveBlock.StackBass, 10f)) - Level(3_000), -1, 1);

        var tight = Level(50, (DriveBlock.Tight, 1f));
        output.WriteLine($"50 Hz: Tight {tight:0.0} dB against {flat:0.0}");
        Assert.True(tight < flat - 6, $"{tight:0.0} against {flat:0.0} dB");
        Assert.InRange(Level(1_000, (DriveBlock.Tight, 1f)) - Level(1_000), -1, 1);
    }

    [Fact]
    public void Bass_rcb_dip_switches_move_its_bands_up() {
        var type = TypeOf("Esoteric Bass RCB");

        double Level(double hz, float dip, params (int, float)[] settings) =>
            SteadyDb(
                Render(type, Sine(hz, 0.0005f), [(DriveBlock.Drive, 0f), (DriveBlock.DipSwitches, dip), ..settings])
            );

        // A low shelf moved up reaches higher; a treble shelf moved up boosts less below its new corner.
        var bass = (Level(200, 0f, (DriveBlock.StackBass, 10f)), Level(200, 1f, (DriveBlock.StackBass, 10f)));
        var treble = (Level(3_000, 0f, (DriveBlock.Tone, 10f)), Level(3_000, 2f, (DriveBlock.Tone, 10f)));
        output.WriteLine(
            $"200 Hz with Bass full: {bass.Item1:0.0} stock, {bass.Item2:0.0} Bass Up; 3 kHz with Treble full: "
            + $"{treble.Item1:0.0}, {treble.Item2:0.0} Treble Up"
        );
        Assert.True(bass.Item2 > bass.Item1 + 3, $"{bass}");
        Assert.True(treble.Item2 < treble.Item1 - 3, $"{treble}");
        // Each switch moves its own band only; both together are both.
        Assert.Equal(Level(3_000, 0f, (DriveBlock.Tone, 10f)), Level(3_000, 1f, (DriveBlock.Tone, 10f)), 0.5);
        Assert.Equal(treble.Item2, Level(3_000, 3f, (DriveBlock.Tone, 10f)), 0.5);
    }

    [Fact]
    public void Super_fuzz_tone_switch_scoops_or_switches_the_filter_out_and_the_tone_knob_does_nothing() {
        var type = TypeOf("Super Fuzz");
        var noise = Signals.Noise(48_000, 5, 0.002f);

        double Tilt(params (int, float)[] settings) {
            var rendered = Render(type, noise, [(DriveBlock.Drive, 0f), ..settings]);
            return Band(rendered, 700, 1_400) - Band(rendered, 100, 300);
        }

        var (scoop, flat) = (Tilt(), Tilt((DriveBlock.ToneSwitch, 1f)));
        output.WriteLine($"mids against the bass: {scoop:0.0} dB scooped, {flat:0.0} dB flat");
        Assert.True(flat > scoop + 3, $"{flat:0.0} against {scoop:0.0} dB");
        Assert.Equal(Render(type, noise, (DriveBlock.Tone, 0f)), Render(type, noise, (DriveBlock.Tone, 10f)));
    }

    static int TypeOf(string name) =>
        DriveBlock.CircuitTypes + DriveCircuits.All.ToList().FindIndex(c => c.Name == name);

    static float[] Sine(double hz, float amplitude, int length = 19_200) => Signals.Sine(hz, Rate, length, amplitude);

    static float[] Render(int type, float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<DriveBlock>([(DriveBlock.Type, type), ..settings]);
        return BlockRender.Mono(block, input, 64);
    }

    static double SteadyDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));

    /// <summary>THD of a coherent 1 kHz-ish sine at <paramref name="amplitude" /> volts.</summary>
    static double Thd(int type, float drive, float amplitude, params (int Index, float Value)[] settings) {
        var input = Signals.CoherentSine(171, Length, amplitude);
        var rendered = Render(type, Signals.Concat(input, input), [(DriveBlock.Drive, drive), ..settings]);
        return Analysis.Thd(rendered.AsSpan(Length), 171);
    }

    /// <summary>
    ///     Energy from <paramref name="low" /> to <paramref name="high" /> Hz in the last 32k samples, in dB.
    /// </summary>
    static double Band(float[] signal, double low, double high) {
        var magnitudes = Fft.Magnitudes(signal.AsSpan(signal.Length - 32_768));
        double energy = 0;
        for (var k = (int)(low * 32_768 / Rate); k <= (int)(high * 32_768 / Rate); k++) {
            energy += magnitudes[k] * magnitudes[k];
        }

        return 10 * Math.Log10(energy);
    }
}
