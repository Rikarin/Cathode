using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Blocks Guide parity features of the dynamics blocks (docs/08 §6.2, §6.11, §6.18).</summary>
public class DynamicsParityTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(0f, -20f, 0f, -20f)] // 20 dB over a limiter: held at the threshold
    [InlineData(-10f, -20f, 0f, -20f)]
    [InlineData(-30f, -20f, 0f, -30f)] // below: untouched
    [InlineData(-20f, -20f, 8f, -21f)] // the knee: -W / 8
    public void Infinite_ratio_limits(float input, float threshold, float knee, float expected) =>
        Assert.Equal(
            expected,
            CompressorBlock.StaticCurve(input, threshold, CompressorBlock.InfiniteRatio, knee),
            1e-4f
        );

    [Theory]
    [InlineData(50f, 2f)] // half the slope over the threshold: 2:1
    [InlineData(100f, CompressorBlock.InfiniteRatio)]
    [InlineData(-100f, 0.5f)] // twice the slope: upward expansion
    [InlineData(0f, 1f)]
    public void Dynamics_maps_to_a_ratio(float dynamics, float ratio) =>
        Assert.Equal(ratio, CompressorBlock.DynamicsRatio(dynamics), 1e-4f);

    [Fact]
    public void Dynamics_type_expands_upwards_for_negative_values() {
        using var block = TestBlocks.Create<CompressorBlock>(
            (CompressorBlock.Type, CompressorBlock.DynamicsProcessor),
            (CompressorBlock.Dynamics, -100f),
            (CompressorBlock.Threshold, -30f),
            (CompressorBlock.Knee, 0f)
        );
        var input = Signals.Sine(1_000, Rate, 48_000, Db(-20)); // RMS -23 dB, 7 dB over: raised by 7 dB

        Assert.Equal(-13f, SteadyPeakDb(block, input), 0.5f);
    }

    [Fact]
    public void Detector_choice_overrides_the_type() {
        // A sine's RMS is 3 dB under its peak: RMS detection compresses it less.
        static float Out(int detector) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Type, CompressorBlock.Pedal),
                (CompressorBlock.Knee, 0f),
                (CompressorBlock.Detector, detector)
            );
            return SteadyPeakDb(block, Signals.Sine(1_000, Rate, 48_000));
        }

        var peak = Out(CompressorBlock.DetectorPeak);
        var rms = Out(CompressorBlock.DetectorRms);
        var both = Out(CompressorBlock.DetectorRmsPeak);
        Assert.InRange(peak, -16f, -14f); // the peak detector ripples on a sine
        Assert.Equal(-15f + 3.01f * 0.75f, rms, 0.5f); // 3 dB less over, 3/4 of it kept
        Assert.InRange(both, peak + 0.5f, rms - 0.5f);
    }

    [Fact]
    public void Line_input_level_moves_the_threshold_12_dB() {
        static float Out(int inputLevel) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Knee, 0f),
                (CompressorBlock.InputLevel, inputLevel)
            );
            return SteadyPeakDb(block, Signals.Sine(1_000, Rate, 48_000));
        }

        // 20 - 12 = 8 dB over at 4:1: 9 dB less reduction.
        Assert.Equal(9f, Out(1) - Out(0), 0.5f);
    }

    [Fact]
    public void Emphasis_makes_bass_pump_less() {
        static float Reduction(float emphasis) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Emphasis, emphasis),
                (CompressorBlock.EmphasisFrequency, 1_000f)
            );
            BlockRender.Mono(block, Signals.Sine(80, Rate, 24_000), 64);
            var bass = block.GainReductionDb;
            using var treble = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Emphasis, emphasis),
                (CompressorBlock.EmphasisFrequency, 1_000f)
            );
            BlockRender.Mono(treble, Signals.Sine(8_000, Rate, 24_000), 64);
            return treble.GainReductionDb - bass; // how much more the treble is compressed
        }

        Assert.Equal(0f, Reduction(0f), 1f);
        Assert.InRange(Reduction(100f), -12f, -5f); // the shelf adds up to 12 dB of treble to the detector at 4:1
    }

    [Fact]
    public void Sidechain_eq_band_shapes_the_detector() {
        using var plain = TestBlocks.Create<CompressorBlock>();
        using var cut = TestBlocks.Create<CompressorBlock>(
            (CompressorBlock.SidechainFilter, 3f),
            (CompressorBlock.SidechainFrequency, 1_000f),
            (CompressorBlock.SidechainGain, -24f),
            (CompressorBlock.SidechainQ, 1f)
        );
        var tone = Signals.Sine(1_000, Rate, 24_000);
        BlockRender.Mono(plain, tone, 64);
        BlockRender.Mono(cut, tone, 64);

        Assert.True(plain.GainReductionDb < -10f);
        Assert.True(cut.GainReductionDb > -0.1f, "24 dB down, the detector sits under the threshold (inside the knee)");
    }

    [Fact]
    public void Feedback_topology_compresses_less_than_feed_forward() {
        static float Out(int type) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Type, type),
                (CompressorBlock.Knee, 0f),
                (CompressorBlock.Detector, CompressorBlock.DetectorPeak)
            );
            return SteadyPeakDb(block, Signals.Sine(1_000, Rate, 48_000));
        }

        var forward = Out(CompressorBlock.Pedal);
        var feedback = Out(CompressorBlock.Feedback);
        Assert.InRange(forward, -16f, -14f);

        // The detector sees the output y: y = T + (x' - T) / R with x' = y, so y = T: the loop settles near the
        // threshold, between it and the input.
        Assert.InRange(feedback, forward - 6f, -1f);
        Assert.NotEqual(forward, feedback, 0.5f);
    }

    [Fact]
    public void Auto_attack_reacts_faster_to_a_transient() {
        static float FirstMsReduction(bool auto) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Attack, 30f),
                (CompressorBlock.AutoTime, auto ? 1f : 0f)
            );
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, Db(-40)), 64);
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 240), 64); // 5 ms burst
            return block.GainReductionDb;
        }

        Assert.True(FirstMsReduction(true) < FirstMsReduction(false) - 1f, "auto attacks faster on a burst");
    }

    [Fact]
    public void Crest_factor_scale_is_unity_for_a_sine() {
        var crest = new CrestFactor();
        var coefficient = CrestFactor.Coefficient(Rate);
        var scale = 0f;
        foreach (var x in Signals.Sine(1_000, Rate, 96_000)) {
            scale = crest.Tick(MathF.Abs(x), coefficient);
        }

        Assert.Equal(1f, scale, 0.05f);
    }

    [Fact]
    public void Light_types_change_the_opto_release() {
        static float Recovery(int light) {
            using var block = TestBlocks.Create<CompressorBlock>(
                (CompressorBlock.Type, CompressorBlock.Opto),
                (CompressorBlock.Threshold, -30f),
                (CompressorBlock.Ratio, 10f),
                (CompressorBlock.Release, 100f),
                (CompressorBlock.LightType, light)
            );
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000), 64);
            BlockRender.Mono(block, new float[4_800], 64);
            return block.GainReductionDb;
        }

        Assert.True(Recovery(2) < Recovery(0) - 1f, "a vactrol holds on longer");
        Assert.True(Recovery(1) > Recovery(0) + 1f, "a fast LED lets go sooner");
    }

    [Fact]
    public void Modern_gate_holds_open_through_its_hysteresis() {
        static float Gain(int type) {
            using var block = TestBlocks.Create<GateBlock>(
                (GateBlock.Type, type),
                (GateBlock.Threshold, -40f),
                (GateBlock.Hold, 0f),
                (GateBlock.Release, 5f)
            );
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 9_600, Db(-30)), 64);
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 9_600, Db(-43)), 64); // 3 dB under the threshold
            return block.CurrentGain;
        }

        Assert.True(Gain(GateBlock.Gate) < 0.01f, "the classic gate closes 3 dB under");
        Assert.Equal(1f, Gain(GateBlock.ModernGate), 1e-3f);
    }

    [Fact]
    public void Modern_gate_closes_past_its_hysteresis() {
        using var block = TestBlocks.Create<GateBlock>(
            (GateBlock.Type, GateBlock.ModernGate),
            (GateBlock.Threshold, -40f),
            (GateBlock.Hold, 0f),
            (GateBlock.Release, 5f)
        );
        BlockRender.Mono(block, Signals.Sine(1_000, Rate, 9_600, Db(-30)), 64);
        BlockRender.Mono(block, Signals.Sine(1_000, Rate, 9_600, Db(-50)), 64);

        Assert.True(block.CurrentGain < 0.01f);
    }

    [Theory]
    [InlineData(-20f, 0f, -60f)] // 20 dB under at 4:1 (slope 3): -60 dB
    [InlineData(-20f, 12f, -60f)] // outside the knee: the same
    [InlineData(0f, 12f, -4.5f)] // at the threshold: -slope W / 8
    [InlineData(5f, 12f, -0.125f)] // just over, inside the knee
    [InlineData(7f, 12f, 0f)]
    public void Expander_knee_rounds_the_corner(float under, float knee, float expected) =>
        Assert.Equal(expected, GateBlock.ExpanderGainDb(under, 3f, knee), 1e-3f);

    [Fact]
    public void Multiband_slopes_both_sum_flat_when_nothing_is_compressed() {
        foreach (var slope in new[] { MultibandCompressorBlock.Slope24, MultibandCompressorBlock.Slope12 }) {
            using var block = TestBlocks.Create<MultibandCompressorBlock>(
                (MultibandCompressorBlock.Slope, slope),
                (MultibandCompressorBlock.Threshold(0), 0f),
                (MultibandCompressorBlock.Threshold(1), 0f),
                (MultibandCompressorBlock.Threshold(2), 0f)
            );
            var response = BlockRender.Mono(block, Signals.Impulse(16_384, 0.1f), 64);
            foreach (var f in new[] { 50.0, 200, 700, 2_500, 8_000 }) {
                Assert.Equal(-20.0, Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.1);
            }
        }
    }

    [Fact]
    public void Second_order_crossover_rolls_off_at_12_dB_per_octave() {
        using var low = TestBlocks.Create<MultibandCompressorBlock>(
            (MultibandCompressorBlock.Slope, MultibandCompressorBlock.Slope12),
            (MultibandCompressorBlock.Mute(1), 1f),
            (MultibandCompressorBlock.Mute(2), 1f),
            (MultibandCompressorBlock.Threshold(0), 0f)
        );
        var response = BlockRender.Mono(low, Signals.Impulse(16_384, 0.1f), 64);

        Assert.Equal(-26.02, Analysis.ToDb(Analysis.MagnitudeAt(response, 200, Rate)), 0.2); // -6 dB at the split
        Assert.InRange(Analysis.ToDb(Analysis.MagnitudeAt(response, 800, Rate)) + 20, -26, -22); // two octaves: ~-24
    }

    [Fact]
    public void Muting_two_bands_solos_the_third() {
        using var block = TestBlocks.Create<MultibandCompressorBlock>(
            (MultibandCompressorBlock.Mute(0), 1f),
            (MultibandCompressorBlock.Mute(2), 1f),
            (MultibandCompressorBlock.Threshold(1), 0f)
        );
        var response = BlockRender.Mono(block, Signals.Impulse(16_384, 0.1f), 64);

        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 30, Rate)) < -60);
        Assert.Equal(-20.0, Analysis.ToDb(Analysis.MagnitudeAt(response, 700, Rate)), 0.5);
        Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, 15_000, Rate)) < -50);
    }

    [Fact]
    public void Multiband_rms_detector_compresses_a_sine_less_than_peak() {
        static float Reduction(int detector) {
            using var block =
                TestBlocks.Create<MultibandCompressorBlock>((MultibandCompressorBlock.Detector, detector));
            BlockRender.Mono(block, Signals.Sine(700, Rate, 24_000), 64);
            return block.GainReductionDb(1);
        }

        Assert.True(Reduction(1) > Reduction(0) + 1f);
    }

    static float Db(float db) => MathF.Pow(10f, db / 20f);

    static float SteadyPeakDb(IDspBlock block, float[] input) =>
        (float)Analysis.ToDb(Analysis.Peak(BlockRender.Mono(block, input, 64).AsSpan(input.Length / 2)));
}
