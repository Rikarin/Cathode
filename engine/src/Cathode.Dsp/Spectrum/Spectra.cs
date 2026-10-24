using Cathode.Dsp.Convolution;

namespace Cathode.Dsp.Spectrum;

/// <summary>The analysis window of a <see cref="PowerSpectrum" />.</summary>
public enum SpectrumWindow {
    /// <summary>The default: a good all-round compromise.</summary>
    Hann,

    /// <summary>Four-term Blackman-Harris: the lowest leakage, for a wide dynamic range.</summary>
    BlackmanHarris,

    /// <summary>Flat top: the most accurate levels for a tone between bins, at the cost of resolution.</summary>
    FlatTop,

    /// <summary>No window: the sharpest resolution and the most leakage.</summary>
    Rectangular
}

/// <summary>
///     Long-term average power spectrum (Welch's method: windowed frames, Hann by default, power averaged). Used off the
///     audio thread
///     by the RTA and Tone Match. Frames below <see cref="SilenceDb" /> are skipped, so pauses in a performance do not
///     pull the average towards the noise floor.
/// </summary>
public sealed class PowerSpectrum {
    /// <summary>Frames quieter than this (RMS, dBFS) are left out of the average.</summary>
    public const double SilenceDb = -70;

    readonly Fft fft;
    readonly float[] window;
    readonly float[] re;
    readonly float[] im;
    readonly double[] sum;
    readonly double windowPower;

    public int Size { get; }

    /// <summary>Frames averaged so far.</summary>
    public int Frames { get; private set; }

    /// <remarks>Each frame's power is divided by the window's energy, so band levels hardly change with the window.</remarks>
    public PowerSpectrum(int size = 4_096, SpectrumWindow shape = SpectrumWindow.Hann) {
        Size = size;
        fft = new(size);
        window = new float[size];
        re = new float[size];
        im = new float[size];
        sum = new double[size / 2 + 1];
        double power = 0;
        for (var i = 0; i < size; i++) {
            window[i] = (float)Window(shape, 2 * Math.PI * i / size);
            power += window[i] * window[i];
        }

        windowPower = power;
    }

    /// <summary>Adds one frame of exactly <see cref="Size" /> samples. Returns false if it was too quiet to count.</summary>
    public bool AddFrame(ReadOnlySpan<float> frame) {
        double energy = 0;
        for (var i = 0; i < Size; i++) {
            energy += frame[i] * frame[i];
        }

        if (10 * Math.Log10(energy / Size + 1e-30) < SilenceDb) {
            return false;
        }

        for (var i = 0; i < Size; i++) {
            re[i] = frame[i] * window[i];
            im[i] = 0f;
        }

        fft.Forward(re, im);
        for (var k = 0; k < sum.Length; k++) {
            sum[k] += (re[k] * (double)re[k] + im[k] * (double)im[k]) / windowPower;
        }

        Frames++;
        return true;
    }

    /// <summary>Adds a whole signal as half-overlapping frames.</summary>
    public void AddSignal(ReadOnlySpan<float> signal) {
        for (var start = 0; start + Size <= signal.Length; start += Size / 2) {
            AddFrame(signal.Slice(start, Size));
        }
    }

    public void Clear() {
        Array.Clear(sum);
        Frames = 0;
    }

    /// <summary>Average power per bin (0 … Size/2), or all zeros before any frame.</summary>
    public double[] Mean() {
        var mean = new double[sum.Length];
        if (Frames > 0) {
            for (var k = 0; k < mean.Length; k++) {
                mean[k] = sum[k] / Frames;
            }
        }

        return mean;
    }

    /// <summary>The window at phase <paramref name="x" /> (0..2π across the frame).</summary>
    static double Window(SpectrumWindow shape, double x) =>
        shape switch {
            SpectrumWindow.BlackmanHarris => 0.35875
                - 0.48829 * Math.Cos(x)
                + 0.14128 * Math.Cos(2 * x)
                - 0.01168 * Math.Cos(3 * x),
            SpectrumWindow.FlatTop => 0.21557895
                - 0.41663158 * Math.Cos(x)
                + 0.277263158 * Math.Cos(2 * x)
                - 0.083578947 * Math.Cos(3 * x)
                + 0.006947368 * Math.Cos(4 * x),
            SpectrumWindow.Rectangular => 1,
            _ => 0.5 * (1 - Math.Cos(x))
        };
}

/// <summary>Fractional-octave views of a power spectrum: RTA bands and smoothing.</summary>
public static class FractionalOctave {
    /// <summary>
    ///     Band centres from <paramref name="low" /> to <paramref name="high" /> Hz, <paramref name="perOctave" /> per
    ///     octave, anchored at 1 kHz.
    /// </summary>
    public static double[] Centers(int perOctave, double low = 20, double high = 20_000) {
        var first = (int)Math.Ceiling(perOctave * Math.Log2(low / 1_000));
        var last = (int)Math.Floor(perOctave * Math.Log2(high / 1_000));
        var centers = new double[last - first + 1];
        for (var i = 0; i < centers.Length; i++) {
            centers[i] = 1_000 * Math.Pow(2, (double)(first + i) / perOctave);
        }

        return centers;
    }

    /// <summary>Band levels in dB (power summed across each band's bins; bands narrower than a bin take the nearest bin).</summary>
    public static float[] Bands(
        ReadOnlySpan<double> power,
        double sampleRate,
        ReadOnlySpan<double> centers,
        int perOctave
    ) {
        var size = 2 * (power.Length - 1);
        var binHz = sampleRate / size;
        var half = Math.Pow(2, 0.5 / perOctave);
        var levels = new float[centers.Length];
        for (var b = 0; b < centers.Length; b++) {
            var from = Math.Max(1, (int)Math.Ceiling(centers[b] / half / binHz));
            var to = Math.Min(power.Length - 1, (int)Math.Floor(centers[b] * half / binHz));
            double total = 0;
            if (to < from) {
                total = power[Math.Clamp((int)Math.Round(centers[b] / binHz), 1, power.Length - 1)];
            } else {
                for (var k = from; k <= to; k++) {
                    total += power[k];
                }
            }

            levels[b] = (float)(10 * Math.Log10(total + 1e-20));
        }

        return levels;
    }

    /// <summary>
    ///     Smooths per-bin dB values over a window <paramref name="fraction" /> of an octave wide (e.g. 1/3), averaging
    ///     in dB. Bin 0 (DC) copies bin 1.
    /// </summary>
    public static double[] Smooth(ReadOnlySpan<double> db, double fraction) {
        var smoothed = new double[db.Length];
        var half = Math.Pow(2, fraction / 2);
        for (var k = 1; k < db.Length; k++) {
            var from = Math.Max(1, (int)Math.Floor(k / half));
            var to = Math.Min(db.Length - 1, (int)Math.Ceiling(k * half));
            double total = 0;
            for (var j = from; j <= to; j++) {
                total += db[j];
            }

            smoothed[k] = total / (to - from + 1);
        }

        smoothed[0] = smoothed.Length > 1 ? smoothed[1] : 0;
        return smoothed;
    }
}

/// <summary>Minimum-phase FIR design from a magnitude response, by the real cepstrum (homomorphic) method.</summary>
public static class MinimumPhase {
    /// <summary>
    ///     A <paramref name="taps" />-long minimum-phase filter whose magnitude follows <paramref name="magnitudeDb" />:
    ///     one value per bin 0 … N/2 of an N-point FFT, N a power of two at least twice <paramref name="taps" />.
    ///     Minimum phase puts the energy at the start, so the filter adds no latency and short taps lose little.
    /// </summary>
    public static float[] Design(ReadOnlySpan<double> magnitudeDb, int taps) {
        var size = 2 * (magnitudeDb.Length - 1);
        if (taps * 2 > size) {
            throw new ArgumentOutOfRangeException(nameof(taps), taps, "Use a finer magnitude grid for this many taps.");
        }

        var fft = new Fft(size);
        var re = new float[size];
        var im = new float[size];

        // Real cepstrum of the log magnitude (natural log), a symmetric spectrum.
        for (var k = 0; k <= size / 2; k++) {
            var log = (float)(magnitudeDb[k] * Math.Log(10) / 20);
            re[k] = log;
            if (k > 0 && k < size / 2) {
                re[size - k] = log;
            }
        }

        fft.Inverse(re, im);

        // Fold the anticausal half onto the causal half: the cepstrum of the minimum-phase filter.
        for (var n = 1; n < size / 2; n++) {
            re[n] *= 2f;
            re[size - n] = 0f;
        }

        Array.Clear(im);
        fft.Forward(re, im);

        // exp() of the complex log spectrum, then back to time.
        for (var k = 0; k < size; k++) {
            var magnitude = MathF.Exp(re[k]);
            var phase = im[k];
            re[k] = magnitude * MathF.Cos(phase);
            im[k] = magnitude * MathF.Sin(phase);
        }

        fft.Inverse(re, im);
        var filter = re[..taps];
        const int fade = 64;
        for (var i = 0; i < fade && i < taps; i++) {
            filter[taps - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
        }

        return filter;
    }

    /// <summary>Magnitude in dB of <paramref name="filter" /> on an N-point grid (bins 0 … N/2).</summary>
    public static double[] MagnitudeDb(ReadOnlySpan<float> filter, int size) {
        var fft = new Fft(size);
        var re = new float[size];
        var im = new float[size];
        filter[..Math.Min(filter.Length, size)].CopyTo(re);
        fft.Forward(re, im);
        var db = new double[size / 2 + 1];
        for (var k = 0; k < db.Length; k++) {
            db[k] = 10 * Math.Log10(re[k] * (double)re[k] + im[k] * (double)im[k] + 1e-20);
        }

        return db;
    }
}
