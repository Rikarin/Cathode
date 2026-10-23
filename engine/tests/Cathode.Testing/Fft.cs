using System.Numerics;

namespace Cathode.Testing;

/// <summary>Straightforward double-precision radix-2 FFT. Built for analysis accuracy, not speed.</summary>
public static class Fft
{
    public static void Forward(Complex[] data)
    {
        var n = data.Length;
        if (n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Length must be a power of two.", nameof(data));
        }

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var wLength = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + length / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + length / 2] = u - v;
                    w *= wLength;
                }
            }
        }
    }

    /// <summary>Single-sided magnitude spectrum (bins 0..N/2) of a real signal, scaled so a full-scale sine reads 1.0.</summary>
    public static double[] Magnitudes(ReadOnlySpan<float> signal)
    {
        var data = new Complex[signal.Length];
        for (var i = 0; i < signal.Length; i++)
        {
            data[i] = signal[i];
        }

        Forward(data);
        var result = new double[signal.Length / 2 + 1];
        for (var k = 0; k < result.Length; k++)
        {
            var scale = k == 0 || k == signal.Length / 2 ? 1.0 : 2.0;
            result[k] = data[k].Magnitude * scale / signal.Length;
        }

        return result;
    }
}
