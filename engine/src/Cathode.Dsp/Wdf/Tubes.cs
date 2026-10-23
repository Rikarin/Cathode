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
    /// <summary>ECC83 / 12AX7: μ 100, the high-gain preamp triode (about 1.0 mA at 250 V, -2 V).</summary>
    public static TriodeModel Ecc83 { get; } = new(100, 1.4, 1_060, 600, 300);

    /// <summary>ECC81 / 12AT7: μ 60, higher current (reverb drivers, phase inverters).</summary>
    public static TriodeModel Ecc81 { get; } = new(60, 1.35, 460, 300, 300);

    /// <summary>ECC82 / 12AU7: μ 21.5, low gain and low plate resistance (cathode followers, phase inverters).</summary>
    public static TriodeModel Ecc82 { get; } = new(21.5, 1.3, 1_180, 84, 300);
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
    public readonly void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride) {
        var vgk = voltages[0];
        var vpk = voltages[1];
        currents[0] = Tube.GridCurrent(vgk, model.GridConductance, model.GridSharpness, model.GridExponent, out jacobian[0]);

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
    public readonly void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride) {
        var vg1 = voltages[0];
        var vg2 = voltages[1];
        var vp = voltages[2];
        currents[0] = Tube.GridCurrent(vg1, model.GridConductance, model.GridSharpness, model.GridExponent, out jacobian[0]);
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
    public static double GridCurrent(double vgk, double conductance, double sharpness, double exponent, out double slope) {
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
