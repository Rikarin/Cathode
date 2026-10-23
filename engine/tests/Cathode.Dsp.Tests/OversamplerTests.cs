using Cathode.Dsp.Resampling;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class OversamplerTests {
    [Fact]
    public void Design_gets_more_coefficients_for_steeper_filters() {
        Assert.InRange(HalfBand.Design(100, 0.26).Length, 2, 6);
        Assert.InRange(HalfBand.Design(100, 0.04).Length, 8, 16);
        Assert.True(HalfBand.Design(100, 0.01).Length > HalfBand.Design(100, 0.04).Length);
        Assert.All(HalfBand.Design(100, 0.04), static c => Assert.InRange(c, 0.0, 1.0));
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(1_000.0)]
    [InlineData(10_000.0)]
    [InlineData(18_000.0)]
    public void Round_trip_keeps_the_audio_band_level(double frequency) {
        var oversampler = new Oversampler4x(64);
        var input = Signals.Sine(frequency, 48_000, 48_000, 0.5f);
        var output = new float[input.Length];

        for (var offset = 0; offset < input.Length; offset += 64) {
            oversampler.Up(input.AsSpan(offset, 64));
            oversampler.Down(output.AsSpan(offset, 64));
        }

        var gainDb = Analysis.ToDb(Analysis.Rms(output.AsSpan(4_800)) / Analysis.Rms(input.AsSpan(4_800)));
        Assert.InRange(gainDb, -0.1, 0.1);
    }

    [Fact]
    public void Content_above_the_base_nyquist_is_rejected_on_the_way_down() {
        // Put a 60 kHz tone directly into the 4x domain: it must not fold back into the audio band.
        var oversampler = new Oversampler4x(64);
        var output = new float[48_000];
        var phase = 0.0;
        for (var offset = 0; offset < output.Length; offset += 64) {
            var four = oversampler.Up(new float[64]);
            for (var i = 0; i < four.Length; i++) {
                four[i] = (float)(0.5 * Math.Sin(phase));
                phase += 2 * Math.PI * 60_000 / 192_000;
            }

            oversampler.Down(output.AsSpan(offset, 64));
        }

        var rejectionDb = Analysis.ToDb(Analysis.Rms(output.AsSpan(4_800)) / (0.5 / Math.Sqrt(2)));
        Assert.True(rejectionDb < -90, $"60 kHz leaked through at {rejectionDb:0.0} dB");
    }
}
