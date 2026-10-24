using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DynamicDistortionBlockTests {
    const int Length = 8_192;
    const int Bin = 43; // ~252 Hz

    [Fact]
    public void Thd_rises_with_input_level_faster_than_a_static_drive() {
        var staticRise = ThdRise(0f);
        var dynamicRise = ThdRise(100f);

        Assert.True(dynamicRise > 4 * staticRise, $"THD rose {dynamicRise:0}x dynamic vs {staticRise:0}x static");
    }

    [Fact]
    public void Negative_dynamics_clean_up_loud_playing() {
        var staticThd = Thd(-6, 0f, 5f);
        var cleaningThd = Thd(-6, -100f, 5f);

        Assert.True(cleaningThd < staticThd / 4, $"THD {cleaningThd:0.000} vs static {staticThd:0.000}");
    }

    [Fact]
    public void Below_threshold_it_matches_the_static_drive() {
        Assert.Equal(Thd(-50, 0f, 5f), Thd(-50, 100f, 5f), 1e-4);
    }

    /// <summary>How many times larger the THD is at -12 dBFS than at -30 dBFS.</summary>
    static double ThdRise(float dynamics) => Thd(-12, dynamics) / Thd(-30, dynamics);

    static double Thd(double levelDb, float dynamics, float drive = 0f) {
        using var block = TestBlocks.Create<DynamicDistortionBlock>(
            (DynamicDistortionBlock.Drive, drive),
            (DynamicDistortionBlock.Dynamics, dynamics),
            (DynamicDistortionBlock.Threshold, -40f),
            (DynamicDistortionBlock.Release, 500f)
        );
        var input = Signals.Sine(Bin * 48_000.0 / Length, 48_000, 3 * Length, (float)Math.Pow(10, levelDb / 20));

        var output = BlockRender.Mono(block, input, 64);

        return Analysis.Thd(output.AsSpan(2 * Length), Bin);
    }
}
