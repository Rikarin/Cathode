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
        if (size < 2 || (size & (size - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "FFT size must be a power of two >= 2.");
        }

        Size = size;
        cos = new(size / 2);
        sin = new(size / 2);
        for (var i = 0; i < size / 2; i++) {
            var angle = -2.0 * Math.PI * i / size;
            cos[i] = (float)Math.Cos(angle);
            sin[i] = (float)Math.Sin(angle);
        }
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

        var cos = this.cos.Span;
        var sin = this.sin.Span;
        var sign = inverse ? -1f : 1f;
        for (var length = 2; length <= n; length <<= 1) {
            var half = length >> 1;
            var stride = n / length;
            for (var start = 0; start < n; start += length) {
                for (var k = 0; k < half; k++) {
                    var wr = cos[k * stride];
                    var wi = sign * sin[k * stride];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
