using Cathode.Dsp.Convolution;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Tuning;

/// <summary>A detected pitch.</summary>
/// <param name="Frequency">Fundamental in Hz.</param>
/// <param name="Note">Nearest note name, e.g. "E".</param>
/// <param name="Octave">Scientific pitch octave (A4 = 440 Hz is octave 4).</param>
/// <param name="Cents">Deviation from the nearest note, -50..50.</param>
/// <param name="Clarity">Normalised peak height, 0..1. Higher is a cleaner periodic signal.</param>
public readonly record struct TunerReading(double Frequency, string Note, int Octave, double Cents, double Clarity);

/// <summary>
///     Guitar and bass tuner using the McLeod Pitch Method (McLeod and Wyvill, "A Smarter Way to Find Pitch", ICMC 2005).
///     Input is decimated 2x, then the normalised square difference function (NSDF) of a 2048-sample window is computed
///     from an FFT autocorrelation. The first "key maximum" above 93 % of the highest is the period, refined by parabolic
///     interpolation. Runs on an analysis thread, never the audio thread: it is fed from <c>CathodeEngine.InputTap</c>.
/// </summary>
public sealed class Tuner {
    public const int Window = 2_048;
    public const double MinFrequency = 25;
    public const double MaxFrequency = 1_600;

    const int FftSize = 2 * Window;
    const double KeyMaximumThreshold = 0.93;
    const double MinClarity = 0.8;
    const double MinRms = 1e-3; // -60 dBFS

    static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    readonly HalfBandStage decimator = new(HalfBand.Design(80, 0.1));
    readonly Fft fft = new(FftSize);
    readonly float[] window = new float[Window];
    readonly float[] pending = new float[2];
    readonly float[] re = new float[FftSize];
    readonly float[] im = new float[FftSize];
    readonly double[] nsdf = new double[Window];
    readonly float[] decimated = new float[1];
    int pendingCount;
    int filled;

    /// <summary>Frequency of A4, normally 440 Hz (430..450 allowed).</summary>
    public double Reference { get; set; } = 440;

    /// <summary>Rate of the decimated analysis signal.</summary>
    public double SampleRate { get; }

    public Tuner(double sampleRate) {
        SampleRate = sampleRate / 2;
    }

    /// <summary>Feeds input samples at the full rate.</summary>
    public void Push(ReadOnlySpan<float> samples) {
        foreach (var sample in samples) {
            pending[pendingCount++] = sample;
            if (pendingCount < 2) {
                continue;
            }

            pendingCount = 0;
            decimator.Down(pending, this.decimated);
            Array.Copy(this.window, 1, this.window, 0, Window - 1);
            this.window[^1] = this.decimated[0];
            filled = Math.Min(Window, filled + 1);
        }
    }

    /// <summary>Analyses the most recent window. Returns null for silence, noise or chords without a clear period.</summary>
    public TunerReading? Detect() {
        if (filled < Window || Rms() < MinRms) {
            return null;
        }

        ComputeNsdf();
        var minLag = (int)(SampleRate / MaxFrequency);
        var maxLag = Math.Min(Window - 2, (int)(SampleRate / MinFrequency));

        // Key maxima: the highest point of each positive region between zero crossings.
        Span<int> keys = stackalloc int[64];
        var keyCount = 0;
        var lag = 1;
        while (lag < maxLag && nsdf[lag] > 0) {
            lag++; // skip the lobe around lag 0 (starting at minLag would skip a short period's first lobe instead)
        }

        var highest = 0.0;
        while (lag < maxLag && keyCount < keys.Length) {
            while (lag < maxLag && nsdf[lag] <= 0) {
                lag++;
            }

            var best = -1;
            while (lag < maxLag && nsdf[lag] > 0) {
                if (best < 0 || nsdf[lag] > nsdf[best]) {
                    best = lag;
                }

                lag++;
            }

            if (best >= minLag) {
                keys[keyCount++] = best;
                highest = Math.Max(highest, nsdf[best]);
            }
        }

        foreach (var key in keys[..keyCount]) {
            if (nsdf[key] < KeyMaximumThreshold * highest) {
                continue;
            }

            if (nsdf[key] < MinClarity) {
                return null;
            }

            var (period, clarity) = Parabola(key);
            return Reading(SampleRate / period, clarity);
        }

        return null;
    }

    /// <summary>Nearest note and cents for <paramref name="frequency" /> against <see cref="Reference" />.</summary>
    public TunerReading Reading(double frequency, double clarity) {
        var midi = 69 + 12 * Math.Log2(frequency / Reference);
        var nearest = (int)Math.Round(midi);
        var name = Notes[(nearest % 12 + 12) % 12];
        return new(frequency, name, nearest / 12 - 1, (midi - nearest) * 100, clarity);
    }

    double Rms() {
        double sum = 0;
        foreach (var x in this.window) {
            sum += (double)x * x;
        }

        return Math.Sqrt(sum / Window);
    }

    void ComputeNsdf() {
        Array.Clear(re);
        Array.Clear(im);
        this.window.CopyTo(re, 0);
        fft.Forward(re, im);
        for (var k = 0; k < FftSize; k++) {
            re[k] = re[k] * re[k] + im[k] * im[k];
            im[k] = 0;
        }

        fft.Inverse(re, im); // re[lag] = sum over j of x[j] * x[j + lag]

        double m = 0;
        foreach (var x in this.window) {
            m += 2.0 * x * x;
        }

        for (var lag = 0; lag < Window; lag++) {
            nsdf[lag] = m > 0 ? 2 * re[lag] / m : 0;
            var a = this.window[lag];
            var b = this.window[Window - 1 - lag];
            m -= (double)a * a + (double)b * b;
        }
    }

    (double Period, double Clarity) Parabola(int lag) {
        var a = nsdf[lag - 1];
        var b = nsdf[lag];
        var c = nsdf[lag + 1];
        var denominator = a - 2 * b + c;
        if (denominator == 0) {
            return (lag, b);
        }

        var offset = 0.5 * (a - c) / denominator;
        return (lag + offset, b - 0.25 * (a - c) * offset);
    }
}
