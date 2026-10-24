using System.Numerics;

namespace Cathode.Dsp.Cabinets;

/// <summary>
///     Fractional-octave magnitude smoothing of an impulse response (the Cab block's Smoothing): the power spectrum is
///     averaged over a window <c>octaves</c> wide around each bin, on a log-frequency scale, and the IR is passed
///     through the minimum-phase filter whose magnitude is the ratio of the smoothed to the original response (made by
///     the real cepstrum, as <see cref="CabIrSynthesizer" /> does). The result has the smoothed magnitude; as the
///     correction is causal and minimum phase, the onset (the slots' alignment) stays where it was. Narrow peaks and
///     notches (combs between mics, cabinet ripple) are evened out, as a smoothed measurement would show them; the
///     correction is limited to <see cref="MaxBoost" /> where a notch is filled in. Native scratch only, allocated
///     once: runs on the control thread at IR build time. Not thread-safe.
/// </summary>
public sealed class IrSmoother : IDisposable {
    /// <summary>The most a bin is raised (filling a notch), as a gain: 24 dB.</summary>
    public const float MaxBoost = 16f;

    /// <summary>The least the correction may ring on past the original end, samples (within the buffer).</summary>
    const int MinTail = 4_096;

    /// <summary>Fade at the end of the smoothed IR, samples.</summary>
    const int FadeLength = 64;

    readonly NativeFft fft;
    readonly NativeBuffer re;
    readonly NativeBuffer im;
    readonly NativeBuffer power;
    readonly NativeBuffer cepstrumRe;
    readonly NativeBuffer cepstrumIm;

    /// <summary>The longest IR this smoother takes.</summary>
    public int MaxLength { get; }

    public IrSmoother(int maxLength) {
        MaxLength = maxLength;
        var size = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(64, 2 * maxLength));
        fft = new(size);
        re = new(size);
        im = new(size);
        power = new(size / 2 + 1);
        cepstrumRe = new(size);
        cepstrumIm = new(size);
    }

    /// <summary>
    ///     Smooths the first <paramref name="used" /> samples of <paramref name="ir" /> over <paramref name="octaves" />
    ///     (0 leaves it untouched) and returns the new used length: the correction rings on past the original end, by
    ///     at least <see cref="MinTail" /> samples where <paramref name="ir" /> has room.
    /// </summary>
    public int Smooth(Span<float> ir, int used, double octaves) {
        used = Math.Min(used, Math.Min(ir.Length, MaxLength));
        if (octaves <= 0 || used < 2) {
            return used;
        }

        var n = fft.Size;
        var half = n / 2;
        var re = this.re.Span;
        var im = this.im.Span;
        var power = this.power.Span;
        var logGain = cepstrumRe.Span;
        re.Clear();
        im.Clear();
        ir[..used].CopyTo(re);
        fft.Forward(re, im);
        for (var k = 0; k <= half; k++) {
            power[k] = re[k] * re[k] + im[k] * im[k];
        }

        // A window [k / r, k r] slides up monotonically, so a running sum does the averaging in O(N). The correction is
        // collected as a natural-log magnitude.
        var ratio = Math.Pow(2, octaves / 2);
        var low = 1;
        var high = 0;
        var sum = 0.0;
        var maxLog = Math.Log(MaxBoost);
        logGain[0] = 0f;
        for (var k = 1; k <= half; k++) {
            var to = Math.Min(half, (int)Math.Ceiling(k * ratio));
            var from = Math.Max(1, (int)Math.Floor(k / ratio));
            while (high < to) {
                sum += power[++high];
            }

            while (low < from) {
                sum -= power[low++];
            }

            var smoothed = Math.Max(0, sum / (high - low + 1));
            logGain[k] = power[k] > 1e-24f && smoothed > 1e-24
                ? (float)Math.Clamp(0.5 * Math.Log(smoothed / power[k]), -maxLog, maxLog)
                : 0f;
        }

        MinimumPhase();

        // The correction on the IR's spectrum, then back to time.
        var gainRe = cepstrumRe.Span;
        var gainIm = cepstrumIm.Span;
        for (var k = 0; k <= half; k++) {
            var r = re[k] * gainRe[k] - im[k] * gainIm[k];
            var i = re[k] * gainIm[k] + im[k] * gainRe[k];
            re[k] = r;
            im[k] = i;
        }

        im[0] = 0f;
        im[half] = 0f;
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
            im[n - k] = -im[k];
        }

        fft.Inverse(re, im);
        var length = Math.Min(ir.Length, Math.Min(half, used + Math.Max(used / 2, MinTail)));
        re[..length].CopyTo(ir);
        var fade = Math.Min(FadeLength, length);
        for (var i = 0; i < fade; i++) {
            ir[length - fade + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / fade));
        }

        return length;
    }

    public void Dispose() {
        fft.Dispose();
        re.Dispose();
        im.Dispose();
        power.Dispose();
        cepstrumRe.Dispose();
        cepstrumIm.Dispose();
    }

    /// <summary>
    ///     <c>cepstrumRe[0..N/2]</c> holds a natural-log magnitude; leaves the minimum-phase spectrum with that magnitude
    ///     in <c>cepstrumRe</c>/<c>cepstrumIm</c>, bins 0 … N/2.
    /// </summary>
    void MinimumPhase() {
        var n = fft.Size;
        var half = n / 2;
        var re = cepstrumRe.Span;
        var im = cepstrumIm.Span;
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
        }

        im.Clear();
        fft.Inverse(re, im);
        for (var i = 1; i < half; i++) {
            re[i] *= 2f;
            re[n - i] = 0f;
        }

        im.Clear();
        fft.Forward(re, im);
        for (var k = 0; k <= half; k++) {
            var magnitude = MathF.Exp(re[k]);
            var phase = im[k];
            re[k] = magnitude * MathF.Cos(phase);
            im[k] = magnitude * MathF.Sin(phase);
        }
    }
}
