using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Drive round 4, group D2 (docs/09 stream D2): each new distortion's controls do what the pedal's do. The generic
///     properties (gain range, symmetry, tone tilt, aliasing, decay, latency, allocation) are in
///     <see cref="DriveCatalogueTests" /> and <see cref="DriveStageTests" />.
/// </summary>
public class DriveRound4DistortionTests(ITestOutputHelper output) {
    const double Rate = 48_000;
    const int Length = 8_192;

    static readonly string[] Names = [
        "M-Zone Distortion", "Fat Rat", "Suhr Riot Ge", "Suhr Riot LED", "Suhr Riot LED/Si", "MCMLXXXI Drive",
        "Box O' Crunch", "Blackglass 7K", "Esoteric ACB", "Esoteric Bass RCB", "FAS LED-Drive", "Ruckus",
        "Shimmer Drive",
        "Tube Drive 3-Knob", "Tube Drive 4-Knob"
    ];

    public static TheoryData<string> Pedals => [..Names];

    [Fact]
    public void The_group_is_appended_in_order_and_every_name_is_unique() {
        var names = DriveCircuits.All.Select(static c => c.Name).ToList();
        var first = names.IndexOf(Names[0]);
        Assert.True(first >= 24, "the group must come after the original catalogue");
        Assert.Equal(Names, names.Skip(first).Take(Names.Length));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Pedals))]
    public void Drive_raises_the_distortion(string name) {
        var type = TypeOf(name);
        var low = Thd(type, 0f, 0.05f);
        var high = Thd(type, 10f, 0.05f);
        output.WriteLine($"{name}: THD {low:P1} at drive 0, {high:P1} at 10 (50 mV, 1 kHz)");
        Assert.True(high > low && high > 0.05, $"{low:P1} -> {high:P1}");
    }

    [Fact]
    public void Metal_zone_eq_boosts_and_cuts_its_bands_and_the_mid_sweeps() {
        var network = Circuit("M-Zone Distortion").Tone;
        Assert.Equal(ToneKind.ActiveEq, network.Kind);

        // Every band at noon is flat; each pot moves its own band by about ±14 dB.
        foreach (var hz in new[] { 60.0, 300, 1_000, 4_000, 10_000 }) {
            Assert.Equal(0, Response(network, hz), 0.3);
        }

        Assert.InRange(Response(network, 50, bass: 1), 10, 16);
        Assert.InRange(Response(network, 50, bass: 0), -16, -10);
        Assert.InRange(Response(network, 8_000, 1), 9, 16);
        Assert.InRange(Response(network, 8_000, 0), -16, -9);
        Assert.True(Response(network, 8_000, bass: 1) < 1, "the low band reaches the treble");

        // Middle at full on a 400 Hz centre peaks there, not at 3 kHz; swept to 3 kHz, the other way round.
        var low = (At400: Response(network, 400, middle: 1, midHz: 400),
            At3k: Response(network, 3_000, middle: 1, midHz: 400));
        var high = (At400: Response(network, 400, middle: 1, midHz: 3_000),
            At3k: Response(network, 3_000, middle: 1, midHz: 3_000));
        output.WriteLine($"mid at 400 Hz: {low}; at 3 kHz: {high}");
        Assert.True(low.At400 > 10 && low.At3k < 4, $"{low}");
        Assert.True(high.At3k > 10 && high.At400 < 4, $"{high}");
        Assert.InRange(Response(network, 1_000, middle: 0, midHz: 1_000), -16, -10);
    }

    [Fact]
    public void Metal_zone_middle_freq_and_pots_reach_the_block() {
        var type = TypeOf("M-Zone Distortion");
        Assert.Equal("Middle Freq", DriveBlock.Info.Params[DriveBlock.PedalMidFreq].TypeLabels![type]);
        Assert.Equal("Bass Pot", DriveBlock.Info.Params[DriveBlock.StackBass].TypeLabels![type]);
        Assert.Equal("Middle Pot", DriveBlock.Info.Params[DriveBlock.StackMiddle].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.PedalMidFreq].TypeLabels![TypeOf("Rodent Dist")]);

        // Full drive squares everything up, so compare spectra of a two-tone chord: the swept mid lifts its region.
        var chord = Signals.Concat(
            Mix(Signals.Sine(400, Rate, 19_200, 0.02f), Signals.Sine(2_500, Rate, 19_200, 0.02f))
        );
        var lowMid = Render(
            type,
            chord,
            (DriveBlock.Drive, 3f),
            (DriveBlock.StackMiddle, 10f),
            (DriveBlock.PedalMidFreq, 400f)
        );
        var highMid = Render(
            type,
            chord,
            (DriveBlock.Drive, 3f),
            (DriveBlock.StackMiddle, 10f),
            (DriveBlock.PedalMidFreq, 2_500f)
        );
        var ratioLow = Band(lowMid, 400) - Band(lowMid, 2_500);
        var ratioHigh = Band(highMid, 400) - Band(highMid, 2_500);
        output.WriteLine(
            $"400 Hz over 2.5 kHz: {ratioLow:0.0} dB with the mid at 400 Hz, {ratioHigh:0.0} dB at 2.5 kHz"
        );
        Assert.True(ratioLow - ratioHigh > 10, $"{ratioLow:0.0} against {ratioHigh:0.0} dB");
    }

    [Fact]
    public void Fat_rat_has_more_bass_into_its_clipping_than_the_rat() {
        // Quiet and low: the gain stage's own response, before anything clips.
        static double Bass(string name) =>
            SteadyDb(Render(TypeOf(name), Signals.Sine(80, Rate, 19_200, 0.001f), (DriveBlock.Drive, 6f)))
            - SteadyDb(Render(TypeOf(name), Signals.Sine(2_000, Rate, 19_200, 0.001f), (DriveBlock.Drive, 6f)));

        var fat = Bass("Fat Rat");
        var rat = Bass("Rodent Dist");
        output.WriteLine($"80 Hz against 2 kHz: Fat Rat {fat:0.0} dB, RAT {rat:0.0} dB");
        Assert.True(fat > rat + 6, $"{fat:0.0} against {rat:0.0} dB");
    }

    [Fact]
    public void Riot_voicings_clip_in_the_order_of_their_diodes() {
        // Germanium clips first and LEDs last; LED one way and silicon the other makes even harmonics.
        var ge = Thd(TypeOf("Suhr Riot Ge"), 2f, 0.02f);
        var led = Thd(TypeOf("Suhr Riot LED"), 2f, 0.02f);
        output.WriteLine($"THD at drive 2: Ge {ge:P1}, LED {led:P1}");
        Assert.True(ge > 2 * led, $"Ge {ge:P1}, LED {led:P1}");
        Assert.True(Circuit("Suhr Riot LED").IsSymmetric && Circuit("Suhr Riot Ge").IsSymmetric);
        Assert.False(Circuit("Suhr Riot LED/Si").IsSymmetric);
        Assert.True(Even(TypeOf("Suhr Riot LED/Si")) > Even(TypeOf("Suhr Riot LED")) + 40);
    }

    [Fact]
    public void Crunch_box_pushes_the_mids_into_its_clipping() {
        var type = TypeOf("Box O' Crunch");

        double Level(double hz) =>
            SteadyDb(Render(type, Signals.Sine(hz, Rate, 19_200, 0.0005f), (DriveBlock.Drive, 5f)));

        var mids = Level(1_000) - Level(100);
        output.WriteLine($"1 kHz over 100 Hz: {mids:0.0} dB");
        Assert.True(mids > 6, $"{mids:0.0} dB");
    }

    [Fact]
    public void Mcmlxxxi_drive_has_a_wide_gain_range() {
        var type = TypeOf("MCMLXXXI Drive");

        double Level(float drive) =>
            SteadyDb(Render(type, Signals.Sine(1_000, Rate, 19_200, 0.0005f), (DriveBlock.Drive, drive)));

        var range = Level(10f) - Level(0f);
        output.WriteLine($"small-signal gain range {range:0.0} dB");
        Assert.InRange(range, 25, 45);
    }

    [Fact]
    public void B7k_grunt_sets_the_bass_and_attack_the_treble_into_its_clipping() {
        var type = TypeOf("Blackglass 7K");
        Assert.Equal("Grunt", DriveBlock.Info.Params[DriveBlock.Grunt].TypeLabels![type]);
        Assert.Equal("Attack", DriveBlock.Info.Params[DriveBlock.Attack].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.Grunt].TypeLabels![TypeOf("Rodent Dist")]);

        // Small signals at low drive, where both switches act on what reaches the inverters.
        double Level(double hz, params (int, float)[] settings) =>
            SteadyDb(Render(type, Signals.Sine(hz, Rate, 19_200, 0.0005f), [(DriveBlock.Drive, 0f), ..settings]));

        var stock = Level(40);
        var fat = Level(40, (DriveBlock.Grunt, 1f));
        var raw = Level(40, (DriveBlock.Grunt, 2f));
        output.WriteLine($"40 Hz: stock {stock:0.0}, Fat {fat:0.0}, Raw {raw:0.0} dB");
        Assert.True(fat > stock + 1 && raw < stock - 3, $"{fat:0.0} / {stock:0.0} / {raw:0.0}");

        var flat = Level(5_000);
        var boost = Level(5_000, (DriveBlock.Attack, 1f));
        var cut = Level(5_000, (DriveBlock.Attack, 2f));
        output.WriteLine($"5 kHz: flat {flat:0.0}, Boost {boost:0.0}, Cut {cut:0.0} dB");
        Assert.True(boost > flat + 2 && cut < flat - 2, $"{boost:0.0} / {flat:0.0} / {cut:0.0}");
        Assert.Equal(Level(100), Level(100, (DriveBlock.Attack, 0f)), 1e-9);

        // The low-mid pot works the 500 Hz band.
        Assert.True(Level(500, (DriveBlock.StackMiddle, 10f)) > Level(500) + 3);
    }

    [Theory]
    [InlineData("Esoteric ACB", 80)]
    [InlineData("Esoteric Bass RCB", 50)]
    [InlineData("Tube Drive 4-Knob", 60)]
    public void Bass_pot_boosts_and_cuts_the_lows(string name, double hz) {
        var type = TypeOf(name);
        Assert.Equal("Bass Pot", DriveBlock.Info.Params[DriveBlock.StackBass].TypeLabels![type]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.StackMiddle].TypeLabels![type]);

        double Level(float bass) =>
            SteadyDb(
                Render(
                    type,
                    Signals.Sine(hz, Rate, 19_200, 0.0005f),
                    (DriveBlock.Drive, 0f),
                    (DriveBlock.StackBass, bass)
                )
            );

        var (cut, flat, boost) = (Level(0f), Level(5f), Level(10f));
        output.WriteLine($"{name} at {hz} Hz: {cut:0.0} / {flat:0.0} / {boost:0.0} dB");
        Assert.True(boost > flat + 6 && cut < flat - 6, $"{cut:0.0} / {flat:0.0} / {boost:0.0}");
    }

    [Fact]
    public void Bass_rcb_is_full_range_and_clean_at_low_gain() {
        var type = TypeOf("Esoteric Bass RCB");

        double Level(double hz) =>
            SteadyDb(Render(type, Signals.Sine(hz, Rate, 19_200, 0.01f), (DriveBlock.Drive, 2f)));

        Assert.InRange(Level(40) - Level(1_000), -3, 3);
        Assert.True(Thd(type, 2f, 0.05f) < 0.005);
    }

    [Fact]
    public void Acb_soft_clips_where_the_esoteric_boost_stays_clean() {
        var acb = Thd(TypeOf("Esoteric ACB"), 5f, 0.05f);
        var rcb = Thd(TypeOf("Esoteric Boost"), 5f, 0.05f);
        output.WriteLine($"THD at drive 5: ACB {acb:P1}, RC Booster {rcb:P1}");
        Assert.True(acb > 0.02 && acb > 10 * rcb, $"{acb:P1} against {rcb:P1}");
    }

    [Fact]
    public void Led_drive_clips_higher_than_with_silicon() {
        var type = TypeOf("FAS LED-Drive");
        var input = Signals.Sine(500, Rate, 19_200, 0.1f);
        var led = SteadyDb(Render(type, input, (DriveBlock.Drive, 10f)));
        var silicon = SteadyDb(Render(type, input, (DriveBlock.Drive, 10f), (DriveBlock.DiodeType, 1f)));
        output.WriteLine($"full drive: LEDs {led:0.0} dB, silicon {silicon:0.0} dB");
        Assert.True(led > silicon + 5, $"{led:0.0} against {silicon:0.0} dB");
    }

    [Fact]
    public void Ruckus_is_the_riot_core_with_silicon() {
        var ruckus = Circuit("Ruckus");
        var riot = Circuit("Suhr Riot Ge");
        Assert.Equal(riot.Stages[0], ruckus.Stages[0]);
        Assert.Equal(DiodeStacks.Pair(DiodeModel.Silicon), ruckus.Stages[1].Diodes);
        Assert.True(ruckus.IsSymmetric);
    }

    [Fact]
    public void Shimmer_drive_is_a_bright_clean_boost_at_low_drive() {
        var type = TypeOf("Shimmer Drive");

        double Level(double hz) =>
            SteadyDb(Render(type, Signals.Sine(hz, Rate, 19_200, 0.005f), (DriveBlock.Drive, 5f)));

        var tilt = Level(4_000) - Level(200);
        output.WriteLine($"4 kHz over 200 Hz: {tilt:0.0} dB; THD at drive 0 {Thd(type, 0f, 0.1f):P2}");
        Assert.True(tilt > 3, $"{tilt:0.0} dB");
        Assert.True(Thd(type, 0f, 0.1f) < 0.005);
    }

    [Theory]
    [InlineData("Tube Drive 3-Knob")]
    [InlineData("Tube Drive 4-Knob")]
    public void Tube_drivers_clip_asymmetrically_in_their_triode_and_recover(string name) {
        var type = TypeOf(name);
        var circuit = Circuit(name);
        Assert.Contains(circuit.Stages, static s => s.Kind == StageKind.Triode);
        Assert.False(circuit.IsSymmetric);
        Assert.True(DriveCircuits.TryGetOperatingPoint(circuit.Stages[1], out var point));
        // At rest the 12AX7 conducts with its grid a little below its cathode.
        var gridCathode = point[3];
        var plateCathode = point[4];
        output.WriteLine($"{name}: operating point V_gk {gridCathode:0.000} V, V_pk {plateCathode:0.0} V");
        Assert.InRange(gridCathode, -1.5, 0);
        Assert.InRange(plateCathode, 5, 38);
        Assert.True(Even(type) > -30, $"H2 {Even(type):0.0} dBc");
    }

    static DriveCircuit Circuit(string name) => DriveCircuits.All.Single(c => c.Name == name);

    static int TypeOf(string name) =>
        DriveBlock.CircuitTypes + DriveCircuits.All.ToList().FindIndex(c => c.Name == name);

    static float[] Render(int type, float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<DriveBlock>([(DriveBlock.Type, type), ..settings]);
        return BlockRender.Mono(block, input, 64);
    }

    static float[] Mix(float[] a, float[] b) => [..a.Select((v, i) => v + b[i])];

    static double SteadyDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));

    /// <summary>THD of a coherent 1 kHz-ish sine at <paramref name="amplitude" /> volts.</summary>
    static double Thd(int type, float drive, float amplitude) {
        var input = Signals.CoherentSine(171, Length, amplitude);
        var rendered = Render(type, Signals.Concat(input, input), (DriveBlock.Drive, drive), (DriveBlock.Tone, 5f));
        return Analysis.Thd(rendered.AsSpan(Length), 171);
    }

    /// <summary>The second harmonic relative to the fundamental at drive 6, for 0.2 V at about 190 Hz.</summary>
    static double Even(int type) {
        var input = Signals.CoherentSine(32, Length, 0.2f);
        var rendered = Render(type, Signals.Concat(input, input), (DriveBlock.Drive, 6f)).AsSpan(Length);
        var magnitudes = Fft.Magnitudes(rendered);
        return Analysis.ToDb(magnitudes[64] / magnitudes[32]);
    }

    /// <summary>
    ///     Energy around <paramref name="hz" /> (±5 %) in the second half of <paramref name="signal" />, in dB.
    /// </summary>
    static double Band(float[] signal, double hz) {
        var magnitudes = Fft.Magnitudes(signal.AsSpan(signal.Length - 16_384));
        double energy = 0;
        for (var k = (int)(hz * 0.95 * 16_384 / Rate); k <= (int)(hz * 1.05 * 16_384 / Rate); k++) {
            energy += magnitudes[k] * magnitudes[k];
        }

        return 10 * Math.Log10(energy);
    }

    /// <summary>The active EQ's response at <paramref name="hz" />, in dB, from its impulse response.</summary>
    static double Response(
        ToneNetwork network,
        double hz,
        double tone = 0.5,
        double bass = 0.5,
        double middle = 0.5,
        double midHz = 0
    ) {
        var stage = new ToneStage();
        stage.Configure(network, tone, Rate, bass, middle, midHz);
        var impulse = new float[32_768];
        for (var i = 0; i < impulse.Length; i++) {
            impulse[i] = (float)stage.Tick(i == 0 ? 1 : 0);
        }

        return Analysis.ToDb(Analysis.MagnitudeAt(impulse, hz, Rate));
    }
}
