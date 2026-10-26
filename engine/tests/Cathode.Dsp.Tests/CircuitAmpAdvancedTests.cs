using Cathode.Dsp.Amp;
using Cathode.Dsp.Amp.Models;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Circuit Amp's advanced page (docs/08 §4, stream PB): tube hardness, the generic switches, tone-stack type and
///     location, plate frequencies, cathode-follower and bias-excursion controls, suppressor diodes, master location
///     and cap, output mode, the Auto speaker load and the screen supply. Every one is stock at its default (the
///     all-model check is <see cref="AmpDefaultsTests" />); here each does what it says, analytically where it can.
/// </summary>
public class CircuitAmpAdvancedTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    /// <summary>Every advanced control, away from its default.</summary>
    static readonly (int Index, float Value)[] Advanced = [
        (CircuitAmpBlock.PowerTubeHardness, 10),
        (CircuitAmpBlock.PowerTubeHardness, 0),
        (CircuitAmpBlock.PreampHardness, 10),
        (CircuitAmpBlock.PreampHardness, 0),
        (CircuitAmpBlock.PresenceShift, 1),
        (CircuitAmpBlock.Fat, 1),
        (CircuitAmpBlock.InputCut, 1),
        (CircuitAmpBlock.HighTrebleKnob, 10),
        (CircuitAmpBlock.Overdrive, 10),
        (CircuitAmpBlock.StackType, 2),
        (CircuitAmpBlock.StackPlace, 1),
        (CircuitAmpBlock.StackPlace, 2),
        (CircuitAmpBlock.StackPlace, 3),
        (CircuitAmpBlock.Triode1Plate, 400),
        (CircuitAmpBlock.Triode2Plate, 25),
        (CircuitAmpBlock.FollowerCompression, 400),
        (CircuitAmpBlock.FollowerHarmonics, 50),
        (CircuitAmpBlock.PreampExcursion, 0),
        (CircuitAmpBlock.InverterExcursion, 400),
        (CircuitAmpBlock.PowerTubeExcursion, 0),
        (CircuitAmpBlock.ExcursionTime, 400),
        (CircuitAmpBlock.RecoveryTime, 25),
        (CircuitAmpBlock.MasterExcursion, 0),
        (CircuitAmpBlock.Suppressor, 1),
        (CircuitAmpBlock.MasterLocation, 1),
        (CircuitAmpBlock.MasterCap, 400),
        (CircuitAmpBlock.OutputMode, 1),
        (CircuitAmpBlock.SpeakerCurve, CircuitAmpBlock.SpeakerAuto),
        (CircuitAmpBlock.ScreenFrequency, 25),
        (CircuitAmpBlock.ScreenQ, 3)
    ];

    public static TheoryData<int> Types => [..Enumerable.Range(0, AmpDesign.All.Count)];

    [Theory]
    [MemberData(nameof(Types))]
    public void Advanced_controls_are_bounded_on_every_model(int type) {
        var input = CircuitAmpBlockTests.Chord(9_600, 0.5f);
        var stock = Render(type, input, (CircuitAmpBlock.Gain, 8f));
        foreach (var (index, value) in Advanced) {
            var changed = Render(type, input, (CircuitAmpBlock.Gain, 8f), (index, value));
            var peak = Analysis.ToDb(Analysis.Peak(changed));
            var id = CircuitAmpBlock.Info.Params[index].Id;
            output.WriteLine(
                $"{AmpDesign.All[type].Name,-24} {id,-11} {value,5}: {Analysis.NullResidualDb(stock, changed) - Analysis.ToDb(Analysis.Rms(stock)),7:0.0} dB, peak {peak:0.0}"
            );
            Assert.True(Analysis.AllFinite(changed), $"{id} = {value}");
            Assert.True(peak < 6, $"{id} = {value}: peak {peak:0.0} dBFS");
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void Hardened_pentode_tables_match_the_hardened_law(double hardness) {
        foreach (var model in new[] { PentodeModel.EL34, PentodeModel.SixL6GC, PentodeModel.EL84 }) {
            var tables = PentodeTables.For(model);
            var law = model.Hardened(hardness);
            foreach (var (vg1, vg2, vp) in new[] {
                         (-30.0, 350.0, 400.0), (-45, 400, 60), (-5, 300, 20), (-60, 420, 500)
                     }) {
                var factor = tables.ScreenFactor(vg2, model.Kp * hardness);
                var fast = tables.PlateCurrent(factor, vg1, vg2, vp, model.Kp * hardness, hardness, out _, out _);
                var exact = KorenPentode.Plate(law, vg1, vg2, vp);
                Assert.Equal(exact, fast, Math.Max(1e-7 * exact, 1e-12));
            }
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(4.0)]
    public void Hardened_triode_matches_the_hardened_law(double hardness) {
        var model = TriodeModel.Ecc83;
        var fast = new FastTriode(model, hardness);
        var exact = new Triode(model.Hardened(hardness));
        Span<double> currents = stackalloc double[2];
        Span<double> expected = stackalloc double[2];
        Span<double> jacobian = stackalloc double[4];
        foreach (var (vgk, vpk) in new[] { (-1.0, 200.0), (-3.0, 150.0), (-0.2, 80.0), (-5.0, 300.0) }) {
            fast.Evaluate([vgk, vpk], currents, jacobian, 2);
            exact.Evaluate([vgk, vpk], expected, jacobian, 2);
            Assert.Equal(expected[1], currents[1], Math.Max(1e-7 * expected[1], 1e-12));
        }
    }

    [Fact]
    public void Hardness_sharpens_the_cut_off_knee() {
        // Deep in cut-off a harder tube draws less; well above the knee it draws the same.
        var soft = PentodeModel.EL34.Hardened(0.5);
        var hard = PentodeModel.EL34.Hardened(2);
        Assert.True(KorenPentode.Plate(hard, -50, 400, 400) < KorenPentode.Plate(soft, -50, 400, 400));
        var on = KorenPentode.Plate(PentodeModel.EL34, 0, 400, 400);
        Assert.Equal(on, KorenPentode.Plate(hard, 0, 400, 400), 0.02 * on);
    }

    [Fact]
    public void Power_tube_hardness_changes_a_driven_power_amp() {
        var type = Index(Plexi.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 0.5f);
        var stock = Render(type, input, (CircuitAmpBlock.Gain, 8f));
        var hard = Render(type, input, (CircuitAmpBlock.Gain, 8f), (CircuitAmpBlock.PowerTubeHardness, 10f));
        Assert.True(Residual(stock, hard) > -40, $"{Residual(stock, hard):0.0} dB");
    }

    [Fact]
    public void Input_cut_takes_the_lows_off() {
        var type = Index(Blackface.Design);
        var low = Signals.Sine(60, Rate, 19_200, 0.01f);
        var stock = SteadyDb(Render(type, low, (CircuitAmpBlock.Gain, 2f)));
        var cut = SteadyDb(Render(type, low, (CircuitAmpBlock.Gain, 2f), (CircuitAmpBlock.InputCut, 1f)));
        Assert.True(cut < stock - 4, $"{cut:0.0} against {stock:0.0} dB");
    }

    [Theory]
    [InlineData(10f, 8_000, 6)]
    [InlineData(0f, 8_000, -6)]
    [InlineData(10f, 200, 0)]
    public void High_treble_is_a_shelf_before_the_power_amp(float knob, double frequency, double expectedDb) {
        var type = Index(Blackface.Design);
        var tone = Signals.Sine(frequency, Rate, 19_200, 0.005f);
        var stock = SteadyDb(Render(type, tone, (CircuitAmpBlock.Gain, 2f)));
        var shelved = SteadyDb(Render(type, tone, (CircuitAmpBlock.Gain, 2f), (CircuitAmpBlock.HighTrebleKnob, knob)));
        output.WriteLine($"{frequency} Hz: {shelved - stock:0.0} dB");
        if (expectedDb == 0) {
            Assert.Equal(0, shelved - stock, 1.5);
        } else {
            Assert.True(Math.Sign(shelved - stock) == Math.Sign(expectedDb) && Math.Abs(shelved - stock) > 4);
        }
    }

    [Fact]
    public void High_treble_shelf_reaches_its_gain() {
        var shelf = new HighTreble();
        shelf.Design(1, 96_000);
        var impulse = Signals.Impulse(16_384);
        var response = impulse.Select(x => (float)shelf.Tick(x)).ToArray();
        Assert.Equal(HighTreble.RangeDb, Analysis.ToDb(Analysis.MagnitudeAt(response, 30_000, 96_000)), 1.0);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 50, 96_000)), 0.2);
    }

    [Fact]
    public void Overdrive_adds_gain_into_the_last_stage() {
        var type = Index(Blackface.Design);
        var quiet = Signals.Sine(330, Rate, 19_200, 0.002f);
        var stock = SteadyDb(Render(type, quiet, (CircuitAmpBlock.Gain, 2f)));
        var driven = SteadyDb(Render(type, quiet, (CircuitAmpBlock.Gain, 2f), (CircuitAmpBlock.Overdrive, 5f)));
        // 10 dB more swing into the last stage of a clean amp: about 10 dB more out.
        Assert.Equal(10, driven - stock, 2.5);
    }

    [Fact]
    public void Generic_switches_hide_where_the_model_has_its_own() {
        var labels = CircuitAmpBlock.Info.Params.Single(static p => p.Id == "fatsw").TypeLabels!;
        for (var type = 0; type < AmpDesign.All.Count; type++) {
            Assert.Equal(AmpDesign.All[type].Panel.HasSwitch("Fat"), labels[type] == string.Empty);
        }
    }

    [Fact]
    public void High_treble_hides_and_does_nothing_on_a_model_with_its_own() {
        var labels = CircuitAmpBlock.Info.Params.Single(static p => p.Id == "hitreble").TypeLabels!;
        var type = Index(JazzChorus120.Design);
        Assert.Equal("Hi Treble", JazzChorus120.Design.Panel.Control1);
        Assert.Equal(string.Empty, labels[type]);
        Assert.Equal("High Treble", labels[Index(Blackface.Design)]);

        var input = CircuitAmpBlockTests.Chord(9_600, 0.1f);
        Assert.Equal(Render(type, input), Render(type, input, (CircuitAmpBlock.HighTrebleKnob, 10f)));
    }

    [Fact]
    public void Fat_and_presence_shift_are_audible() {
        var type = Index(Plexi.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 0.1f);
        var stock = Render(type, input);
        Assert.True(Residual(stock, Render(type, input, (CircuitAmpBlock.Fat, 1f))) > -40);
        Assert.True(Residual(stock, Render(type, input, (CircuitAmpBlock.PresenceShift, 1f))) > -60);
    }

    [Fact]
    public void Stack_type_substitution_changes_the_stack_and_its_own_stack_is_stock() {
        var type = Index(Plexi.Design);
        var own = Plexi.Design.Preamp.OfType<ToneStackStageDesign>().First().Stack;
        var ownIndex = AmpStacks.All.ToList().IndexOf(own) + 1;
        var other = AmpStacks.All.ToList().IndexOf(ToneStackDesign.Fender) + 1;
        var input = CircuitAmpBlockTests.Chord(9_600, 0.1f);
        var stock = Render(type, input);
        Assert.Equal(stock, Render(type, input, (CircuitAmpBlock.StackType, ownIndex)));
        Assert.True(Residual(stock, Render(type, input, (CircuitAmpBlock.StackType, other))) > -30);
    }

    [Theory]
    [InlineData("Plexi", 1)] // the cathode follower's stack ahead of V1
    [InlineData("Plexi", 2)] // after V1
    [InlineData("Blackface", 3)] // V1A's stack after V2A, into the mixer
    public void Stack_location_moves_the_stack_and_keeps_the_amp_quiet_at_rest(string model, int location) {
        var type = Index(model == "Plexi" ? Plexi.Design : Blackface.Design);
        using var block = TestBlocks.Create<CircuitAmpBlock>(
            (CircuitAmpBlock.Type, type),
            (CircuitAmpBlock.StackPlace, location)
        );
        // Each stage keeps its operating point, so silence in is silence out (no thump, no offset).
        var silence = BlockRender.Mono(block, new float[9_600], 64);
        Assert.True(Analysis.Peak(silence.AsSpan(4_800)) < 1e-3, $"{Analysis.Peak(silence.AsSpan(4_800))}");
        var input = CircuitAmpBlockTests.Chord(9_600, 0.1f);
        var moved = Render(type, input, (CircuitAmpBlock.StackPlace, location));
        Assert.True(Residual(Render(type, input), moved) > -40);
    }

    [Fact]
    public void Plate_frequency_moves_the_cold_clippers_treble_cut() {
        var type = Index(Slo100.Design);
        var tone = Signals.Sine(4_000, Rate, 19_200, 0.002f);
        var stock = SteadyDb(Render(type, tone, (CircuitAmpBlock.Gain, 3f)));
        var t1 = AmpFeatures.Has(Slo100.Design, "t1plate")
            ? CircuitAmpBlock.Triode1Plate
            : CircuitAmpBlock.Triode2Plate;
        var opened = SteadyDb(Render(type, tone, (CircuitAmpBlock.Gain, 3f), (t1, 400f)));
        Assert.True(opened > stock + 0.5, $"{opened:0.0} against {stock:0.0} dB");
        Assert.False(AmpFeatures.Has(Blackface.Design, "t2plate"));
    }

    [Fact]
    public void Cathode_follower_controls_change_a_driven_follower() {
        var type = Index(Plexi.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 0.5f);
        var stock = Render(type, input, (CircuitAmpBlock.Gain, 10f));
        var compression = Render(type, input, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.FollowerCompression, 0f));
        var harmonics = Render(type, input, (CircuitAmpBlock.Gain, 10f), (CircuitAmpBlock.FollowerHarmonics, 200f));
        Assert.True(Residual(stock, compression) > -60, $"{Residual(stock, compression):0.0}");
        Assert.True(Residual(stock, harmonics) > -50, $"{Residual(stock, harmonics):0.0}");
        Assert.False(AmpFeatures.Has(Blackface.Design, "cfcomp"));
    }

    [Fact]
    public void Power_tube_bias_excursion_off_stops_grid_current() {
        var type = Index(Plexi.Design);
        using var block = TestBlocks.Create<CircuitAmpBlock>(
            (CircuitAmpBlock.Type, type),
            (CircuitAmpBlock.Gain, 10f),
            (CircuitAmpBlock.PowerTubeExcursion, 0f)
        );
        var input = CircuitAmpBlockTests.Chord(9_600, 1f);
        var most = 0.0;
        for (var offset = 0; offset < input.Length; offset += 64) {
            var chunk = input.AsSpan(offset, 64).ToArray();
            BlockRender.Mono(block, chunk, 64);
            most = Math.Max(most, block.Channel.Power.GridCurrents);
        }

        Assert.Equal(0, most);
        using var stock = TestBlocks.Create<CircuitAmpBlock>((CircuitAmpBlock.Type, type), (CircuitAmpBlock.Gain, 10f));
        var conducted = 0.0;
        for (var offset = 0; offset < input.Length; offset += 64) {
            BlockRender.Mono(stock, [..input.AsSpan(offset, 64)], 64);
            conducted = Math.Max(conducted, stock.Channel.Power.GridCurrents);
        }

        Assert.True(conducted > 0);
    }

    [Fact]
    public void Suppressor_diodes_keep_the_plates_above_the_cathode() {
        var type = Index(Plexi.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 1f);

        double Lowest(bool diodes) {
            using var block = TestBlocks.Create<CircuitAmpBlock>(
                (CircuitAmpBlock.Type, type),
                (CircuitAmpBlock.Gain, 10f),
                (CircuitAmpBlock.Bias, 0f),
                (CircuitAmpBlock.TransformerHigh, 25f),
                (CircuitAmpBlock.Suppressor, diodes ? 1f : 0f)
            );
            var lowest = double.MaxValue;
            for (var offset = 0; offset < input.Length; offset += 16) {
                BlockRender.Mono(block, [..input.AsSpan(offset, 16)], 16);
                var (a, b) = block.Channel.Power.PlateVoltages;
                lowest = Math.Min(lowest, Math.Min(a, b) - block.Channel.Power.CathodeVoltage);
            }

            return lowest;
        }

        var without = Lowest(false);
        var with = Lowest(true);
        output.WriteLine($"lowest plate: {without:0.0} V without, {with:0.0} V with the diodes");
        Assert.True(with > -5, $"{with:0.0} V");
        Assert.True(with >= without - 1e-6);
    }

    [Fact]
    public void Post_inverter_master_drives_the_inverter_harder() {
        var type = Index(Index(JCM2203.Design) >= 0 ? JCM2203.Design : Plexi.Design);
        Assert.True(AmpFeatures.Has(AmpDesign.All[type], "mvloc"));
        var input = CircuitAmpBlockTests.Chord(9_600, 0.5f);
        (int, float)[] quietMaster = [(CircuitAmpBlock.Gain, 8f), (CircuitAmpBlock.Master, 3f)];
        var pre = Render(type, input, quietMaster);
        var post = Render(type, input, [..quietMaster, (CircuitAmpBlock.MasterLocation, 1f)]);
        Assert.True(Residual(pre, post) > -40, $"{Residual(pre, post):0.0}");
        Assert.True(Analysis.AllFinite(post));
    }

    [Fact]
    public void Solid_state_output_mode_leaves_the_speaker_stages_to_the_cab() {
        var type = Index(Blackface.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 0.3f);
        var plain = Render(type, input);
        var real = Render(
            type,
            input,
            (CircuitAmpBlock.OutputMode, 1f),
            (CircuitAmpBlock.SpeakerDrive, 10f),
            (CircuitAmpBlock.Thump, 10f),
            (CircuitAmpBlock.SpeakerCompression, 10f)
        );
        Assert.Equal(plain, real);
        Assert.NotEqual(plain, Render(type, input, (CircuitAmpBlock.SpeakerDrive, 10f), (CircuitAmpBlock.Thump, 10f)));
    }

    [Fact]
    public void Auto_speaker_follows_the_cabs_enclosure_and_speaker() {
        var closed = CabCatalogue.Enclosures.ToList().FindIndex(static e => e.Name == "4x12 Closed Back");
        var open = CabCatalogue.Enclosures.ToList().FindIndex(static e => e.Name == "1x12 Open Back");
        var speaker = CabCatalogue.Speakers[0];
        var closedLoad = AutoSpeaker.For(closed, 0)!;
        var openLoad = AutoSpeaker.For(open, 0)!;
        // An open back resonates at the speaker's own Fs; a closed box's air raises it.
        Assert.Equal(speaker.Fs, openLoad.Resonance, 1e-9);
        Assert.True(closedLoad.Resonance > speaker.Fs * 1.05);
        Assert.Null(AutoSpeaker.For(-1, -1));

        var type = Index(Blackface.Design);
        var input = CircuitAmpBlockTests.Chord(9_600, 0.3f);
        var stock = Render(type, input);
        // No Cab to follow: stock.
        Assert.Equal(stock, Render(type, input, (CircuitAmpBlock.SpeakerCurve, CircuitAmpBlock.SpeakerAuto)));

        using var cab = new CabBlock();
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Source), CabBlock.SourceDynaCab);
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Cab), closed);
        cab.Params.SetTarget(CabBlock.SlotParam(0, CabBlock.Mute), 0);
        Assert.Equal((closed, (int)cab.Params.Current(CabBlock.SlotParam(0, CabBlock.Speaker))), cab.DynaCab);
        using var amp = TestBlocks.Create<CircuitAmpBlock>(
            (CircuitAmpBlock.Type, type),
            (CircuitAmpBlock.SpeakerCurve, CircuitAmpBlock.SpeakerAuto)
        );
        amp.FollowCab(cab);
        var followed = BlockRender.Stereo(amp, input, input, static _ => 64).Left;
        Assert.True(Residual(stock, followed) > -60, $"{Residual(stock, followed):0.0}");
    }

    [Fact]
    public void Screen_q_rings_the_screen_node() {
        var type = Index(Plexi.Design);

        (double Idle, double Highest) Recovery(float q) {
            using var block = TestBlocks.Create<CircuitAmpBlock>(
                (CircuitAmpBlock.Type, type),
                (CircuitAmpBlock.Gain, 10f),
                (CircuitAmpBlock.ScreenQ, q)
            );
            var rest = block.Channel.Power.ScreenSupply;
            // A loud burst draws the screens down, then silence: the node recovers.
            BlockRender.Mono(block, CircuitAmpBlockTests.Chord(9_600, 1f), 64);
            var highest = double.MinValue;
            var silence = new float[64];
            for (var i = 0; i < 300; i++) {
                BlockRender.Mono(block, silence, 64);
                highest = Math.Max(highest, block.Channel.Power.ScreenSupply);
            }

            return (rest, highest);
        }

        var (rest, damped) = Recovery(0);
        var (_, ringing) = Recovery(4);
        output.WriteLine(
            $"screen at rest {rest:0.00} V; highest on recovery {damped:0.00} V (Q 0), {ringing:0.00} V (Q 4)"
        );
        Assert.True(damped <= rest + 0.05, $"{damped:0.00} V against {rest:0.00} V");
        Assert.True(ringing > rest + 0.1, $"{ringing:0.00} V against {rest:0.00} V");
    }

    static int Index(AmpDesign design) => AmpDesign.All.ToList().IndexOf(design);

    static float[] Render(int type, float[] input, params (int Index, float Value)[] settings) {
        using var block = TestBlocks.Create<CircuitAmpBlock>([(CircuitAmpBlock.Type, type), ..settings]);
        return BlockRender.Stereo(block, input, input, static _ => 64).Left;
    }

    static double Residual(float[] reference, float[] changed) =>
        Analysis.NullResidualDb(reference, changed) - Analysis.ToDb(Analysis.Rms(reference));

    static double SteadyDb(float[] signal) => Analysis.ToDb(Analysis.Rms(signal.AsSpan(signal.Length / 2)));
}
