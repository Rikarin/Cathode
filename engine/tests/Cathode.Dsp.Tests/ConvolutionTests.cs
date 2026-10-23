using Cathode.Dsp.Convolution;
using Cathode.Testing;
using CsCheck;
using Fft = Cathode.Dsp.Convolution.Fft;

namespace Cathode.Dsp.Tests;

public class ConvolutionTests {
    [Fact]
    public void Fft_round_trips_and_matches_a_known_spectrum() {
        var fft = new Fft(16);
        var re = Signals.Noise(16, 3);
        var im = new float[16];
        var original = (float[])re.Clone();

        fft.Forward(re, im);
        Assert.Equal(original.Sum(), re[0], 1e-4f); // DC bin is the sum
        fft.Inverse(re, im);

        for (var i = 0; i < 16; i++) {
            Assert.Equal(original[i], re[i], 1e-5f);
            Assert.Equal(0f, im[i], 1e-5f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1_000)]
    [InlineData(4_096)]
    public void Matches_direct_convolution_for_any_ir_length_and_block_sizes(int irLength) {
        var ir = Signals.Noise(irLength, irLength).Select((v, i) => v * MathF.Exp(-i / 800f)).ToArray();
        var input = Signals.Noise(6_000, 1, 0.5f);
        var expected = Direct(input, ir);
        var random = new Random(irLength);

        var convolver = new Convolver(ir);
        var output = (float[])input.Clone();
        for (var offset = 0; offset < output.Length;) {
            var frames = Math.Min(random.Next(1, 300), output.Length - offset);
            convolver.Process(output.AsSpan(offset, frames));
            offset += frames;
        }

        var relativeDb = Analysis.NullResidualDb(output, expected) - Analysis.ToDb(Analysis.Rms(expected));
        Assert.True(relativeDb < -100, $"residual {relativeDb:0.0} dB");
    }

    [Fact]
    public void Has_zero_latency() {
        var ir = Signals.Noise(500, 2);
        var convolver = new Convolver(ir);
        var output = Signals.Impulse(600);

        convolver.Process(output);

        for (var i = 0; i < ir.Length; i++) {
            Assert.Equal(ir[i], output[i], 1e-5f);
        }
    }

    [Fact]
    public void Reset_restores_a_fresh_state_and_processing_does_not_allocate() {
        var ir = Signals.Noise(2_048, 4);
        var input = Signals.Noise(1_024, 5);
        var convolver = new Convolver(ir);
        var first = (float[])input.Clone();
        convolver.Process(first);
        convolver.Reset();
        var second = (float[])input.Clone();

        // Warm up on scratch data (JIT tiering), then reset again so the measured call still starts fresh.
        var allocated = AllocationProbe.Measure(
            () => convolver.Process(second),
            () => {
                for (var i = 0; i < 50; i++) {
                    convolver.Process((float[])input.Clone());
                }

                convolver.Reset();
            }
        );

        Assert.Equal(first, second);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Linearity_holds_for_random_signals() =>
        Gen.Float[-1f, 1f]
            .Array[300]
            .Select(Gen.Float[-2f, 2f])
            .Sample(
                (x, a) => {
                    var ir = Signals.Noise(200, 7);
                    var scaled = x.Select(v => v * a).ToArray();
                    var c1 = new Convolver(ir);
                    var c2 = new Convolver(ir);
                    var y = (float[])x.Clone();
                    c1.Process(y);
                    c2.Process(scaled);
                    for (var i = 0; i < y.Length; i++) {
                        Assert.Equal(y[i] * a, scaled[i], 1e-4f);
                    }
                },
                iter: 50
            );

    [Theory]
    [InlineData(44_100, 48_000)]
    [InlineData(96_000, 48_000)]
    [InlineData(48_000, 44_100)]
    public void Resampling_preserves_an_in_band_sine(double from, double to) {
        const double frequency = 1_000;
        var input = Signals.Sine(frequency, from, (int)from / 4);
        var output = Resampler.Resample(input, from, to);
        var expected = Signals.Sine(frequency, to, output.Length);

        // Ignore the filter's edges.
        var start = 200;
        var length = output.Length - 400;
        var relativeDb = Analysis.NullResidualDb(output.AsSpan(start, length), expected.AsSpan(start, length));
        Assert.Equal((int)Math.Round(input.Length * to / from), output.Length);
        Assert.True(relativeDb < -70, $"residual {relativeDb:0.0} dB");
    }

    static float[] Direct(float[] x, float[] h) {
        var y = new float[x.Length];
        for (var n = 0; n < x.Length; n++) {
            double sum = 0;
            for (var k = 0; k < h.Length && k <= n; k++) {
                sum += (double)h[k] * x[n - k];
            }

            y[n] = (float)sum;
        }

        return y;
    }
}
