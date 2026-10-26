using Cathode.Dsp.Blocks;
using Cathode.Dsp.Modulation;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The parameters, types and helpers added for parity with the Blocks Guide (docs/08-blocks-parity.md).</summary>
public class BlocksParityTests {
    const double Rate = 48_000;

    public static TheoryData<LfoShape> Shapes => [..Enum.GetValues<LfoShape>()];

    // LFO waveforms and tempo divisions

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_lfo_shape_stays_within_plus_and_minus_one(LfoShape shape) {
        foreach (var duty in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }) {
            var lfo = new Lfo();
            for (var i = 0; i < 4_800; i++) {
                Assert.InRange(lfo.Value(shape, 0.3, duty), -1.0001f, 1.0001f);
                lfo.Advance(7, Rate);
            }
        }
    }

    [Fact]
    public void Triangle_duty_skews_it_from_a_falling_to_a_rising_saw() {
        Assert.Equal(1, Lfo.Shape(LfoShape.Triangle, 0.5), 1e-12);
        Assert.Equal(-1, Lfo.Shape(LfoShape.Triangle, 0), 1e-12);
        Assert.Equal(Lfo.Shape(LfoShape.SawDown, 0.3), Lfo.Shape(LfoShape.Triangle, 0.3, 0), 1e-12);
        Assert.Equal(Lfo.Shape(LfoShape.SawUp, 0.3), Lfo.Shape(LfoShape.Triangle, 0.3, 1), 1e-12);
        Assert.Equal(1, Lfo.Shape(LfoShape.Trapezoid, 0.4), 1e-12); // clipped: it dwells at the top
        Assert.Equal(-Lfo.Shape(LfoShape.Log, 0.2), Lfo.Shape(LfoShape.Exponential, 0.2), 1e-12);
    }

    [Fact]
    public void Square_duty_sets_the_share_of_the_cycle_spent_high() {
        var high = Enumerable.Range(0, 1_000).Count(static i => Lfo.Shape(LfoShape.Square, i / 1_000.0, 0.25) > 0);

        Assert.InRange(high, 240, 260);
    }

    [Fact]
    public void Random_holds_a_level_for_each_cycle_and_replays_after_a_reset() {
        var lfo = new Lfo();
        var first = new List<float>();
        for (var i = 0; i < 4_000; i++) {
            first.Add(lfo.Value(LfoShape.Random));
            lfo.Advance(48, Rate); // a cycle every 1,000 samples
        }

        lfo.Reset();
        var second = Enumerable.Range(0, 4_000)
            .Select(_ => {
                    var v = lfo.Value(LfoShape.Random);
                    lfo.Advance(48, Rate);
                    return v;
                }
            );

        Assert.Equal(first, second);
        Assert.Equal(first[10], first[900]);
        Assert.Equal(4, first.Distinct().Count());
    }

    [Fact]
    public void Tempo_divisions_keep_their_first_indices_and_convert_to_time_and_rate() {
        Assert.Equal(
            ["Off", "1/1", "1/2", "1/4", "1/4.", "1/4T", "1/8", "1/8.", "1/8T", "1/16"],
            TempoDivisions.Names[..10]
        );
        Assert.Equal(TempoDivisions.Names.Length, TempoDivisions.Beats.Length);
        Assert.Equal(500, TempoDivisions.Milliseconds(3, 120), 1e-9);
        Assert.Equal(2, TempoDivisions.Rate(3, 120, 5), 1e-9);
        Assert.Equal(5, TempoDivisions.Rate(0, 120, 5), 1e-9);
    }

    // Common output stage

    [Fact]
    public void Balance_turns_the_far_side_down_and_leaves_the_centre_at_unity() {
        using var block =
            TestBlocks.Create<FilterBlock>((FilterBlock.Type, FilterBlock.Null), (FilterBlock.Balance, -50f));
        var input = Signals.Step(256);

        var (left, right) = BlockRender.Stereo(block, input, input, static _ => 64);

        Assert.Equal(1f, left[^1], 1e-6f);
        Assert.Equal(0.5f, right[^1], 1e-6f);
    }

    // Tremolo/Panner

    [Fact]
    public void Harmonic_trem_without_depth_sums_its_bands_back_exactly() {
        using var block = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Type, TremoloBlock.HarmonicTrem),
            (TremoloBlock.Depth, 0f)
        );
        var input = Signals.Noise(4_800, 1);

        var output = BlockRender.Mono(block, input, 64);

        Assert.True(Analysis.NullResidualDb(input, output) < -120);
    }

    [Fact]
    public void Harmonic_trem_moves_the_lows_and_highs_in_antiphase() {
        using var block = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Type, TremoloBlock.HarmonicTrem),
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 2f)
        );
        var low = Signals.Sine(80, Rate, 24_000, 0.5f);
        var high = Signals.Sine(5_000, Rate, 24_000, 0.5f);

        using var other = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Type, TremoloBlock.HarmonicTrem),
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 2f)
        );
        var lowOut = BlockRender.Mono(block, low, 64);
        var highOut = BlockRender.Mono(other, high, 64);

        // At the LFO's peak (an eighth of a second in) the lows are up and the highs down; at its trough the reverse.
        Assert.True(Analysis.Peak(lowOut.AsSpan(5_500, 1_000)) > 0.4f);
        Assert.True(Analysis.Peak(highOut.AsSpan(5_500, 1_000)) < 0.15f);
        Assert.True(Analysis.Peak(lowOut.AsSpan(17_500, 1_000)) < 0.15f);
        Assert.True(Analysis.Peak(highOut.AsSpan(17_500, 1_000)) > 0.4f);
    }

    [Fact]
    public void Optical_trem_never_boosts_and_pulses_lopsidedly() {
        using var block = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Type, TremoloBlock.OpticalTrem),
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 4f)
        );
        var input = Signals.Step(48_000);

        var output = BlockRender.Mono(block, input, 64);

        Assert.All(output, static s => Assert.InRange(s, 0f, 1.0001f));
        Assert.True(output.Min() < 0.3f, "the trough should be deep");
    }

    [Fact]
    public void Tempo_locks_the_tremolo_rate() {
        using var synced = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Tempo, 3f),
            (TremoloBlock.Rate, 7f),
            (TremoloBlock.Depth, 100f)
        );
        using var free = TestBlocks.Create<TremoloBlock>((TremoloBlock.Rate, 2f), (TremoloBlock.Depth, 100f));
        var input = Signals.Step(24_000);

        Assert.True(
            Analysis.NullResidualDb(BlockRender.Mono(free, input, 64), BlockRender.Mono(synced, input, 64)) < -100
        );
    }

    // Chorus and flanger

    [Fact]
    public void Chorus_phase_reverse_inverts_only_the_wet_signal() {
        using var normal = TestBlocks.Create<ChorusBlock>((ChorusBlock.Mix, 100f));
        using var reversed = TestBlocks.Create<ChorusBlock>((ChorusBlock.Mix, 100f), (ChorusBlock.PhaseReverse, 3f));
        var input = Signals.Noise(4_800, 2);

        var a = BlockRender.Mono(normal, input, 64);
        var b = BlockRender.Mono(reversed, input, 64);

        Assert.True(Analysis.NullResidualDb(a, b.Select(static s => -s).ToArray()) < -140);
    }

    [Fact]
    public void Chorus_voices_thicken_the_sound() {
        using var one = TestBlocks.Create<ChorusBlock>((ChorusBlock.Mix, 100f));
        using var four = TestBlocks.Create<ChorusBlock>((ChorusBlock.Mix, 100f), (ChorusBlock.Voices, 3f));
        var input = Signals.Noise(9_600, 3);

        Assert.True(Analysis.NullResidualDb(BlockRender.Mono(one, input, 64), BlockRender.Mono(four, input, 64)) > -20);
    }

    [Fact]
    public void Flanger_high_cut_darkens_the_wet_signal() {
        using var bright = TestBlocks.Create<FlangerBlock>((FlangerBlock.Mix, 100f), (FlangerBlock.Feedback, 0f));
        using var dark = TestBlocks.Create<FlangerBlock>(
            (FlangerBlock.Mix, 100f),
            (FlangerBlock.Feedback, 0f),
            (FlangerBlock.HighCut, 1_000f)
        );
        var input = Signals.Sine(8_000, Rate, 9_600, 0.5f);

        var brightRms = Analysis.Rms(BlockRender.Mono(bright, input, 64).AsSpan(4_800));
        var ratio = Analysis.Rms(BlockRender.Mono(dark, input, 64).AsSpan(4_800)) / brightRms;

        // First order, as in the guide: three octaves above the cut is about 18 dB down.
        Assert.InRange(Analysis.ToDb(ratio), -20, -16);
    }

    // Filter

    [Fact]
    public void Filter_tilt_cuts_the_lows_and_lifts_the_highs_by_half_the_gain() {
        using var block = TestBlocks.Create<FilterBlock>((FilterBlock.Type, FilterBlock.Tilt), (FilterBlock.Gain, 12f));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        Assert.Equal(-6, Analysis.ToDb(Analysis.MagnitudeAt(response, 30, Rate)), 0.5);
        Assert.Equal(6, Analysis.ToDb(Analysis.MagnitudeAt(response, 18_000, Rate)), 0.5);
    }

    [Fact]
    public void Filter_all_pass_is_flat_and_fourth_order_low_pass_is_steeper() {
        using var allPass = TestBlocks.Create<FilterBlock>((FilterBlock.Type, FilterBlock.AllPass));
        using var second = TestBlocks.Create<FilterBlock>();
        using var fourth = TestBlocks.Create<FilterBlock>((FilterBlock.Order, 1f));

        var flat = BlockRender.Mono(allPass, Signals.Impulse(16_384), 64);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(flat, 300, Rate)), 0.05);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(flat, 5_000, Rate)), 0.05);

        var at4k2 = Analysis.ToDb(
            Analysis.MagnitudeAt(BlockRender.Mono(second, Signals.Impulse(16_384), 64), 4_000, Rate)
        );
        var at4k4 = Analysis.ToDb(
            Analysis.MagnitudeAt(BlockRender.Mono(fourth, Signals.Impulse(16_384), 64), 4_000, Rate)
        );
        Assert.Equal(2 * at4k2, at4k4, 0.1);
    }

    [Fact]
    public void Feed_forward_comb_notches_at_odd_multiples_of_half_the_inverse_time() {
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Type, FilterBlock.FeedForwardComb),
            (FilterBlock.CombTime, 1f),
            (FilterBlock.Depth, 100f)
        );

        var response = BlockRender.Mono(block, Signals.Impulse(8_192), 64);

        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 500, Rate)) < -40);
        Assert.Equal(6.02, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.05);
    }

    [Fact]
    public void Filter_pans_swap_the_sides_and_phase_invert_flips_them() {
        using var block = TestBlocks.Create<FilterBlock>(
            (FilterBlock.Type, FilterBlock.Null),
            (FilterBlock.PanLeft, 100f),
            (FilterBlock.PanRight, -100f),
            (FilterBlock.PhaseInvert, 1f)
        );
        var left = Signals.Step(256, 0.5f);
        var right = Signals.Step(256, 0.25f);

        var (outLeft, outRight) = BlockRender.Stereo(block, left, right, static _ => 64);

        Assert.Equal(-0.25f, outLeft[^1], 1e-6f);
        Assert.Equal(0.5f, outRight[^1], 1e-6f);
    }

    // Volume/Pan

    [Theory]
    [InlineData(0, 0.5f)]
    [InlineData(4, 0.1f)] // Log 10A: 10 % at half rotation
    [InlineData(6, 0.5f)] // the S-taper crosses the middle at half
    public void Volume_tapers_meet_their_midpoints(int taper, float expected) {
        Assert.Equal(expected, VolumePanBlock.TaperGain(taper, 0.5f), 1e-5f);
        Assert.Equal(1f, VolumePanBlock.TaperGain(taper, 1f), 1e-6f);
        Assert.Equal(0f, VolumePanBlock.TaperGain(taper, 0f), 1e-6f);
    }

    [Fact]
    public void Auto_swell_fades_each_note_in_over_the_attack() {
        using var block = TestBlocks.Create<VolumePanBlock>(
            (VolumePanBlock.Type, VolumePanBlock.AutoSwell),
            (VolumePanBlock.Attack, 100f),
            (VolumePanBlock.Threshold, -40f)
        );
        var input = Signals.Concat(new float[1_000], Signals.Step(19_000, 0.5f));

        var output = BlockRender.Mono(block, input, 64);

        Assert.True(MathF.Abs(output[1_010]) < 0.01f, "the attack starts from silence");
        Assert.InRange(output[1_000 + 2_400], 0.1f, 0.4f); // half-way through the 100 ms swell
        Assert.Equal(0.5f, output[^1], 1e-5f);
    }

    [Fact]
    public void Input_select_feeds_one_side_to_both() {
        using var block = TestBlocks.Create<VolumePanBlock>((VolumePanBlock.InputSelect, 2f));
        var (left, right) = BlockRender.Stereo(
            block,
            Signals.Step(128, 0.5f),
            Signals.Step(128, 0.25f),
            static _ => 64
        );

        Assert.Equal(0.25f, left[^1]);
        Assert.Equal(0.25f, right[^1]);
    }

    // Dynamics

    [Fact]
    public void Gate_type_drops_quiet_signals_by_the_attenuation() {
        using var block = TestBlocks.Create<GateBlock>(
            (GateBlock.Type, GateBlock.Gate),
            (GateBlock.Attenuation, 40f),
            (GateBlock.Threshold, -20f),
            (GateBlock.Hold, 0f),
            (GateBlock.Release, 5f)
        );
        var input = Signals.Sine(1_000, Rate, 48_000, 0.01f); // -40 dBFS

        var output = BlockRender.Mono(block, input, 64);

        Assert.Equal(-80, Analysis.ToDb(Analysis.Peak(output.AsSpan(24_000))), 1);
    }

    [Fact]
    public void Sustainer_raises_quiet_signals_below_the_threshold() {
        using var block = TestBlocks.Create<CompressorBlock>(
            (CompressorBlock.Type, CompressorBlock.Sustainer),
            (CompressorBlock.Threshold, -20f),
            (CompressorBlock.Ratio, 2f),
            (CompressorBlock.Knee, 0f)
        );
        var input = Signals.Sine(1_000, Rate, 48_000, 0.01f); // -40 dBFS peak, -43 dBFS RMS

        var output = BlockRender.Mono(block, input, 64);

        // Halfway up to the threshold: about +11.5 dB on the RMS level.
        Assert.Equal(
            11.5,
            Analysis.ToDb(Analysis.Rms(output.AsSpan(24_000)) / Analysis.Rms(input.AsSpan(24_000))),
            0.5
        );
    }

    // Delay

    [Fact]
    public void Ping_pong_echoes_alternate_sides() {
        using var block = TestBlocks.Create<DelayBlock>(
            (DelayBlock.Type, DelayBlock.PingPong),
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 50f),
            (DelayBlock.Mix, 50f)
        );

        var (left, right) = BlockRender.Stereo(block, Signals.Impulse(2_000), Signals.Impulse(2_000), static _ => 64);

        Assert.Equal(1f, left[480], 1e-5f); // the mono input (1) enters the left line
        Assert.Equal(0f, right[480], 1e-6f);
        Assert.Equal(0.5f, right[960], 1e-5f);
        Assert.Equal(0f, left[960], 1e-6f);
    }

    [Fact]
    public void Ratio_shortens_the_right_time() {
        using var block = TestBlocks.Create<DelayBlock>(
            (DelayBlock.Time, 10f),
            (DelayBlock.Ratio, 50f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f)
        );

        var (left, right) = BlockRender.Stereo(block, Signals.Impulse(1_000), Signals.Impulse(1_000), static _ => 64);

        Assert.Equal(1f, left[480], 1e-5f);
        Assert.Equal(1f, right[240], 1e-5f);
    }

    [Fact]
    public void Hold_closes_the_input_and_loops_the_repeats() {
        using var block = TestBlocks.Create<DelayBlock>(
            (DelayBlock.Time, 10f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f)
        );
        BlockRender.Mono(block, Signals.Impulse(256), 64); // the impulse is in the line
        block.Params.SetTarget(DelayBlock.Hold, 1f);

        var held = BlockRender.Mono(block, Signals.Step(4_800), 64); // new input is ignored

        Assert.Equal(1f, held[480 - 256], 1e-5f);
        Assert.Equal(1f, held[960 - 256], 1e-5f);
        Assert.Equal(1f, held[4_800 - 256 - 480 * 2], 1e-5f);
    }

    [Fact]
    public void Ducker_lowers_the_echoes_while_the_input_is_loud() {
        using var ducked = TestBlocks.Create<DelayBlock>(
            (DelayBlock.Time, 50f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 50f),
            (DelayBlock.DuckAttenuation, 20f),
            (DelayBlock.DuckThreshold, -30f)
        );
        var input = Signals.Sine(500, Rate, 24_000, 0.5f);

        var output = BlockRender.Mono(ducked, input, 64);

        // Dry and wet are both at full level at 50 %; with the wet 20 dB down, the sum stays near the dry level.
        Assert.True(Analysis.Peak(output.AsSpan(12_000)) < 0.5f * (1 + 0.15f));
    }

    [Fact]
    public void Bit_reduction_quantises_the_repeats() {
        using var block = TestBlocks.Create<DelayBlock>(
            (DelayBlock.Time, 1f),
            (DelayBlock.Feedback, 0f),
            (DelayBlock.Mix, 100f),
            (DelayBlock.BitReduction, 20f) // 4 bits: steps of 1/8
        );

        var output = BlockRender.Mono(block, Signals.Sine(100, Rate, 4_800, 0.9f), 64);

        Assert.All(output, static s => Assert.Equal(0, s * 8 - MathF.Round(s * 8), 1e-4f));
    }

    // Reverb, EQ, ring modulator, wah, enhancer

    [Fact]
    public void Reverb_input_gain_feeds_the_tank_but_not_the_dry() {
        using var block = TestBlocks.Create<ReverbBlock>((ReverbBlock.InputGain, 0f), (ReverbBlock.Mix, 100f));

        var output = BlockRender.Mono(block, Signals.Noise(4_800, 4), 64);

        Assert.All(output, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Reverb_hold_keeps_the_tail_ringing() {
        using var block = TestBlocks.Create<ReverbBlock>((ReverbBlock.Decay, 0.5f), (ReverbBlock.Mix, 100f));
        BlockRender.Mono(block, Signals.Noise(4_800, 5), 64);
        block.Params.SetTarget(ReverbBlock.Hold, 1f);

        var held = BlockRender.Mono(block, new float[96_000], 64);

        // A 0.5 s decay would be 120 dB down after two seconds; held, the tail is still there.
        Assert.True(Analysis.ToDb(Analysis.Rms(held.AsSpan(86_400))) > -40);
    }

    [Fact]
    public void Peq_blocking_band_one_is_a_high_pass() {
        using var block = TestBlocks.Create<ParametricEqBlock>(
            (ParametricEqBlock.LowType, 2f),
            (ParametricEqBlock.Frequency(1), 1_000f)
        );

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 100, Rate)) < -35);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 10_000, Rate)), 0.2);
    }

    [Fact]
    public void Variable_q_widens_gentle_graphic_eq_settings() {
        using var constant = TestBlocks.Create<GraphicEqBlock>((5, 3f));
        using var variable = TestBlocks.Create<GraphicEqBlock>((5, 3f), (GraphicEqBlock.QMode, 1f));

        var a = BlockRender.Mono(constant, Signals.Impulse(16_384), 64);
        var b = BlockRender.Mono(variable, Signals.Impulse(16_384), 64);

        Assert.Equal(3, Analysis.ToDb(Analysis.MagnitudeAt(b, 1_000, Rate)), 0.05);
        Assert.True(Analysis.MagnitudeAt(b, 2_000, Rate) > Analysis.MagnitudeAt(a, 2_000, Rate) * 1.05);
    }

    [Fact]
    public void Ring_modulator_multiplier_scales_the_carrier() {
        using var doubled = TestBlocks.Create<RingModulatorBlock>(
            (RingModulatorBlock.Frequency, 220f),
            (RingModulatorBlock.Multiplier, 2f)
        );
        using var direct = TestBlocks.Create<RingModulatorBlock>((RingModulatorBlock.Frequency, 440f));
        var input = Signals.Noise(4_800, 6);

        Assert.True(
            Analysis.NullResidualDb(BlockRender.Mono(direct, input, 64), BlockRender.Mono(doubled, input, 64)) < -100
        );
    }

    [Fact]
    public void Wah_log_taper_bunches_the_sweep_toward_the_heel() {
        Assert.Equal(25f, WahBlock.Tapered(1, 50f), 1e-4f);
        Assert.Equal(75f, WahBlock.Tapered(2, 50f), 1e-4f);
        Assert.Equal(50f, WahBlock.Tapered(0, 50f), 1e-4f);
    }

    [Fact]
    public void Enhancer_pans_can_fold_the_image_to_mono() {
        using var block = TestBlocks.Create<EnhancerBlock>((EnhancerBlock.PanLeft, 0f), (EnhancerBlock.PanRight, 0f));
        var (left, right) = BlockRender.Stereo(block, Signals.Noise(4_800, 7), Signals.Noise(4_800, 8), static _ => 64);

        Assert.Equal(left, right);
    }

    // Mixer

    [Fact]
    public void Mixer_rows_take_their_gain_and_balance_and_mono_sums_the_output() {
        using var mixer = TestBlocks.Create<MixerBlock>(
            (MixerBlock.Gain(0), 50f),
            (MixerBlock.RowBalance(1), -100f),
            (MixerBlock.Mode, 1f)
        );
        var one = new StereoBuffer(Signals.Step(64), Signals.Step(64));
        var two = new StereoBuffer(Signals.Step(64), Signals.Step(64));
        var sum = new StereoBuffer(new float[64], new float[64]);

        mixer.MixInput(0, one, sum);
        mixer.MixInput(1, two, sum);
        Assert.Equal(1.5f, sum.Left[^1], 1e-6f); // 0.5 + 1
        Assert.Equal(0.5f, sum.Right[^1], 1e-6f); // 0.5 + 0

        mixer.Process(sum, new(Rate, 120, 0));
        Assert.Equal(1f, sum.Left[^1], 1e-6f);
        Assert.Equal(1f, sum.Right[^1], 1e-6f);
    }
}
