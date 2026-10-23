using System.Numerics;

namespace Cathode.Testing;

/// <summary>Measurements shared by tests, benchmarks and the analysis tool.</summary>
public static class Analysis
{
    public static double ToDb(double gain) => 20 * Math.Log10(Math.Max(gain, 1e-15));

    public static double Rms(ReadOnlySpan<float> signal)
    {
        if (signal.IsEmpty)
        {
            return 0;
        }

        double sum = 0;
        foreach (var s in signal)
        {
            sum += (double)s * s;
        }

        return Math.Sqrt(sum / signal.Length);
    }

    public static double Peak(ReadOnlySpan<float> signal)
    {
        double peak = 0;
        foreach (var s in signal)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }

        return peak;
    }

    /// <summary>RMS of <c>a - b</c> in dBFS. Two identical signals give -300 dB.</summary>
    public static double NullResidualDb(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Signals must have the same length.", nameof(b));
        }

        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = (double)a[i] - b[i];
            sum += d * d;
        }

        return sum == 0 ? -300 : ToDb(Math.Sqrt(sum / a.Length));
    }

    /// <summary>
    /// |H(e^jω)| at <paramref name="frequency"/>, evaluated exactly (DTFT) from an impulse response. Unlike an FFT
    /// this has no bin quantisation, so it can be compared against analytic transfer functions.
    /// </summary>
    public static double MagnitudeAt(ReadOnlySpan<float> impulseResponse, double frequency, double sampleRate)
    {
        var w = -2 * Math.PI * frequency / sampleRate;
        var sum = Complex.Zero;
        for (var n = 0; n < impulseResponse.Length; n++)
        {
            sum += impulseResponse[n] * Complex.FromPolarCoordinates(1, w * n);
        }

        return sum.Magnitude;
    }

    /// <summary>Total harmonic distortion of a coherent sine at <paramref name="fundamentalBin"/>, as a ratio.</summary>
    public static double Thd(ReadOnlySpan<float> signal, int fundamentalBin, int harmonics = 9)
    {
        var magnitudes = Fft.Magnitudes(signal);
        double sum = 0;
        for (var h = 2; h <= harmonics && h * fundamentalBin < magnitudes.Length; h++)
        {
            sum += magnitudes[h * fundamentalBin] * magnitudes[h * fundamentalBin];
        }

        return Math.Sqrt(sum) / magnitudes[fundamentalBin];
    }

    /// <summary>
    /// Signal-to-aliasing ratio in dB of a nonlinear system's response to a coherent sine at
    /// <paramref name="fundamentalBin"/>: energy at the true harmonics (k x f0 below Nyquist) against all other
    /// non-DC energy, which is mostly harmonics folded back from above Nyquist. Pick a fundamental bin whose
    /// folded harmonics don't land on harmonic bins (a prime works well).
    /// </summary>
    public static double SignalToAliasingDb(ReadOnlySpan<float> signal, int fundamentalBin)
    {
        var magnitudes = Fft.Magnitudes(signal);
        double harmonic = 0, total = 0;
        for (var k = 1; k < magnitudes.Length; k++)
        {
            var energy = magnitudes[k] * magnitudes[k];
            total += energy;
            if (k % fundamentalBin == 0)
            {
                harmonic += energy;
            }
        }

        return 10 * Math.Log10(harmonic / Math.Max(total - harmonic, 1e-30));
    }

    /// <summary>
    /// Error-to-signal ratio with first-order pre-emphasis (1 - 0.95 z^-1), the standard amp-modelling metric.
    /// 0 is a perfect match; 1 is as large as the signal itself.
    /// </summary>
    public static double Esr(ReadOnlySpan<float> target, ReadOnlySpan<float> prediction, double preEmphasis = 0.95)
    {
        if (target.Length != prediction.Length)
        {
            throw new ArgumentException("Signals must have the same length.", nameof(prediction));
        }

        double error = 0, energy = 0, previousTarget = 0, previousPrediction = 0;
        for (var i = 0; i < target.Length; i++)
        {
            var t = target[i] - preEmphasis * previousTarget;
            var p = prediction[i] - preEmphasis * previousPrediction;
            previousTarget = target[i];
            previousPrediction = prediction[i];
            error += (t - p) * (t - p);
            energy += t * t;
        }

        return energy == 0 ? (error == 0 ? 0 : double.PositiveInfinity) : error / energy;
    }

    public static bool AllFinite(ReadOnlySpan<float> signal)
    {
        foreach (var s in signal)
        {
            if (!float.IsFinite(s))
            {
                return false;
            }
        }

        return true;
    }

    public static bool AnySubnormal(ReadOnlySpan<float> signal)
    {
        foreach (var s in signal)
        {
            if (float.IsSubnormal(s))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Index of the largest absolute sample.</summary>
    public static int PeakIndex(ReadOnlySpan<float> signal)
    {
        var index = 0;
        for (var i = 1; i < signal.Length; i++)
        {
            if (Math.Abs(signal[i]) > Math.Abs(signal[index]))
            {
                index = i;
            }
        }

        return index;
    }
}
