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

    /// <summary>
    ///     A JFET's square-law stage: tanh on the positive half, a gentler 2 tanh(x / 2) on the negative (the drain
    ///     approaching pinch-off rounds slowly), so even harmonics with a soft onset.
    /// </summary>
    Fet,

    /// <summary>
    ///     A leaky half-wave rectifier: tanh on the positive half, a fifth of it on the negative (octave-rich).
    /// </summary>
    Rectifier,

    /// <summary>
    ///     A sine knee, sin(x) to ±π/2 and flat beyond: harder than tanh, softer than a hard clip, the curve Clip Shape
    ///     varies furthest.
    /// </summary>
    Variable,

    /// <summary>
    ///     A high-voltage triode: 1.5 tanh(x / 1.5) on the positive half (more headroom), tanh on the negative.
    /// </summary>
    HvTube,

    /// <summary>A starved-plate triode: tanh on the positive half, a hard tanh(2 x) / 2 on the negative.</summary>
    LvTube,

    /// <summary>A silicon diode pair's exponential knee: sign(x) (1 - e^-|x|).</summary>
    Silicon,

    /// <summary>A 4558 with diodes in its feedback (the Tube Screamer's): tanh plus a twentieth of the input.</summary>
    OpAmpDiode,

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
    ///     (−1..1): towards the soft <see cref="ClipperShape.Germanium" /> curve below 0, towards a hard clip above,
    ///     the shape itself at 0. The blend is linear in the curves, so it is linear in their antiderivatives too.
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
            ClipperShape.Fet => x >= 0 ? Math.Tanh(x) : 2 * Math.Tanh(0.5 * x),
            ClipperShape.Rectifier => x >= 0 ? Math.Tanh(x) : 0.2 * Math.Tanh(x),
            ClipperShape.Variable => Math.Abs(x) < 0.5 * Math.PI ? Math.Sin(x) : Math.Sign(x),
            ClipperShape.HvTube => x >= 0 ? 1.5 * Math.Tanh(x / 1.5) : Math.Tanh(x),
            ClipperShape.LvTube => x >= 0 ? Math.Tanh(x) : 0.5 * Math.Tanh(2 * x),
            ClipperShape.Silicon => Math.Sign(x) * (1 - Math.Exp(-Math.Abs(x))),
            ClipperShape.OpAmpDiode => Math.Tanh(x) + 0.05 * x,
            ClipperShape.Linear => x,
            _ => Math.Tanh(x + Bias) - TanhBias
        };

    /// <summary>
    ///     <see cref="Function" /> blended towards <paramref name="other" /> by <paramref name="weight" /> (0..1).
    /// </summary>
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
            ClipperShape.Fet => x >= 0 ? LogCosh(x) : 4 * LogCosh(0.5 * x),
            ClipperShape.Rectifier => x >= 0 ? LogCosh(x) : 0.2 * LogCosh(x),
            ClipperShape.Variable => Math.Abs(x) < 0.5 * Math.PI ? 1 - Math.Cos(x) : Math.Abs(x) - 0.5 * Math.PI + 1,
            ClipperShape.HvTube => x >= 0 ? 2.25 * LogCosh(x / 1.5) : LogCosh(x),
            ClipperShape.LvTube => x >= 0 ? LogCosh(x) : 0.25 * LogCosh(2 * x),
            ClipperShape.Silicon => Math.Abs(x) + Math.Exp(-Math.Abs(x)) - 1,
            ClipperShape.OpAmpDiode => LogCosh(x) + 0.025 * x * x,
            ClipperShape.Linear => 0.5 * x * x,
            _ => LogCosh(x + Bias) - TanhBias * x
        };

    /// <summary>log(cosh(x)) without overflow for large |x|.</summary>
    static double LogCosh(double x) {
        var a = Math.Abs(x);
        return a + Math.Log(1 + Math.Exp(-2 * a)) - Math.Log(2);
    }
}
