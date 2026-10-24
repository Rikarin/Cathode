using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class VibeBlockTests {
    const double Rate = 48_000;

    [Fact]
    public void Static_chorus_notches_where_the_unequal_stages_sum_to_180_and_540_degrees() {
        using var vibe = Create((VibeBlock.Intensity, 0f), (VibeBlock.Mix, 50f));

        var response = BlockRender.Mono(vibe, Signals.Impulse(32_768), 64);

        foreach (var notch in new[] { PhaseCrossing(Math.PI), PhaseCrossing(3 * Math.PI) }) {
            Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(response, notch, Rate)) < -30, $"no notch at {notch:0} Hz");
        }

        // In between, the chain is back in phase with the dry signal.
        Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, PhaseCrossing(2 * Math.PI), Rate)), 0.1);

        // Four equal stages at the first stage's frequency would notch at fc tan(pi / 8): the Vibe does not.
        var equalStageNotch = VibeBlock.RestFrequency * Math.Tan(Math.PI / 8);
        Assert.True(Math.Abs(PhaseCrossing(Math.PI) / equalStageNotch - 1) > 0.2, "notches like an even phaser");
    }

    [Fact]
    public void Vibrato_mode_is_the_allpass_chain_alone() {
        using var vibe = Create((VibeBlock.Mode, 1f), (VibeBlock.Intensity, 0f), (VibeBlock.Mix, 50f));
        var impulse = Signals.Impulse(32_768);

        var response = BlockRender.Mono(vibe, impulse, 64);

        // No dry component: the response is flat (no notches) yet not the input.
        double[] probes = [50, PhaseCrossing(Math.PI), PhaseCrossing(2 * Math.PI), PhaseCrossing(3 * Math.PI), 10_000];
        foreach (var f in probes) {
            Assert.Equal(0, Analysis.ToDb(Analysis.MagnitudeAt(response, f, Rate)), 0.05);
        }

        Assert.True(Analysis.NullResidualDb(response.AsSpan(0, 64), impulse.AsSpan(0, 64)) > -40);
    }

    [Fact]
    public void Lamp_sweep_moves_the_notches_and_vibrato_wobbles_the_pitch() {
        using var chorus = Create();
        using var vibrato = Create((VibeBlock.Mode, 1f));
        var input = Signals.Sine(800, Rate, 96_000, 0.5f);

        var swept = BlockRender.Mono(chorus, input, 64);
        var wobbled = BlockRender.Mono(vibrato, input, 64);

        // Chorus: the level of a steady tone rises and falls as notches sweep past it.
        var levels = Enumerable.Range(1, 18)
            .Select(k => Analysis.ToDb(Analysis.Rms(swept.AsSpan(k * 4_800, 960))))
            .ToArray();
        Assert.True(
            levels.Max() - levels.Min() > 6,
            $"sweep moves the level only {levels.Max() - levels.Min():0.0} dB"
        );

        // Vibrato: an allpass never changes a steady tone's level much, but it smears the line into sidebands.
        var spectrum = Fft.Magnitudes(wobbled.AsSpan(24_000, 65_536));
        var line = (int)Math.Round(800 * 65_536 / Rate);
        double sidebands = 0;
        for (var k = line + 3; k < line + 60; k++) {
            sidebands += spectrum[k] * spectrum[k] + spectrum[2 * line - k] * spectrum[2 * line - k];
        }

        Assert.True(Math.Sqrt(sidebands) / spectrum[line] > 0.05, "no pitch modulation");
    }

    /// <summary>
    ///     Frequency where the static chain's phase lag reaches <paramref name="phase" />: each digital (prewarped TPT)
    ///     stage lags <c>2 atan(tan(pi f / fs) / tan(pi fc / fs))</c>, with the stages at the capacitor ratios.
    /// </summary>
    static double PhaseCrossing(double phase) {
        double Lag(double f) {
            double sum = 0;
            foreach (var ratio in VibeBlock.StageRatios) {
                var fc = Math.Min(Rate * 0.45, VibeBlock.RestFrequency * ratio);
                sum += 2 * Math.Atan(Math.Tan(Math.PI * f / Rate) / Math.Tan(Math.PI * fc / Rate));
            }

            return sum;
        }

        double low = 1, high = Rate / 2 - 1;
        for (var i = 0; i < 100; i++) {
            var mid = 0.5 * (low + high);
            if (Lag(mid) < phase) {
                low = mid;
            } else {
                high = mid;
            }
        }

        return low;
    }

    static VibeBlock Create(params (int Index, float Value)[] settings) {
        var block = new VibeBlock();
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }
}
