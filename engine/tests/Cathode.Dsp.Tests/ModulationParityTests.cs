using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;
using Cathode.Testing;
using static Cathode.Dsp.Tests.PitchBlockTests;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The modulation, pitch and synthesis parameters added for parity with the Blocks Guide (docs/08-blocks-parity.md,
///     stream P3): analytic checks of the new DSP.
/// </summary>
public class ModulationParityTests {
    const double Rate = 48_000;

    // Shared LFO conditioning

    [Fact]
    public void Quantize_steps_the_lfo_into_even_levels() {
        Assert.Equal(-1f, LfoConditioner.Quantize(-0.9f, 3));
        Assert.Equal(0f, LfoConditioner.Quantize(0.2f, 3));
        Assert.Equal(1f, LfoConditioner.Quantize(0.8f, 3));
        Assert.Equal(0.37f, LfoConditioner.Quantize(0.37f, 0));
        Assert.Equal(
            4,
            Enumerable.Range(0, 1_000).Select(static i => LfoConditioner.Quantize(i / 500f - 1, 4)).Distinct().Count()
        );
    }

    [Fact]
    public void Auto_depth_holds_depth_times_rate_constant_above_one_hertz() {
        Assert.Equal(1f, LfoConditioner.AutoDepth(2, 0.5));
        Assert.Equal(0.25f, LfoConditioner.AutoDepth(2, 4), 1e-6f);
        Assert.Equal(0.5f, LfoConditioner.AutoDepth(1, 4), 1e-6f);
        Assert.Equal(1f, LfoConditioner.AutoDepth(0, 8));
    }

    [Fact]
    public void Hicut_smooths_a_square_lfo() {
        var conditioner = new LfoConditioner();
        conditioner.Design(Rate, 2f, 0);
        var lfo = new Lfo();
        var largestStep = 0f;
        var previous = 0f;
        for (var i = 0; i < 48_000; i++) {
            var (left, right) = (Lfo.Shape(LfoShape.SawUp, lfo.Phase) > 0 ? 1f : -1f, 0f);
            conditioner.Tick(ref left, ref right);
            if (i > 0) {
                largestStep = Math.Max(largestStep, Math.Abs(left - previous));
            }

            previous = left;
            lfo.Advance(2, Rate);
        }

        Assert.True(largestStep < 0.001f, $"step {largestStep}");
    }

    // Chorus

    [Fact]
    public void Chorus_width_moves_the_right_delay_to_one_millisecond() {
        using var chorus = TestBlocks.Create<ChorusBlock>(
            (ChorusBlock.Depth, 0f),
            (ChorusBlock.Mix, 100f),
            (ChorusBlock.Delay, 10f),
            (ChorusBlock.Width, 100f)
        );

        var (left, right) = BlockRender.Stereo(chorus, Signals.Impulse(2_000), Signals.Impulse(2_000), static _ => 64);

        Assert.Equal(480, Analysis.PeakIndex(left));
        Assert.Equal(48, Analysis.PeakIndex(right));
    }

    [Theory]
    [InlineData(1, 0.7f)]
    [InlineData(3, 0.3f)]
    public void Dimension_cancels_the_common_part_of_the_two_wets(int mode, float expected) {
        using var chorus = TestBlocks.Create<ChorusBlock>((ChorusBlock.Dimension, mode), (ChorusBlock.Mix, 100f));

        var (left, right) = BlockRender.Stereo(chorus, Signals.Step(9_600), Signals.Step(9_600), static _ => 64);

        Assert.Equal(expected, left[^1], 1e-4f);
        Assert.Equal(expected, right[^1], 1e-4f);
    }

    [Fact]
    public void Chorus_takes_eight_voices_and_stays_at_equal_power() {
        using var chorus = TestBlocks.Create<ChorusBlock>((ChorusBlock.Voices, 7f), (ChorusBlock.Mix, 100f));
        var noise = Signals.Noise(48_000, 4, 0.3f);

        var output = BlockRender.Mono(chorus, noise, 64);

        Assert.InRange(Analysis.Rms(output.AsSpan(4_800)) / Analysis.Rms(noise.AsSpan(4_800)), 0.7, 1.4);
    }

    // Flanger

    [Fact]
    public void Exponential_vco_response_sweeps_by_ratio() {
        Assert.Equal(MathF.Sqrt(2 * 8), FlangerBlock.SweptDelay(2, 6, 0.5f, true, 1f), 1e-4f);
        Assert.Equal(5f, FlangerBlock.SweptDelay(2, 6, 0.5f, false, 1f), 1e-6f);
        Assert.Equal(2f + 6f * 0.25f, FlangerBlock.SweptDelay(2, 6, 0.5f, false, 2f), 1e-5f);
    }

    [Fact]
    public void Thru_zero_cancels_where_the_wet_meets_the_delayed_dry() {
        using var flanger = TestBlocks.Create<FlangerBlock>(
            (FlangerBlock.Depth, 0f),
            (FlangerBlock.Feedback, 0f),
            (FlangerBlock.Delay, 2f),
            (FlangerBlock.ThruZero, FlangerBlock.ThruZeroManual),
            (FlangerBlock.DryDelay, 2f),
            (FlangerBlock.PhaseReverse, 3f)
        );
        var noise = Signals.Noise(9_600, 3, 0.5f);

        var output = BlockRender.Mono(flanger, noise, 64);

        Assert.True(Analysis.Rms(output.AsSpan(4_800)) < 1e-4, $"residual {Analysis.Rms(output.AsSpan(4_800))}");
    }

    [Fact]
    public void Min_time_lifts_the_sweep() {
        using var flanger = TestBlocks.Create<FlangerBlock>(
            (FlangerBlock.Depth, 0f),
            (FlangerBlock.Feedback, 0f),
            (FlangerBlock.Mix, 100f),
            (FlangerBlock.Delay, 1f),
            (FlangerBlock.MinTime, 5f)
        );

        var output = BlockRender.Mono(flanger, Signals.Impulse(1_000), 64);

        Assert.Equal(240, Analysis.PeakIndex(output));
    }

    // Phaser

    [Fact]
    public void Vcr_laws_map_the_lfo_position() {
        Assert.Equal(550f, PhaserBlock.VcrFrequency(PhaserBlock.VcrLinear, 100, 1_000, 0.5f), 1e-3f);
        Assert.Equal(325f, PhaserBlock.VcrFrequency(PhaserBlock.VcrJfet, 100, 1_000, 0.5f), 1e-3f);
        Assert.Equal(
            MathF.Sqrt(100_000),
            PhaserBlock.VcrFrequency(PhaserBlock.VcrExponential, 100, 1_000, 0.5f),
            1e-2f
        );
    }

    [Fact]
    public void Tone_tilts_the_wet_signal_up_to_six_db() {
        using var flat = TestBlocks.Create<PhaserBlock>((PhaserBlock.Depth, 0f), (PhaserBlock.Mix, 100f));
        using var bright = TestBlocks.Create<PhaserBlock>(
            (PhaserBlock.Depth, 0f),
            (PhaserBlock.Mix, 100f),
            (PhaserBlock.Tone, 100f)
        );
        var tone = Signals.Sine(12_000, Rate, 9_600, 0.25f);

        var ratio = Analysis.Rms(BlockRender.Mono(bright, tone, 64).AsSpan(4_800))
            / Analysis.Rms(BlockRender.Mono(flat, tone, 64).AsSpan(4_800));

        Assert.InRange(Analysis.ToDb(ratio), 5, 6.1);
    }

    [Fact]
    public void Barberpole_keeps_an_allpass_level_while_it_sweeps() {
        using var phaser = TestBlocks.Create<PhaserBlock>(
            (PhaserBlock.Type, PhaserBlock.Barberpole),
            (PhaserBlock.Mix, 100f),
            (PhaserBlock.Rate, 2f)
        );
        var tone = Signals.Sine(700, Rate, 48_000, 0.5f);

        var output = BlockRender.Mono(phaser, tone, 64);

        Assert.True(Analysis.Peak(output) <= 0.51, $"peak {Analysis.Peak(output)}");
        Assert.True(Analysis.Rms(output.AsSpan(4_800)) > 0.15, $"rms {Analysis.Rms(output.AsSpan(4_800))}");
    }

    // Filter

    [Fact]
    public void Shelf_two_overshoots_its_gain_and_the_plain_shelf_does_not() {
        static double Peak(int type) {
            using var block = TestBlocks.Create<FilterBlock>(
                (FilterBlock.Type, type),
                (FilterBlock.Gain, 12f),
                (FilterBlock.Q, 3f),
                (FilterBlock.Frequency, 500f)
            );
            var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);
            return Enumerable.Range(0, 200)
                .Max(i => Analysis.ToDb(Analysis.MagnitudeAt(response, 20 * Math.Pow(1.04, i), Rate)));
        }

        // The plain shelf's Q stops at 1, a slight bump; Shelf 2 takes the Q of 3.
        Assert.InRange(Peak((int)BiquadType.LowShelf), 11.5, 13);
        Assert.True(Peak(FilterBlock.LowShelf2) > Peak((int)BiquadType.LowShelf) + 3);
    }

    [Fact]
    public void Peaking_two_narrows_as_it_boosts() {
        static double At(int type, double frequency) {
            using var block = TestBlocks.Create<FilterBlock>((FilterBlock.Type, type), (FilterBlock.Gain, 12f));
            return Analysis.MagnitudeAt(BlockRender.Mono(block, Signals.Impulse(16_384), 64), frequency, Rate);
        }

        Assert.Equal(At((int)BiquadType.Peak, 1_000), At(FilterBlock.Peaking2, 1_000), 0.01);
        Assert.True(At(FilterBlock.Peaking2, 1_600) < 0.8 * At((int)BiquadType.Peak, 1_600));
    }

    [Fact]
    public void Allpass_feedback_turns_the_flat_response_into_peaks() {
        using var block =
            TestBlocks.Create<FilterBlock>((FilterBlock.Type, FilterBlock.AllPass), (FilterBlock.Feedback, 80f));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        var magnitudes = Enumerable.Range(0, 200)
            .Select(i => Analysis.MagnitudeAt(response, 20 * Math.Pow(1.04, i), Rate))
            .ToArray();
        Assert.True(magnitudes.Max() > 3, $"max {magnitudes.Max()}");
        Assert.True(Analysis.AllFinite(response));
    }

    [Fact]
    public void Comb_and_all_pass_controls_show_only_on_their_types_and_depth_sits_on_basic() {
        static ParamDescriptor Param(string id) => FilterBlock.Info.Params.Single(p => p.Id == id);

        for (var type = 0; type < Param("type").Options!.Count; type++) {
            var comb = type is FilterBlock.FeedForwardComb or FilterBlock.FeedbackComb;
            Assert.Equal(comb, Param("depth").TypeLabels![type] != string.Empty);
            Assert.Equal(comb, Param("combTime").TypeLabels![type] != string.Empty);
            Assert.Equal(type == FilterBlock.AllPass, Param("feedback").TypeLabels![type] != string.Empty);
        }

        Assert.Equal("Basic", Param("depth").Page);
    }

    public static TheoryData<int> FilterTypes => [..Enumerable.Range(0, FilterBlock.Info.Params[0].Options!.Count)];

    [Theory]
    [MemberData(nameof(FilterTypes))]
    public void A_filter_type_hides_exactly_the_controls_it_ignores(int type) {
        // Each control moved well off its starting point (the LFO switched on and swept; the gain starts at +6 dB, so
        // that a peak or shelf is not flat): a hidden one must not change the sound, a shown one must.
        (string Id, float Value)[] moves = [
            ("freq", 300f),
            ("q", 4f),
            ("gain", 12f),
            ("order", 1f),
            ("lfoOn", 1f)
        ];
        var reference = Render(type);

        foreach (var (id, value) in moves) {
            var param = FilterBlock.Info.Params.Single(p => p.Id == id);
            var shown = param.TypeLabels is not { } labels || labels[type] != string.Empty;
            var changed = !Render(type, (FilterBlock.Info.IndexOf(id), value)).SequenceEqual(reference);
            Assert.True(shown == changed, $"type {type}: {id} is {(shown ? "shown" : "hidden")} but changed={changed}");
        }

        static float[] Render(int type, params (int Index, float Value)[] settings) {
            using var block = TestBlocks.Create<FilterBlock>(
                [
                    (FilterBlock.Type, type),
                    (FilterBlock.Gain, 6f),
                    (FilterBlock.ModFrequency, 200f),
                    (FilterBlock.LfoRate, 5f),
                    ..settings
                ]
            );
            return BlockRender.Mono(block, Signals.Noise(9_600, 3, 0.25f), 64);
        }
    }

    [Fact]
    public void Filter_cuts_are_first_order() {
        using var block =
            TestBlocks.Create<FilterBlock>((FilterBlock.Type, FilterBlock.Null), (FilterBlock.LowCut, 1_000f));

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        Assert.Equal(-3, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.2);
        Assert.Equal(-20, Analysis.ToDb(Analysis.MagnitudeAt(response, 100, Rate)), 0.5);
    }

    // Tremolo

    [Fact]
    public void Panner_width_past_one_hundred_dwells_at_the_sides() {
        static double SidesShare(float width) {
            using var block = TestBlocks.Create<TremoloBlock>(
                (TremoloBlock.Type, TremoloBlock.Panner),
                (TremoloBlock.Depth, width),
                (TremoloBlock.Rate, 2f)
            );
            var (_, right) = BlockRender.Stereo(block, Signals.Step(48_000), Signals.Step(48_000), static _ => 64);
            return right.Count(static s => s < 1e-3f) / (double)right.Length;
        }

        Assert.True(SidesShare(100) < 0.02);
        Assert.InRange(SidesShare(200), 0.25, 0.4);
    }

    [Fact]
    public void Trigger_restarts_the_lfo_when_the_playing_starts() {
        static float[] After(int silence) {
            using var block = TestBlocks.Create<TremoloBlock>(
                (TremoloBlock.Depth, 100f),
                (TremoloBlock.Rate, 3f),
                (TremoloBlock.Trigger, -30f)
            );
            var output = BlockRender.Mono(block, Signals.Concat(new float[silence], Signals.Step(4_800, 0.5f)), 64);
            return output[silence..];
        }

        Assert.Equal(After(1_000), After(7_777));
    }

    [Theory]
    [InlineData(90f)]
    [InlineData(270f)]
    public void Trigger_restarts_the_lfo_at_its_start_phase(float startPhase) {
        using var block = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 3f),
            (TremoloBlock.Trigger, -30f),
            (TremoloBlock.StartPhase, startPhase)
        );

        var output = BlockRender.Mono(block, Signals.Concat(new float[7_777], Signals.Step(4_800, 0.5f)), 64);

        // A sine LFO at phase p gives the gain 1 - (1 - sin(2 pi p)) / 2 at full depth.
        var expected = 0.5f * 0.5f * (1 + MathF.Sin(2 * MathF.PI * startPhase / 360f));
        Assert.Equal(expected, output[7_777], 1e-3f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Linkwitz_riley_harmonic_split_sums_flat_without_depth(int slope) {
        using var block = TestBlocks.Create<TremoloBlock>(
            (TremoloBlock.Type, TremoloBlock.HarmonicTrem),
            (TremoloBlock.Depth, 0f),
            (TremoloBlock.CrossoverSlope, slope)
        );

        var response = BlockRender.Mono(block, Signals.Impulse(16_384), 64);

        foreach (var frequency in new[] { 50.0, 300, 700, 2_000, 10_000 }) {
            Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, frequency, Rate)), 0.05);
        }
    }

    // Ring modulator

    [Theory]
    [InlineData(RingModulatorBlock.UpperSideband, 1_200.0, 800.0)]
    [InlineData(RingModulatorBlock.LowerSideband, 800.0, 1_200.0)]
    public void Single_sideband_keeps_one_sideband(int type, double kept, double cancelled) {
        using var block = TestBlocks.Create<RingModulatorBlock>(
            (RingModulatorBlock.Type, type),
            (RingModulatorBlock.Frequency, 200f)
        );

        var output = BlockRender.Mono(block, Signals.Sine(1_000, Rate, 48_000, 0.5f), 64)[4_800..];

        Assert.Equal(0.5, Level(output, kept), 0.03);
        Assert.True(
            Level(output, cancelled) < 0.01 * Level(output, kept),
            $"other sideband {Level(output, cancelled):0.0000}"
        );
    }

    [Fact]
    public void Pitch_tracking_sets_the_carrier_to_the_note() {
        using var block = TestBlocks.Create<RingModulatorBlock>(
            (RingModulatorBlock.Type, RingModulatorBlock.UpperSideband),
            (RingModulatorBlock.Tracking, 1f),
            (RingModulatorBlock.Multiplier, 0.5f)
        );

        var output = BlockRender.Mono(block, Signals.Sine(220, Rate, 48_000, 0.4f), 64)[24_000..];

        Assert.Equal(330, PeakFrequency(output), 3.0);
    }

    // Rotary

    [Fact]
    public void Near_field_paths_and_levels_follow_the_geometry() {
        var (facing, loud) = RotaryBlock.NearField(3f, 1f);
        var (away, quiet) = RotaryBlock.NearField(3f, -1f);

        Assert.Equal(-1f, facing, 1e-6f);
        Assert.Equal(1f, loud, 1e-6f);
        Assert.Equal(1f, away, 1e-6f);
        Assert.Equal(0.5f, quiet, 1e-6f);
    }

    [Fact]
    public void Brake_stops_the_rotors() {
        using var block = TestBlocks.Create<RotaryBlock>(
            (RotaryBlock.Speed, RotaryBlock.Brake),
            (RotaryBlock.HornDecel, 0.05f),
            (RotaryBlock.DrumDecel, 0.05f)
        );
        var tone = Signals.Sine(1_000, Rate, 96_000, 0.3f);

        var output = BlockRender.Mono(block, tone, 64);

        // Parked: every later stretch has the same level.
        var levels = Enumerable.Range(0, 8)
            .Select(i => Analysis.Rms(output.AsSpan(48_000 + 6_000 * i, 6_000)))
            .ToArray();
        Assert.True(levels.Max() - levels.Min() < 0.01 * levels.Max(), $"levels {string.Join(", ", levels)}");
    }

    // Resonator

    [Fact]
    public void Stereo_resonator_keeps_the_sides_apart() {
        using var block = TestBlocks.Create<ResonatorBlock>((ResonatorBlock.InputMode, 1f), (ResonatorBlock.Mix, 100f));
        for (var v = 0; v < ResonatorBlock.Voices; v++) {
            block.Params.SetTarget(ResonatorBlock.Pan1 + v, 0f);
        }

        block.Reset();
        var (left, right) = BlockRender.Stereo(block, Signals.Noise(9_600, 2, 0.5f), new float[9_600], static _ => 64);

        Assert.True(Analysis.Rms(left) > 0.01);
        Assert.All(right, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Voice_feedback_shortens_its_ring() {
        static double Tail(float feedback) {
            using var block = TestBlocks.Create<ResonatorBlock>(
                (ResonatorBlock.Mix, 100f),
                (ResonatorBlock.Feedback1, feedback),
                (ResonatorBlock.Feedback1 + 1, feedback),
                (ResonatorBlock.Feedback1 + 2, feedback),
                (ResonatorBlock.Feedback1 + 3, feedback)
            );
            var output = BlockRender.Mono(block, Signals.Concat(Signals.Noise(2_400, 5, 0.5f), new float[24_000]), 64);
            return Analysis.Rms(output.AsSpan(12_000, 4_800));
        }

        Assert.True(Tail(90f) < 0.1 * Tail(100f));
    }

    [Fact]
    public void Post_band_pass_leaves_each_comb_its_fundamental() {
        using var block = TestBlocks.Create<ResonatorBlock>(
            (ResonatorBlock.Tuning, 1f),
            (ResonatorBlock.Mix, 100f),
            (ResonatorBlock.FilterLocation1, ResonatorBlock.FilterPost),
            (ResonatorBlock.Level1 + 1, Decibels.SilenceDb),
            (ResonatorBlock.Level1 + 2, Decibels.SilenceDb),
            (ResonatorBlock.Level1 + 3, Decibels.SilenceDb)
        );

        var output = BlockRender.Mono(block, Signals.Noise(48_000, 6, 0.3f), 64)[4_800..];

        // The comb alone rings every harmonic about equally; the band-pass keeps the fundamental.
        Assert.True(
            Band(output, 110) > 4 * Band(output, 330),
            $"{Band(output, 110):0.0000} vs {Band(output, 330):0.0000}"
        );
    }

    // Synth

    [Fact]
    public void Drone_voice_plays_its_frequency_without_input() {
        using var synth = TestBlocks.Create<SynthBlock>(
            (SynthBlock.VoiceParam(0, SynthBlock.Shape), SynthBlock.Sine),
            (SynthBlock.VoiceParam(0, SynthBlock.Track), SynthBlock.TrackOff),
            (SynthBlock.VoiceParam(0, SynthBlock.Frequency), 440f),
            (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 20_000f)
        );

        var output = BlockRender.Mono(synth, new float[24_000], 64)[4_800..];

        Assert.Equal(440, PeakFrequency(output), 1.0);
        Assert.Equal(0.5, Analysis.Peak(output), 0.05);
    }

    [Fact]
    public void Envelope_voice_plays_its_own_frequency_at_the_input_level() {
        using var synth = TestBlocks.Create<SynthBlock>(
            (SynthBlock.VoiceParam(0, SynthBlock.Shape), SynthBlock.Sine),
            (SynthBlock.VoiceParam(0, SynthBlock.Track), SynthBlock.TrackEnvelope),
            (SynthBlock.VoiceParam(0, SynthBlock.Frequency), 330f),
            (SynthBlock.VoiceParam(0, SynthBlock.Cutoff), 20_000f)
        );

        var output = BlockRender.Mono(synth, Signals.Sine(220, Rate, 24_000, 0.4f), 64)[12_000..];

        Assert.Equal(330, PeakFrequency(output), 1.0);
    }

    [Theory]
    [InlineData(SynthBlock.WhiteNoise)]
    [InlineData(SynthBlock.PinkNoise)]
    [InlineData(SynthBlock.Random)]
    public void Noise_and_random_shapes_are_bounded_and_repeatable(int shape) {
        (int, float)[] settings = [
            (SynthBlock.VoiceParam(2, SynthBlock.Shape), shape),
            (SynthBlock.VoiceParam(2, SynthBlock.Track), SynthBlock.TrackOff),
            (SynthBlock.VoiceParam(2, SynthBlock.VoiceLevel), 0f)
        ];
        using var first = TestBlocks.Create<SynthBlock>(settings);
        using var second = TestBlocks.Create<SynthBlock>(settings);

        var a = BlockRender.Mono(first, new float[9_600], 64);

        Assert.Equal(a, BlockRender.Mono(second, new float[9_600], 37));
        Assert.True(Analysis.Rms(a) > 0.01);
        Assert.True(Analysis.Peak(a) < 2);
    }

    [Fact]
    public void Voice_pan_places_it() {
        using var synth = TestBlocks.Create<SynthBlock>(
            (SynthBlock.VoiceParam(0, SynthBlock.Track), SynthBlock.TrackOff),
            (SynthBlock.VoiceParam(0, SynthBlock.Pan), -100f)
        );

        var (left, right) = BlockRender.Stereo(synth, new float[4_800], new float[4_800], static _ => 64);

        Assert.True(Analysis.Rms(left) > 0.05);
        Assert.True(Analysis.Peak(right) < 1e-6);
    }

    // Vocoder

    [Fact]
    public void Freeze_holds_the_spectrum_after_the_modulator_stops() {
        using var vocoder = TestBlocks.Create<VocoderBlock>();
        var noise = Signals.Noise(9_600, 7, 0.3f);
        BlockRender.Mono(vocoder, noise, 64);

        vocoder.Params.SetTarget(VocoderBlock.Freeze, 1f);
        var frozen = BlockRender.Mono(vocoder, new float[9_600], 64);

        Assert.True(Analysis.Rms(frozen.AsSpan(4_800)) > 0.01, $"frozen {Analysis.Rms(frozen.AsSpan(4_800))}");
    }

    [Fact]
    public void Muted_band_levels_mute_the_vocoder() {
        using var vocoder = TestBlocks.Create<VocoderBlock>((VocoderBlock.Bands, 0f));
        for (var b = 0; b < 4; b++) {
            vocoder.Params.SetTarget(VocoderBlock.BandLevel1 + b, Decibels.SilenceDb);
        }

        vocoder.Reset();
        var output = BlockRender.Mono(vocoder, Signals.Noise(9_600, 8, 0.3f), 64);

        Assert.All(output, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Analysis_row_feeds_only_the_detector_and_the_other_rows_sum() {
        using var vocoder = TestBlocks.Create<VocoderBlock>((VocoderBlock.AnalysisSource, 2f));
        var modulator = Signals.Noise(64, 9, 0.5f);
        var other = Signals.Noise(64, 10, 0.5f);
        var left = new float[64];
        var right = new float[64];
        var destination = new StereoBuffer(left, right);

        vocoder.MixInput(1, new(modulator, modulator), destination); // row 2: the analysis
        Assert.All(left, static s => Assert.Equal(0f, s));
        vocoder.MixInput(0, new(other, other), destination); // row 1: summed into the block input
        Assert.Equal(other, left);
    }

    [Fact]
    public void Analysis_row_drives_the_internal_carrier() {
        using var vocoder = TestBlocks.Create<VocoderBlock>((VocoderBlock.AnalysisSource, 1f));
        var modulator = Signals.Noise(4_800, 11, 0.5f);
        var heard = 0.0;
        for (var offset = 0; offset < modulator.Length; offset += 64) {
            var left = new float[64];
            var right = new float[64];
            var chunk = modulator.AsSpan(offset, 64).ToArray();
            vocoder.MixInput(0, new(chunk, chunk), new(left, right));
            vocoder.Process(new(left, right), new(Rate, 120, offset));
            heard = Math.Max(heard, Analysis.Peak(left));
        }

        Assert.True(heard > 0.01, $"peak {heard}");
    }

    // Wah

    [Fact]
    public void Min_and_max_frequency_scale_the_voicing() {
        Assert.Equal(2 * WahBlock.Frequency(0, 0), WahBlock.Frequency(0, 0, 2f), 1e-3f);
        Assert.Equal(0.5f * WahBlock.Frequency(0, 100), WahBlock.Frequency(0, 100, 0.5f, 0.5f), 1e-2f);
        Assert.Equal(3, WahBlock.FilterVoicings);
    }

    [Fact]
    public void Wah_eq_band_boosts_its_octave() {
        using var wah = TestBlocks.Create<WahBlock>((WahBlock.Mix, 0f), (WahBlock.Eq1 + 4, 12f));

        var response = BlockRender.Mono(wah, Signals.Impulse(16_384), 64);

        Assert.Equal(12, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.2);
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, 63, Rate)), 0.5);
    }

    [Fact]
    public void Inductor_bias_raises_the_circuit_sweep() {
        static double Peak(float bias) {
            using var wah = TestBlocks.Create<WahBlock>(
                (WahBlock.Type, WahBlock.FirstCircuit),
                (WahBlock.Position, 50f),
                (WahBlock.InductorBias, bias)
            );
            BlockRender.Mono(wah, new float[4_800], 64); // settle
            var response = BlockRender.Mono(wah, Signals.Impulse(16_384, 0.01f), 64);
            return Enumerable.Range(0, 300)
                .Select(static i => 200 * Math.Pow(1.01, i))
                .MaxBy(f => Analysis.MagnitudeAt(response, f, Rate));
        }

        Assert.InRange(Peak(100f) / Peak(0f), 1.3, 1.8);
    }

    // Pitch

    [Fact]
    public void Just_ratios_on_the_key() {
        Assert.Equal(1.25, Scales.JustRatio(60, 0, 4), 1e-12);
        Assert.Equal(1.5, Scales.JustRatio(60, 0, 7), 1e-12);
        Assert.Equal(6 / 5.0, Scales.JustRatio(64, 0, 3), 1e-12); // E to G: 3/2 over 5/4
        Assert.Equal(2.5, Scales.JustRatio(60, 0, 16), 1e-12);
    }

    [Fact]
    public void Just_temperament_tunes_the_diatonic_third() {
        using var pitch = CreatePitch(
            (PitchBlock.Type, (float)PitchType.Diatonic),
            (PitchBlock.Temperament, 1f),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(pitch, PitchTrackerTests.HarmonicTone(261.63, 48_000), 64)[24_000..];

        Assert.Equal(261.63 * 1.25, PeakFrequency(output), 0.6);
    }

    [Fact]
    public void Poly_whammy_shifts_a_chord() {
        using var pitch = CreatePitch(
            (PitchBlock.Type, (float)PitchType.Whammy),
            (PitchBlock.WhammyTracking, PitchBlock.WhammyPoly),
            (PitchBlock.Pedal, 100f)
        );
        var chord = Signals.Sine(220, Rate, 48_000, 0.25f)
            .Zip(Signals.Sine(330, Rate, 48_000, 0.25f), static (a, b) => a + b)
            .ToArray();

        var output = BlockRender.Mono(pitch, chord, 64)[12_000..];

        Assert.Equal(0.25, Level(output, 440), 0.05);
        Assert.Equal(0.25, Level(output, 660), 0.05);
        Assert.True(Level(output, 220) < 0.02 && Level(output, 330) < 0.02, "the unshifted chord is still there");
    }

    [Fact]
    public void Delay_mode_feedback_repeats_a_voice_at_its_level() {
        using var pitch = CreatePitch(
            (PitchBlock.Type, (float)PitchType.Detune),
            (PitchBlock.Detune, 0f),
            (PitchBlock.Voice2Level, Decibels.SilenceDb),
            (PitchBlock.Voice1Delay, 100f),
            (PitchBlock.Feedback1, 50f),
            (PitchBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(pitch, Signals.Impulse(24_000), 64);

        var first = Analysis.Peak(output.AsSpan(4_000, 2_000));
        var second = Analysis.Peak(output.AsSpan(8_800, 2_000));
        Assert.True(first > 0.1, $"first {first}");
        Assert.Equal(0.5, second / first, 0.02);
    }

    [Fact]
    public void Arpeggiator_runs_its_repeats_then_stops() {
        using var pitch = CreatePitch(
            (PitchBlock.Type, (float)PitchType.Arpeggiator),
            (PitchBlock.ArpTempo, 3f), // 6000 samples per step
            (PitchBlock.ArpSteps, 1f),
            (PitchBlock.ArpRepeats, 1f),
            (PitchBlock.Mix, 100f)
        );

        var output = BlockRender.Mono(pitch, Signals.Sine(220, Rate, 48_000, 0.3f), 64);

        Assert.True(Analysis.Peak(output.AsSpan(2_000, 8_000)) > 0.1);
        Assert.True(
            Analysis.Peak(output.AsSpan(14_000)) < 1e-3,
            $"still playing at {Analysis.Peak(output.AsSpan(14_000))}"
        );
    }

    [Fact]
    public void Arpeggiator_amplitude_shape_decays_through_each_step() {
        Assert.Equal(1f, PitchBlock.ArpAmplitude(1, 50, 0), 1e-6f);
        Assert.Equal(0.5f, PitchBlock.ArpAmplitude(1, 50, 0.5), 1e-6f);
        Assert.Equal(0.0625f, PitchBlock.ArpAmplitude(2, 100, 0.5), 1e-6f); // swell, alpha 100: the fourth power
        Assert.Equal(1f, PitchBlock.ArpAmplitude(3, 50, 0.5), 1e-6f);
        Assert.Equal(-0.5f, PitchBlock.ArpPan(1, 50, 0, 4));
        Assert.Equal(0.5f, PitchBlock.ArpPan(2, 50, 3, 4), 1e-6f);
    }

    [Fact]
    public void Capo_detune_adds_cents() {
        using var capo = CreatePitch(
            (PitchBlock.Type, (float)PitchType.VirtualCapo),
            (PitchBlock.CapoDetune, 50f)
        );

        var output = BlockRender.Mono(capo, Signals.Sine(220, Rate, 48_000, 0.3f), 64)[24_000..];

        Assert.Equal(220 * Math.Pow(2, 0.5 / 12), PeakFrequency(output), 1.0);
    }

    [Fact]
    public void Stereo_input_mode_keeps_a_single_voice_type_on_its_side() {
        using var capo = CreatePitch(
            (PitchBlock.Type, (float)PitchType.VirtualCapo),
            (PitchBlock.InputMode, 1f)
        );

        var (left, right) = BlockRender.Stereo(
            capo,
            Signals.Sine(220, Rate, 9_600, 0.3f),
            new float[9_600],
            static _ => 64
        );

        Assert.True(Analysis.Rms(left.AsSpan(4_800)) > 0.05);
        Assert.True(Analysis.Peak(right) < 1e-6);
    }

    [Fact]
    public void Detector_row_is_kept_out_of_the_block_input() {
        using var pitch = CreatePitch((PitchBlock.DetectorSource, 3f));
        var detector = Signals.Noise(64, 1, 0.5f);
        var left = new float[64];
        var right = new float[64];

        pitch.MixInput(2, new(detector, detector), new(left, right));

        Assert.All(left, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Splice_length_is_clamped_to_its_range() {
        using var shifter = new SpliceShifter();
        shifter.Prepare(Rate);
        shifter.SetSplice(1);
        var tone = Signals.Sine(220, Rate, 9_600, 0.3f);

        var output = tone.Select(s => shifter.Process(s, 1.5, 0)).ToArray();

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Rms(output.AsSpan(4_800)) > 0.1);
    }

    /// <summary>
    ///     The level around a harmonic, summed over a few cents either side, so a noise-driven ring reads steadily.
    /// </summary>
    static double Band(ReadOnlySpan<float> signal, double frequency) {
        var sum = 0.0;
        for (var i = -4; i <= 4; i++) {
            var level = Level(signal, frequency * (1 + 0.002 * i));
            sum += level * level;
        }

        return Math.Sqrt(sum);
    }

    static PitchBlock CreatePitch(params (int Index, float Value)[] settings) =>
        TestBlocks.Create<PitchBlock>(settings);
}
