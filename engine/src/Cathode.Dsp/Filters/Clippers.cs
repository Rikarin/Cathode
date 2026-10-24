using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

public enum ClipperShape {
    /// <summary>tanh: smooth, symmetric (diode pairs, overdrive).</summary>
    Soft,

    /// <summary>Hard clip at ±1 (op-amp rails, distortion).</summary>
    Hard,

    /// <summary>Biased tanh: asymmetric, so it adds even harmonics (fuzz).</summary>
    Asymmetric,

    /// <summary>x / (1 + |x|): the longest, softest knee (germanium diodes).</summary>
    Germanium,

    /// <summary>2 tanh(x / 2): a diode pair with twice the forward drop (LEDs), louder and cleaner.</summary>
    Led,

    /// <summary>tanh on the positive half, a harder tanh(1.5 x) / 1.5 on the negative (a triode's grid side).</summary>
    Tube,

    /// <summary>x / sqrt(1 + x²): soft rails, between tanh and a hard clip (an op-amp running out of supply).</summary>
    OpAmp,

    /// <summary>No clipping: a clean gain stage.</summary>
    Linear
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

    double previous;
    double previousAntiderivative;

    public void Reset() {
        previous = 0;
        previousAntiderivative = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(ClipperShape shape, float input, bool antialias = true) {
        double x = input;
        if (!antialias) {
            return (float)Function(shape, x);
        }

        var antiderivative = Antiderivative(shape, x);
        var delta = x - previous;
        var y = Math.Abs(delta) < Epsilon
            ? Function(shape, 0.5 * (x + previous)) // ill-conditioned: fall back to the midpoint
            : (antiderivative - previousAntiderivative) / delta;
        previous = x;
        previousAntiderivative = antiderivative;
        return (float)y;
    }

    /// <summary>
    ///     As <see cref="Process(ClipperShape, float, bool)" />, with the curve blended by <paramref name="hardness" />
    ///     (−1..1): towards the soft <see cref="ClipperShape.Germanium" /> curve below 0, towards a hard clip above, the
    ///     shape itself at 0. The blend is linear in the curves, so it is linear in their antiderivatives too.
    /// </summary>
    public float Process(ClipperShape shape, float input, float hardness, bool antialias) {
        if (hardness == 0f) {
            return Process(shape, input, antialias);
        }

        var other = hardness > 0 ? ClipperShape.Hard : ClipperShape.Germanium;
        var w = Math.Min(1.0, Math.Abs(hardness));
        double x = input;
        if (!antialias) {
            return (float)Blend(shape, other, w, x);
        }

        var antiderivative = (1 - w) * Antiderivative(shape, x) + w * Antiderivative(other, x);
        var delta = x - previous;
        var y = Math.Abs(delta) < Epsilon
            ? Blend(shape, other, w, 0.5 * (x + previous))
            : (antiderivative - previousAntiderivative) / delta;
        previous = x;
        previousAntiderivative = antiderivative;
        return (float)y;
    }

    public static double Function(ClipperShape shape, double x) =>
        shape switch {
            ClipperShape.Soft => Math.Tanh(x),
            ClipperShape.Hard => Math.Clamp(x, -1.0, 1.0),
            ClipperShape.Germanium => x / (1 + Math.Abs(x)),
            ClipperShape.Led => 2 * Math.Tanh(0.5 * x),
            ClipperShape.Tube => x >= 0 ? Math.Tanh(x) : Math.Tanh(1.5 * x) / 1.5,
            ClipperShape.OpAmp => x / Math.Sqrt(1 + x * x),
            ClipperShape.Linear => x,
            _ => Math.Tanh(x + Bias) - TanhBias
        };

    /// <summary><see cref="Function" /> blended towards <paramref name="other" /> by <paramref name="weight" /> (0..1).</summary>
    public static double Blend(ClipperShape shape, ClipperShape other, double weight, double x) =>
        (1 - weight) * Function(shape, x) + weight * Function(other, x);

    static double Antiderivative(ClipperShape shape, double x) =>
        shape switch {
            ClipperShape.Soft => LogCosh(x),
            ClipperShape.Hard => Math.Abs(x) <= 1 ? 0.5 * x * x : Math.Abs(x) - 0.5,
            ClipperShape.Germanium => Math.Abs(x) - Math.Log(1 + Math.Abs(x)),
            ClipperShape.Led => 4 * LogCosh(0.5 * x),
            ClipperShape.Tube => x >= 0 ? LogCosh(x) : LogCosh(1.5 * x) / 2.25,
            ClipperShape.OpAmp => Math.Sqrt(1 + x * x) - 1,
            ClipperShape.Linear => 0.5 * x * x,
            _ => LogCosh(x + Bias) - TanhBias * x
        };

    /// <summary>log(cosh(x)) without overflow for large |x|.</summary>
    static double LogCosh(double x) {
        var a = Math.Abs(x);
        return a + Math.Log(1 + Math.Exp(-2 * a)) - Math.Log(2);
    }
}
