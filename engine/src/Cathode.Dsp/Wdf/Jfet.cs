using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     Shichman-Hodges parameters of an N-channel junction FET (SPICE's JFET model without its resistances and
///     capacitances).
/// </summary>
/// <param name="Threshold">The pinch-off voltage V_TO (gate-source, negative), in volts.</param>
/// <param name="Beta">The transconductance parameter β, in A/V²: I_DSS = β V_TO².</param>
/// <param name="Lambda">Channel-length modulation λ, in 1/V (0 for none).</param>
/// <param name="GateSaturation">The saturation current of each gate junction, in amperes.</param>
/// <remarks>
///     A JFET type's pinch-off voltage and I_DSS spread several times over from one part to the next, so a model
///     that uses one fits these to the operating point its drawing gives and says so.
/// </remarks>
public readonly record struct JfetModel(
    double Threshold,
    double Beta,
    double Lambda = 0,
    double GateSaturation = 1e-14
);

/// <summary>
///     An N-channel JFET as a two-port <see cref="INonlinearDevice" />: port 0 is gate-source, port 1 drain-source,
///     both with the source at the minus node (the ports of a triode: declare them with
///     <see cref="WdfNetlist.Triode(int, int, int)" />, drain for plate, gate for grid, source for cathode).
/// </summary>
/// <remarks>
///     <para>
///         Drain current (Shichman and Hodges, 1968), with V_gst = V_gs − V_TO: none for V_gst ≤ 0;
///         β V_ds (2 V_gst − V_ds) (1 + λ V_ds) below pinch-off (V_ds &lt; V_gst); β V_gst² (1 + λ V_ds) above it. With
///         the drain below the source the two change roles (the device is symmetric). The current and its
///         derivatives are continuous across each boundary.
///     </para>
///     <para>
///         The gate's two junctions (to the source and to the drain) are Shockley diodes: they conduct when the gate
///         is driven above the channel, which is what clamps an overdriven stage's input. No capacitances: add them to
///         the netlist where they matter.
///     </para>
/// </remarks>
public struct Jfet(JfetModel model) : INonlinearDevice {
    readonly JfetModel model = model;
    readonly double critical = DeviceMath.CriticalVoltage(model.GateSaturation, DiodeModel.ThermalVoltage);

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
        const double Vt = DiodeModel.ThermalVoltage;
        var toSource = model.GateSaturation * (DeviceMath.LimitedExp(vgs / Vt, out var gs) - 1);
        var toDrain = model.GateSaturation * (DeviceMath.LimitedExp((vgs - vds) / Vt, out var gd) - 1);
        gs *= model.GateSaturation / Vt;
        gd *= model.GateSaturation / Vt;

        double drain, byGate, byDrain;
        if (vds >= 0) {
            drain = Channel(vgs - model.Threshold, vds, out byGate, out byDrain);
        } else {
            // The drain acts as the source: the channel's current for (V_gd, −V_ds), reversed.
            drain = -Channel(vgs - vds - model.Threshold, -vds, out var byControl, out var byChannel);
            byGate = -byControl;
            byDrain = byControl + byChannel;
        }

        currents[0] = toSource + toDrain;
        currents[1] = drain - toDrain;
        jacobian[0] = gs + gd;
        jacobian[1] = -gd;
        jacobian[stride] = byGate - gd;
        jacobian[stride + 1] = byDrain + gd;
    }

    /// <summary>
    ///     The channel current for an overdrive <paramref name="vgst" /> (V_gs − V_TO) and a drain-source voltage
    ///     <paramref name="vds" /> ≥ 0, with its derivatives by each.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly double Channel(double vgst, double vds, out double byGate, out double byDrain) {
        if (!(vgst > 0)) {
            byGate = 0;
            byDrain = 0;
            return 0;
        }

        var modulation = 1 + model.Lambda * vds;
        if (vds < vgst) {
            var ohmic = model.Beta * vds * (2 * vgst - vds);
            byGate = 2 * model.Beta * vds * modulation;
            byDrain = 2 * model.Beta * (vgst - vds) * modulation + model.Lambda * ohmic;
            return ohmic * modulation;
        }

        var saturated = model.Beta * vgst * vgst;
        byGate = 2 * model.Beta * vgst * modulation;
        byDrain = model.Lambda * saturated;
        return saturated * modulation;
    }

    /// <summary>
    ///     SPICE's junction limiting on both gate junctions; the drain-source step follows from the two.
    /// </summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        const double Vt = DiodeModel.ThermalVoltage;
        var toSource = DeviceMath.LimitJunction(previous[0], proposed[0], Vt, critical);
        var toDrain = DeviceMath.LimitJunction(previous[0] - previous[1], proposed[0] - proposed[1], Vt, critical);
        proposed[0] = toSource;
        proposed[1] = toSource - toDrain;
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}
