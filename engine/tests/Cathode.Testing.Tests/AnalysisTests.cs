using System.Numerics;

namespace Cathode.Testing.Tests;

public class AnalysisTests {
    [Fact]
    public void Fft_matches_naive_dft() {
        var signal = Signals.Noise(64, 3);
        var fast = signal.Select(static s => new Complex(s, 0)).ToArray();
        Fft.Forward(fast);

        for (var k = 0; k < signal.Length; k++) {
            var slow = Complex.Zero;
            for (var n = 0; n < signal.Length; n++) {
                slow += signal[n] * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * n / signal.Length);
            }

            Assert.Equal(slow.Real, fast[k].Real, 1e-9);
            Assert.Equal(slow.Imaginary, fast[k].Imaginary, 1e-9);
        }
    }

    [Fact]
    public void Coherent_sine_reads_its_amplitude_on_one_bin() {
        var magnitudes = Fft.Magnitudes(Signals.CoherentSine(10, 1024, 0.5f));

        Assert.Equal(0.5, magnitudes[10], 1e-6);
        Assert.True(magnitudes.Where(static (_, k) => k != 10).Max() < 1e-6);
    }

    [Fact]
    public void Thd_of_pure_sine_is_negligible_and_known_harmonic_is_measured() {
        var pure = Signals.CoherentSine(8, 4096);
        Assert.True(Analysis.ToDb(Analysis.Thd(pure, 8)) < -120);

        var third = Signals.CoherentSine(24, 4096, 0.1f);
        var distorted = pure.Zip(third, static (a, b) => a + b).ToArray();
        Assert.Equal(-20.0, Analysis.ToDb(Analysis.Thd(distorted, 8)), 0.01);
    }

    [Fact]
    public void Signal_to_aliasing_reads_a_known_non_harmonic_component() {
        // A sine on bin 61 with one component 40 dB under it on a bin that is no multiple of 61.
        var signal = Sum(Signals.CoherentSine(61, 8192), Signals.CoherentSine(1000, 8192, 0.01f));
        Assert.Equal(40.0, Analysis.SignalToAliasingDb(signal, 61), 0.01);

        // Nothing but the fundamental and its harmonics: no aliasing to measure.
        var harmonicOnly = Sum(Signals.CoherentSine(61, 8192), Signals.CoherentSine(183, 8192, 0.3f));
        Assert.True(Analysis.SignalToAliasingDb(harmonicOnly, 61) > 120);
    }

    [Fact]
    public void Signal_to_aliasing_counts_harmonics_as_signal_and_ignores_dc() {
        // Fundamental 1.0, 2nd harmonic 0.5, 3rd 0.25, a DC offset, and 0.01 on a non-harmonic bin:
        // (1 + 0.25 + 0.0625) / 0.0001.
        var signal = Sum(
            Signals.CoherentSine(61, 8192),
            Signals.CoherentSine(122, 8192, 0.5f),
            Signals.CoherentSine(183, 8192, 0.25f),
            Signals.CoherentSine(1000, 8192, 0.01f),
            Signals.Step(8192, 0.2f)
        );

        Assert.Equal(10 * Math.Log10(1.3125 / 1e-4), Analysis.SignalToAliasingDb(signal, 61), 0.01);
    }

    [Fact]
    public void Signal_to_aliasing_counts_the_bin_next_to_a_harmonic_as_aliasing() {
        // One bin off the 2nd harmonic is not a harmonic: 1 / 0.01.
        var signal = Sum(Signals.CoherentSine(61, 8192), Signals.CoherentSine(123, 8192, 0.1f));
        Assert.Equal(20.0, Analysis.SignalToAliasingDb(signal, 61), 0.01);
    }

    [Theory]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void Octave_bands_read_a_sine_at_minus_3_db_in_its_own_band_only(int sampleRate) {
        double[] centres = [125, 250, 500, 1000, 2000, 4000, 8000];
        var levels = Analysis.OctaveBandLevelsDb(Signals.Sine(1000, sampleRate, sampleRate), sampleRate, centres);

        for (var b = 0; b < centres.Length; b++) {
            if (centres[b] == 1000) {
                // A full-scale sine has a mean square of 1/2.
                Assert.Equal(-3.01, levels[b], 0.05);
            } else {
                Assert.True(levels[b] < -80, $"{centres[b]} Hz band reads {levels[b]:F1} dB");
            }
        }
    }

    [Theory]
    [InlineData(48_000, 1 << 18)]
    [InlineData(96_000, 1 << 19)]
    [InlineData(48_000, 200_000)] // not a power of two: the zero padding must not change the reading
    public void Octave_bands_read_white_noise_by_its_density(int sampleRate, int length) {
        // Uniform noise in [-1, 1] has a mean square of 1/3 spread evenly up to Nyquist, so a band from
        // centre / √2 to centre × √2 holds 1/3 × (centre / √2) / (sampleRate / 2), 3 dB more per octave.
        double[] centres = [250, 500, 1000, 2000, 4000, 8000];
        var levels = Analysis.OctaveBandLevelsDb(Signals.Noise(length, 7), sampleRate, centres);

        for (var b = 0; b < centres.Length; b++) {
            var expected = 10 * Math.Log10(1.0 / 3 * (centres[b] / Math.Sqrt(2)) / (sampleRate / 2.0));
            Assert.True(
                Math.Abs(levels[b] - expected) < 0.5,
                $"{centres[b]} Hz band reads {levels[b]:F2} dB, expected {expected:F2} dB"
            );
        }
    }

    [Fact]
    public void Octave_bands_read_the_same_sound_the_same_at_two_rates() {
        double[] centres = [125, 250, 500, 1000, 2000, 4000, 8000];
        var at48 = Analysis.OctaveBandLevelsDb(Signals.MultiTone(48_000, 1), 48_000, centres);
        var at96 = Analysis.OctaveBandLevelsDb(Signals.MultiTone(96_000, 1), 96_000, centres);

        for (var b = 0; b < centres.Length; b++) {
            Assert.Equal(at48[b], at96[b], 0.05);
        }

        // Three equal tones per octave from 50 Hz to 12 kHz at -20 dBFS RMS: 24 tones, three in each band.
        Assert.All(at48, static level => Assert.Equal(-20 + 10 * Math.Log10(3.0 / 24), level, 0.5));
    }

    [Fact]
    public void Dtft_magnitude_of_unit_impulse_is_flat() {
        var impulse = Signals.Impulse(16);

        foreach (var f in new[] { 20.0, 1000, 10_000, 23_000 }) {
            Assert.Equal(1.0, Analysis.MagnitudeAt(impulse, f, 48_000), 1e-12);
        }
    }

    [Fact]
    public void Esr_and_null_residual_behave_at_the_extremes() {
        var a = Signals.Noise(4800, 1);
        var silence = new float[a.Length];

        Assert.Equal(0, Analysis.Esr(a, a));
        Assert.Equal(1, Analysis.Esr(a, silence), 1e-9);
        Assert.Equal(-300, Analysis.NullResidualDb(a, a));
        Assert.Equal(Analysis.ToDb(Analysis.Rms(a)), Analysis.NullResidualDb(a, silence), 1e-9);
    }

    [Fact]
    public void Allocation_probe_sees_allocations() {
        Assert.Equal(0, AllocationProbe.Measure(static () => { }));
        Assert.True(AllocationProbe.Measure(static () => GC.KeepAlive(new byte[1024])) >= 1024);
    }

    static float[] Sum(params float[][] parts) {
        var sum = new float[parts[0].Length];
        foreach (var part in parts) {
            for (var i = 0; i < sum.Length; i++) {
                sum[i] += part[i];
            }
        }

        return sum;
    }
}
