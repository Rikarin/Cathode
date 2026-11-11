using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     Parameters of an N-channel junction FET in SPICE's level-1 form (Shichman and Hodges, 1968).
/// </summary>
/// <param name="SaturationCurrent">Drain current I_DSS at V_GS = 0 in saturation, in amperes.</param>
/// <param name="PinchOff">Gate-source cut-off voltage V_GS(off), in volts (negative).</param>
/// <param name="GateSaturationCurrent">Saturation current of each gate junction (SPICE's default, 10 fA).</param>
/// <remarks>
///     A JFET type's pinch-off voltage and I_DSS spread several times over from one part to the next, so a model
///     that uses one fits these to the operating point its drawing gives and says so.
/// </remarks>
public readonly record struct JfetModel(
    double SaturationCurrent,
    double PinchOff,
    double GateSaturationCurrent = 1e-14
) {
    /// <summary>Channel-length modulation λ, in 1/V (0 for none): the drain current grows by (1 + λ V_DS).</summary>
    public double Lambda { get; init; }

    /// <summary>
    ///     A JFET given as SPICE writes it: the pinch-off voltage V_TO (gate-source, negative, in volts) and the
    ///     transconductance parameter β in A/V², so I_DSS = β V_TO².
    /// </summary>
    public static JfetModel FromBeta(double pinchOff, double beta) => new(beta * pinchOff * pinchOff, pinchOff);

    /// <summary>
    ///     2N5484 (and the TIS58 it replaces), N-channel: I_DSS 4.3 mA, V_GS(off) −1.6 V, so 5.4 mS at V_GS = 0. The
    ///     datasheet (Motorola, Vishay) gives only ranges, 1 to 5 mA, −0.3 to −3 V and 3 to 6 mS; this sample is fitted
    ///     inside them to the Randall RG100ES drawing (PCB 2146B), whose stages on 2.2k source resistors sit at about
    ///     1 V on the source (0.48 mA here) and gain 6.7 from the input jack unbypassed into the 50k gain pot (2.4 mS)
    ///     and 52 bypassed (1.6 mS): 1.8 mS here, between the drawing's two samples.
    /// </summary>
    public static JfetModel J2N5484 { get; } = new(4.3e-3, -1.6);

    /// <summary>β of I_D = β (V_GS − V_GS(off))², I_DSS / V_GS(off)².</summary>
    public double Beta => SaturationCurrent / (PinchOff * PinchOff);
}

/// <summary>
///     An N-channel JFET as a two-port <see cref="INonlinearDevice" />: port 0 is gate-source, port 1 drain-source,
///     the ports of a triode with the drain for the plate (declare them with
///     <see cref="WdfNetlist.Triode(int, int, int)" />). The source current is their sum.
/// </summary>
/// <remarks>
///     Channel current (Shichman-Hodges), with V_ov = V_GS − V_GS(off): β V_DS (2 V_ov − V_DS) below pinch-off
///     (V_DS &lt; V_ov) and β V_ov² above it. For a negative V_DS the drain and source change places. Cut-off is not
///     a corner: V_ov is s ln(1 + exp((V_GS − V_GS(off)) / s)) with s = 2 Vt, so the current dies away exponentially
///     below V_GS(off), as a real channel's does, and Newton always has a slope to follow (with the hard corner an
///     overdriven stage crossing cut-off left samples unconverged).
///     The two gate junctions are Shockley diodes (n = 1), gate to source and gate to drain: a gate driven above the
///     source conducts, which is what clamps an overdriven stage's input and charges its coupling cap. Both channel
///     currents are multiplied by (1 + λ V_DS) where the model has a channel-length modulation
///     (<see cref="JfetModel.Lambda" />). No capacitances: put a Miller cap in the netlist where the drawing has one.
/// </remarks>
public struct Jfet(JfetModel model) : INonlinearDevice {
    const double Vt = DiodeModel.ThermalVoltage;
    const double CutOff = 2 * Vt; // the soft cut-off's width

    // Ohms: what a gate is typically driven from. The junctions' step limit starts where a junction's conductance
    // passes this source's (about 0.43 V), not where pnjlim's 1 Ω puts it (0.73 V, 18 mA): on a 33k drain load a
    // follower's gate stepping from 0.44 V to 0.61 V is already volts of error, and Newton's line search then
    // crawls.
    const double GateSource = 100e3;

    readonly double beta = model.Beta;
    readonly double lambda = model.Lambda;
    readonly double pinchOff = model.PinchOff;
    readonly double gateCurrent = model.GateSaturationCurrent;
    readonly double critical = DeviceMath.CriticalVoltage(model.GateSaturationCurrent * GateSource, Vt);

    public readonly int Ports => 2;

    public readonly double Conductance(int port) => 1e-4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var vgs = voltages[0];
        var vds = voltages[1];
        var vgd = vgs - vds;

        // The gate junctions.
        var igs = gateCurrent * (DeviceMath.LimitedExp(vgs / Vt, out var ggs) - 1);
        var igd = gateCurrent * (DeviceMath.LimitedExp(vgd / Vt, out var ggd) - 1);
        ggs *= gateCurrent / Vt;
        ggd *= gateCurrent / Vt;

        // The channel, forward (drain above source) or with the two swapped.
        double id, gm, gds;
        if (vds >= 0) {
            id = Channel(vgs - pinchOff, vds, out gm, out gds);
        } else {
            id = -Channel(vgd - pinchOff, -vds, out var reverseGm, out var reverseGds);
            gm = -reverseGm;
            gds = reverseGm + reverseGds;
        }

        currents[0] = igs + igd;
        currents[1] = id - igd;
        jacobian[0] = ggs + ggd;
        jacobian[1] = -ggd;
        jacobian[stride] = gm - ggd;
        jacobian[stride + 1] = gds + ggd;
    }

    /// <summary>
    ///     The channel current for <paramref name="overdrive" /> (V_GS − V_GS(off), before the soft cut-off) and a
    ///     drain-source voltage <paramref name="vds" /> ≥ 0, with its slopes against each.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly double Channel(double overdrive, double vds, out double gm, out double gds) {
        var soft = CutOff * DeviceMath.Softplus(overdrive / CutOff, out var logistic);
        var modulation = 1 + lambda * vds;
        if (vds < soft) {
            var ohmic = beta * vds * (2 * soft - vds);
            gm = 2 * beta * vds * logistic * modulation;
            gds = 2 * beta * (soft - vds) * modulation + lambda * ohmic;
            return ohmic * modulation;
        }

        var saturated = beta * soft * soft;
        gm = 2 * beta * soft * logistic * modulation;
        gds = lambda * saturated;
        return saturated * modulation;
    }

    /// <summary>Both gate junctions step like diodes (<see cref="DeviceMath.LimitJunction" />).</summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        var vgs = DeviceMath.LimitJunction(previous[0], proposed[0], Vt, critical);
        var free = vgs - proposed[1];
        var vgd = DeviceMath.LimitJunction(previous[0] - previous[1], free, Vt, critical);
        proposed[0] = vgs;
        // Exact by design: an unlimited port keeps its proposal bit for bit, which is how the solver knows.
#pragma warning disable SK2003
        if (vgd != free) {
            proposed[1] = vgs - vgd;
        }
#pragma warning restore SK2003
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}
