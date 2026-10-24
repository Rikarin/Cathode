using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The grid's Input (noise gate, level) and Output (row mixer, main level and balance) blocks.</summary>
public class InputOutputBlockTests {
    const int Rate = 48_000;

    [Fact]
    public void A_default_input_is_a_wire() {
        using var input = TestBlocks.Create<InputBlock>();
        var signal = Signals.Sine(440, Rate, 4_800, 0.001f);

        Assert.Equal(signal, BlockRender.Mono(input, signal, 64));
    }

    [Theory]
    [InlineData(2f, -10f)]
    [InlineData(3f, -20f)]
    public void The_classic_gate_expands_below_the_threshold(float ratio, float expectedDb) {
        using var input = TestBlocks.Create<InputBlock>(
            (InputBlock.GateType, InputBlock.Classic),
            (InputBlock.Threshold, -60f),
            (InputBlock.Ratio, ratio)
        );
        var quiet = Signals.Sine(1_000, Rate, 48_000, Decibels.ToGain(-70f));

        BlockRender.Mono(input, quiet, 64);

        Assert.Equal(expectedDb, 20 * MathF.Log10(input.CurrentGain), 0.5f);
    }

    [Fact]
    public void The_global_gate_offset_moves_the_threshold() {
        using var input = TestBlocks.Create<InputBlock>(
            (InputBlock.GateType, InputBlock.Classic),
            (InputBlock.Threshold, -60f)
        );
        var quiet = Signals.Sine(1_000, Rate, 48_000, Decibels.ToGain(-70f));
        var left = (float[])quiet.Clone();
        var right = (float[])quiet.Clone();

        for (var offset = 0; offset < quiet.Length; offset += 64) {
            input.Process(
                new(left.AsSpan(offset, 64), right.AsSpan(offset, 64)),
                new(Rate, 120, offset, GateOffsetDb: -20f)
            );
        }

        Assert.Equal(1f, input.CurrentGain, 1e-3f);
    }

    [Fact]
    public void The_intelligent_gate_is_not_held_open_by_mains_hum() {
        // Hum at 50 Hz and its harmonics, 15 dB above the threshold.
        var hum = new float[48_000];
        for (var i = 0; i < hum.Length; i++) {
            var t = i / (double)Rate;
            hum[i] = (float)(Decibels.ToGain(-45.0) * (Math.Sin(2 * Math.PI * 50 * t) + 0.5 * Math.Sin(2 * Math.PI * 100 * t)
                + 0.3 * Math.Sin(2 * Math.PI * 150 * t)) / 1.8);
        }

        using var classic = TestBlocks.Create<InputBlock>(
            (InputBlock.GateType, InputBlock.Classic),
            (InputBlock.Threshold, -60f)
        );
        using var intelligent = TestBlocks.Create<InputBlock>(
            (InputBlock.GateType, InputBlock.Intelligent),
            (InputBlock.Threshold, -60f),
            (InputBlock.Mains, 0f)
        );
        BlockRender.Mono(classic, hum, 64);
        BlockRender.Mono(intelligent, hum, 64);

        Assert.Equal(1f, classic.CurrentGain, 1e-3f);
        Assert.True(intelligent.CurrentGain < 0.1f, $"gain {intelligent.CurrentGain}");
    }

    [Fact]
    public void The_noise_reducer_only_gates_the_highs() {
        using var reducer = TestBlocks.Create<InputBlock>(
            (InputBlock.GateType, InputBlock.NoiseReducer),
            (InputBlock.Threshold, -40f),
            (InputBlock.Ratio, 10f)
        );
        var lows = BlockRender.Mono(reducer, Signals.Sine(80, Rate, 48_000, Decibels.ToGain(-60f)), 64);
        reducer.Reset();
        var highs = BlockRender.Mono(reducer, Signals.Sine(8_000, Rate, 48_000, Decibels.ToGain(-60f)), 64);

        var lowsDb = 20 * Math.Log10(Analysis.Rms(lows.AsSpan(24_000)) * Math.Sqrt(2) / Decibels.ToGain(-60.0));
        var highsDb = 20 * Math.Log10(Analysis.Rms(highs.AsSpan(24_000)) * Math.Sqrt(2) / Decibels.ToGain(-60.0));
        Assert.True(lowsDb > -1.5, $"lows {lowsDb:F1} dB");
        Assert.True(highsDb < -20, $"highs {highsDb:F1} dB");
    }

    [Fact]
    public void Input_level_scales_the_signal() {
        using var input = TestBlocks.Create<InputBlock>((InputBlock.Level, -6.0206f));

        var output = BlockRender.Mono(input, Signals.Step(256), 64);

        Assert.All(output, static s => Assert.Equal(0.5f, s, 1e-4f));
    }

    [Fact]
    public void Output_rows_have_their_own_level_and_balance() {
        using var output = TestBlocks.Create<OutputBlock>(
            (OutputBlock.RowLevel(0), -6.0206f),
            (OutputBlock.RowBalance(1), 100f),
            (OutputBlock.Level, 6.0206f)
        );
        var row1 = Signals.Step(64);
        var row2 = Signals.Step(64);
        var left = new float[64];
        var right = new float[64];
        var destination = new StereoBuffer(left, right);

        output.MixInput(0, new(row1, (float[])row1.Clone()), destination);
        output.MixInput(1, new(row2, (float[])row2.Clone()), destination);
        output.Process(destination, new(Rate, 120, 0));

        // Row 1 at half on both sides; row 2 panned hard right; then +6 dB.
        Assert.All(left, static s => Assert.Equal(1f, s, 1e-3f));
        Assert.All(right, static s => Assert.Equal(3f, s, 1e-3f));
    }

    [Fact]
    public void A_single_output_row_scales_in_place_like_the_mix() {
        using var mixed = TestBlocks.Create<OutputBlock>((OutputBlock.RowLevel(2), -6.0206f), (OutputBlock.RowBalance(2), -50f));
        using var scaled = TestBlocks.Create<OutputBlock>((OutputBlock.RowLevel(2), -6.0206f), (OutputBlock.RowBalance(2), -50f));
        var signal = Signals.Sine(300, Rate, 64);
        var (mixLeft, mixRight) = (new float[64], new float[64]);
        var (inLeft, inRight) = ((float[])signal.Clone(), (float[])signal.Clone());

        mixed.MixInput(2, new(signal, (float[])signal.Clone()), new(mixLeft, mixRight));
        scaled.ScaleInput(2, new(inLeft, inRight));

        Assert.Equal(mixLeft, inLeft);
        Assert.Equal(mixRight, inRight);
    }
}
