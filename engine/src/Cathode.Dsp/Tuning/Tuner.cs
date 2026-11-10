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
///     Guitar and bass tuner using the McLeod Pitch Method (McLeod and Wyvill, "A Smarter Way to Find Pitch", ICMC
///     2005). Input is decimated to an analysis rate near 24 kHz (2x at 44.1 and 48 kHz, 4x at 88.2 and 96 kHz, 8x at
///     176.4 and 192 kHz, by cascaded half-band stages), so the 2048-sample window is about 85 ms and reaches
///     <see cref="MinFrequency" /> at every rate. The normalised square difference function (NSDF) of the window is
///     computed from an FFT autocorrelation. The first "key maximum" above 93 % of the highest is the period, refined
///     by parabolic interpolation. Runs on an analysis thread, never the audio thread: it is fed from
///     <c>CathodeEngine.InputTap</c>.
/// </summary>
public sealed class Tuner {
    public const int Window = 2_048;
    public const double MinFrequency = 25;
    public const double MaxFrequency = 1_600;

    /// <summary>The analysis rate the decimation aims for, in Hz.</summary>
    const double TargetAnalysisRate = 24_000;

    const int FftSize = 2 * Window;
    const double KeyMaximumThreshold = 0.93;
    const double MinClarity = 0.8;
    const double MinRms = 1e-3; // -60 dBFS

    static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    readonly HalfBandStage[] decimators; // each halves the rate; the first takes the input
    readonly float[][] pending; // per stage: the first sample of the pair it is waiting to halve
    readonly bool[] waiting;
    readonly Fft fft = new(FftSize);
    readonly float[] history = new float[2 * Window]; // every sample written twice, so the window is contiguous
    readonly float[] re = new float[FftSize];
    readonly float[] im = new float[FftSize];
    readonly double[] nsdf = new double[Window];
    readonly float[] decimated = new float[1];
    int filled;
    int write;

    /// <summary>Frequency of A4, normally 440 Hz (430..450 allowed).</summary>
    public double Reference { get; set; } = 440;

    /// <summary>Rate of the decimated analysis signal.</summary>
    public double SampleRate { get; }

    /// <summary>How many input samples make one analysis sample: 2, 4 or 8.</summary>
    public int Decimation => 1 << decimators.Length;

    public Tuner(double sampleRate) {
        var stages = Math.Clamp((int)Math.Round(Math.Log2(sampleRate / TargetAnalysisRate)), 1, 3);
        var coefficients = HalfBandStage.Split(HalfBand.Design(80, 0.1));
        decimators = new HalfBandStage[stages];
        pending = new float[stages][];
        waiting = new bool[stages];
        for (var i = 0; i < stages; i++) {
            decimators[i] = new(coefficients);
            pending[i] = new float[2];
        }

        SampleRate = sampleRate / Decimation;
    }

    /// <summary>Feeds input samples at the full rate.</summary>
    public void Push(ReadOnlySpan<float> samples) {
        foreach (var sample in samples) {
            var value = sample;
            var stage = 0;
            for (; stage < decimators.Length; stage++) {
                var pair = pending[stage];
                if (!waiting[stage]) {
                    pair[0] = value;
                    waiting[stage] = true;
                    break;
                }

                pair[1] = value;
                waiting[stage] = false;
                decimators[stage].Down(pair, decimated);
                value = decimated[0];
            }

            if (stage < decimators.Length) {
                continue; // a stage is still waiting for the second sample of its pair
            }

            history[write] = history[write + Window] = value;
            write = write == Window - 1 ? 0 : write + 1;
            filled = Math.Min(Window, filled + 1);
        }
    }

    /// <summary>The most recent <see cref="Window" /> decimated samples, oldest first.</summary>
    ReadOnlySpan<float> Recent => history.AsSpan(write, Window);

    /// <summary>Copies the window <see cref="Detect" /> analyses (decimated samples, oldest first).</summary>
    public void CopyWindow(Span<float> destination) => Recent.CopyTo(destination);

    /// <summary>
    ///     Analyses the most recent window. Returns null for silence, noise or chords without a clear period.
    /// </summary>
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
        foreach (var x in Recent) {
            sum += (double)x * x;
        }

        return Math.Sqrt(sum / Window);
    }

    void ComputeNsdf() {
        Array.Clear(re);
        Array.Clear(im);
        var window = Recent;
        window.CopyTo(re);
        fft.Forward(re, im);
        for (var k = 0; k < FftSize; k++) {
            re[k] = re[k] * re[k] + im[k] * im[k];
            im[k] = 0;
        }

        fft.Inverse(re, im); // re[lag] = sum over j of x[j] * x[j + lag]

        double m = 0;
        foreach (var x in window) {
            m += 2.0 * x * x;
        }

        for (var lag = 0; lag < Window; lag++) {
            nsdf[lag] = m > 0 ? 2 * re[lag] / m : 0;
            var a = window[lag];
            var b = window[Window - 1 - lag];
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
