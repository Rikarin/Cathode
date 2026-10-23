using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

public enum BiquadType {
    LowPass,
    HighPass,
    BandPass,
    Notch,
    Peak,
    LowShelf,
    HighShelf
}

/// <summary>Normalised biquad coefficients (a0 = 1). Double precision keeps low-frequency, high-Q sections stable.</summary>
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2) {
    /// <summary>Shelves above this Q develop a large resonant bump at the corner, so their Q is limited.</summary>
    public const double MaxShelfQ = 1.0;

    public static BiquadCoefficients Identity { get; } = new(1, 0, 0, 0, 0);

    /// <summary>
    ///     RBJ "Audio EQ Cookbook" designs. <paramref name="frequency" /> is clamped below Nyquist, and shelf Q is
    ///     clamped to <see cref="MaxShelfQ" />.
    /// </summary>
    public static BiquadCoefficients Design(
        BiquadType type,
        double sampleRate,
        double frequency,
        double q,
        double gainDb = 0
    ) {
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        q = type is BiquadType.LowShelf or BiquadType.HighShelf ? Math.Clamp(q, 1e-3, MaxShelfQ) : Math.Max(q, 1e-3);
        var w0 = 2.0 * Math.PI * f0 / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2.0 * q);
        var a = Math.Pow(10.0, gainDb / 40.0);

        double b0, b1, b2, a0, a1, a2;
        switch (type) {
            case BiquadType.LowPass:
                b0 = (1 - cos) / 2;
                b1 = 1 - cos;
                b2 = (1 - cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            case BiquadType.HighPass:
                b0 = (1 + cos) / 2;
                b1 = -(1 + cos);
                b2 = (1 + cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            case BiquadType.BandPass:
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            case BiquadType.Notch:
                b0 = 1;
                b1 = -2 * cos;
                b2 = 1;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            case BiquadType.Peak:
                b0 = 1 + alpha * a;
                b1 = -2 * cos;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2 * cos;
                a2 = 1 - alpha / a;
                break;
            case BiquadType.LowShelf: {
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 - (a - 1) * cos + s);
                b1 = 2 * a * (a - 1 - (a + 1) * cos);
                b2 = a * (a + 1 - (a - 1) * cos - s);
                a0 = a + 1 + (a - 1) * cos + s;
                a1 = -2 * (a - 1 + (a + 1) * cos);
                a2 = a + 1 + (a - 1) * cos - s;
                break;
            }

            case BiquadType.HighShelf: {
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 + (a - 1) * cos + s);
                b1 = -2 * a * (a - 1 + (a + 1) * cos);
                b2 = a * (a + 1 + (a - 1) * cos - s);
                a0 = a + 1 - (a - 1) * cos + s;
                a1 = 2 * (a - 1 - (a + 1) * cos);
                a2 = a + 1 - (a - 1) * cos - s;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        return new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>|H(e^jω)| at <paramref name="frequency" />, evaluated from the coefficients.</summary>
    public double MagnitudeAt(double frequency, double sampleRate) {
        var w = 2.0 * Math.PI * frequency / sampleRate;
        var (c1, s1, c2, s2) = (Math.Cos(w), Math.Sin(w), Math.Cos(2 * w), Math.Sin(2 * w));
        var numRe = B0 + B1 * c1 + B2 * c2;
        var numIm = -(B1 * s1 + B2 * s2);
        var denRe = 1 + A1 * c1 + A2 * c2;
        var denIm = -(A1 * s1 + A2 * s2);
        return Math.Sqrt((numRe * numRe + numIm * numIm) / (denRe * denRe + denIm * denIm));
    }
}

/// <summary>Transposed direct form II biquad state for one channel.</summary>
public struct BiquadState {
    const double DenormalThreshold = 1e-30;

    double z1;
    double z2;

    public void Reset() {
        z1 = 0;
        z2 = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(in BiquadCoefficients c, double x) {
        var y = c.B0 * x + z1;
        z1 = c.B1 * x - c.A1 * y + z2;
        z2 = c.B2 * x - c.A2 * y;
        return y;
    }

    public void Process(in BiquadCoefficients c, Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)Tick(c, samples[i]);
        }

        FlushDenormals();
    }

    /// <summary>Zeroes state that has decayed into the subnormal range, where arithmetic gets very slow.</summary>
    public void FlushDenormals() {
        if (Math.Abs(z1) < DenormalThreshold) {
            z1 = 0;
        }

        if (Math.Abs(z2) < DenormalThreshold) {
            z2 = 0;
        }
    }
}
