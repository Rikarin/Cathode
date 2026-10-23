using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class EnhancerBlockTests {
    [Theory]
    [InlineData(0f)] // Modern
    [InlineData(1f)] // Classic
    public void Widens_a_mono_source_while_the_mono_sum_matches_the_input(float type) {
        using var block = TestBlocks.Create<EnhancerBlock>((EnhancerBlock.Type, type), (EnhancerBlock.Width, 100f));
        var input = Signals.Noise(48_000, 4, 0.5f);

        var (left, right) = BlockRender.Stereo(block, input, input, _ => 64);
        var mono = left.Zip(right, static (l, r) => 0.5f * (l + r)).ToArray();

        Assert.True(Analysis.NullResidualDb(input, mono) < -130, "mono sum should equal the input");
        Assert.True(Correlation(left, right) < 0.5, $"correlation {Correlation(left, right):0.00}");
    }

    [Fact]
    public void Zero_width_is_transparent() {
        using var block = TestBlocks.Create<EnhancerBlock>((EnhancerBlock.Width, 0f));
        var input = Signals.Noise(4_800, 5);

        var (left, right) = BlockRender.Stereo(block, input, input, _ => 64);

        Assert.True(Analysis.NullResidualDb(input, left) < -140);
        Assert.True(Analysis.NullResidualDb(input, right) < -140);
    }

    [Fact]
    public void Low_cut_keeps_the_bass_centred() {
        using var block = TestBlocks.Create<EnhancerBlock>((EnhancerBlock.Width, 100f), (EnhancerBlock.LowCut, 500f));
        var input = Signals.Sine(30, 48_000, 48_000, 0.5f); // 4 octaves below the cut: about -49 dB of side

        var (left, right) = BlockRender.Stereo(block, input, input, _ => 64);

        var side = left.Zip(right, static (l, r) => 0.5f * (l - r)).ToArray();
        Assert.True(Analysis.ToDb(Analysis.Rms(side.AsSpan(24_000)) / Analysis.Rms(input)) < -40);
    }

    [Fact]
    public void Haas_delays_the_right_channel() {
        using var block = TestBlocks.Create<EnhancerBlock>(
            (EnhancerBlock.Type, 2f),
            (EnhancerBlock.Width, 100f),
            (EnhancerBlock.Time, 10f)
        );
        var impulse = Signals.Impulse(4_800, 1f, 100);

        var (left, right) = BlockRender.Stereo(block, impulse, impulse, _ => 64);

        Assert.Equal(100, Analysis.PeakIndex(left));
        Assert.InRange(Analysis.PeakIndex(right), 100 + 478, 100 + 481); // 10 ms
    }

    static double Correlation(float[] a, float[] b) {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) {
            ab += (double)a[i] * b[i];
            aa += (double)a[i] * a[i];
            bb += (double)b[i] * b[i];
        }

        return ab / Math.Sqrt(aa * bb);
    }
}
