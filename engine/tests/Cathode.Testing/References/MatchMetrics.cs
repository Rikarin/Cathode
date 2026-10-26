using System.Collections.Immutable;
using System.Numerics;

namespace Cathode.Testing.References;

/// <summary>Latency and gain that best align a prediction to a target.</summary>
/// <param name="Lag">Samples the prediction is delayed relative to the target (positive: the prediction is late).</param>
/// <param name="Gain">Least-squares output gain applied to the prediction (negative if the polarity is inverted).</param>
/// <param name="Fraction">
///     A further delay of the prediction, in samples (-0.5 to 0.5), removed by band-limited interpolation: latencies are
///     rarely whole samples (an oversampler's filters, a capture's interface), and half a sample is a large error at high
///     frequencies, where the pre-emphasised ESR looks.
/// </param>
public readonly record struct Alignment(int Lag, double Gain, double Fraction = 0) {
    /// <summary>Half-length of the interpolator's windowed sinc, in samples.</summary>
    public const int InterpolatorTaps = 32;

    public double GainDb => Analysis.ToDb(Math.Abs(Gain));

    /// <summary>
    ///     The prediction shifted by <see cref="Lag" /> (and <see cref="Fraction" />) and scaled by <see cref="Gain" />,
    ///     as long as the input.
    /// </summary>
    public float[] Apply(ReadOnlySpan<float> prediction) {
        var result = new float[prediction.Length];
        if (Fraction == 0) {
            for (var i = 0; i < result.Length; i++) {
                var j = i + Lag;
                result[i] = j >= 0 && j < prediction.Length ? (float)(prediction[j] * Gain) : 0f;
            }

            return result;
        }

        var kernel = Interpolator(Fraction);
        for (var i = 0; i < result.Length; i++) {
            double sum = 0;
            for (var k = -InterpolatorTaps; k <= InterpolatorTaps; k++) {
                var j = i + Lag + k;
                if (j >= 0 && j < prediction.Length) {
                    sum += kernel[k + InterpolatorTaps] * prediction[j];
                }
            }

            result[i] = (float)(sum * Gain);
        }

        return result;
    }

    /// <summary>
    ///     The Hann-windowed sinc that reads a signal <paramref name="fraction" /> samples later:
    ///     y[i] = Σ h[k] x[i + k], k = -<see cref="InterpolatorTaps" /> … <see cref="InterpolatorTaps" />.
    /// </summary>
    public static double[] Interpolator(double fraction) {
        var kernel = new double[2 * InterpolatorTaps + 1];
        for (var k = -InterpolatorTaps; k <= InterpolatorTaps; k++) {
            var t = k - fraction;
            var window = 0.5 + 0.5 * Math.Cos(Math.PI * t / (InterpolatorTaps + 1));
            kernel[k + InterpolatorTaps] = (Math.Abs(t) < 1e-12 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t)) * window;
        }

        return kernel;
    }
}

/// <summary>
///     The comparison metrics (docs/06 §5.4): ESR, multi-resolution STFT distance, harmonic profile and small-signal
///     frequency response.
/// </summary>
public static class MatchMetrics {
    /// <summary>Pre-emphasis of the reported ESR (Wright et al.: 1 − 0.95 z⁻¹).</summary>
    public const double PreEmphasis = 0.95;

    /// <summary>Harmonics analysed in a <see cref="HarmonicProfile" />, the fundamental included.</summary>
    public const int Harmonics = 9;

    /// <summary>Harmonics below this (dB under the fundamental) are treated as this, so noise floors do not dominate.</summary>
    public const double HarmonicFloorDb = -80;

    /// <summary>STFT sizes of <see cref="Mrstft" />.</summary>
    public static readonly ImmutableArray<int> StftSizes = [256, 1_024, 4_096];

    /// <summary>Third-octave band centres of the frequency response.</summary>
    public static readonly ImmutableArray<double> ResponseCenters = [..FractionalOctaveCenters(40, 16_000)];

    /// <summary>
    ///     The lag within ±<paramref name="maxLag" /> maximising |cross-correlation|, refined to a fraction of a sample
    ///     (the delay maximising the normalised correlation of the interpolated prediction), then the least-squares gain
    ///     there.
    /// </summary>
    public static Alignment Align(ReadOnlySpan<float> target, ReadOnlySpan<float> prediction, int maxLag = 512) {
        var length = Math.Min(target.Length, prediction.Length);
        var best = 0;
        var bestValue = double.NegativeInfinity;
        for (var lag = -maxLag; lag <= maxLag; lag++) {
            double sum = 0;
            var start = Math.Max(0, -lag);
            var end = Math.Min(length, length - lag);
            for (var i = start; i < end; i += 2) {
                sum += target[i] * (double)prediction[i + lag];
            }

            if (Math.Abs(sum) > bestValue) {
                bestValue = Math.Abs(sum);
                best = lag;
            }
        }

        // The cross-correlation r(m) = Σ t[i] p[i + m] around the lag and the prediction's autocorrelation a(m): with
        // them, the shifted prediction's correlation with the target and its energy are short sums over the kernel.
        const int Taps = Alignment.InterpolatorTaps;
        var cross = new double[2 * Taps + 1];
        for (var k = -Taps; k <= Taps; k++) {
            var lag = best + k;
            double sum = 0;
            for (var i = Math.Max(0, -lag); i < Math.Min(length, length - lag); i++) {
                sum += target[i] * (double)prediction[i + lag];
            }

            cross[k + Taps] = sum;
        }

        var auto = new double[2 * Taps + 1];
        for (var m = 0; m <= 2 * Taps; m++) {
            double sum = 0;
            for (var i = 0; i + m < length; i++) {
                sum += prediction[i] * (double)prediction[i + m];
            }

            auto[m] = sum;
        }

        (double Cross, double Energy) At(double fraction) {
            if (fraction == 0) {
                return (cross[Taps], auto[0]);
            }

            var h = Alignment.Interpolator(fraction);
            double c = 0, e = 0;
            for (var k = 0; k < h.Length; k++) {
                c += h[k] * cross[k];
                for (var l = 0; l < h.Length; l++) {
                    e += h[k] * h[l] * auto[Math.Abs(k - l)];
                }
            }

            return (c, e);
        }

        double Score(double fraction) {
            var (c, e) = At(fraction);
            return e > 0 ? Math.Abs(c) / Math.Sqrt(e) : 0;
        }

        var fraction = 0.0;
        var score = Score(0);
        for (var d = -0.5; d <= 0.5 + 1e-9; d += 0.05) {
            if (Score(d) > score) {
                (fraction, score) = (d, Score(d));
            }
        }

        for (var step = 0.025; step > 1e-4; step /= 2) {
            foreach (var d in new[] { fraction - step, fraction + step }) {
                if (Math.Abs(d) <= 0.5 && Score(d) > score) {
                    (fraction, score) = (d, Score(d));
                }
            }
        }

        // A whole-sample latency stays exact: keep no fraction unless it measurably helps.
        if (score <= Score(0) * (1 + 1e-9)) {
            fraction = 0;
        }

        var (crossAt, energyAt) = At(fraction);
        return new(best, energyAt > 0 ? crossAt / energyAt : 0, fraction);
    }

    /// <summary>
    ///     Multi-resolution STFT distance (as auraloss): per size, spectral convergence ‖|Y|−|X|‖/‖|X|‖ plus the mean
    ///     absolute log-magnitude difference, Hann windows hopping by a quarter; averaged over <see cref="StftSizes" />.
    ///     0 is a perfect match; unrelated signals of similar level score around 2–4.
    /// </summary>
    public static double Mrstft(ReadOnlySpan<float> target, ReadOnlySpan<float> prediction) {
        var peak = Math.Max(Analysis.Peak(target), 1e-9);
        var floor = 1e-5 * peak;
        double total = 0;
        foreach (var size in StftSizes) {
            var window = Hann(size);
            double difference = 0, reference = 0, log = 0;
            var bins = 0L;
            for (var start = 0; start + size <= target.Length; start += size / 4) {
                var x = Spectrum(target.Slice(start, size), window);
                var y = Spectrum(prediction.Slice(start, size), window);
                for (var k = 0; k < x.Length; k++) {
                    difference += (y[k] - x[k]) * (y[k] - x[k]);
                    reference += x[k] * x[k];
                    log += Math.Abs(Math.Log((y[k] + floor) / (x[k] + floor)));
                }

                bins += x.Length;
            }

            total += (reference > 0 ? Math.Sqrt(difference / reference) : 0) + (bins > 0 ? log / bins : 0);
        }

        return total / StftSizes.Length;
    }

    /// <summary>
    ///     Levels of harmonics 1 … <see cref="Harmonics" /> of a coherent tone (the last
    ///     <see cref="MatchSignals.HarmonicWindow" /> samples of <paramref name="output" />): index 0 is the fundamental
    ///     in dBFS, the rest are relative to it in dB, floored at <see cref="HarmonicFloorDb" />.
    /// </summary>
    public static double[] HarmonicProfile(ReadOnlySpan<float> output, int cycles) {
        var window = output[^MatchSignals.HarmonicWindow..];
        var magnitudes = Fft.Magnitudes(window);
        var profile = new double[Harmonics];
        var fundamental = Analysis.ToDb(magnitudes[cycles]);
        profile[0] = fundamental;
        for (var h = 2; h <= Harmonics; h++) {
            var bin = h * cycles;
            profile[h - 1] = bin < magnitudes.Length
                ? Math.Max(HarmonicFloorDb, Analysis.ToDb(magnitudes[bin]) - fundamental)
                : HarmonicFloorDb;
        }

        return profile;
    }

    /// <summary>
    ///     Third-octave magnitude response (dB) at <see cref="ResponseCenters" /> from a sweep and the output it caused:
    ///     band energy of the output over band energy of the input.
    /// </summary>
    public static double[] FrequencyResponse(ReadOnlySpan<float> input, ReadOnlySpan<float> output, double sampleRate) {
        var size = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(input.Length, output.Length));
        var x = Transform(input, size);
        var y = Transform(output, size);
        var result = new double[ResponseCenters.Length];
        var width = Math.Pow(2, 1.0 / 6);
        for (var b = 0; b < ResponseCenters.Length; b++) {
            var low = (int)Math.Floor(ResponseCenters[b] / width * size / sampleRate);
            var high = (int)Math.Ceiling(ResponseCenters[b] * width * size / sampleRate);
            double inEnergy = 0, outEnergy = 0;
            for (var k = Math.Max(1, low); k <= Math.Min(size / 2, high); k++) {
                inEnergy += x[k].Magnitude * x[k].Magnitude;
                outEnergy += y[k].Magnitude * y[k].Magnitude;
            }

            result[b] = 10 * Math.Log10(Math.Max(outEnergy, 1e-30) / Math.Max(inEnergy, 1e-30));
        }

        return result;
    }

    /// <summary>
    ///     RMS difference in dB between two responses over 80 Hz – 8 kHz, after removing their mean difference (so
    ///     only the shape counts), and the mean difference removed.
    /// </summary>
    public static (double RmsDb, double OffsetDb) ResponseDistance(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> candidate
    ) {
        var bands = Enumerable.Range(0, ResponseCenters.Length)
            .Where(static b => ResponseCenters[b] >= 80 && ResponseCenters[b] <= 8_000)
            .ToArray();
        var offset = bands.Average(b => reference[b] - candidate[b]);
        var rms = Math.Sqrt(bands.Average(b => Math.Pow(reference[b] - candidate[b] - offset, 2)));
        return (rms, offset);
    }

    /// <summary>
    ///     RMS difference in dB of harmonics 2–5 across levels (both profiles relative to their own fundamentals, so
    ///     level-matched).
    /// </summary>
    public static double HarmonicDistance(IReadOnlyList<double[]> reference, IReadOnlyList<double[]> candidate) {
        double sum = 0;
        var count = 0;
        for (var level = 0; level < reference.Count; level++) {
            for (var h = 1; h <= 4; h++) {
                sum += Math.Pow(reference[level][h] - candidate[level][h], 2);
                count++;
            }
        }

        return Math.Sqrt(sum / count);
    }

    static double[] FractionalOctaveCenters(double low, double high) {
        var centers = new List<double>();
        for (var n = -30; n <= 30; n++) {
            var f = 1_000 * Math.Pow(2, n / 3.0);
            if (f >= low && f <= high) {
                centers.Add(f);
            }
        }

        return [..centers];
    }

    static double[] Hann(int size) {
        var window = new double[size];
        for (var i = 0; i < size; i++) {
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
        }

        return window;
    }

    static double[] Spectrum(ReadOnlySpan<float> frame, double[] window) {
        var data = new Complex[frame.Length];
        for (var i = 0; i < frame.Length; i++) {
            data[i] = frame[i] * window[i];
        }

        Fft.Forward(data);
        var magnitudes = new double[frame.Length / 2 + 1];
        for (var k = 0; k < magnitudes.Length; k++) {
            magnitudes[k] = data[k].Magnitude;
        }

        return magnitudes;
    }

    static Complex[] Transform(ReadOnlySpan<float> signal, int size) {
        var data = new Complex[size];
        for (var i = 0; i < signal.Length; i++) {
            data[i] = signal[i];
        }

        Fft.Forward(data);
        return data;
    }
}
