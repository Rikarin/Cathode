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
}
