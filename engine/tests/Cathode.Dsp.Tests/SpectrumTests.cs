using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Spectrum;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class SpectrumTests {
    const double Rate = 48_000;

    [Fact]
    public void A_sine_lands_in_its_rta_band() {
        var spectrum = new PowerSpectrum();
        spectrum.AddSignal(Signals.Sine(1_000, Rate, 48_000, 0.5f));
        var centers = FractionalOctave.Centers(6);

        var bands = FractionalOctave.Bands(spectrum.Mean(), Rate, centers, 6);

        var loudest = Array.IndexOf(bands, bands.Max());
        Assert.Equal(1_000, centers[loudest], 1);
        Assert.Equal(20 * Math.Log10(0.5 / Math.Sqrt(2)), bands[loudest] - 10 * Math.Log10(4_096.0 / 2), 1.0);
    }

    [Theory]
    [InlineData(SpectrumWindow.Hann)]
    [InlineData(SpectrumWindow.BlackmanHarris)]
    [InlineData(SpectrumWindow.FlatTop)]
    [InlineData(SpectrumWindow.Rectangular)]
    public void Every_window_reads_a_sine_at_the_same_band_level(SpectrumWindow window) {
        // 1 kHz falls between bins (11.72 Hz apart), so the window's leakage is in play; its energy is normalised out.
        var spectrum = new PowerSpectrum(4_096, window);
        spectrum.AddSignal(Signals.Sine(1_000, Rate, 48_000, 0.5f));
        var centers = FractionalOctave.Centers(3);

        var bands = FractionalOctave.Bands(spectrum.Mean(), Rate, centers, 3);

        var loudest = Array.IndexOf(bands, bands.Max());
        Assert.Equal(1_000, centers[loudest], 1);
        Assert.Equal(20 * Math.Log10(0.5 / Math.Sqrt(2)), bands[loudest] - 10 * Math.Log10(4_096.0 / 2), 1.0);
    }

    [Fact]
    public void Silence_is_left_out_of_the_average() {
        var spectrum = new PowerSpectrum();
        spectrum.AddSignal(Signals.Concat(Signals.Noise(16_384, 1, 0.1f), new float[16_384]));

        Assert.Equal(8, spectrum.Frames); // the 8 frames that touch the noise; the silent ones are skipped
    }

    [Fact]
    public void Minimum_phase_design_follows_the_magnitude_and_front_loads_the_energy() {
        // A +9 dB high shelf at 2 kHz, on the 4096-point grid.
        var shelf = BiquadCoefficients.Design(BiquadType.HighShelf, Rate, 2_000, 0.7071, 9);
        var db = new double[2_049];
        for (var k = 0; k < db.Length; k++) {
            db[k] = 20 * Math.Log10(shelf.MagnitudeAt(k * Rate / 4_096, Rate));
        }

        var filter = MinimumPhase.Design(db, 2_048);

        foreach (var hz in new[] { 100.0, 1_000, 2_000, 8_000 }) {
            Assert.Equal(
                20 * Math.Log10(shelf.MagnitudeAt(hz, Rate)),
                Analysis.ToDb(Analysis.MagnitudeAt(filter, hz, Rate)),
                0.25
            );
        }

        var energy = filter.Sum(static v => v * (double)v);
        var head = filter.Take(64).Sum(static v => v * (double)v);
        Assert.True(head / energy > 0.99, $"only {head / energy:P2} of the energy in the first 64 taps");
    }

    [Fact]
    public void Tone_match_turns_the_rigs_spectrum_into_the_references() {
        // The "reference" is the rig's sound through a mid boost and a treble cut; matching must recover that EQ.
        var rig = Signals.Noise(480_000, 4, 0.2f);
        var target = (float[])rig.Clone();
        Filter(target, BiquadCoefficients.Design(BiquadType.Peak, Rate, 800, 1.0, 6));
        Filter(target, BiquadCoefficients.Design(BiquadType.HighShelf, Rate, 4_000, 0.7071, -8));

        var filter = ToneMatchDesign.Design(Mean(target), Mean(rig), Rate);
        var matched = Convolve(rig, filter);

        var error = Bands(matched).Zip(Bands(target), static (a, b) => a - b).ToArray();
        var offset = error.Average(); // Tone Match is level-neutral; compare the shape
        Assert.All(error, e => Assert.InRange(e - offset, -1.0, 1.0));
    }

    [Fact]
    public void Matching_again_through_the_correction_replaces_it_instead_of_stacking() {
        var rig = Signals.Noise(480_000, 5, 0.2f);
        var target = (float[])rig.Clone();
        Filter(target, BiquadCoefficients.Design(BiquadType.LowShelf, Rate, 300, 0.7071, 6));
        var first = ToneMatchDesign.Design(Mean(target), Mean(rig), Rate);

        var second = ToneMatchDesign.Design(Mean(target), Mean(Convolve(rig, first)), Rate, first);

        var a = MinimumPhase.MagnitudeDb(first, ToneMatchDesign.GridSize);
        var b = MinimumPhase.MagnitudeDb(second, ToneMatchDesign.GridSize);
        for (var k = 20; k < 1_400; k += 20) { // 230 Hz to 16 kHz
            Assert.Equal(a[k], b[k], 1.0);
        }
    }

    static double[] Mean(float[] signal) {
        var spectrum = new PowerSpectrum(ToneMatchDesign.GridSize);
        spectrum.AddSignal(signal);
        return spectrum.Mean();
    }

    /// <summary>Third-octave bands from 125 Hz to 8 kHz.</summary>
    static float[] Bands(float[] signal) {
        var centers = FractionalOctave.Centers(3, 125, 8_000);
        return FractionalOctave.Bands(Mean(signal), Rate, centers, 3);
    }

    static void Filter(float[] signal, BiquadCoefficients coefficients) {
        var state = new BiquadState();
        state.Process(coefficients, signal);
    }

    static float[] Convolve(float[] signal, float[] filter) {
        using var convolver = new Convolver(filter);
        var output = (float[])signal.Clone();
        convolver.Process(output);
        return output;
    }
}
