using Cathode.Audio;
using Cathode.Engine;

namespace Cathode.Engine.Tests;

public class LatencyProbeTests {
    [Theory]
    [InlineData(64, 371)]
    [InlineData(32, 128)]
    [InlineData(128, 1_000)]
    public void Measures_a_simulated_loopback_delay(int blockSize, int delay) {
        var probe = new LatencyProbe(48_000);
        var loopback = new float[delay + blockSize];
        var random = new Random(3);
        var input = new float[blockSize];
        var outLeft = new float[blockSize];
        var outRight = new float[blockSize];
        var written = 0L;

        for (var callback = 0; callback < 48_000 * 4 / blockSize; callback++) {
            // What arrives now is what was played `delay` samples ago, attenuated, plus a little noise.
            for (var i = 0; i < blockSize; i++) {
                var t = written + i - delay;
                input[i] = (t >= 0 ? loopback[t % loopback.Length] * 0.3f : 0f)
                    + (float)(random.NextDouble() - 0.5) * 0.002f;
            }

            probe.Process(new AudioIO(input, input, outLeft, outRight));
            for (var i = 0; i < blockSize; i++) {
                loopback[(written + i) % loopback.Length] = outLeft[i];
            }

            written += blockSize;
        }

        var lags = probe.Measurements();
        Assert.True(lags.Length >= 10, $"only {lags.Length} measurements");
        Assert.All(lags, lag => Assert.Equal(delay, lag));
        Assert.Equal(0, probe.Missed);
    }

    [Fact]
    public void Reports_missed_clicks_when_nothing_comes_back() {
        var probe = new LatencyProbe(48_000);
        var silence = new float[64];
        var outLeft = new float[64];
        var outRight = new float[64];

        for (var callback = 0; callback < 48_000 * 2 / 64; callback++) {
            probe.Process(new AudioIO(silence, silence, outLeft, outRight));
        }

        Assert.Equal(0, probe.Count);
        Assert.True(probe.Missed > 0);
        Assert.Null(LatencyProbe.Median(probe.Measurements()));
    }
}
