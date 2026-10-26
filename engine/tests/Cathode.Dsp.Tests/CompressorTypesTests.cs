using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The Compressor's JFET, Tube and Compander types and its single Compression knob (docs/08 §6.2).</summary>
public class CompressorTypesTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(CompressorBlock.Jfet)]
    [InlineData(CompressorBlock.Tube)]
    [InlineData(CompressorBlock.Compander)]
    public void Each_type_compresses_a_loud_tone_and_leaves_a_quiet_one(int type) {
        var loud = Steady(type, 0f);
        var quiet = Steady(type, -40f, -90f);

        Assert.True(loud < -6, $"a 0 dBFS tone came out at {loud:0.0} dB");
        Assert.InRange(quiet, -42.5, -39.5); // well under the threshold (the Tube's wide knee bends a little)
    }

    [Fact]
    public void The_jfet_cell_adds_odd_harmonics_that_grow_with_the_reduction() {
        var light = ThirdHarmonicDb(CompressorBlock.Jfet, -10f);
        var heavy = ThirdHarmonicDb(CompressorBlock.Jfet, -40f);
        var clean = ThirdHarmonicDb(CompressorBlock.Feedback, -40f);

        Assert.True(heavy > light + 3, $"heavy {heavy:0.0} dB vs light {light:0.0} dB");
        Assert.True(heavy > clean + 20, $"jfet {heavy:0.0} dB vs plain feedback {clean:0.0} dB");
    }

    [Fact]
    public void The_jfet_curve_is_unity_for_small_signals_and_monotonic() {
        Assert.Equal(1e-4f, CompressorBlock.JfetColour(1e-4f, 0.5f), 1e-9f);
        var previous = float.MinValue;
        for (var v = -4f; v <= 4f; v += 0.01f) {
            var y = CompressorBlock.JfetColour(v, 0.5f);
            Assert.True(y > previous);
            previous = y;
        }
    }

    [Fact]
    public void The_tube_adds_second_harmonic_and_no_dc() {
        var output = Render(CompressorBlock.Tube, Signals.Sine(1_000, Rate, 48_000, 0.5f), -20f);
        var steady = output.AsSpan(24_000);
        var fundamental = Analysis.MagnitudeAt(steady, 1_000, Rate);
        var second = Analysis.ToDb(Analysis.MagnitudeAt(steady, 2_000, Rate) / fundamental);
        var mean = 0.0;
        foreach (var v in steady) {
            mean += v;
        }

        Assert.InRange(second, -50, -15);
        Assert.True(Math.Abs(mean / steady.Length) < 1e-4, $"dc {mean / steady.Length}");
    }

    [Fact]
    public void The_tube_knee_is_wide() {
        // 3 dB under the threshold (in RMS) the tube already compresses; the plain feedback type does not.
        var tube = Steady(CompressorBlock.Tube, -20f);
        var plain = Steady(CompressorBlock.Feedback, -20f);

        Assert.True(tube < plain - 0.5, $"tube {tube:0.00} dB vs feedback {plain:0.00} dB");
    }

    [Fact]
    public void The_compander_expands_below_its_expander_threshold() {
        // A -70 dB peak tone is -73 dB RMS, 13 dB under the -60 dB expander threshold: at 2:1 it falls 13 dB more.
        var expanded = Steady(CompressorBlock.Compander, -70f, rms: true);
        var plain = Steady(CompressorBlock.Studio, -70f, rms: true);

        Assert.InRange(expanded - plain, -14, -12);
    }

    [Theory]
    [InlineData(0f, -6f, 1f)]
    [InlineData(50f, -28f, 5.75f)]
    [InlineData(100f, -50f, 20f)]
    public void The_compression_knob_sets_threshold_and_ratio_together(float knob, float threshold, float ratio) {
        var (t, r) = CompressorBlock.SingleKnob(knob);

        Assert.Equal(threshold, t, 1e-4f);
        Assert.Equal(ratio, r, 1e-4f);
    }

    [Fact]
    public void More_compression_means_a_lower_output() {
        var levels = new[] { 10f, 40f, 70f, 100f }
            .Select(static knob => Steady(CompressorBlock.Pedal, 0f, compression: knob))
            .ToArray();

        Assert.True(levels.Zip(levels.Skip(1)).All(static p => p.Second < p.First), string.Join(", ", levels));
    }

    /// <summary>The steady output peak (or RMS) in dB for a 1 kHz tone at <paramref name="levelDb" />.</summary>
    static double Steady(
        int type,
        float levelDb,
        float expanderThreshold = -60f,
        float? compression = null,
        bool rms = false
    ) {
        var input = Signals.Sine(1_000, Rate, 48_000, (float)Math.Pow(10, levelDb / 20));
        var output = Render(type, input, -20f, expanderThreshold, compression).AsSpan(24_000);
        return Analysis.ToDb(rms ? Analysis.Rms(output) * Math.Sqrt(2) : Analysis.Peak(output));
    }

    static double ThirdHarmonicDb(int type, float threshold) {
        var steady = Render(type, Signals.Sine(1_000, Rate, 48_000, 0.9f), threshold).AsSpan(24_000);
        return Analysis.ToDb(Analysis.MagnitudeAt(steady, 3_000, Rate) / Analysis.MagnitudeAt(steady, 1_000, Rate));
    }

    static float[] Render(
        int type,
        float[] input,
        float threshold,
        float expanderThreshold = -60f,
        float? compression = null
    ) {
        using var block = new CompressorBlock();
        block.Params.SetTarget(CompressorBlock.Type, type);
        block.Params.SetTarget(CompressorBlock.Threshold, threshold);
        block.Params.SetTarget(CompressorBlock.Ratio, 4f);
        block.Params.SetTarget(CompressorBlock.Knee, 0f);
        block.Params.SetTarget(CompressorBlock.ExpanderThreshold, expanderThreshold);
        if (compression is { } knob) {
            block.Params.SetTarget(CompressorBlock.Controls, 1f);
            block.Params.SetTarget(CompressorBlock.Compression, knob);
        }

        block.Prepare(new(Rate, 64));
        return BlockRender.Mono(block, input, 64);
    }
}
