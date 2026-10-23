namespace Cathode.Dsp.Convolution;

/// <summary>
///     In-place iterative radix-2 complex FFT on split real/imaginary arrays, with precomputed twiddles and
///     bit-reversal. Allocation-free after construction.
/// </summary>
public sealed class Fft {
    readonly float[] _cos;
    readonly float[] _sin;
    readonly int[] _reverse;

    public Fft(int size) {
        if (size < 2 || (size & (size - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "FFT size must be a power of two >= 2.");
        }

        Size = size;
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++) {
            var angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }

        _reverse = new int[size];
        var bits = 0;
        while ((1 << bits) < size) {
            bits++;
        }

        for (var i = 0; i < size; i++) {
            var r = 0;
            for (var b = 0; b < bits; b++) {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reverse[i] = r;
        }
    }

    public int Size { get; }

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

    void Transform(Span<float> re, Span<float> im, bool inverse) {
        var n = Size;
        if (re.Length < n || im.Length < n) {
            throw new ArgumentException("Buffers are shorter than the FFT size.");
        }

        for (var i = 0; i < n; i++) {
            var j = _reverse[i];
            if (i < j) {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        var sign = inverse ? -1f : 1f;
        for (var length = 2; length <= n; length <<= 1) {
            var half = length >> 1;
            var stride = n / length;
            for (var start = 0; start < n; start += length) {
                for (var k = 0; k < half; k++) {
                    var wr = _cos[k * stride];
                    var wi = sign * _sin[k * stride];
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
