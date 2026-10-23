using System.Numerics;
using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Monophonic pitch tracker for the audio thread: YIN (de Cheveigné and Kawahara, "YIN, a fundamental frequency
///     estimator for speech and music", JASA 2002) on a low-passed copy of the input decimated to about 24 kHz.
/// </summary>
/// <remarks>
///     <para>
///         Samples are fed one at a time with <see cref="Push" />. Every <see cref="Hop" /> decimated samples (about
///         5.3 ms) the difference function of the most recent ~21 ms window is evaluated over lags covering
///         <see cref="MinFrequency" />..<see cref="MaxFrequency" />, so the estimate depends only on the sample stream,
///         never on how the host slices it into blocks. The first dip of the cumulative mean normalised difference
///         below 0.15 is the period, refined by a parabola through the raw difference function.
///     </para>
///     <para>
///         The analysis is O(window x lags) with SIMD, roughly 200 k multiply-adds per hop at 48 kHz. No allocation
///         after <see cref="Prepare" />; the history lives in native memory. Chords and noise read as unvoiced, and
///         <see cref="Frequency" /> then holds the last voiced estimate.
///     </para>
/// </remarks>
public sealed class PitchTracker : IDisposable {
    public const double MinFrequency = 70;
    public const double MaxFrequency = 1_400;

    /// <summary>Decimated samples between analyses.</summary>
    public const int Hop = 128;

    const double TargetAnalysisRate = 24_000;
    const double WindowSeconds = 0.0214;
    const float DipThreshold = 0.15f;
    const float MaxAperiodicity = 0.3f;
    const double MinRms = 1e-3; // -60 dBFS: quieter input reads as silence

    NativeBuffer history = NativeBuffer.Empty;
    NativeBuffer difference = NativeBuffer.Empty;
    SvfCoefficients lowPassA;
    SvfCoefficients lowPassB;
    SvfState stageA;
    SvfState stageB;
    double sampleRate = 48_000;
    double analysisRate = 24_000;
    int factor = 2;
    int window;
    int minLag;
    int maxLag;
    int length;
    int write;
    int filled;
    int decimation;
    int hopCount;

    /// <summary>Last voiced fundamental in Hz, or 0 before the first voiced analysis.</summary>
    public double Frequency { get; private set; }

    /// <summary>Whether the most recent analysis found a clear period.</summary>
    public bool Voiced { get; private set; }

    /// <summary>1 minus the aperiodicity of the most recent analysis: near 1 for a clean tone, 0 for silence.</summary>
    public float Clarity { get; private set; }

    /// <summary>Input samples between analyses: <see cref="Hop" /> decimated samples.</summary>
    public int HopSamples => Hop * factor;

    /// <summary>Period of <see cref="Frequency" /> in input samples, or 0 before the first voiced analysis.</summary>
    public double Period => Frequency > 0 ? sampleRate / Frequency : 0;

    /// <summary>Control thread: sizes the analysis for <paramref name="sampleRate" />.</summary>
    public void Prepare(double sampleRate) {
        this.sampleRate = sampleRate;
        factor = Math.Max(1, (int)Math.Round(sampleRate / TargetAnalysisRate));
        analysisRate = sampleRate / factor;
        window = (int)Math.Ceiling(WindowSeconds * analysisRate);
        minLag = Math.Max(2, (int)Math.Floor(analysisRate / MaxFrequency));
        maxLag = (int)Math.Ceiling(analysisRate / MinFrequency);
        length = window + maxLag + 2;

        // Fourth-order Butterworth at 3 kHz: anti-alias for the decimation, and it tames the upper harmonics.
        var cutoff = Math.Min(3_000, 0.2 * analysisRate);
        lowPassA = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, cutoff, 0.5412);
        lowPassB = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, cutoff, 1.3066);

        history.Dispose();
        difference.Dispose();
        history = new(2 * length); // every sample is written twice, so the latest window is always contiguous
        difference = new(2 * (maxLag + 2));
        Reset();
    }

    public void Reset() {
        history.Clear();
        difference.Clear();
        stageA.Reset();
        stageB.Reset();
        write = 0;
        filled = 0;
        decimation = 0;
        hopCount = 0;
        Frequency = 0;
        Voiced = false;
        Clarity = 0;
    }

    /// <summary>Feeds one input sample. Returns true when a new analysis has just completed.</summary>
    public bool Push(float sample) {
        var filtered = (float)stageB.Tick(lowPassB, stageA.Tick(lowPassA, sample));
        if (++decimation < factor) {
            return false;
        }

        decimation = 0;
        if (MathF.Abs(filtered) < 1e-20f) {
            filtered = 0;
        }

        history[write] = filtered;
        history[write + length] = filtered;
        if (++write == length) {
            write = 0;
        }

        filled = Math.Min(length, filled + 1);
        if (++hopCount < Hop) {
            return false;
        }

        hopCount = 0;
        stageA.FlushDenormals();
        stageB.FlushDenormals();
        Analyse();
        return true;
    }

    public void Dispose() {
        history.Dispose();
        difference.Dispose();
        history = NativeBuffer.Empty;
        difference = NativeBuffer.Empty;
    }

    static float SquaredDifference(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var i = 0;
        var sum = 0f;
        if (Vector.IsHardwareAccelerated) {
            var acc = Vector<float>.Zero;
            for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count) {
                var d = new Vector<float>(a[i..]) - new Vector<float>(b[i..]);
                acc += d * d;
            }

            sum = Vector.Sum(acc);
        }

        for (; i < a.Length; i++) {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return sum;
    }

    void Analyse() {
        var x = history.Span.Slice(write, length); // oldest first
        if (filled < length || Rms(x[^window..]) < MinRms) {
            Voiced = false;
            Clarity = 0;
            return;
        }

        // d(lag) = sum over the window of (x[j] - x[j + lag])^2, and its cumulative mean normalised form d'.
        var span = difference.Span;
        var d = span[..(maxLag + 2)];
        var normalised = span[(maxLag + 2)..];
        var reference = x[..window];
        var cumulative = 0.0;
        for (var lag = 1; lag <= maxLag + 1; lag++) {
            d[lag] = SquaredDifference(reference, x.Slice(lag, window));
            cumulative += d[lag];
            normalised[lag] = cumulative > 0 ? (float)(d[lag] * lag / cumulative) : 1f;
        }

        // Absolute threshold: the first dip below it, followed down to its local minimum; else the global minimum.
        var best = -1;
        for (var lag = minLag; lag <= maxLag; lag++) {
            if (normalised[lag] < DipThreshold) {
                while (lag < maxLag && normalised[lag + 1] < normalised[lag]) {
                    lag++;
                }

                best = lag;
                break;
            }
        }

        if (best < 0) {
            best = minLag;
            for (var lag = minLag + 1; lag <= maxLag; lag++) {
                if (normalised[lag] < normalised[best]) {
                    best = lag;
                }
            }
        }

        Clarity = Math.Clamp(1f - normalised[best], 0f, 1f);
        if (normalised[best] > MaxAperiodicity) {
            Voiced = false;
            return;
        }

        // Parabolic interpolation on the raw difference function, which is unbiased near its minimum.
        double a = d[best - 1], b = d[best], c = d[best + 1];
        var denominator = a - 2 * b + c;
        var offset = denominator > 0 ? Math.Clamp(0.5 * (a - c) / denominator, -1, 1) : 0;
        Frequency = analysisRate / (best + offset);
        Voiced = true;
    }

    static double Rms(ReadOnlySpan<float> x) {
        var sum = 0.0;
        foreach (var s in x) {
            sum += (double)s * s;
        }

        return Math.Sqrt(sum / x.Length);
    }
}
