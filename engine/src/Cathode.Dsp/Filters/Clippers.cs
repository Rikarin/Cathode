using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

public enum ClipperShape {
    /// <summary>tanh: smooth, symmetric (diode pairs, overdrive).</summary>
    Soft,

    /// <summary>Hard clip at ±1 (op-amp rails, distortion).</summary>
    Hard,

    /// <summary>Biased tanh: asymmetric, so it adds even harmonics (fuzz).</summary>
    Asymmetric
}

/// <summary>
///     A memoryless clipper with first-order antiderivative anti-aliasing (Parker, Zavalishin and Le Bivic, DAFx-16;
///     Bilbao et al., IEEE SPL 2017): y[n] = (F(x[n]) - F(x[n-1])) / (x[n] - x[n-1]), where F is the antiderivative of
///     the clipping function. Adds half a sample of delay.
/// </summary>
public struct AdaaClipper {
    const double Bias = 0.3;
    const double Epsilon = 1e-4;
    static readonly double TanhBias = Math.Tanh(Bias);

    double _previous;
    double _previousAntiderivative;

    public void Reset() {
        _previous = 0;
        _previousAntiderivative = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(ClipperShape shape, float input, bool antialias = true) {
        double x = input;
        if (!antialias) {
            return (float)Function(shape, x);
        }

        var antiderivative = Antiderivative(shape, x);
        var delta = x - _previous;
        var y = Math.Abs(delta) < Epsilon
            ? Function(shape, 0.5 * (x + _previous)) // ill-conditioned: fall back to the midpoint
            : (antiderivative - _previousAntiderivative) / delta;
        _previous = x;
        _previousAntiderivative = antiderivative;
        return (float)y;
    }

    public static double Function(ClipperShape shape, double x) =>
        shape switch {
            ClipperShape.Soft => Math.Tanh(x),
            ClipperShape.Hard => Math.Clamp(x, -1.0, 1.0),
            _ => Math.Tanh(x + Bias) - TanhBias,
        };

    static double Antiderivative(ClipperShape shape, double x) =>
        shape switch {
            ClipperShape.Soft => LogCosh(x),
            ClipperShape.Hard => Math.Abs(x) <= 1 ? 0.5 * x * x : Math.Abs(x) - 0.5,
            _ => LogCosh(x + Bias) - TanhBias * x,
        };

    /// <summary>log(cosh(x)) without overflow for large |x|.</summary>
    static double LogCosh(double x) {
        var a = Math.Abs(x);
        return a + Math.Log(1 + Math.Exp(-2 * a)) - Math.Log(2);
    }
}
