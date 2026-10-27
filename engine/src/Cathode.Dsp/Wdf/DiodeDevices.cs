using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     A diode (or <c>count</c> identical diodes in series) as a one-port <see cref="INonlinearDevice" />, anode at the
///     port's plus node: Shockley's i = Is (e^(v / n Vt) - 1), with an optional series resistance Rs.
/// </summary>
/// <remarks>
///     With Rs the junction voltage is v - Rs i, and the current has the closed form
///     i = (n Vt / Rs) ω(ln(Is Rs / n Vt) + (v + Is Rs) / n Vt) - Is with the Wright omega function ω, whose slope is
///     di/dv = 1 / (Rs + n Vt / (i + Is)). It grows linearly instead of exponentially at high current, so Newton needs
///     no step limiting. Without Rs, steps are limited like SPICE's pnjlim.
/// </remarks>
public struct Diode : INonlinearDevice {
    readonly double saturationCurrent;
    readonly double vt; // n Vt × count
    readonly double seriesResistance;
    readonly double critical;
    readonly double omegaBase; // ln(Is Rs / n Vt) + Is Rs / n Vt

    public readonly int Ports => 1;

    /// <param name="model">The junction.</param>
    /// <param name="count">Identical diodes in series.</param>
    /// <param name="seriesResistance">
    ///     Bulk resistance Rs in ohms (0 for none; a 1N4148 has about 0.6 Ω, an LED 5-20 Ω).
    /// </param>
    public Diode(DiodeModel model, int count = 1, double seriesResistance = 0) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        saturationCurrent = model.SaturationCurrent;
        vt = model.EmissionVoltage(count);
        this.seriesResistance = Math.Max(seriesResistance, 0);
        critical = DeviceMath.CriticalVoltage(saturationCurrent, vt);
        var x = saturationCurrent * this.seriesResistance / vt;
        omegaBase = this.seriesResistance > 0 ? Math.Log(x) + x : 0;
    }

    public readonly double Conductance(int port) => 1e-3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var v = voltages[0];
        if (seriesResistance > 0) {
            var i = vt / seriesResistance * WrightOmega.Fast(omegaBase + v / vt) - saturationCurrent;
            currents[0] = i;
            jacobian[0] = 1 / (seriesResistance + vt / Math.Max(i + saturationCurrent, 1e-300));
            return;
        }

        var e = DeviceMath.LimitedExp(v / vt, out var slope);
        currents[0] = saturationCurrent * (e - 1);
        jacobian[0] = saturationCurrent * slope / vt;
    }

    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        if (seriesResistance == 0) {
            proposed[0] = DeviceMath.LimitJunction(previous[0], proposed[0], vt, critical);
        }
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}

/// <summary>
///     Two antiparallel diode stacks as a one-port <see cref="INonlinearDevice" />, the device form of
///     <see cref="DiodePair" />: <c>forwardCount</c> diodes conduct for positive port voltage and
///     <c>reverseCount</c> for negative, i = Is_f (e^(v / n_f Vt) - 1) - Is_r (e^(-v / n_r Vt) - 1). A count of zero
///     leaves that direction open.
/// </summary>
public struct DiodeClipper : INonlinearDevice {
    readonly double forwardIs;
    readonly double forwardVt;
    readonly double forwardCritical;
    readonly double reverseIs;
    readonly double reverseVt;
    readonly double reverseCritical;

    public readonly int Ports => 1;

    public DiodeClipper(DiodeModel forward, int forwardCount, DiodeModel reverse, int reverseCount) {
        forwardIs = forwardCount > 0 ? forward.SaturationCurrent : 0;
        forwardVt = forwardCount > 0 ? forward.EmissionVoltage(forwardCount) : 1;
        reverseIs = reverseCount > 0 ? reverse.SaturationCurrent : 0;
        reverseVt = reverseCount > 0 ? reverse.EmissionVoltage(reverseCount) : 1;
        forwardCritical = forwardIs > 0 ? DeviceMath.CriticalVoltage(forwardIs, forwardVt) : double.PositiveInfinity;
        reverseCritical = reverseIs > 0 ? DeviceMath.CriticalVoltage(reverseIs, reverseVt) : double.PositiveInfinity;
    }

    public static DiodeClipper Symmetric(DiodeModel model, int count = 1) => new(model, count, model, count);

    public readonly double Conductance(int port) => 1e-3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var v = voltages[0];
        var forward = DeviceMath.LimitedExp(v / forwardVt, out var forwardSlope);
        var reverse = DeviceMath.LimitedExp(-v / reverseVt, out var reverseSlope);
        currents[0] = forwardIs * (forward - 1) - reverseIs * (reverse - 1);
        jacobian[0] = forwardIs * forwardSlope / forwardVt + reverseIs * reverseSlope / reverseVt;
    }

    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        if (proposed[0] >= 0) {
            proposed[0] = DeviceMath.LimitJunction(previous[0], proposed[0], forwardVt, forwardCritical);
        } else {
            proposed[0] = -DeviceMath.LimitJunction(-previous[0], -proposed[0], reverseVt, reverseCritical);
        }
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}
