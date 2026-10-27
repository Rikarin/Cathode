using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Round 4, group D3 of the Drive catalogue (docs/09, stream D3): each new type's controls do what the pedal's do.
///     The generic checks (gain, aliasing, symmetry, tone tilt, bounds, CPU) are <see cref="DriveCatalogueTests" />'s.
/// </summary>
public class DriveRound4FuzzTests(ITestOutputHelper output) {
    const int Length = 8_192;
    const double Rate = 48_000;
    const int LowBin = 32; // about 190 Hz

    public static TheoryData<string> CleanTypes => ["Micro Boost", "Bosom Boost", "FAS Boost", "Integral Pre"];

    [Fact]
    public void Every_d3_type_is_in_the_catalogue_after_the_earlier_ones() {
        string[] names = [
            "Master Fuzz", "Super Fuzz", "Hard Fuzz", "Tape Distortion", "Bit Crusher", "SDD Preamp", "FET Preamp",
            "Integral Pre", "Micro Boost", "Mid Boost", "Bosom Boost", "FAS Boost"
        ];
        var all = DriveCircuits.All.Select(static c => c.Name).ToArray();
        Assert.Equal(names, all.TakeLast(names.Length));
        Assert.Equal("NKT Face Fuzz", all[23]); // round 3's last type keeps its index
    }

    [Fact]
    public void Master_fuzz_attack_brings_up_the_fuzz_and_the_starved_supply_sputters() {
        var quiet = Signals.CoherentSine(LowBin, Length, 0.03f);
        var (lowGain, lowThd) = GainAndThd("Master Fuzz", 0f, quiet, LowBin);
        var (highGain, highThd) = GainAndThd("Master Fuzz", 10f, quiet, LowBin);
        output.WriteLine(
            $"Master Fuzz at 30 mV: {lowGain:0.0} dB / THD {lowThd:P0} at attack 0, {highGain:0.0} dB / {highThd:P0} at 10"
        );
        Assert.True(highThd > lowThd + 0.15, "attack does not bring up the fuzz");

        // Asymmetric, as a starved germanium circuit is: strong even harmonics.
        var spectrum = Spectrum("Master Fuzz", 5f, 10f, Signals.CoherentSine(LowBin, Length, 0.1f));
        var second = Analysis.ToDb(spectrum[2 * LowBin] / spectrum[LowBin]);
        output.WriteLine($"Master Fuzz H2 {second:0.0} dBc");
        Assert.True(second > -25, $"H2 {second:0.0} dBc");
    }

    [Fact]
    public void Super_fuzz_makes_an_octave_up_and_its_tone_scoops_the_mids_at_noon() {
        var spectrum = Spectrum("Super Fuzz", 5f, 5f, Signals.CoherentSine(LowBin, Length, 0.1f));
        var octave = Analysis.ToDb(spectrum[2 * LowBin] / spectrum[LowBin]);
        output.WriteLine($"Super Fuzz octave {octave:0.0} dB against the fundamental");
        Assert.True(octave > -6, $"octave {octave:0.0} dB below the fundamental");

        // A fuzz without the rectifier (the Face) has much less of it.
        var face = Spectrum("Face Fuzz", 5f, 5f, Signals.CoherentSine(LowBin, Length, 0.1f));
        Assert.True(octave > Analysis.ToDb(face[2 * LowBin] / face[LowBin]) + 6);

        // The tone switch's scoop: the low-pass and high-pass halves cancel around 1 kHz; Flat switches the filter out.
        static double Mid(float position) =>
            Analysis.ToDb(
                Analysis.MagnitudeAt(Impulse("Super Fuzz", 5f, 5f, (DriveBlock.ToneSwitch, position)), 1_000, Rate)
            );

        var (scoop, flat) = (Mid(0f), Mid(1f));
        output.WriteLine($"Super Fuzz small-signal 1 kHz: {scoop:0.0} dB scooped, {flat:0.0} dB flat");
        Assert.True(scoop < flat - 3, "no mid scoop");

        var (lowGain, lowThd) = GainAndThd("Super Fuzz", 0f, Signals.CoherentSine(LowBin, Length, 0.03f), LowBin);
        var (highGain, highThd) = GainAndThd("Super Fuzz", 10f, Signals.CoherentSine(LowBin, Length, 0.03f), LowBin);
        output.WriteLine($"Super Fuzz at 30 mV: {lowGain:0.0} dB at drive 0, {highGain:0.0} dB at 10");
        Assert.True(highGain > lowGain + 3, "drive does not raise the gain");
    }

    [Fact]
    public void Hard_fuzz_squares_the_wave_and_drive_sets_how_hard() {
        var quiet = Signals.CoherentSine(LowBin, Length, 0.03f);
        var (_, low) = GainAndThd("Hard Fuzz", 0f, quiet, LowBin);
        var (_, high) = GainAndThd("Hard Fuzz", 10f, quiet, LowBin);
        var spectrum = Spectrum("Hard Fuzz", 10f, 10f, Signals.CoherentSine(LowBin, Length, 0.1f));
        var third = Analysis.ToDb(spectrum[3 * LowBin] / spectrum[LowBin]);
        // Even harmonics come from the bias and the 1 + 2 diodes while the wave is not yet a full square.
        var edge = Spectrum("Hard Fuzz", 5f, 10f, quiet);
        var second = Analysis.ToDb(edge[2 * LowBin] / edge[LowBin]);
        output.WriteLine(
            $"Hard Fuzz at 30 mV: THD {low:P0} at drive 0, {high:P0} at 10; H3 {third:0.0} dBc at 10, H2 {second:0.0} dBc at 5"
        );
        Assert.True(high > low + 0.1, "drive does not clip harder");
        Assert.True(third > -12, $"H3 {third:0.0} dBc: not close to a square (-9.5 dBc)");
        Assert.True(second > -30, $"H2 {second:0.0} dBc: the bias and the diodes make no even harmonics");
    }

    [Fact]
    public void Tape_is_flat_when_quiet_and_compresses_the_treble_first_when_hot() {
        const int Bass = 34; // about 200 Hz
        const int Treble = 853; // about 5 kHz

        static double Gain(int bin, float drive, float amplitude) {
            var input = Signals.CoherentSine(bin, Length, amplitude);
            return Analysis.ToDb(Spectrum("Tape Distortion", drive, 10f, input)[bin] / Fft.Magnitudes(input)[bin]);
        }

        // Record and playback equalisation cancel on small signals.
        var tilt = Gain(Treble, 0f, 0.01f) - Gain(Bass, 0f, 0.01f);
        // Hot, the pre-emphasised treble reaches the tape's saturation first: it loses more than the bass does.
        var bassLoss = Gain(Bass, 0f, 0.01f) + 30 - Gain(Bass, 10f, 0.01f * 8);
        var trebleLoss = Gain(Treble, 0f, 0.01f) + 30 - Gain(Treble, 10f, 0.01f * 8);
        output.WriteLine(
            $"Tape: quiet treble/bass {tilt:0.0} dB; hot compression bass {bassLoss:0.0} dB, treble {trebleLoss:0.0} dB"
        );
        Assert.InRange(tilt, -1.5, 1.5);
        Assert.True(bassLoss > 1, "the bass does not saturate at all");
        Assert.True(trebleLoss > bassLoss + 2, "the treble does not saturate first");
    }

    [Fact]
    public void Bit_crusher_quantises_quiet_notes_worst_and_its_rate_folds_images_below_the_note() {
        const int Note = 171; // about 1 kHz
        var transparent = Sinad(Spectrum("Bit Crusher", 0f, 10f, Signals.CoherentSine(Note, Length, 0.1f)), Note);
        var crushed = Sinad(Spectrum("Bit Crusher", 10f, 10f, Signals.CoherentSine(Note, Length, 0.2f)), Note);
        // At drive 7 (4.6 bits, held at 6.3 kHz): the quantisation error is fixed, so it is worse against a quieter
        // note. A 100 Hz note measured up to 3 kHz keeps the hold's images out of the band.
        const int Slow = 17;
        var loud = Sinad(Spectrum("Bit Crusher", 7f, 10f, Signals.CoherentSine(Slow, Length, 0.2f)), Slow, 512);
        var quiet = Sinad(Spectrum("Bit Crusher", 7f, 10f, Signals.CoherentSine(Slow, Length, 0.05f)), Slow, 512);
        output.WriteLine(
            $"Bit Crusher SINAD {transparent:0.0} dB at drive 0, {crushed:0.0} dB at 10; "
            + $"at 7 {loud:0.0} dB loud, {quiet:0.0} dB quiet"
        );
        Assert.True(transparent > 60, "drive 0 is not transparent");
        Assert.True(crushed < 30, "full drive does not crush");
        Assert.True(quiet < loud - 6, "a quiet note is not crushed harder (a clipper would do the opposite)");

        // Held at 1.5 kHz, a 1 kHz note images at 500 Hz: below the note, where no harmonic distortion can reach.
        var spectrum = Spectrum("Bit Crusher", 10f, 10f, Signals.CoherentSine(Note, Length, 0.2f));
        var image = Analysis.ToDb(Band(spectrum, 80, 90) / spectrum[Note]);
        var clean = Spectrum("Bit Crusher", 0f, 10f, Signals.CoherentSine(Note, Length, 0.2f));
        var none = Analysis.ToDb(Band(clean, 80, 90) / clean[Note]);
        output.WriteLine($"Bit Crusher image at 500 Hz {image:0.0} dBc at drive 10, {none:0.0} dBc at 0");
        Assert.True(image > -20, $"image {image:0.0} dBc");
        Assert.True(none < -80, $"image {none:0.0} dBc at drive 0");
    }

    [Fact]
    public void Sdd_preamp_has_a_wide_gain_range_and_a_treble_emphasis() {
        var low = Analysis.MagnitudeAt(Impulse("SDD Preamp", 0f, 5f), 500, Rate);
        var full = Impulse("SDD Preamp", 10f, 5f);
        var range = Analysis.ToDb(Analysis.MagnitudeAt(full, 500, Rate) / low);
        var mid = Impulse("SDD Preamp", 5f, 5f);
        var emphasis = Analysis.ToDb(Analysis.MagnitudeAt(mid, 8_000, Rate) / Analysis.MagnitudeAt(mid, 300, Rate));
        output.WriteLine($"SDD Preamp gain range {range:0.0} dB, 8 kHz over 300 Hz {emphasis:0.0} dB at drive 5");
        Assert.True(range > 30, $"gain range {range:0.0} dB");
        Assert.InRange(emphasis, 2, 8);
    }

    [Fact]
    public void Fet_preamp_is_asymmetric_and_its_stack_pots_work() {
        var spectrum = Spectrum("FET Preamp", 5f, 5f, Signals.CoherentSine(LowBin, Length, 0.1f));
        var second = Analysis.ToDb(spectrum[2 * LowBin] / spectrum[LowBin]);
        Assert.True(second > -35, $"H2 {second:0.0} dBc: a JFET stage is square-law");
        var thin = Impulse("FET Preamp", 0f, 5f, (DriveBlock.StackBass, 0f));
        var bassy = Impulse("FET Preamp", 0f, 5f, (DriveBlock.StackBass, 10f));
        var bass = Analysis.ToDb(Analysis.MagnitudeAt(bassy, 80, Rate) / Analysis.MagnitudeAt(thin, 80, Rate));
        output.WriteLine($"FET Preamp H2 {second:0.0} dBc, Bass Pot sweeps 80 Hz by {bass:0.0} dB");
        Assert.True(bass > 6, $"bass pot {bass:0.0} dB");
    }

    [Fact]
    public void Integral_pre_eq_moves_bass_and_middle() {
        static double At(double hz, int pot, float value) =>
            Analysis.ToDb(Analysis.MagnitudeAt(Impulse("Integral Pre", 5f, 5f, (pot, value)), hz, Rate));

        var bass = At(80, DriveBlock.StackBass, 10f) - At(80, DriveBlock.StackBass, 0f);
        var middle = At(1_000, DriveBlock.StackMiddle, 10f) - At(1_000, DriveBlock.StackMiddle, 0f);
        output.WriteLine($"Integral Pre: Bass Pot sweeps 80 Hz by {bass:0.0} dB, Middle Pot 1 kHz by {middle:0.0} dB");
        Assert.True(bass > 6, $"bass {bass:0.0} dB");
        Assert.True(middle > 6, $"middle {middle:0.0} dB");
    }

    [Fact]
    public void Micro_boost_gain_spans_about_26_db() {
        var range = GainRange("Micro Boost");
        output.WriteLine($"Micro Boost gain range {range:0.0} dB");
        Assert.InRange(range, 22, 28); // 1 + 56k / (2.7k + 500k) to 1 + 56k / 2.7k
    }

    [Fact]
    public void Mid_boost_humps_the_mids() {
        var response = Impulse("Mid Boost", 5f, 5f);
        var low = Analysis.ToDb(Analysis.MagnitudeAt(response, 100, Rate));
        var mid = Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate));
        var high = Analysis.ToDb(Analysis.MagnitudeAt(response, 8_000, Rate));
        output.WriteLine($"Mid Boost small signal: 100 Hz {low:0.0}, 1 kHz {mid:0.0}, 8 kHz {high:0.0} dB");
        Assert.True(mid > low + 6, "no bass cut under the hump");
        Assert.True(mid > high + 3, "no treble cut over the hump");
    }

    [Fact]
    public void Bosom_boost_gain_spans_about_27_db_and_its_treble_cuts_and_boosts() {
        var range = GainRange("Bosom Boost");
        var dark = Impulse("Bosom Boost", 5f, 0f);
        var bright = Impulse("Bosom Boost", 5f, 10f);
        var treble = Analysis.ToDb(Analysis.MagnitudeAt(bright, 6_000, Rate) / Analysis.MagnitudeAt(dark, 6_000, Rate));
        output.WriteLine($"Bosom Boost gain range {range:0.0} dB, treble sweep at 6 kHz {treble:0.0} dB");
        Assert.InRange(range, 22, 30);
        Assert.True(treble > 10, $"treble {treble:0.0} dB");
    }

    [Fact]
    public void Fas_boost_gain_spans_about_21_db_with_a_gentle_presence_lift() {
        var range = GainRange("FAS Boost");
        var response = Impulse("FAS Boost", 10f, 5f);
        var presence = Analysis.ToDb(
            Analysis.MagnitudeAt(response, 8_000, Rate) / Analysis.MagnitudeAt(response, 300, Rate)
        );
        output.WriteLine($"FAS Boost gain range {range:0.0} dB, presence {presence:+0.0} dB at 8 kHz");
        Assert.InRange(range, 17, 24);
        Assert.InRange(presence, 1, 6);
    }

    /// <summary>
    ///     The clean types stay clean on a picked note (<see cref="DriveCatalogueTests" />) but clip on their rails
    ///     when hit hard.
    /// </summary>
    [Theory]
    [MemberData(nameof(CleanTypes))]
    public void Clean_types_clip_on_their_rails_when_hit_hard(string name) {
        var (_, thd) = GainAndThd(name, 10f, Signals.CoherentSine(LowBin, Length, 1.5f), LowBin);
        output.WriteLine($"{name}: THD {thd:P1} at 1.5 V, full drive");
        Assert.True(thd > 0.03, $"THD {thd:P1}");
    }

    static double GainRange(string name) {
        var low = Analysis.MagnitudeAt(Impulse(name, 0f, 5f), 1_000, Rate);
        var high = Analysis.MagnitudeAt(Impulse(name, 10f, 5f), 1_000, Rate);
        return Analysis.ToDb(high / low);
    }

    static (double GainDb, double Thd) GainAndThd(string name, float drive, float[] input, int bin) {
        var rendered = Steady(name, drive, 10f, input);
        return (Analysis.ToDb(Analysis.Rms(rendered) / Analysis.Rms(input)), Analysis.Thd(rendered, bin));
    }

    /// <summary>Signal to noise and distortion: the fundamental's bins against everything else above DC.</summary>
    static double Sinad(double[] spectrum, int bin, int maxBin = int.MaxValue) {
        double signal = 0, rest = 0;
        for (var k = 3; k < Math.Min(spectrum.Length, maxBin); k++) {
            var energy = spectrum[k] * spectrum[k];
            if (Math.Abs(k - bin) <= 1) {
                signal += energy;
            } else {
                rest += energy;
            }
        }

        return 10 * Math.Log10(signal / rest);
    }

    static double Band(double[] spectrum, int from, int to) {
        double energy = 0;
        for (var k = from; k <= to; k++) {
            energy += spectrum[k] * spectrum[k];
        }

        return Math.Sqrt(energy);
    }

    static double[] Spectrum(string name, float drive, float tone, float[] period) =>
        Fft.Magnitudes(Steady(name, drive, tone, period));

    /// <summary>The third repetition of <paramref name="period" />, once the circuit has settled.</summary>
    static float[] Steady(string name, float drive, float tone, float[] period) {
        using var block = Create(name, drive, tone);
        return BlockRender.Mono(block, Signals.Concat(period, period, period), 64)[(2 * period.Length)..];
    }

    /// <summary>
    ///     A small-signal impulse response, scaled back to unit input: the render with the impulse less the same render
    ///     without it, so that a circuit still settling onto its bias (a biased stage charging its capacitors) cancels.
    /// </summary>
    static float[] Impulse(string name, float drive, float tone, params (int Index, float Value)[] extra) {
        const float Amplitude = 1e-3f;
        using var block = Create(name, drive, tone, extra);
        var rendered = BlockRender.Mono(
            block,
            Signals.Concat(new float[Length], Signals.Impulse(Length, Amplitude)),
            64
        );
        using var reference = Create(name, drive, tone, extra);
        var silent = BlockRender.Mono(reference, new float[2 * Length], 64);
        return [..rendered.Zip(silent, static (a, b) => (a - b) / Amplitude).Skip(Length)];
    }

    static DriveBlock Create(string name, float drive, float tone, params (int Index, float Value)[] extra) {
        var index = DriveCircuits.All.Select(static c => c.Name).ToList().IndexOf(name);
        Assert.True(index >= 0, $"no type {name}");
        var block = new DriveBlock();
        block.Params.SetTarget(DriveBlock.Type, DriveBlock.CircuitTypes + index);
        block.Params.SetTarget(DriveBlock.Drive, drive);
        block.Params.SetTarget(DriveBlock.Tone, tone);
        foreach (var (i, value) in extra) {
            block.Params.SetTarget(i, value);
        }

        block.Prepare(new(48_000, 64));
        return block;
    }
}
