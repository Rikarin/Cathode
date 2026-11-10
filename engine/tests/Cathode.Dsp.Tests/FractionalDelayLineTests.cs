using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Tests;

public class FractionalDelayLineTests {
    [Fact]
    public void Integer_delays_are_exact_and_a_ramp_interpolates_exactly() {
        using var line = new FractionalDelayLine();
        line.Allocate(64);
        for (var i = 0; i < 40; i++) {
            line.Write(i);
        }

        Assert.Equal(36f, line.Read(4f)); // the next write is sample 40
        Assert.Equal(30f, line.Read(10f));
        Assert.Equal(35.25f, line.Read(4.75f), 1e-5f); // a ramp interpolates exactly
    }

    [Theory]
    [InlineData(600_000)] // past 2^19, where a single-precision read position has 1/16-sample steps
    [InlineData(1_600_000)] // past 2^20: 1/8 sample
    public void A_fractional_read_is_as_fine_far_into_a_long_line_as_at_its_start(int offset) {
        // The same sine (64 samples a period, the offset a whole number of periods) sits behind the write index of
        // both lines, so every read must agree; only the size of the write index differs.
        Assert.Equal(0, offset % 64);
        using var near = new FractionalDelayLine();
        using var far = new FractionalDelayLine();
        near.Allocate(2_000_000);
        far.Allocate(2_000_000);
        for (var i = 0; i < 20_000 + offset; i++) {
            var sample = MathF.Sin(2 * MathF.PI * (i % 64) / 64f);
            far.Write(sample);
            if (i < 20_000) {
                near.Write(sample);
            }
        }

        var worst = 0f;
        for (var step = 0; step < 2_000; step++) {
            var delay = 18_000f + step * 0.013f; // a slow sweep, as a wow or a gliding Time makes
            worst = Math.Max(worst, MathF.Abs(far.Read(delay) - near.Read(delay)));
        }

        Assert.True(worst < 1e-5f, $"reads differ by up to {worst:0.0e0}");
    }
}
