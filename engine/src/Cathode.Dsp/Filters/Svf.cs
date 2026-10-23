using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

/// <summary>
///     Coefficients for a trapezoidal (TPT/ZDF) state-variable filter, after Andrew Simper's "SVF linear trap
///     optimised 2" (Cytomic, 2013) and Zavalishin's "The Art of VA Filter Design".
/// </summary>
/// <remarks>
///     The magnitude responses equal the RBJ designs in <see cref="BiquadCoefficients" />, but the state variables are
///     the integrator states of an analog prototype, so the filter stays well-behaved when its coefficients move
///     quickly. Use this, not a direct-form biquad, for any filter whose parameters can be modulated.
/// </remarks>
public readonly record struct SvfCoefficients(double A1, double A2, double A3, double M0, double M1, double M2) {
    public static SvfCoefficients Design(
        BiquadType type,
        double sampleRate,
        double frequency,
        double q,
        double gainDb = 0
    ) {
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        q = type is BiquadType.LowShelf or BiquadType.HighShelf
            ? Math.Clamp(q, 1e-3, BiquadCoefficients.MaxShelfQ)
            : Math.Max(q, 1e-3);
        var a = Math.Pow(10.0, gainDb / 40.0);
        var g = Math.Tan(Math.PI * f0 / sampleRate);
        var k = 1.0 / q;

        double m0, m1, m2;
        switch (type) {
            case BiquadType.LowPass:
                (m0, m1, m2) = (0, 0, 1);
                break;
            case BiquadType.HighPass:
                (m0, m1, m2) = (1, -k, -1);
                break;
            case BiquadType.BandPass:
                (m0, m1, m2) = (0, k, 0); // constant 0 dB peak, like the RBJ band-pass
                break;
            case BiquadType.Notch:
                (m0, m1, m2) = (1, -k, 0);
                break;
            case BiquadType.Peak:
                k = 1.0 / (q * a);
                (m0, m1, m2) = (1, k * (a * a - 1), 0);
                break;
            case BiquadType.LowShelf:
                g /= Math.Sqrt(a);
                (m0, m1, m2) = (1, k * (a - 1), a * a - 1);
                break;
            case BiquadType.HighShelf:
                g *= Math.Sqrt(a);
                (m0, m1, m2) = (a * a, k * (1 - a) * a, 1 - a * a);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        var a1 = 1.0 / (1.0 + g * (g + k));
        var a2 = g * a1;
        return new(a1, a2, g * a2, m0, m1, m2);
    }
}

/// <summary>State of one <see cref="SvfCoefficients" /> filter channel (two trapezoidal integrators).</summary>
public struct SvfState {
    const double DenormalThreshold = 1e-30;

    double _ic1;
    double _ic2;

    public void Reset() {
        _ic1 = 0;
        _ic2 = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(in SvfCoefficients c, double v0) {
        var v3 = v0 - _ic2;
        var v1 = c.A1 * _ic1 + c.A2 * v3;
        var v2 = _ic2 + c.A2 * _ic1 + c.A3 * v3;
        _ic1 = 2 * v1 - _ic1;
        _ic2 = 2 * v2 - _ic2;
        return c.M0 * v0 + c.M1 * v1 + c.M2 * v2;
    }

    public void Process(in SvfCoefficients c, Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)Tick(c, samples[i]);
        }

        FlushDenormals();
    }

    /// <summary>Zeroes state that has decayed into the subnormal range, where arithmetic gets very slow.</summary>
    public void FlushDenormals() {
        if (Math.Abs(_ic1) < DenormalThreshold) {
            _ic1 = 0;
        }

        if (Math.Abs(_ic2) < DenormalThreshold) {
            _ic2 = 0;
        }
    }
}
