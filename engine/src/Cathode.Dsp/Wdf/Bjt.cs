using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>Whether a bipolar transistor is NPN or PNP.</summary>
public enum BjtPolarity {
    Npn,
    Pnp
}

/// <summary>
///     Ebers-Moll parameters of a bipolar transistor (the transport form, as SPICE's Gummel-Poon reduces to without
///     high-injection, Early and resistance terms).
/// </summary>
/// <param name="Polarity">NPN or PNP.</param>
/// <param name="SaturationCurrent">Transport saturation current Is, in amperes.</param>
/// <param name="ForwardBeta">Forward current gain βF (hFE).</param>
/// <param name="ReverseBeta">Reverse current gain βR.</param>
/// <param name="Ideality">Emission coefficient n of both junctions.</param>
/// <remarks>
///     The presets are typical values from published SPICE models and datasheets (hFE in the middle of the spread);
///     germanium parts vary by a factor of several from one sample to the next, and their leakage with temperature.
/// </remarks>
public readonly record struct BjtModel(
    BjtPolarity Polarity,
    double SaturationCurrent,
    double ForwardBeta,
    double ReverseBeta,
    double Ideality = 1
) {
    /// <summary>2N3904, small-signal silicon NPN (Is 6.7 fA, βF 416, βR 0.74; the common SPICE model).</summary>
    public static BjtModel Q2N3904 { get; } = new(BjtPolarity.Npn, 6.734e-15, 416.4, 0.7371);

    /// <summary>2N5088, high-gain silicon NPN of the Big Muff (Is 5.9 fA, βF 1100, βR 1.8).</summary>
    public static BjtModel Q2N5088 { get; } = new(BjtPolarity.Npn, 5.911e-15, 1_100, 1.8);

    /// <summary>BC108, silicon NPN of the silicon Fuzz Face (Is 7 fA, βF 380, βR 2.4).</summary>
    public static BjtModel BC108 { get; } = new(BjtPolarity.Npn, 7.049e-15, 381.7, 2.359);

    /// <summary>
    ///     AC128, germanium PNP of the Fuzz Face and Tone Bender (Is 2 µA, βF 90, βR 5, n 1.1: about 0.18 V base-emitter
    ///     at 1 mA, with the leakage of a germanium junction).
    /// </summary>
    public static BjtModel AC128 { get; } = new(BjtPolarity.Pnp, 2e-6, 90, 5, 1.1);

    /// <summary>NKT275, the germanium PNP of the early Fuzz Face (Is 3 µA, βF 75, βR 4, n 1.1).</summary>
    public static BjtModel NKT275 { get; } = new(BjtPolarity.Pnp, 3e-6, 75, 4, 1.1);

    /// <summary>AC127, germanium NPN (Is 2 µA, βF 100, βR 5, n 1.1).</summary>
    public static BjtModel AC127 { get; } = new(BjtPolarity.Npn, 2e-6, 100, 5, 1.1);

    /// <summary>n Vt.</summary>
    public double EmissionVoltage => Ideality * DiodeModel.ThermalVoltage;
}

/// <summary>
///     An Ebers-Moll bipolar transistor as a two-port <see cref="INonlinearDevice" />: port 0 is base-emitter, port 1
///     base-collector, both with the base at the plus node (declare them with
///     <see cref="WdfNetlist.Transistor(int, int, int)" />). The port currents are then i₀ = I_E (in at the base, out at
///     the emitter) and i₁ = -I_C, which add up to the base current.
/// </summary>
/// <remarks>
///     <para>
///         For an NPN, with e_f = exp(v_BE / n Vt) and e_r = exp(v_BC / n Vt) (Ebers and Moll, 1954, transport form):
///         I_C = Is (e_f - e_r) - Is / βR (e_r - 1) and I_B = Is / βF (e_f - 1) + Is / βR (e_r - 1), so
///         i₀ = I_C + I_B = Is (e_f - e_r) + Is / βF (e_f - 1) and i₁ = -I_C. A PNP is the NPN with every port voltage
///         and current negated. No Early effect, no ohmic base/collector/emitter resistances and no junction
///         capacitances: put real resistors and capacitors in the netlist where they matter (a germanium fuzz's
///         collector-base Miller capacitance, say).
///     </para>
/// </remarks>
public struct Bjt(BjtModel model) : INonlinearDevice {
    readonly double sign = model.Polarity == BjtPolarity.Npn ? 1 : -1;
    readonly double saturationCurrent = model.SaturationCurrent;
    readonly double forwardLeak = model.SaturationCurrent / model.ForwardBeta;
    readonly double reverseLeak = model.SaturationCurrent / model.ReverseBeta;
    readonly double vt = model.EmissionVoltage;
    readonly double critical = DeviceMath.CriticalVoltage(model.SaturationCurrent, model.EmissionVoltage);

    public readonly int Ports => 2;

    public readonly double Conductance(int port) => 1e-4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride) {
        // In NPN orientation: a PNP's currents are the negated NPN currents at negated voltages, its Jacobian unchanged.
        var ef = DeviceMath.LimitedExp(sign * voltages[0] / vt, out var df);
        var er = DeviceMath.LimitedExp(sign * voltages[1] / vt, out var dr);
        var transport = saturationCurrent * (ef - er);
        currents[0] = sign * (transport + forwardLeak * (ef - 1));
        currents[1] = sign * (reverseLeak * (er - 1) - transport);
        df /= vt;
        dr /= vt;
        jacobian[0] = (saturationCurrent + forwardLeak) * df;
        jacobian[1] = -saturationCurrent * dr;
        jacobian[stride] = -saturationCurrent * df;
        jacobian[stride + 1] = (saturationCurrent + reverseLeak) * dr;
    }

    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        proposed[0] = sign * DeviceMath.LimitJunction(sign * previous[0], sign * proposed[0], vt, critical);
        proposed[1] = sign * DeviceMath.LimitJunction(sign * previous[1], sign * proposed[1], vt, critical);
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}
