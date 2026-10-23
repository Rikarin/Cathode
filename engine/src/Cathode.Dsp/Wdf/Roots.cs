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

    /// <summary>1N4007-style rectifier (the usual SPICE fit, Is 7.03 nA, n 1.81): about 0.6 V at 1 mA.</summary>
    public static DiodeModel Rectifier { get; } = new(7.03e-9, 1.81);

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

    /// <summary>
    ///     Port voltage from the conducting stack's closed form. Both directions share one Wright omega call: the reverse
    ///     stack is the forward formula with a, R Is and n Vt negated, which is exact in floating point.
    /// </summary>
    readonly double Closed(double a) {
        var forward = UsesForward(a);
        if (!forward && reverseIs == 0) {
            return a;
        }

        var sign = forward ? 1.0 : -1.0;
        var w = WrightOmega.Fast((forward ? forwardBase : reverseBase) + sign * a * (forward ? forwardInverse : reverseInverse));
        return a + sign * (forward ? forwardRIs : reverseRIs) - sign * (forward ? forwardVt : reverseVt) * w;
    }

    /// <summary>B(a), the antiderivative of the closed-form b(a) = 2 v(a) - a, with B(0) = 0.</summary>
    readonly double Integral(double a) {
        var forward = UsesForward(a);
        if (!forward && reverseIs == 0) {
            return 0.5 * a * a;
        }

        var sign = forward ? 1.0 : -1.0;
        var vt = forward ? forwardVt : reverseVt;
        var w = WrightOmega.Fast((forward ? forwardBase : reverseBase) + sign * a * (forward ? forwardInverse : reverseInverse));
        var offset = forward ? forwardOffset : reverseOffset;
        return a * (0.5 * a + 2 * (sign * (forward ? forwardRIs : reverseRIs))) - vt * vt * w * (w + 2) + offset;
    }
}

