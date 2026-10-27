using Cathode.Dsp.Blocks;
using Cathode.Testing;
using System.Numerics;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Drive round 4, group D1 (docs/09): ten overdrives from the guide's list, and the per-pedal knobs of PB1 (the
///     Timmy's Bass and Treble, the Zendrive's Voice, the OCD's HP/LP). Each pedal's Drive spans the gain its netlist
///     gives, its Tone brightens, and it clips the way its diodes sit (across the feedback, which keeps the clean
///     signal in the output, or to ground, which clamps it). The generic checks run in
///     <see cref="DriveCatalogueTests" />; defaults stay stock (<see cref="AmpDefaultsTests" />).
/// </summary>
public class DriveRound4OverdriveTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Names = [
        "Full OD", "OD-One Overdrive", "OD 250", "Sonic Drive", "Eternal Love", "Jam Ray", "Horizon Precision Drive",
        "Tone of Kings", "Hoodoo Drive", "Valve Screamer VS9"
    ];

    /// <summary>
    ///     Per pedal, from its netlist: the drive stage's feedback resistance and ground-leg resistance at Drive 0 and
    ///     10 (the pots at their ends), its feedback and ground-leg capacitors, whether it is symmetric, and whether
    ///     its diodes go to ground.
    /// </summary>
    public static TheoryData<string, Stage, bool, bool> Pedals =>
        new() {
            { "Full OD", new(47e3, 547e3, 2.2e3, 2.2e3, 47e-12, 220e-9), true, false },
            { "OD-One Overdrive", new(10e3, 1.01e6, 4.7e3, 4.7e3, 47e-12, 47e-9), false, false },
            { "OD 250", new(1e6, 1e6, 504.7e3, 4.7e3, 0, 100e-9), true, true },
            { "Sonic Drive", new(51e3, 551e3, 4.7e3, 4.7e3, 51e-12, 47e-9), true, true },
            { "Eternal Love", new(51e3, 551e3, 4.7e3, 4.7e3, 51e-12, 100e-9), true, true },
            { "Jam Ray", new(10e3, 510e3, 4.7e3, 4.7e3, 47e-12, 100e-9), true, false },
            { "Horizon Precision Drive", new(1e3, 501e3, 1e3, 1e3, 100e-12, 1e-6), true, true },
            { "Tone of Kings", new(10e3, 110e3, 6.8e3, 6.8e3, 1e-9, 1e-6), true, false },
            { "Hoodoo Drive", new(51e3, 551e3, 4.7e3, 4.7e3, 51e-12, 100e-9), true, false },
            { "Valve Screamer VS9", new(51e3, 551e3, 4.7e3, 4.7e3, 51e-12, 47e-9), true, false }
        };

    [Fact]
    public void Round_four_overdrives_are_appended_in_order_after_the_earlier_catalogue() {
        var names = DriveCircuits.All.Select(static c => c.Name).ToList();
        var first = names.IndexOf("NKT Face Fuzz") + 1; // the last circuit before round 4
        Assert.Equal(Names, names.Skip(first).Take(Names.Length));
        Assert.Equal(DriveBlock.CircuitTypes + first, TypeOf("Full OD"));
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Drive_spans_the_gain_of_the_pedals_netlist(
        string name,
        Stage stage,
        bool symmetric,
        bool toGround
    ) {
        _ = (symmetric, toGround);

        // Quiet enough that no diode conducts: the ratio is the drive stage's gain swing.
        double Level(float drive) =>
            SteadyDb(Render(name, Signals.Sine(500, Rate, 19_200, 3e-4f), (DriveBlock.Drive, drive)));

        var swing = Level(10f) - Level(0f);
        var expected = stage.SwingDb(500);
        output.WriteLine($"{name}: Drive swings {swing:0.0} dB (netlist {expected:0.0} dB)");
        Assert.InRange(swing, expected - 1.5, expected + 1.5);
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Tone_brightens_steadily(string name, Stage stage, bool symmetric, bool toGround) {
        _ = (stage, symmetric, toGround);

        double Treble(float tone) {
            var at4k = SteadyDb(
                Render(name, Signals.Sine(4_000, Rate, 19_200, 1e-3f), (DriveBlock.Drive, 0f), (DriveBlock.Tone, tone))
            );
            var at300 = SteadyDb(
                Render(name, Signals.Sine(300, Rate, 19_200, 1e-3f), (DriveBlock.Drive, 0f), (DriveBlock.Tone, tone))
            );
            return at4k - at300;
        }

        var (dark, mid, bright) = (Treble(0f), Treble(5f), Treble(10f));
        output.WriteLine($"{name}: 4 kHz against 300 Hz {dark:0.0} / {mid:0.0} / {bright:0.0} dB at Tone 0 / 5 / 10");
        Assert.True(mid > dark + 0.5, $"Tone 5 not brighter than 0: {mid:0.0} against {dark:0.0} dB");
        Assert.True(bright > mid + 0.5, $"Tone 10 not brighter than 5: {bright:0.0} against {mid:0.0} dB");
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Clipping_follows_where_the_diodes_sit(string name, Stage stage, bool symmetric, bool toGround) {
        _ = stage;
        var circuit = DriveCircuits.All[TypeOf(name) - DriveBlock.CircuitTypes];
        Assert.Equal(symmetric, circuit.IsSymmetric);

        // Full drive, 24 dB more input: diodes to ground clamp the output; diodes across a non-inverting stage's
        // feedback leave its input in the output, so it keeps growing.
        double Level(float amplitude) =>
            SteadyDb(Render(name, Signals.Sine(500, Rate, 19_200, amplitude), (DriveBlock.Drive, 10f)));

        var growth = Level(0.48f) - Level(0.03f);
        output.WriteLine($"{name}: +24 dB in gives {growth:0.0} dB out at full drive");
        if (toGround) {
            Assert.True(growth < 3, $"{growth:0.0} dB: not clamped");
        } else {
            Assert.True(growth > 3, $"{growth:0.0} dB: the clean signal is not in the output");
        }
    }

    [Fact]
    public void Od_one_clips_asymmetrically_and_od_250_harder_than_a_screamer() {
        var input = Signals.Sine(200, Rate, 19_200, 0.2f);
        var odOne = Render("OD-One Overdrive", input, (DriveBlock.Drive, 6f));
        var magnitudes = Fft.Magnitudes(odOne.AsSpan(odOne.Length - 16_384));
        var bin = (int)Math.Round(200 * 16_384 / Rate);
        // The 1 + 2 diodes: a strong second harmonic (Hann window spreads each line over neighbouring bins).
        var second = Analysis.ToDb(Peak(magnitudes, 2 * bin) / Peak(magnitudes, bin));
        Assert.True(second > -35, $"H2 {second:0.0} dBc");

        // Diodes to ground after a rail-to-rail stage square the wave more than a TS's soft feedback clipping.
        var crest250 = Crest(Render("OD 250", input, (DriveBlock.Drive, 10f)));
        var crestTs = Crest(Render("TS808 OD", input, (DriveBlock.Drive, 10f)));
        output.WriteLine($"crest factor: OD 250 {crest250:0.00}, TS808 {crestTs:0.00}");
        Assert.True(crest250 < crestTs, $"OD 250 crest {crest250:0.00} against TS808 {crestTs:0.00}");
    }

    [Fact]
    public void Transparent_bass_restores_the_low_end_and_its_tone_is_the_treble() {
        var type = TypeOf("Transparent OD");
        Assert.Equal("Bass", DriveBlock.Info.Params[DriveBlock.PedalBass].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.PedalBass].TypeLabels![TypeOf("Zen OD")]);
        Assert.Equal("Treble", DriveBlock.Info.Params[DriveBlock.Tone].TypeLabels![type]);
        Assert.Equal("Tone", DriveBlock.Info.Params[DriveBlock.Tone].TypeLabels![TypeOf("Zen OD")]);

        // Drive at noon, quiet: the gain stage's response. Bass up lets the ground leg pass the lows.
        static double Level(double frequency, float bass) =>
            SteadyDb(
                Render(
                    "Transparent OD",
                    Signals.Sine(frequency, Rate, 19_200, 3e-4f),
                    (DriveBlock.Drive, 5f),
                    (DriveBlock.PedalBass, bass)
                )
            );

        var stock = Level(100, 0f);
        var half = Level(100, 5f);
        var full = Level(100, 10f);
        output.WriteLine($"100 Hz: {stock:0.0} / {half:0.0} / {full:0.0} dB at Bass 0 / 5 / 10");
        Assert.True(half > stock + 3 && full > half + 2, "Bass does not restore the lows");
        Assert.True(Math.Abs(Level(3_000, 10f) - Level(3_000, 0f)) < 1, "Bass moves the treble");
        Assert.Equal(stock, Level(100, 0f), 1e-9);
    }

    [Fact]
    public void Zen_voice_turned_down_lowers_the_gain_and_the_bass_corner() {
        var type = TypeOf("Zen OD");
        Assert.Equal("Voice", DriveBlock.Info.Params[DriveBlock.Voice].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.Voice].TypeLabels![TypeOf("TS808 OD")]);

        static double Level(double frequency, float voice) =>
            SteadyDb(
                Render(
                    "Zen OD",
                    Signals.Sine(frequency, Rate, 19_200, 3e-4f),
                    (DriveBlock.Drive, 5f),
                    (DriveBlock.Voice, voice)
                )
            );

        var midStock = Level(1_000, 10f);
        var midDown = Level(1_000, 0f);
        var tiltStock = Level(150, 10f) - midStock;
        var tiltDown = Level(150, 0f) - midDown;
        output.WriteLine(
            $"1 kHz {midStock:0.0} -> {midDown:0.0} dB; 150 Hz against 1 kHz {tiltStock:0.0} -> {tiltDown:0.0} dB"
        );
        Assert.True(midDown < midStock - 3, "Voice down does not lower the gain");
        Assert.True(tiltDown > tiltStock + 2, "Voice down does not lower the bass corner");
    }

    [Fact]
    public void Ocd_high_peak_opens_the_top_and_drives_the_diodes_harder() {
        var type = TypeOf("Obsessive OD");
        Assert.Equal("HP/LP", DriveBlock.Info.Params[DriveBlock.HighPeak].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.HighPeak].TypeLabels![TypeOf("TS808 OD")]);

        static double Level(double frequency, float peak) =>
            SteadyDb(
                Render(
                    "Obsessive OD",
                    Signals.Sine(frequency, Rate, 19_200, 3e-4f),
                    (DriveBlock.Drive, 0f),
                    (DriveBlock.Tone, 10f),
                    (DriveBlock.HighPeak, peak)
                )
            );

        var top = Level(8_000, 1f) - Level(8_000, 0f);
        var low = Level(200, 1f) - Level(200, 0f);
        output.WriteLine($"HP against LP: {top:0.0} dB at 8 kHz, {low:0.0} dB at 200 Hz");
        Assert.True(top > 3, $"{top:0.0} dB at 8 kHz");
        Assert.True(Math.Abs(low) < 0.5, $"{low:0.0} dB at 200 Hz");
    }

    /// <summary>A non-inverting drive stage: feedback R in parallel with C over a series R + C ground leg.</summary>
    public sealed record Stage(
        double Feedback0,
        double Feedback10,
        double Ground0,
        double Ground10,
        double FeedbackCapacitance,
        double GroundCapacitance
    ) {
        /// <summary>The gain at Drive 10 over Drive 0 at <paramref name="frequency" />, in dB.</summary>
        public double SwingDb(double frequency) =>
            20 * Math.Log10(Gain(Feedback10, Ground10, frequency) / Gain(Feedback0, Ground0, frequency));

        double Gain(double feedback, double ground, double frequency) {
            var s = new Complex(0, 2 * Math.PI * frequency);
            var zf = feedback / (1 + s * feedback * FeedbackCapacitance);
            var zg = ground + 1 / (s * GroundCapacitance);
            return Complex.Abs(1 + zf / zg);
        }
    }

    static double Peak(double[] magnitudes, int bin) =>
        Math.Max(magnitudes[bin - 1], Math.Max(magnitudes[bin], magnitudes[bin + 1]));

    static double Crest(float[] signal) {
        var tail = signal.AsSpan(signal.Length / 2);
        return Analysis.Peak(tail) / Analysis.Rms(tail);
    }

    static int TypeOf(string name) =>
        DriveBlock.CircuitTypes + DriveCircuits.All.ToList().FindIndex(c => c.Name == name);

    static float[] Render(string name, float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<DriveBlock>([(DriveBlock.Type, TypeOf(name)), ..settings]);
        return BlockRender.Mono(block, input, 64);
    }

    static double SteadyDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));
}
