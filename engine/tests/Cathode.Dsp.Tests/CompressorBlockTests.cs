using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class CompressorBlockTests {
    [Theory]
    [InlineData(-30f, 0f, 4f, 0f, -30f)] // below threshold: unchanged
    [InlineData(0f, -20f, 4f, 0f, -15f)] // 20 dB over at 4:1 -> 5 dB over
    [InlineData(-10f, -20f, 2f, 0f, -15f)]
    [InlineData(-20f, -20f, 4f, 12f, -21.125f)] // at the threshold, the knee gives (1/R - 1) * W / 8
    [InlineData(-10f, -20f, 4f, 12f, -17.5f)] // above the knee: back on the straight line
    public void Static_curve_matches_the_formula(
        float input,
        float threshold,
        float ratio,
        float knee,
        float expected
    ) =>
        Assert.Equal(expected, CompressorBlock.StaticCurve(input, threshold, ratio, knee), 1e-4f);

    [Fact]
    public void Pedal_compresses_a_steady_tone_to_the_static_curve() {
        using var block = Create(0);
        var input = Signals.Sine(1_000, 48_000, 48_000); // 0 dBFS peak

        var output = BlockRender.Mono(block, input, 64);

        var peakDb = Analysis.ToDb(Analysis.Peak(output.AsSpan(24_000)));
        Assert.InRange(peakDb, -16, -14); // expected -15 dBFS
    }

    [Fact]
    public void Studio_detects_rms() {
        using var block = Create(1);
        var input = Signals.Sine(1_000, 48_000, 48_000); // -3.01 dBFS RMS

        var output = BlockRender.Mono(block, input, 64);

        var rmsDb = Analysis.ToDb(Analysis.Rms(output.AsSpan(24_000)));
        Assert.InRange(rmsDb, -16.25, -15.25); // -20 + (-3.01 + 20) / 4 = -15.75
    }

    [Fact]
    public void Quiet_signals_pass_untouched() {
        using var block = Create(0);
        var input = Signals.Sine(1_000, 48_000, 9_600, Db(-30));

        Assert.Equal(input, BlockRender.Mono(block, input, 64));
    }

    [Fact]
    public void Opto_recovers_more_slowly_after_heavy_compression() {
        static float RecoveryDb(float type) {
            using var block = Create(type, -30, 10, release: 100);
            BlockRender.Mono(block, Signals.Sine(1_000, 48_000, 24_000), 64);
            BlockRender.Mono(block, new float[4_800], 64); // 100 ms of silence
            return block.GainReductionDb;
        }

        Assert.True(
            RecoveryDb(2) < RecoveryDb(0) - 3,
            $"opto {RecoveryDb(2):0.0} dB vs pedal {RecoveryDb(0):0.0} dB still reduced after 100 ms"
        );
    }

    static float Db(double db) => (float)Math.Pow(10, db / 20);

    static CompressorBlock Create(
        float type,
        float threshold = -20,
        float ratio = 4,
        float knee = 0,
        float release = 150
    ) {
        var block = new CompressorBlock();
        block.Params.SetTarget(CompressorBlock.Type, type);
        block.Params.SetTarget(CompressorBlock.Threshold, threshold);
        block.Params.SetTarget(CompressorBlock.Ratio, ratio);
        block.Params.SetTarget(CompressorBlock.Knee, knee);
        block.Params.SetTarget(CompressorBlock.Release, release);
        block.Prepare(new(48_000, 64));
        return block;
    }
}
