using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Wdf;

/// <summary>
///     Koren's triode parameters ("Improved vacuum tube models for SPICE simulations", Glass Audio 8(5), 1996) and
///     Dempwolf and Zölzer's grid-current law ("A physically-motivated triode model for circuit simulations", DAFx-11).
/// </summary>
/// <param name="Mu">Amplification factor μ.</param>
/// <param name="Exponent">Exponent of the plate current (about 3/2 for Child's law).</param>
/// <param name="Kg1">Plate-current scale.</param>
/// <param name="Kp">Shapes the cut-off knee.</param>
/// <param name="Kvb">Shapes the low-plate-voltage region, in V².</param>
/// <param name="GridConductance">Grid-current scale G_g, in A / V^ξ.</param>
/// <param name="GridSharpness">Grid-current knee C_g, in 1 / V.</param>
/// <param name="GridExponent">Grid-current exponent ξ.</param>
/// <remarks>
///     The presets are Koren's published fits. The grid law uses Dempwolf's 12AX7 fit for all of them (without his
///     constant offset current, so that no current flows at deep cut-off): about 10 µA at 0 V grid-cathode, rising
///     steeply beyond, which is what makes a stage block and bias-shift when driven hard.
/// </remarks>
public readonly record struct TriodeModel(
    double Mu,
    double Exponent,
    double Kg1,
    double Kp,
    double Kvb,
    double GridConductance = 6.06e-4,
    double GridSharpness = 13.9,
    double GridExponent = 1.354
) {
    /// <summary>
    ///     <paramref name="count" /> of these triodes in parallel (plates, grids and cathodes tied) as one: plate and grid
    ///     currents scale with the count at the same voltages, which Koren's and Dempwolf's laws take exactly through
    ///     K_g1 and G_g.
    /// </summary>
    public TriodeModel Paralleled(int count) =>
        this with { Kg1 = Kg1 / count, GridConductance = GridConductance * count };

    /// <summary>ECC83 / 12AX7: μ 100, the high-gain preamp triode (about 1.0 mA at 250 V, -2 V).</summary>
    public static TriodeModel Ecc83 { get; } = new(100, 1.4, 1_060, 600, 300);

    /// <summary>ECC81 / 12AT7: μ 60, higher current (reverb drivers, phase inverters).</summary>
    public static TriodeModel Ecc81 { get; } = new(60, 1.35, 460, 300, 300);

    /// <summary>ECC82 / 12AU7: μ 21.5, low gain and low plate resistance (cathode followers, phase inverters).</summary>
    public static TriodeModel Ecc82 { get; } = new(21.5, 1.3, 1_180, 84, 300);

    /// <summary>
    ///     12AY7 / 6072: μ 44, the tweed Fenders' first stage. Fitted here (K_g1, K_p, with Koren's 12AX7 exponent and
    ///     K_vb) to the datasheet point 250 V, -4 V: 3.0 mA, 1.75 mS (r_p 25k).
    /// </summary>
    public static TriodeModel TwelveAY7 { get; } = new(44, 1.4, 1_684, 214, 300);

    /// <summary>
    ///     5751: μ 70, a lower-gain 12AX7 (a common "smoother" substitute). Fitted like <see cref="TwelveAY7" /> to
    ///     250 V, -3 V: 1.0 mA, 1.2 mS (r_p 58k).
    /// </summary>
    public static TriodeModel FiveSevenFiveOne { get; } = new(70, 1.4, 1_508, 370.5, 300);

    /// <summary>
    ///     6EU7: μ 100, a low-hum twin triode with the 12AX7's ratings on a different base (Gibson's GA-17RVT Scout). Fitted
    ///     with μ held at the datasheet's 100 (RCA 6EU7, data 1 and 2, 8-60) by least squares on log errors to the
    ///     tabulated characteristics (100 V, -1 V: 0.5 mA, 1.25 mS, 80k; 250 V, -2 V: 1.2 mA, 1.6 mS, 62.5k) and 17 points
    ///     read off the average plate characteristics (92CM-10470: 0 to -3 V at 0.5, 1 and 2 mA). The fit runs to
    ///     Ex 1.0 (refitted with Koren's 1.4, the chart is 32 % off RMS): the chart points within 11 % RMS, 32 % at worst;
    ///     the tabulated points 0.475 mA, 1.18 mS, 77k and 1.42 mA, 1.48 mS, 60k. The Scout's stages as drawn (470k from
    ///     257 V, 4.7k) then sit at 119 V and 1.38 V against the drawing's 120 V and 1.2 V (Koren's 12AX7 gives 136 V).
    /// </summary>
    public static TriodeModel SixEU7 { get; } = new(100, 1.0, 988, 503, 4_240);
}

/// <summary>
///     Koren's pentode / beam tetrode parameters (same paper as <see cref="TriodeModel" />), with Dempwolf's grid
///     current on the control grid.
/// </summary>
/// <param name="Mu">Screen-grid amplification factor μ.</param>
/// <param name="Exponent">Exponent of plate and screen currents.</param>
/// <param name="Kg1">Plate-current scale.</param>
/// <param name="Kg2">Screen-current scale.</param>
/// <param name="Kp">Shapes the cut-off knee.</param>
/// <param name="Kvb">Plate-voltage knee in volts (the atan(V_pk / Kvb) factor).</param>
/// <param name="GridConductance">Control-grid current scale, as <see cref="TriodeModel.GridConductance" />.</param>
/// <param name="GridSharpness">Control-grid current knee.</param>
/// <param name="GridExponent">Control-grid current exponent.</param>
/// <remarks>
///     EL34 and 6L6GC are Koren's fits. EL84 is fitted here with Koren's equations to the datasheet's class-A point
///     (250 V plate and screen, -7.3 V grid: 48 mA plate, 5.5 mA screen) with μ 19, Ex 1.35, Kp 200, Kvb 20. In Koren's
///     model the screen current does not depend on the plate voltage, so it misses the screen-current rise when the
///     plate swings below the screen.
/// </remarks>
public readonly record struct PentodeModel(
    double Mu,
    double Exponent,
    double Kg1,
    double Kg2,
    double Kp,
    double Kvb,
    double GridConductance = 6.06e-4,
    double GridSharpness = 13.9,
    double GridExponent = 1.354
) {
    /// <summary>EL34 / 6CA7 power pentode (Marshall, Hiwatt).</summary>
    public static PentodeModel EL34 { get; } = new(11, 1.35, 650, 4_200, 60, 24);

    /// <summary>6L6GC beam tetrode (Fender).</summary>
    public static PentodeModel SixL6GC { get; } = new(8.7, 1.35, 1_460, 4_500, 48, 12);

    /// <summary>EL84 / 6BQ5 power pentode (Vox AC30, small combos).</summary>
    public static PentodeModel EL84 { get; } = new(19, 1.35, 677.4, 1_977, 200, 20);

    /// <summary>
    ///     6V6GT beam tetrode (Deluxe, Princeton, tweeds). Fitted like <see cref="EL84" />: μ 9.8, Ex 1.35, K_vb 12, and
    ///     K_g1, K_p, K_g2 from 250 V plate and screen, -12.5 V: 45 mA, 4.1 mS, 4.5 mA screen.
    /// </summary>
    public static PentodeModel SixV6GT { get; } = new(9.8, 1.35, 2_276, 7_097, 45, 12);

    /// <summary>
    ///     KT66 beam tetrode (JTM45). Fitted with μ 8, K_vb 16 to 250 V, -15 V: 85 mA, 7 mS, 6 mA screen (approximate
    ///     GEC figures).
    /// </summary>
    public static PentodeModel KT66 { get; } = new(8, 1.35, 1_532, 7_186, 75.7, 16);

    /// <summary>KT88 beam tetrode, Koren's fit (Engl, Bogner, some Marshalls).</summary>
    public static PentodeModel KT88 { get; } = new(8.8, 1.35, 730, 4_200, 32, 16);

    /// <summary>6550 beam tetrode, Koren's fit (US-market Marshalls, Ampeg SVT).</summary>
    public static PentodeModel SixtyFiveFifty { get; } = new(7.9, 1.35, 890, 4_800, 60, 24);

    /// <summary>
    ///     EF86 / 6267 small-signal pentode, as a preamp stage (Vox AC15, Matchless). Fitted with Ex 1.2, K_vb 5 and a
    ///     sharp cut-off (K_p 1000) to two points: the datasheet's 250 V plate, 140 V screen, -2 V (3.0 mA, 0.6 mA screen)
    ///     and the Vox AC15's resistance-coupled stage as built (220k plate, 1M screen resistor, 2.2k cathode from 291 V:
    ///     0.91 mA and 0.19 mA at -2.4 V, the screen 102.6 V and the plate 88.6 V above the cathode; AmpBooks, with Vox's
    ///     measured 90 V plate), which sets μ to 30. Its slope there is 1.1 mS (gain about 165 into 220k || 500k); at the
    ///     datasheet point 1.35 mS against the datasheet's 2.0, which no Koren fit through both currents reaches. Use the
    ///     exact <see cref="Pentode" /> device: its screen argument is below the power-tube tables' range.
    /// </summary>
    public static PentodeModel EF86 { get; } = new(30, 1.2, 3_367, 5_427, 1_000, 5);

    /// <summary>
    ///     6AQ5 / 6005 beam pentode, a 6V6GT on a seven-pin base (Gibson Scout, small combos). Fitted with Ex 1.35 by least
    ///     squares on log errors to GE's ET-T885 (6-55): the class-A points 180 V plate and screen, -8.5 V (29 mA, 3.0 mA
    ///     screen, 3.7 mS, r_p 58k) and 250 V, -12.5 V (45 mA, 4.5 mA, 4.1 mS, 52k), and the triode connection at 250 V,
    ///     -12.5 V (49.5 mA). It gives 29.5 / 3.04 mA, 3.65 mS, 48k and 44.6 / 4.44 mA, 4.14 mS, 62k, and 49.1 mA strapped;
    ///     μ comes out at 9.5, the datasheet's triode μ. K_vb (33 V) is free here, so that r_p comes out near the datasheet's
    ///     instead of three times it, as K_vb 12 gives.
    /// </summary>
    public static PentodeModel SixAQ5 { get; } = new(9.535, 1.35, 2_257, 7_733, 56.43, 32.88);
}

/// <summary>
///     A triode as a two-port <see cref="INonlinearDevice" />: port 0 is grid-cathode, port 1 plate-cathode (declare them
///     with <see cref="WdfNetlist.Triode(int, int, int)" />). The cathode current is their sum.
/// </summary>
/// <remarks>
///     Plate current (Koren): E₁ = (V_pk / K_p) ln(1 + exp(K_p (1/μ + V_gk / √(K_vb + V_pk²)))) and
///     I_p = 2 E₁^Ex / K_g1 for E₁ &gt; 0, else 0 (Koren's (|E₁|^Ex + sgn(E₁)|E₁|^Ex) / K_g1). Grid current (Dempwolf):
///     I_g = G_g (ln(1 + exp(C_g V_gk)) / C_g)^ξ. Koren's model is the choice here over Dempwolf's full model because
///     its parameters exist for most tubes; the grid law is added because Koren's has none, and grid conduction is what
///     makes a driven stage compress, block and shift its bias. No interelectrode capacitances: add the Miller
///     capacitance to the netlist where the stage needs it.
/// </remarks>
public struct Triode(TriodeModel model) : INonlinearDevice {
    readonly TriodeModel model = model;

    public readonly int Ports => 2;

    public readonly double Conductance(int port) => 1e-5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var vgk = voltages[0];
        var vpk = voltages[1];
        currents[0] =
            Tube.GridCurrent(vgk, model.GridConductance, model.GridSharpness, model.GridExponent, out jacobian[0]);

        var root = Math.Sqrt(model.Kvb + vpk * vpk);
        var softplus = DeviceMath.Softplus(model.Kp * (1 / model.Mu + vgk / root), out var logistic);
        var e1 = vpk * softplus / model.Kp;
        if (!(e1 > 0)) {
            currents[1] = 0;
            return;
        }

        var ip = 2 * Math.Pow(e1, model.Exponent) / model.Kg1;
        var slope = model.Exponent * ip / e1; // dI_p / dE₁
        currents[1] = ip;
        jacobian[stride] = slope * vpk * logistic / root;
        jacobian[stride + 1] = slope * (softplus / model.Kp - vpk * vpk * logistic * vgk / (root * root * root));
    }

    /// <summary>The plate voltage may at most halve per Newton step; see <see cref="Tube.LimitPlate" />.</summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) =>
        proposed[1] = Tube.LimitPlate(previous[1], proposed[1]);

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}

/// <summary>
///     A pentode or beam tetrode as a three-port <see cref="INonlinearDevice" />: control grid-cathode, screen-cathode,
///     plate-cathode (declare them with <see cref="WdfNetlist.Pentode(int, int, int, int)" />).
/// </summary>
/// <remarks>
///     Koren: E₁ = (V_g2k / K_p) ln(1 + exp(K_p (1/μ + V_g1k / V_g2k))), I_p = 2 E₁^Ex / K_g1 × atan(V_pk / K_vb) and
///     I_g2 = (V_g2k / μ + V_g1k)^Ex / K_g2, each zero where its base is not positive (the plate term is also zero for a
///     negative plate voltage). The control grid draws Dempwolf's grid current, as in <see cref="Triode" />.
/// </remarks>
public struct Pentode(PentodeModel model) : INonlinearDevice {
    const double MinimumScreen = 1e-3; // V: below it the screen is treated as off

    readonly PentodeModel model = model;

    public readonly int Ports => 3;

    public readonly double Conductance(int port) => 1e-4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Evaluate(
        ReadOnlySpan<double> voltages,
        Span<double> currents,
        Span<double> jacobian,
        int stride
    ) {
        var vg1 = voltages[0];
        var vg2 = voltages[1];
        var vp = voltages[2];
        currents[0] =
            Tube.GridCurrent(vg1, model.GridConductance, model.GridSharpness, model.GridExponent, out jacobian[0]);
        currents[1] = 0;
        currents[2] = 0;

        var screen = vg2 / model.Mu + vg1;
        if (screen > 0) {
            var ig2 = Math.Pow(screen, model.Exponent) / model.Kg2;
            var slope = model.Exponent * ig2 / screen;
            currents[1] = ig2;
            jacobian[stride] = slope;
            jacobian[stride + 1] = slope / model.Mu;
        }

        if (!(vg2 > MinimumScreen)) {
            return;
        }

        var softplus = DeviceMath.Softplus(model.Kp * (1 / model.Mu + vg1 / vg2), out var logistic);
        var e1 = vg2 * softplus / model.Kp;
        if (!(e1 > 0) || !(vp > 0)) {
            return;
        }

        var power = 2 * Math.Pow(e1, model.Exponent) / model.Kg1;
        var slopeE1 = model.Exponent * power / e1;
        var ratio = vp / model.Kvb;
        var knee = Math.Atan(ratio);
        currents[2] = power * knee;
        jacobian[2 * stride] = slopeE1 * logistic * knee; // dE₁/dV_g1k = logistic
        jacobian[2 * stride + 1] = slopeE1 * (softplus / model.Kp - logistic * vg1 / vg2) * knee;
        jacobian[2 * stride + 2] = power / (model.Kvb * (1 + ratio * ratio));
    }

    /// <summary>Screen and plate may at most halve per Newton step; see <see cref="Tube.LimitPlate" />.</summary>
    public readonly void Limit(ReadOnlySpan<double> previous, Span<double> proposed) {
        proposed[1] = Tube.LimitPlate(previous[1], proposed[1]);
        proposed[2] = Tube.LimitPlate(previous[2], proposed[2]);
    }

    public readonly void Commit(ReadOnlySpan<double> voltages) { }

    public readonly void Reset() { }
}

/// <summary>Shared tube equations.</summary>
static class Tube {
    /// <summary>
    ///     Newton step limiting for a plate or screen voltage: on the way down it may at most halve per step. Below
    ///     zero volts the current is zero with zero slope, so from a hard-conducting plate (a grid driven positive) plain
    ///     Newton jumps far below zero, finds a flat characteristic, jumps back up and cycles; halving approaches a
    ///     bottomed-out plate geometrically instead, and quadratic convergence takes over near it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LimitPlate(double previous, double proposed) =>
        previous > 0 && proposed < 0.5 * previous ? 0.5 * previous : proposed;

    /// <summary>Dempwolf's grid current G_g (ln(1 + e^(C_g v)) / C_g)^ξ, and its slope.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GridCurrent(
        double vgk,
        double conductance,
        double sharpness,
        double exponent,
        out double slope
    ) {
        if (sharpness * vgk < -20) {
            slope = 0; // below about 1e-16 A: skip the exponential, logarithm and power
            return 0;
        }

        var softplus = DeviceMath.Softplus(sharpness * vgk, out var logistic) / sharpness;
        if (!(softplus > 0)) {
            slope = 0;
            return 0;
        }

        var current = conductance * Math.Pow(softplus, exponent);
        slope = exponent * current / softplus * logistic;
        return current;
    }
}
