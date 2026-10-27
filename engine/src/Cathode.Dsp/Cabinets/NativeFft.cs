using Cathode.Dsp.Convolution;

namespace Cathode.Dsp.Cabinets;

/// <summary>
///     Radix-2 complex FFT whose twiddles live in native memory (bit reversal is computed in place), so a block can
///     own a large one without putting anything on the managed large object heap. Not thread-safe; dispose it with
///     its owner.
/// </summary>
public sealed class NativeFft : IDisposable {
    readonly NativeBuffer cos;
    readonly NativeBuffer sin;

    public int Size { get; }

    public NativeFft(int size) {
        Size = Fft.CheckSize(size);
        cos = new(size / 2);
        sin = new(size / 2);
        Fft.Twiddles(cos.Span, sin.Span, size);
    }

    public void Forward(Span<float> re, Span<float> im) => Transform(re, im, false);

    /// <summary>Inverse transform, scaled by 1/N so that Inverse(Forward(x)) == x.</summary>
    public void Inverse(Span<float> re, Span<float> im) {
        Transform(re, im, true);
        var scale = 1f / Size;
        for (var i = 0; i < Size; i++) {
            re[i] *= scale;
            im[i] *= scale;
        }
    }

    /// <summary>
    ///     <c>re[0..N/2]</c> holds a natural-log magnitude; leaves the minimum-phase spectrum with that magnitude in
    ///     <c>re</c>/<c>im</c>, bins 0 … N/2 (through the real cepstrum).
    /// </summary>
    public void MinimumPhase(Span<float> re, Span<float> im) {
        var n = Size;
        var half = n / 2;
        for (var k = 1; k < half; k++) {
            re[n - k] = re[k];
        }

        im.Clear();
        Inverse(re, im);

        // Fold the anticausal half of the real cepstrum onto the causal half.
        for (var i = 1; i < half; i++) {
            re[i] *= 2f;
            re[n - i] = 0f;
        }

        im.Clear();
        Forward(re, im);
        for (var k = 0; k <= half; k++) {
            var magnitude = MathF.Exp(re[k]);
            var phase = im[k];
            re[k] = magnitude * MathF.Cos(phase);
            im[k] = magnitude * MathF.Sin(phase);
        }
    }

    public void Dispose() {
        cos.Dispose();
        sin.Dispose();
    }

    void Transform(Span<float> re, Span<float> im, bool inverse) {
        var n = Size;
        if (re.Length < n || im.Length < n) {
            throw new ArgumentException("Buffers are shorter than the FFT size.");
        }

        // Bit reversal by the Gold-Rader counter.
        for (int i = 0, j = 0; i < n - 1; i++) {
            if (i < j) {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }

            var bit = n >> 1;
            while ((j & bit) != 0) {
                j ^= bit;
                bit >>= 1;
            }

            j |= bit;
        }

        Fft.Butterflies(cos.Span, sin.Span, re, im, n, inverse);
    }
}
