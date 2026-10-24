using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Drive block's EQ and Advanced pages and the Dynamic Distortion's filter section (docs/08 §6.5–6.6).</summary>
public class DriveParityTests {
    const double Rate = 48_000;

    /// <summary>Clip Type None: the Distortion voicing is then a linear chain, so EQ responses can be measured.</summary>
    const float Clean = (float)ClipperShape.Linear + 1;

    [Theory]
    [InlineData(DriveBlock.Bass, BiquadType.LowShelf, 100f)]
    [InlineData(DriveBlock.Mid, BiquadType.Peak, 800f)]
    [InlineData(DriveBlock.HighMid, BiquadType.Peak, 2_500f)]
    [InlineData(DriveBlock.Treble, BiquadType.HighShelf, 5_000f)]
    public void Eq1_bands_have_their_designed_response(int band, BiquadType type, float frequency) {
        var flat = CleanResponse();
        var shaped = CleanResponse((band, 9f));
        var q = type == BiquadType.Peak ? 0.7 : 0.7071;
        var expected = BiquadCoefficients.Design(type, Rate, frequency, q, 9);

        foreach (var f in new[] { 60.0, 250, 800, 2_500, 6_000, 12_000 }) {
            var measured = Analysis.ToDb(Analysis.MagnitudeAt(shaped, f, Rate) / Analysis.MagnitudeAt(flat, f, Rate));
            Assert.Equal(Analysis.ToDb(expected.MagnitudeAt(f, Rate)), measured, 0.1);
        }
    }

    [Fact]
    public void Low_and_high_cut_are_minus_three_db_at_their_corners() {
        var flat = CleanResponse();
        var cut = CleanResponse((DriveBlock.LowCut, 200f), (DriveBlock.HighCut, 3_000f));

        double At(double f) => Analysis.ToDb(Analysis.MagnitudeAt(cut, f, Rate) / Analysis.MagnitudeAt(flat, f, Rate));

        Assert.Equal(-3.01, At(200), 0.1);
        Assert.Equal(-3.01, At(3_000), 0.1);
        Assert.Equal(0, At(800), 0.5);
    }

    [Theory]
    [InlineData(DriveBlock.EqPre)]
    [InlineData(DriveBlock.EqPost)]
    public void Eq2_band_lifts_its_centre_before_or_after_the_drive(int location) {
        var flat = CleanResponse((DriveBlock.Eq2Location, location));
        var band = Array.IndexOf(GraphicEqualizer.Layouts[DriveBlock.Eq2Layout].Labels, "1k");
        var lifted = CleanResponse((DriveBlock.Eq2Location, location), (DriveBlock.Eq2Band + band, 9f));

        Assert.Equal(
            9,
            Analysis.ToDb(Analysis.MagnitudeAt(lifted, 1_000, Rate) / Analysis.MagnitudeAt(flat, 1_000, Rate)),
            0.3
        );
        Assert.Equal(
            0,
            Analysis.ToDb(Analysis.MagnitudeAt(lifted, 100, Rate) / Analysis.MagnitudeAt(flat, 100, Rate)),
            0.3
        );
    }

    [Fact]
    public void Eq2_does_nothing_while_off() {
        var flat = CleanResponse();
        var off = CleanResponse((DriveBlock.Eq2Band + 5, 12f));
        Assert.Equal(flat, off);
    }

    [Fact]
    public void Bit_reduce_quantises_and_sample_rate_holds() {
        using var block = Create(
            (DriveBlock.Type, 1f),
            (DriveBlock.ClipType, Clean),
            (DriveBlock.Drive, 0f),
            (DriveBlock.BitReduce, 16f),
            (DriveBlock.SampleRate, 6_000f)
        );
        var output = BlockRender.Mono(block, Signals.Sine(220, Rate, 4_800, 0.2f), 64);

        Assert.All(output, y => Assert.Equal(0, y * 128 - MathF.Round(y * 128), 3)); // 8 bits: steps of 1/128
        var changes = 0;
        for (var i = 1; i < output.Length; i++) {
            changes += output[i] != output[i - 1] ? 1 : 0;
        }

        Assert.InRange(changes, 10, output.Length / 8 + 1); // held for 8 samples at a time
    }

    [Fact]
    public void Slew_rate_bounds_the_steepest_step() {
        static double Steepest(float slew) {
            using var block = Create(
                (DriveBlock.Type, 1f),
                (DriveBlock.ClipType, Clean),
                (DriveBlock.Drive, 0f),
                (DriveBlock.Tone, 10f),
                (DriveBlock.SlewRate, slew)
            );
            var square = Signals.Sine(200, Rate, 9_600).Select(static s => s >= 0 ? 2f : -2f).ToArray();
            var output = BlockRender.Mono(block, square, 64);
            return Enumerable.Range(1, output.Length - 1).Max(i => Math.Abs(output[i] - output[i - 1]));
        }

        var free = Steepest(DriveBlock.SlewOff);
        var limited = Steepest(0.01f); // 0.01 V/µs: 0.21 per sample at 48 kHz
        Assert.True(limited < 0.25, $"steepest step {limited:0.000} (free {free:0.000})");
        Assert.True(free > 5 * limited, $"free {free:0.000} vs limited {limited:0.000}");
    }

    [Fact]
    public void Bias_adds_even_harmonics_to_a_symmetric_clipper() {
        static double Second(float bias) {
            using var block = Create(
                (DriveBlock.Type, 1f),
                (DriveBlock.Drive, 3f),
                (DriveBlock.Tone, 10f),
                (DriveBlock.Bias, bias)
            );
            var input = Signals.CoherentSine(32, 8_192, 0.3f);
            var rendered = BlockRender.Mono(block, Signals.Concat(input, input, input), 64).AsSpan(16_384);
            var magnitudes = Fft.Magnitudes(rendered);
            return magnitudes[64] / magnitudes[32];
        }

        Assert.True(Second(60f) > 30 * Second(0f), $"second harmonic {Second(60f):0.0000} vs {Second(0f):0.0000}");
    }

    [Fact]
    public void Clip_shape_moves_the_curve_towards_a_hard_clip_or_a_soft_knee() {
        Assert.Equal(
            AdaaClipper.Function(ClipperShape.Hard, 1.7),
            AdaaClipper.Blend(ClipperShape.Soft, ClipperShape.Hard, 1, 1.7)
        );

        static float[] Render(float shape, float clipType) {
            using var block = Create(
                (DriveBlock.Type, 0f),
                (DriveBlock.Drive, 6f),
                (DriveBlock.ClipShape, shape),
                (DriveBlock.ClipType, clipType)
            );
            return BlockRender.Mono(block, Signals.Sine(220, Rate, 4_800, 0.3f), 64);
        }

        // Fully one way the curve is the hard clip; fully the other, the germanium knee.
        Assert.Equal(Render(0f, 2f), Render(100f, 0f));
        Assert.Equal(Render(0f, 4f), Render(-100f, 0f));
        Assert.NotEqual(Render(0f, 0f), Render(50f, 0f));
    }

    [Fact]
    public void Diode_overrides_replace_the_clipping_diodes() {
        var index = Enumerable.Range(0, DriveCircuits.All.Count)
            .First(i => DriveCircuits.HasDiodes(DriveCircuits.All[i]));
        Assert.Same(DriveCircuits.All[index], DriveCircuits.Variant(index, 0, 0));
        var led = DriveCircuits.Variant(index, 4, 0);
        Assert.Same(led, DriveCircuits.Variant(index, 4, 0)); // built once: no allocation when chosen
        Assert.Contains(led.Stages, static s => s.Diodes.ForwardCount > 0 && s.Diodes.Forward == DiodeModel.Led);

        double Loudness(int diode) {
            using var block = Create(
                (DriveBlock.Type, DriveBlock.CircuitTypes + index),
                (DriveBlock.Drive, 8f),
                (DriveBlock.DiodeType, diode)
            );
            return Analysis.Rms(BlockRender.Mono(block, Signals.Sine(220, Rate, 9_600, 0.5f), 64).AsSpan(4_800));
        }

        Assert.True(Analysis.ToDb(Loudness(4) / Loudness(2)) > 6, "LEDs clip far higher than germanium diodes");
    }

    [Fact]
    public void Diode_controls_are_hidden_where_they_do_nothing() {
        var labels = DriveBlock.Info.Params[DriveBlock.DiodeType].TypeLabels!;
        Assert.Equal(string.Empty, labels[0]);
        Assert.Equal("Clip Type", DriveBlock.Info.Params[DriveBlock.ClipType].TypeLabels![1]);
        Assert.Equal(string.Empty, DriveBlock.Info.Params[DriveBlock.ClipType].TypeLabels![DriveBlock.CircuitTypes]);
        for (var i = 0; i < DriveCircuits.All.Count; i++) {
            Assert.Equal(
                DriveCircuits.HasDiodes(DriveCircuits.All[i]),
                labels[DriveBlock.CircuitTypes + i] != string.Empty
            );
        }
    }

    [Fact]
    public void Bass_response_tightens_a_circuit_and_dry_level_blends_the_clean_input() {
        var index = Enumerable.Range(0, DriveCircuits.All.Count).First(i => DriveCircuits.All[i].InputHighPassHz > 0);

        double LowEnd(params (int, float)[] settings) {
            using var block = Create(
                [(DriveBlock.Type, DriveBlock.CircuitTypes + index), (DriveBlock.Drive, 2f), ..settings]
            );
            return Analysis.Rms(BlockRender.Mono(block, Signals.Sine(60, Rate, 19_200, 0.05f), 64).AsSpan(9_600));
        }

        Assert.True(LowEnd((DriveBlock.BassResponse, 400f)) < 0.7 * LowEnd(), "a higher coupling corner cuts the lows");
        Assert.NotEqual(LowEnd(), LowEnd((DriveBlock.DryLevel, 0f)));
    }

    [Fact]
    public void Dynamic_distortion_emphasis_is_flat_when_clean_and_shapes_the_clipping() {
        double Thd(float levelDb, int filter, int bin) {
            using var block = TestBlocks.Create<DynamicDistortionBlock>(
                (DynamicDistortionBlock.Drive, 4f),
                (DynamicDistortionBlock.Dynamics, 0f),
                (DynamicDistortionBlock.FilterType, filter),
                (DynamicDistortionBlock.FilterFrequency, 2_000f),
                (DynamicDistortionBlock.FilterQ, 1f),
                (DynamicDistortionBlock.FilterGain, 12f)
            );
            var input = Signals.CoherentSine(bin, 8_192, (float)Decibels.ToGain((double)levelDb));
            return Analysis.Thd(BlockRender.Mono(block, Signals.Concat(input, input), 64).AsSpan(8_192), bin);
        }

        const int AtPeak = 341; // about 2 kHz
        Assert.True(
            Thd(-12, DynamicDistortionBlock.FilterPeak, AtPeak)
            > 2 * Thd(-12, DynamicDistortionBlock.FilterNone, AtPeak)
        );

        using var quiet = TestBlocks.Create<DynamicDistortionBlock>(
            (DynamicDistortionBlock.Drive, 0f),
            (DynamicDistortionBlock.Dynamics, 0f),
            (DynamicDistortionBlock.FilterType, DynamicDistortionBlock.FilterPeak),
            (DynamicDistortionBlock.FilterFrequency, 2_000f),
            (DynamicDistortionBlock.FilterGain, 12f)
        );
        var response = BlockRender.Mono(quiet, Signals.Impulse(16_384, 1e-4f), 64);
        foreach (var f in new[] { 200.0, 2_000, 8_000 }) {
            Assert.Equal(-80, Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.2); // the inverse undoes it
        }
    }

    [Fact]
    public void Dynamic_distortion_mono_sums_and_bias_adds_even_harmonics() {
        using var mono = TestBlocks.Create<DynamicDistortionBlock>((DynamicDistortionBlock.Mode, 1f));
        var (left, right) = BlockRender.Stereo(mono, Signals.Noise(4_800, 3), new float[4_800], _ => 64);
        Assert.Equal(left, right);
        Assert.True(Analysis.Rms(right) > 0.01);

        double Second(float bias) {
            using var block = TestBlocks.Create<DynamicDistortionBlock>(
                (DynamicDistortionBlock.Drive, 4f),
                (DynamicDistortionBlock.Dynamics, 0f),
                (DynamicDistortionBlock.Bias, bias)
            );
            var input = Signals.CoherentSine(32, 8_192, 0.3f);
            var magnitudes = Fft.Magnitudes(BlockRender.Mono(block, Signals.Concat(input, input), 64).AsSpan(8_192));
            return magnitudes[64] / magnitudes[32];
        }

        Assert.True(Second(60f) > 30 * Second(0f));
    }

    static float[] CleanResponse(params (int Index, float Value)[] settings) {
        using var block = Create(
            [
                (DriveBlock.Type, 1f), (DriveBlock.ClipType, Clean), (DriveBlock.Drive, 0f), (DriveBlock.Tone, 10f),
                ..settings
            ]
        );
        return BlockRender.Mono(block, Signals.Impulse(16_384, 0.1f), 64);
    }

    static DriveBlock Create(params (int Index, float Value)[] settings) => TestBlocks.Create<DriveBlock>(settings);
}
