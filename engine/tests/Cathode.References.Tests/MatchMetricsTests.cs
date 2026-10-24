using Cathode.Testing;
using Cathode.Testing.References;

namespace Cathode.References.Tests;

public class MatchMetricsTests {
    const double Rate = 48_000;

    [Theory]
    [InlineData(37, 0.5)]
    [InlineData(-12, 2.0)]
    [InlineData(5, -1.0)]
    public void Alignment_recovers_latency_gain_and_polarity(int lag, double gain) {
        var target = MatchSignals.Performance(Rate, [0.3], 0.1);
        var prediction = new float[target.Length];
        for (var i = 0; i < target.Length; i++) {
            var j = i - lag;
            prediction[i] = j >= 0 && j < target.Length ? (float)(target[j] / gain) : 0f;
        }

        var alignment = MatchMetrics.Align(target, prediction);

        Assert.Equal(lag, alignment.Lag);
        Assert.Equal(gain, alignment.Gain, 3);
        Assert.True(
            Analysis.Esr(
                target.AsSpan(1_000, target.Length - 2_000),
                alignment.Apply(prediction).AsSpan(1_000, target.Length - 2_000),
                0
            )
            < 1e-6
        );
    }

    [Fact]
    public void Mrstft_is_zero_for_identical_signals_and_grows_with_the_difference() {
        var target = MatchSignals.Performance(Rate, [0.3], 0.1);
        var slightly = target.Select(static (v, i) => v + 0.01f * MathF.Sin(i * 0.3f) * MathF.Abs(v)).ToArray();
        var clipped = target.Select(static v => MathF.Tanh(8 * v) / 8).ToArray();

        var same = MatchMetrics.Mrstft(target, target);
        var small = MatchMetrics.Mrstft(target, slightly);
        var large = MatchMetrics.Mrstft(target, clipped);

        Assert.Equal(0, same, 9);
        Assert.InRange(small, 1e-4, large);
        Assert.True(large > 0.2, $"hard clipping only scored {large}");
    }

    [Fact]
    public void Harmonic_profile_reads_a_known_second_harmonic() {
        // x + 0.1 x² on a unit sine: H2 = 0.05, so -26 dB under the fundamental; nothing above it.
        var tone = MatchSignals.HarmonicTone(Rate, 1, out var cycles);
        var output = tone.Select(static x => x + 0.1f * x * x).ToArray();

        var profile = MatchMetrics.HarmonicProfile(output, cycles);

        Assert.Equal(0, profile[0], 1);
        Assert.Equal(Analysis.ToDb(0.05), profile[1], 1);
        Assert.Equal(MatchMetrics.HarmonicFloorDb, profile[2]);
    }

    [Fact]
    public void Frequency_response_of_a_one_pole_low_pass() {
        var sweep = MatchSignals.Sweep(Rate);
        var output = new float[sweep.Length];
        var a = Math.Exp(-2 * Math.PI * 1_000 / Rate);
        double state = 0;
        for (var i = 0; i < sweep.Length; i++) {
            state = (1 - a) * sweep[i] + a * state;
            output[i] = (float)state;
        }

        var flat = MatchMetrics.FrequencyResponse(sweep, sweep, Rate);
        var response = MatchMetrics.FrequencyResponse(sweep, output, Rate);
        var at = (double f) => response[Array.FindIndex(MatchMetrics.ResponseCenters, c => Math.Abs(c / f - 1) < 0.05)];

        Assert.All(flat, static db => Assert.Equal(0, db, 6));
        Assert.Equal(0, at(100), 0);
        Assert.Equal(-3, at(1_000), 0);
        Assert.InRange(at(10_000), -20.5, -19); // the digital pole sits slightly above the analogue -20 dB
        Assert.True(MatchMetrics.ResponseDistance(flat, flat).RmsDb < 1e-9);
    }

    [Fact]
    public void Calibration_follows_the_NAM_convention() {
        Assert.Equal(4.12, Calibration.VoltsAtFullScale(11.5), 2);
        Assert.Equal(11.5, Calibration.DbuForVoltsAtFullScale(Calibration.VoltsAtFullScale(11.5)), 9);
        Assert.Equal(0.25f, Calibration.ToDigital([1.03f], 11.5)[0], 2);
    }
}
