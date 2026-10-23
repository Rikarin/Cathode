using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>An ideal voltage source: v = V, so b = 2V - a.</summary>
public struct IdealVoltageSource : IWdfRoot {
    /// <summary>Source voltage; set it before each <see cref="WdfCircuit{TTree,TRoot}.Tick" />.</summary>
    public double Volts;

    public readonly double Voltage => Volts;

    public readonly void Adapt(double resistance) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Scatter(double a) => 2 * Volts - a;

    public readonly void Reset() { }
}

/// <summary>An open circuit (i = 0, b = a): the tree's port voltage is its open-circuit voltage.</summary>
public struct OpenCircuit : IWdfRoot {
    double a;

    public readonly double Voltage => a;

    public readonly void Adapt(double resistance) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Scatter(double a) {
        this.a = a;
        return a;
    }

    public void Reset() => a = 0;
}

/// <summary>A short circuit (v = 0, b = -a).</summary>
public struct ShortCircuit : IWdfRoot {
    public readonly double Voltage => 0;

    public readonly void Adapt(double resistance) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double Scatter(double a) => -a;

    public readonly void Reset() { }
}

/// <summary>Shockley parameters of a junction: i = Is (exp(v / (n Vt)) - 1).</summary>
public readonly record struct DiodeModel(double SaturationCurrent, double Ideality) {
    /// <summary>kT/q at about 27 °C, in volts.</summary>
    public const double ThermalVoltage = 0.02585;

    /// <summary>1N914 / 1N4148 small-signal silicon: about 0.6 V at 1 mA.</summary>
    public static DiodeModel Silicon { get; } = new(2.52e-9, 1.752);

    /// <summary>1N34A-style germanium: about 0.3 V at 1 mA, with a soft knee.</summary>
    public static DiodeModel Germanium { get; } = new(2e-7, 1.3);

    /// <summary>BAT41-style Schottky: about 0.3 V at 1 mA, sharper than germanium.</summary>
    public static DiodeModel Schottky { get; } = new(1e-8, 1.04);

    /// <summary>Red LED: about 1.7 V at 1 mA.</summary>
    public static DiodeModel Led { get; } = new(1e-18, 1.9);

    /// <summary>
    ///     A MOSFET wired as a clipper (gate to drain), approximated as a very soft junction around 1.5 V; not a
    ///     square-law device model.
    /// </summary>
    public static DiodeModel Mosfet { get; } = new(1e-10, 4);

    /// <summary>The emission voltage n Vt of <paramref name="count" /> identical diodes in series.</summary>
    public double EmissionVoltage(int count) => Ideality * ThermalVoltage * count;
}

/// <summary>
///     Two antiparallel diode stacks at the root: <c>forwardCount</c> diodes conduct for positive port voltage and
///     <c>reverseCount</c> for negative, so unequal stacks (or types) give asymmetric clipping. A count of zero leaves
///     that direction open, which makes a single diode; both zero makes an open circuit.
/// </summary>
/// <remarks>
///     <para>
///         Solves (a - v) / R = i(v) for the port voltage. The conducting stack alone has the closed form
///         v = a + R Is - n Vt ω(ln(R Is / n Vt) + (a + R Is) / n Vt) (Werner, Nangia, Smith and Abel, "Resolving wave
///         digital filters with multiple/multiport nonlinearities", DAFx-15; ω is the Wright omega function). That is
///         the initial guess; Newton steps on the full two-stack equation (usually one) then remove the other stack's
///         leakage, so the result matches an iterated implicit solve to better than 1e-6 V.
///     </para>
///     <para>
///         With <see cref="Antialiased" /> set, the root instead returns the first-order antiderivative antialiased
///         wave b = (B(a[n]) - B(a[n-1])) / (a[n] - a[n-1]), where B integrates the closed form (∫ω = ω²/2 + ω), after
///         Albertini, Bernardini and Sarti, "Antiderivative antialiasing in nonlinear wave digital filters", DAFx-20.
///         The closed form ignores the other stack's leakage (at most R Is), and the root's response is half a sample
///         late, which at 4x oversampling is a few microseconds.
///     </para>
/// </remarks>
public struct DiodePair : IWdfRoot {
    const double MaxExponent = 300;
    const int MaxIterations = 4;
    const double IllConditioned = 1e-4; // V: below this change of a, the antialiased form falls back to the midpoint

    double forwardIs;
    double forwardVt;
    double forwardInverse; // 1 / n Vt
    double reverseIs;
    double reverseVt;
    double reverseInverse;
    double resistance;
    double forwardRIs;
    double forwardBase; // ω(forwardBase + a / n Vt) solves the forward stack
    double forwardOffset; // makes B(0) = 0
    double reverseRIs;
    double reverseBase;
    double reverseOffset;
    double previous;
    double previousIntegral;
    double voltage;

    /// <summary>Use antiderivative antialiasing instead of the exact solve.</summary>
    public bool Antialiased { readonly get; set; }

    /// <summary>True when neither direction conducts.</summary>
    public readonly bool IsOpen => forwardIs == 0 && reverseIs == 0;

    public readonly double Voltage => voltage;

    public DiodePair(DiodeModel forward, int forwardCount, DiodeModel reverse, int reverseCount) =>
        Configure(forward, forwardCount, reverse, reverseCount);

    public static DiodePair Symmetric(DiodeModel model, int count = 1) => new(model, count, model, count);

    /// <summary>Changes the diodes and re-adapts to the current resistance.</summary>
    public void Configure(DiodeModel forward, int forwardCount, DiodeModel reverse, int reverseCount) {
        forwardIs = forwardCount > 0 ? forward.SaturationCurrent : 0;
        forwardVt = forwardCount > 0 ? forward.EmissionVoltage(forwardCount) : 1;
        reverseIs = reverseCount > 0 ? reverse.SaturationCurrent : 0;
        reverseVt = reverseCount > 0 ? reverse.EmissionVoltage(reverseCount) : 1;
        forwardInverse = 1 / forwardVt;
        reverseInverse = 1 / reverseVt;
        Adapt(resistance > 0 ? resistance : 1);
    }

    public void Adapt(double resistance) {
        this.resistance = resistance;
        forwardRIs = resistance * forwardIs;
        reverseRIs = resistance * reverseIs;
        var w = forwardRIs / forwardVt; // ω at a = 0
        forwardBase = forwardIs > 0 ? Math.Log(w) + w : 0;
        forwardOffset = forwardVt * forwardVt * w * (w + 2);
        w = reverseRIs / reverseVt;
        reverseBase = reverseIs > 0 ? Math.Log(w) + w : 0;
        reverseOffset = reverseVt * reverseVt * w * (w + 2);
        previousIntegral = Integral(previous); // the antiderivative changed with R
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Scatter(double a) {
        if (!Antialiased) {
            voltage = Solve(a);
            return 2 * voltage - a;
        }

        var integral = Integral(a);
        var delta = a - previous;
        double b;
        if (Math.Abs(delta) > IllConditioned) {
            b = (integral - previousIntegral) / delta;
            voltage = 0.5 * (b + 0.5 * (a + previous));
        } else {
            var middle = 0.5 * (a + previous);
            voltage = Closed(middle);
            b = 2 * voltage - middle;
        }

        previous = a;
        previousIntegral = integral;
        return b;
    }

    public void Reset() {
        previous = 0;
        previousIntegral = 0;
        voltage = 0;
    }

    /// <summary>The exact port voltage for incident wave <paramref name="a" />.</summary>
    public readonly double Solve(double a) {
        var v = Closed(a);
        if (IsOpen) {
            return v;
        }

        // Newton on g(v) = (a - v) / R - i(v), which is monotonic. Convergence is quadratic, so a step below 1 µV
        // leaves an error far below that and ends the loop; usually one step is enough.
        for (var k = 0; k < MaxIterations; k++) {
            var forward = forwardIs * Math.Exp(Math.Min(v / forwardVt, MaxExponent));
            var reverse = reverseIs * Math.Exp(Math.Min(-v / reverseVt, MaxExponent));
            var g = (a - v) / resistance - forward + forwardIs + reverse - reverseIs;
            var step = g / (1 / resistance + forward / forwardVt + reverse / reverseVt);
            v += step;
            if (step * step < 1e-12) {
                break;
            }
        }

        return v;
    }

    /// <summary>The stack that conducts in the direction of a (or the only one), by itself.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly bool UsesForward(double a) => forwardIs > 0 && (a >= 0 || reverseIs == 0);

    /// <summary>Port voltage from the conducting stack's closed form.</summary>
    readonly double Closed(double a) {
        if (UsesForward(a)) {
            return a + forwardRIs - forwardVt * WrightOmega.Fast(forwardBase + a * forwardInverse);
        }

        return reverseIs > 0
            ? a - reverseRIs + reverseVt * WrightOmega.Fast(reverseBase - a * reverseInverse)
            : a;
    }

    /// <summary>B(a), the antiderivative of the closed-form b(a) = 2 v(a) - a, with B(0) = 0.</summary>
    readonly double Integral(double a) {
        if (UsesForward(a)) {
            var w = WrightOmega.Fast(forwardBase + a * forwardInverse);
            return a * (0.5 * a + 2 * forwardRIs) - forwardVt * forwardVt * w * (w + 2) + forwardOffset;
        }

        if (reverseIs > 0) {
            var w = WrightOmega.Fast(reverseBase - a * reverseInverse);
            return a * (0.5 * a - 2 * reverseRIs) - reverseVt * reverseVt * w * (w + 2) + reverseOffset;
        }

        return 0.5 * a * a;
    }
}

/// <summary>
///     The Wright omega function ω(x), the solution of ω + ln ω = x (equivalently W(e^x) with Lambert's W). It turns the
///     implicit equation of a diode behind a resistor into a closed form.
/// </summary>
public static class WrightOmega {
    // Initial estimate: the cubic of D'Angelo, Gabrielli and Turchet, "Fast approximation of the Lambert W function
    // for virtual analog modelling" (DAFx-19), the series e^x - e^2x below it and x - ln x above it, the last two with
    // rough (2e-4) exponential and logarithm. One step of Fritsch's third-order iteration (Lawrence, Corless and
    // Jeffrey, ACM TOMS 38(3), 2012) then lands within 1e-5, for a single accurate logarithm.
    const double Low = -2.0;
    const double High = 8.0;
    const double C3 = -1.314293149877800e-3;
    const double C2 = 4.775931364975583e-2;
    const double C1 = 3.631952663804445e-1;
    const double C0 = 6.313183464296682e-1;

    /// <summary>ω(x) to a relative error below 1e-5.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Fast(double x) {
        double y;
        if (x < Low) {
            var e = RoughExp(x);
            y = e * (1 - e);
        } else if (x < High) {
            y = C0 + x * (C1 + x * (C2 + x * C3));
        } else {
            y = x - RoughLog(x);
        }

        return Fritsch(x, y);
    }

    /// <summary>Iterates to double precision. Reference use only.</summary>
    public static double Precise(double x) {
        var y = Fast(x);
        for (var i = 0; i < 20; i++) {
            var next = Fritsch(x, y);
            if (Math.Abs(next - y) <= 1e-15 * y) {
                return next;
            }

            y = next;
        }

        return y;
    }

    /// <summary>e^x for x below zero, within 2e-4 relative: two polynomials instead of a library call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double RoughExp(double x) {
        var t = x * 1.4426950408889634; // log2(e)
        if (t < -1_000) {
            return 0;
        }

        var whole = Math.Floor(t);
        var f = t - whole;
        var fraction = 0.9998124566870177 + f * (0.6968362422187562 + f * (0.2241283726980907 + f * 0.0790204128174744));
        return fraction * BitConverter.Int64BitsToDouble((long)(whole + 1_023) << 52);
    }

    /// <summary>ln x for x of at least 1, within 2e-4 absolute.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double RoughLog(double x) {
        var bits = BitConverter.DoubleToInt64Bits(x);
        var exponent = (bits >> 52) - 1_023;
        var t = BitConverter.Int64BitsToDouble((bits & 0x000F_FFFF_FFFF_FFFF) | 0x3FF0_0000_0000_0000) - 1;
        var log2 = 2.0317972985635672e-4 +
                   t * (1.4361078271459524 + t * (-0.6695422759217478 + t * (0.3122409720346481 + t * -0.0791581614460981)));
        return (exponent + log2) * 0.6931471805599453;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Fritsch(double x, double y) {
        if (y <= 0) {
            return 0; // e^x underflowed: ω is below the smallest double too
        }

        var r = x - y - Math.Log(y);
        var z = 1 + y;
        var q = z * (z + 2.0 / 3.0 * r);
        return y * (1 + r * (q - 0.5 * r) / (z * (q - r)));
    }
}
