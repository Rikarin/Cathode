using System.Numerics;

namespace Cathode.Testing.References;

/// <summary>Latency and gain that best align a prediction to a target.</summary>
/// <param name="Lag">Samples the prediction is delayed relative to the target (positive: the prediction is late).</param>
/// <param name="Gain">Least-squares output gain applied to the prediction (negative if the polarity is inverted).</param>
public readonly record struct Alignment(int Lag, double Gain) {
    public double GainDb => Analysis.ToDb(Math.Abs(Gain));

    /// <summary>The prediction shifted by <see cref="Lag" /> and scaled by <see cref="Gain" />, as long as the input.</summary>
    public float[] Apply(ReadOnlySpan<float> prediction) {
        var result = new float[prediction.Length];
        for (var i = 0; i < result.Length; i++) {
            var j = i + Lag;
            result[i] = j >= 0 && j < prediction.Length ? (float)(prediction[j] * Gain) : 0f;
        }

        return result;
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
    public static readonly int[] StftSizes = [256, 1_024, 4_096];

    /// <summary>Third-octave band centres of the frequency response.</summary>
    public static readonly double[] ResponseCenters = FractionalOctaveCenters(40, 16_000);

    /// <summary>
    ///     The lag within ±<paramref name="maxLag" /> maximising |cross-correlation|, then the least-squares gain at that
    ///     lag.
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

        double cross = 0, energy = 0;
        for (var i = Math.Max(0, -best); i < Math.Min(length, length - best); i++) {
            cross += target[i] * (double)prediction[i + best];
            energy += prediction[i + best] * (double)prediction[i + best];
        }

        return new(best, energy > 0 ? cross / energy : 0);
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
