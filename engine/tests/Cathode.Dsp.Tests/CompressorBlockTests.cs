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
    public void The_gain_reduction_meter_reads_the_reduction_as_positive_dB_and_0_below_the_threshold() {
        using var block = Create(0);
        var meter = new float[1];
        Assert.Equal(["gainReduction"], block.MeterIds);

        BlockRender.Mono(block, Signals.Sine(1_000, 48_000, 48_000), 64); // 0 dBFS: 20 dB over at 4:1
        block.ReadMeters(meter);
        Assert.InRange(meter[0], 12f, 15.5f); // up to 15 dB at the sine's peaks, released a little between them
        Assert.Equal(-block.GainReductionDb, meter[0]);

        block.Reset();
        BlockRender.Mono(block, Signals.Sine(1_000, 48_000, 9_600, Db(-30)), 64);
        block.ReadMeters(meter);
        Assert.Equal(0f, meter[0]);

        meter[0] = 99f;
        ((IBlockMeters)block).ReadRestingMeters(meter); // what a bypassed compressor reads
        Assert.Equal(0f, meter[0]);
    }

    [Fact]
    public void The_meter_counts_the_companders_expander_but_not_the_sustainers_boost() {
        var meter = new float[1];
        using var compander = Create(CompressorBlock.Compander);
        BlockRender.Mono(compander, Signals.Sine(1_000, 48_000, 48_000, Db(-80)), 64); // 20 dB under its expander
        compander.ReadMeters(meter);
        Assert.True(meter[0] > 10f, $"compander {meter[0]:0.0} dB");

        using var sustainer = Create(CompressorBlock.Sustainer);
        BlockRender.Mono(sustainer, Signals.Sine(1_000, 48_000, 48_000, Db(-40)), 64);
        sustainer.ReadMeters(meter);
        Assert.True(sustainer.GainReductionDb > 1f, "the sustainer raises a quiet signal");
        Assert.Equal(0f, meter[0]);
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

    [Theory]
    [InlineData(CompressorBlock.Pedal, 4f, 0f, 7.5f)] // 0 dBFS comes out at -15: half of 15 dB
    [InlineData(CompressorBlock.Pedal, 4f, CompressorBlock.LineLevelDb, 3f)] // read at -12: 6 dB of reduction
    [InlineData(CompressorBlock.Sustainer, 4f, 0f, 0f)] // raises quiet signals only
    [InlineData(CompressorBlock.DynamicsProcessor, 2f, 0f, 5f)] // Dynamics 50 %
    [InlineData(CompressorBlock.DynamicsProcessor, 2f / 3, 0f, 0f)] // Dynamics -50 %: expands upwards
    public void Auto_makeup_is_half_the_reduction_of_a_full_scale_signal(
        int type,
        float ratio,
        float lineDb,
        float expected
    ) =>
        Assert.Equal(expected, CompressorBlock.AutoMakeupDb(type, -20f, ratio, 0f, lineDb), 1e-4f);

    [Theory]
    [InlineData(CompressorBlock.Sustainer, 50f)]
    [InlineData(CompressorBlock.DynamicsProcessor, -50f)]
    public void Auto_makeup_leaves_types_that_do_not_reduce_loud_signals_alone(int type, float dynamics) {
        float[] Render(bool autoMakeup) {
            using var block = Create(type);
            block.Params.SetTarget(CompressorBlock.Dynamics, dynamics);
            block.Params.SetTarget(CompressorBlock.AutoMakeup, autoMakeup ? 1f : 0f);
            block.Reset();
            return BlockRender.Mono(block, Signals.Sine(1_000, 48_000, 24_000, Db(-6)), 64);
        }

        Assert.Equal(Render(false), Render(true));
    }

    [Fact]
    public void Ratio_is_hidden_on_the_dynamics_type_whose_dynamics_knob_sets_it() {
        var labels = CompressorBlock.Info.Params[CompressorBlock.Ratio].TypeLabels!;

        for (var type = 0; type < CompressorBlock.TypeCount; type++) {
            Assert.Equal(type == CompressorBlock.DynamicsProcessor ? string.Empty : "Ratio", labels[type]);
        }
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
