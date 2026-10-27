namespace Cathode.Dsp.Convolution;

/// <summary>
///     Impulse response measurement with an exponential sine sweep (Farina, AES 2000): play <see cref="Sweep" />,
///     record the result, and <see cref="Deconvolve" /> it with the sweep's inverse filter. Harmonic distortion lands
///     before the linear response, so <see cref="Trim" /> cutting just ahead of the main peak drops it.
/// </summary>
/// <param name="sampleRate">The rate the sweep is played and recorded at.</param>
/// <param name="seconds">Sweep length; longer sweeps raise the signal-to-noise ratio (3 dB per doubling).</param>
/// <param name="startHz">Lowest frequency.</param>
/// <param name="endHz">Highest frequency (clamped below Nyquist).</param>
public sealed class SweepMeasurement(
    double sampleRate,
    double seconds = 5,
    double startHz = 20,
    double endHz = 20_000
) {
    /// <summary>Fade at each end of the sweep, so it starts and stops without clicks.</summary>
    const double FadeSeconds = 0.05;

    float[]? sweep;
    float[]? inverse;
    double gain;

    public double SampleRate { get; } = sampleRate;

    /// <summary>The excitation at full scale; scale it to the playback level.</summary>
    public float[] Sweep => sweep ??= Build();

    double End => Math.Min(endHz, SampleRate * 0.45);

    /// <summary>
    ///     The impulse response in <paramref name="recorded" /> (which must start when the sweep starts and run on past
    ///     it by at least the room's or cab's decay time): sample 0 of the result is zero lag, scaled so that a
    ///     straight wire gives a unit impulse.
    /// </summary>
    public float[] Deconvolve(ReadOnlySpan<float> recorded) {
        var inverse = Inverse();
        var full = Convolve(recorded, inverse);
        var start = inverse.Length - 1; // the inverse filter is the time-reversed sweep: zero lag sits here
        var length = Math.Max(0, full.Length - start);
        var result = new float[length];
        for (var i = 0; i < length; i++) {
            result[i] = (float)(full[start + i] * gain);
        }

        return result;
    }

    /// <summary>
    ///     Cuts <paramref name="ir" /> to <paramref name="lengthMs" />, starting <paramref name="preRollMs" /> before
    ///     its peak, with a short fade at the end.
    /// </summary>
    public float[] Trim(ReadOnlySpan<float> ir, double lengthMs, double preRollMs = 1) {
        var peak = 0;
        for (var i = 1; i < ir.Length; i++) {
            if (MathF.Abs(ir[i]) > MathF.Abs(ir[peak])) {
                peak = i;
            }
        }

        var start = Math.Max(0, peak - (int)Math.Round(preRollMs * 0.001 * SampleRate));
        var length = Math.Min(ir.Length - start, (int)Math.Round(lengthMs * 0.001 * SampleRate));
        var trimmed = ir.Slice(start, length).ToArray();
        var fade = Math.Min(64, length);
        for (var i = 0; i < fade; i++) {
            trimmed[length - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
        }

        return trimmed;
    }

    float[] Build() {
        var length = (int)(seconds * SampleRate);
        var rate = Math.Log(End / startHz);
        var fade = (int)(FadeSeconds * SampleRate);
        var signal = new float[length];
        for (var i = 0; i < length; i++) {
            var t = i / SampleRate;
            var phase = 2 * Math.PI * startHz * seconds / rate * (Math.Exp(t / seconds * rate) - 1);
            var envelope = i < fade ? 0.5 * (1 - Math.Cos(Math.PI * i / fade))
                : i >= length - fade ? 0.5 * (1 - Math.Cos(Math.PI * (length - 1 - i) / fade))
                : 1;
            signal[i] = (float)(Math.Sin(phase) * envelope);
        }

        return signal;
    }

    /// <summary>
    ///     The time-reversed sweep, rising 6 dB per octave so the sweep's pink energy (it lingers in the lows) becomes
    ///     white; scaled so the sweep convolved with it has unit gain at 1 kHz.
    /// </summary>
    float[] Inverse() {
        if (inverse is not null) {
            return inverse;
        }

        var sweep = Sweep;
        var length = sweep.Length;
        var rate = Math.Log(End / startHz);
        var filter = new float[length];
        for (var i = 0; i < length; i++) {
            var t = (length - 1 - i) / SampleRate;
            filter[i] = (float)(sweep[length - 1 - i] * Math.Exp((t / seconds - 1) * rate));
        }

        inverse = filter;
        var self = Convolve(sweep, filter);
        double re = 0, im = 0;
        // A band-limited impulse rings on both sides of zero lag; a real capture keeps both (the device latency).
        var span = (int)(0.1 * SampleRate);
        for (var i = Math.Max(0, length - 1 - span); i < Math.Min(self.Length, length - 1 + span); i++) {
            var angle = 2 * Math.PI * 1_000 * (i - (length - 1)) / SampleRate;
            re += self[i] * Math.Cos(angle);
            im -= self[i] * Math.Sin(angle);
        }

        gain = 1 / Math.Sqrt(re * re + im * im);
        return filter;
    }

    /// <summary>Linear convolution by FFT, in double precision for the dynamic range a long sweep buys.</summary>
    static double[] Convolve(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var length = a.Length + b.Length - 1;
        var size = 1;
        while (size < length) {
            size <<= 1;
        }

        var aRe = new double[size];
        var aIm = new double[size];
        var bRe = new double[size];
        var bIm = new double[size];
        for (var i = 0; i < a.Length; i++) {
            aRe[i] = a[i];
        }

        for (var i = 0; i < b.Length; i++) {
            bRe[i] = b[i];
        }

        Transform(aRe, aIm, false);
        Transform(bRe, bIm, false);
        for (var k = 0; k < size; k++) {
            var re = aRe[k] * bRe[k] - aIm[k] * bIm[k];
            var im = aRe[k] * bIm[k] + aIm[k] * bRe[k];
            aRe[k] = re;
            aIm[k] = im;
        }

        Transform(aRe, aIm, true);
        var result = new double[length];
        for (var i = 0; i < length; i++) {
            result[i] = aRe[i] / size;
        }

        return result;
    }

    /// <summary>Iterative radix-2 FFT in place (unscaled both ways).</summary>
    static void Transform(double[] re, double[] im, bool inverse) {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++) {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) {
                j ^= bit;
            }

            j ^= bit;
            if (i < j) {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1) {
            var angle = (inverse ? 2 : -2) * Math.PI / length;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var start = 0; start < n; start += length) {
                double cRe = 1, cIm = 0;
                for (var k = 0; k < length / 2; k++) {
                    var a = start + k;
                    var b = a + length / 2;
                    var tRe = re[b] * cRe - im[b] * cIm;
                    var tIm = re[b] * cIm + im[b] * cRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    var next = cRe * wRe - cIm * wIm;
                    cIm = cRe * wIm + cIm * wRe;
                    cRe = next;
                }
            }
        }
    }
}
