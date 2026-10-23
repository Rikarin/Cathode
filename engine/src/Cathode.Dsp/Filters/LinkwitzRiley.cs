using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

/// <summary>
///     Coefficients for a 4th-order Linkwitz–Riley crossover built from trapezoidal state-variable filters (see
///     <see cref="SvfCoefficients" />): each band is a 2nd-order Butterworth section squared, so both bands are -6 dB at
///     the crossover frequency and in phase, and their sum is the 2nd-order allpass <c>(s² - √2 s + 1) / (s² + √2 s + 1)</c>,
///     flat in magnitude. The bilinear transform preserves that identity exactly.
/// </summary>
public readonly record struct LinkwitzRileyCoefficients(double A1, double A2, double A3) {
    /// <summary>Damping 1/Q of the Butterworth sections.</summary>
    public const double K = 1.4142135623730951;

    public static LinkwitzRileyCoefficients Design(double sampleRate, double frequency) {
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        var g = Math.Tan(Math.PI * f0 / sampleRate);
        var a1 = 1.0 / (1.0 + g * (g + K));
        var a2 = g * a1;
        return new(a1, a2, g * a2);
    }
}

/// <summary>Integrator pair of one state-variable section, returning band-pass and low-pass outputs.</summary>
struct SvfSection {
    const double DenormalThreshold = 1e-30;

    double ic1;
    double ic2;

    public void Reset() {
        ic1 = 0;
        ic2 = 0;
    }

    /// <summary>Returns the unnormalised band-pass <c>s / D</c> in <paramref name="band" /> and the low-pass <c>1 / D</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(in LinkwitzRileyCoefficients c, double v0, out double band) {
        var v3 = v0 - ic2;
        var v1 = c.A1 * ic1 + c.A2 * v3;
        var v2 = ic2 + c.A2 * ic1 + c.A3 * v3;
        ic1 = 2 * v1 - ic1;
        ic2 = 2 * v2 - ic2;
        band = v1;
        return v2;
    }

    public void FlushDenormals() {
        if (Math.Abs(ic1) < DenormalThreshold) {
            ic1 = 0;
        }

        if (Math.Abs(ic2) < DenormalThreshold) {
            ic2 = 0;
        }
    }
}

/// <summary>
///     One channel of a Linkwitz–Riley 4th-order band split. The first Butterworth section is shared by both bands
///     (its low-pass and high-pass outputs come from the same integrators), so a split costs three sections.
/// </summary>
public struct LinkwitzRileySplit {
    SvfSection first;
    SvfSection low;
    SvfSection high;

    public void Reset() {
        first.Reset();
        low.Reset();
        high.Reset();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(in LinkwitzRileyCoefficients c, double x, out double lowBand, out double highBand) {
        var lp = first.Tick(c, x, out var bp);
        var hp = x - LinkwitzRileyCoefficients.K * bp - lp;
        lowBand = low.Tick(c, lp, out _);
        var lp2 = high.Tick(c, hp, out var bp2);
        highBand = hp - LinkwitzRileyCoefficients.K * bp2 - lp2;
    }

    public void FlushDenormals() {
        first.FlushDenormals();
        low.FlushDenormals();
        high.FlushDenormals();
    }
}

/// <summary>
///     The allpass a <see cref="LinkwitzRileySplit" /> sums to. A band that skips a later split runs through this so all
///     bands stay phase-aligned and a multi-way crossover still sums flat.
/// </summary>
public struct LinkwitzRileyAllpass {
    SvfSection section;

    public void Reset() => section.Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(in LinkwitzRileyCoefficients c, double x) {
        section.Tick(c, x, out var bp);
        return x - 2 * LinkwitzRileyCoefficients.K * bp;
    }

    public void FlushDenormals() => section.FlushDenormals();
}
