using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class ModulationBlockTests {
    const double Rate = 48_000;

    [Fact]
    public void Static_flanger_is_a_comb_with_notches_at_odd_multiples_of_half_the_inverse_delay() {
        // 1 ms delay, no sweep, no feedback, 50 % mix under the constant-power law: H = (1 + z^-48) / sqrt 2, notches
        // at 500, 1500, 2500 Hz and peaks of +3 dB.
        using var flanger = Create<FlangerBlock>(
            (FlangerBlock.Depth, 0f),
            (FlangerBlock.Delay, 1f),
            (FlangerBlock.Feedback, 0f),
            (FlangerBlock.Mix, 50f)
        );

        var response = BlockRender.Mono(flanger, Signals.Impulse(4_096), 64);

        foreach (var notch in new[] { 500.0, 1_500, 2_500 }) {
            Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, notch, Rate)) < -40, $"no notch at {notch} Hz");
        }

        Assert.Equal(3.01, Analysis.ToDb(Analysis.MagnitudeAt(response, 1_000, Rate)), 0.05); // peaks in between
    }

    [Fact]
    public void Static_phaser_notches_where_the_chain_reaches_180_and_540_degrees() {
        // Four identical first-order allpasses at fc: phase = -8 atan(f / fc), so notches at fc tan(pi/8) and fc tan(3pi/8).
        using var phaser = Create<PhaserBlock>(
            (PhaserBlock.Depth, 0f),
            (PhaserBlock.Center, 1_000f),
            (PhaserBlock.Stages, 1f),
            (PhaserBlock.Mix, 50f)
        );

        var response = BlockRender.Mono(phaser, Signals.Impulse(16_384), 64);

        foreach (var notch in new[] { 1_000 * Math.Tan(Math.PI / 8), 1_000 * Math.Tan(3 * Math.PI / 8) }) {
            Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, notch, Rate)) < -30, $"no notch at {notch:0} Hz");
        }
    }

    [Fact]
    public void Chorus_at_zero_mix_is_dry_and_otherwise_widens_the_image() {
        using var dry = Create<ChorusBlock>((ChorusBlock.Mix, 0f));
        using var wide = Create<ChorusBlock>();
        var input = Signals.Noise(9_600, 1);

        Assert.Equal(input, BlockRender.Mono(dry, input, 64));
        var (left, right) = BlockRender.Stereo(wide, input, input, static _ => 64);
        Assert.True(Analysis.NullResidualDb(left, right) > -40, "left and right should differ");
    }

    [Theory]
    [InlineData(100f, 0f)]
    [InlineData(50f, 0.5f)]
    public void Tremolo_depth_sets_the_trough(float depth, float trough) {
        using var tremolo = Create<TremoloBlock>((TremoloBlock.Depth, depth), (TremoloBlock.Rate, 5f));

        var output = BlockRender.Mono(tremolo, Signals.Step(48_000), 64);

        Assert.Equal(trough, output.Min(), 0.01f);
        Assert.Equal(1f, output.Max(), 0.01f);
    }

    [Fact]
    public void Panner_keeps_constant_power() {
        using var panner = Create<TremoloBlock>(
            (TremoloBlock.Type, 1f),
            (TremoloBlock.Depth, 100f),
            (TremoloBlock.Rate, 3f)
        );

        var (left, right) = BlockRender.Stereo(panner, Signals.Step(24_000), Signals.Step(24_000), static _ => 64);

        for (var i = 0; i < left.Length; i += 97) {
            Assert.Equal(2f, left[i] * left[i] + right[i] * right[i], 1e-3f);
        }

        Assert.True(left.Min() < 0.01f && right.Min() < 0.01f, "full depth reaches each side");
    }

    static T Create<T>(params (int Index, float Value)[] settings)
        where T : IDspBlock, new() {
        var block = new T();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
