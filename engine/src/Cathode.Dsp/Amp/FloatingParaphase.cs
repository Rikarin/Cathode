using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A floating (self-balancing) paraphase inverter (the Ampeg B-15N): two triodes on a shared, bypassed cathode
///     resistor, the first driven, the second's grid fed through a coupling cap from the junction of a divider between
///     the two plates, with its grid leak to ground; the first has an unbypassed resistor of its own above the shared
///     one, where the global feedback enters.
/// </summary>
/// <param name="Tube">Both triodes.</param>
/// <param name="Supply">The plates' supply.</param>
/// <param name="PlateResistor">Each plate's resistor.</param>
/// <param name="CathodeA">The driven triode's own cathode resistor, unbypassed.</param>
/// <param name="SharedCathode">The shared cathode resistor, bypassed (an AC ground at audio frequencies).</param>
/// <param name="DividerA">From the driven triode's plate to the junction.</param>
/// <param name="DividerB">From the junction to the second triode's plate.</param>
/// <param name="GridLeak">The second triode's grid leak, to ground.</param>
public sealed record FloatingParaphaseDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeA,
    double SharedCathode,
    double DividerA,
    double DividerB,
    double GridLeak
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     A <see cref="FloatingParaphaseDesign" />'s table: both plates' AC voltages as functions of the driven grid's AC
///     voltage, from the exact solution of both triodes with the divider, the shared cathode held at its rest voltage
///     (its bypass cap's corner is a few hertz).
/// </summary>
/// <remarks>
///     The second triode amplifies the difference the divider sees: a little of plate A's swing that plate B has not
///     cancelled. So it balances itself, to within its own gain, and as plate A clips, plate B follows it into
///     clipping late, which is the paraphase's asymmetric breakup.
/// </remarks>
public sealed class FloatingParaphase : PhaseInverter {
    public FloatingParaphaseDesign Design { get; }

    /// <summary>Each triode's plate current at rest.</summary>
    public (double A, double B) RestCurrents { get; }

    internal FloatingParaphase(FloatingParaphaseDesign design) : base(30) {
        Design = design;
        var triode = new Triode(design.Tube);

        // The rest point: bisection on the shared cathode, whose voltage the two currents set.
        var (low, high) = (0.0, design.Supply / 4);
        var (ia, ib, pa, pb) = (0.0, 0.0, 0.0, 0.0);
        for (var n = 0; n < 60; n++) {
            var cathode = 0.5 * (low + high);
            (ia, ib, pa, pb) = Solve(triode, design, 0, cathode, double.NaN, 0, 0);
            if ((ia + ib) * design.SharedCathode > cathode) {
                low = cathode;
            } else {
                high = cathode;
            }
        }

        var shared = 0.5 * (low + high);
        (ia, ib, pa, pb) = Solve(triode, design, 0, shared, double.NaN, 0, 0);
        var junction = (pa * design.DividerB + pb * design.DividerA) / (design.DividerA + design.DividerB);
        RestCurrents = (ia, ib);
        RestCathode = shared;
        RestGrid = 0;
        RestPlateA = pa;
        RestPlateB = pb;

        // The output impedances, each plate's own: a draw from plate A also moves plate B a long way (by -36k per ampere
        // on the B-15N, the divider feeding the second grid), but with that cross term the grid networks' piecewise
        // solve under grid current and the feedback loop's Newton cycled without converging at hard drive. Left out,
        // the loading of plate B by plate A's grid current is missing; the static, unloaded transfer is exact.
        const double Probe = 1e-6;
        var (_, _, aa, _) = Solve(triode, design, 0, shared, junction, Probe, 0);
        var (_, _, _, bb) = Solve(triode, design, 0, shared, junction, 0, Probe);
        OutputImpedance = ((pa - aa) / Probe, 0, 0, (pb - bb) / Probe);

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var (ca, _, va, vb) = Solve(triode, design, vd, shared, junction, 0, 0);
            a[n] = va - pa;
            b[n] = vb - pb;
            gridVoltages[n] = vd - shared - ca * design.CathodeA;
            leakReturns[n] = 0;
            cathodeResistances[n] = design.CathodeA;
        }

        Differentiate();
    }

    /// <summary>
    ///     Both triodes for the driven grid at <paramref name="grid" /> volts with the shared cathode at
    ///     <paramref name="shared" />, and <paramref name="drawA" />, <paramref name="drawB" /> amperes drawn from the
    ///     plates: the currents and plate voltages, by Newton's method on the two currents. With a NaN
    ///     <paramref name="junction" /> it is the DC solution (the second grid at rest, the divider unloaded); otherwise
    ///     the divider's junction rests at <paramref name="junction" /> and its AC part drives the second grid, loaded by
    ///     the grid leak.
    /// </summary>
    static (double A, double B, double PlateA, double PlateB) Solve(
        Triode triode,
        FloatingParaphaseDesign design,
        double grid,
        double shared,
        double junction,
        double drawA,
        double drawB
    ) {
        var dc = double.IsNaN(junction);
        var (ia, ib) = (0.5e-3, 0.5e-3);
        Span<double> residual = stackalloc double[2];
        Span<double> shifted = stackalloc double[2];

        (double, double, double, double) Evaluate(double ia, double ib, Span<double> r) {
            var (pa, pb, gridB) = Plates(design, ia + drawA, ib + drawB, dc, junction);
            var ka = shared + ia * design.CathodeA;
            r[0] = OperatingPoint.PlateCurrent(triode, grid - ka, pa - ka) - ia;
            r[1] = OperatingPoint.PlateCurrent(triode, gridB - shared, pb - shared) - ib;
            return (ia, ib, pa, pb);
        }

        var result = Evaluate(ia, ib, residual);
        for (var iteration = 0; iteration < 100; iteration++) {
            const double H = 1e-9;
            var (r0, r1) = (residual[0], residual[1]);
            Evaluate(ia + H, ib, shifted);
            var (j00, j10) = ((shifted[0] - r0) / H, (shifted[1] - r1) / H);
            Evaluate(ia, ib + H, shifted);
            var (j01, j11) = ((shifted[0] - r0) / H, (shifted[1] - r1) / H);
            var det = j00 * j11 - j01 * j10;
            if (det == 0) {
                break;
            }

            var da = (r1 * j01 - r0 * j11) / det;
            var db = (r0 * j10 - r1 * j00) / det;
            // Damped: no current below zero, no step beyond a milliampere.
            var scale = Math.Min(1, 1e-3 / Math.Max(Math.Abs(da), Math.Abs(db)));
            ia = Math.Max(ia + scale * da, 0);
            ib = Math.Max(ib + scale * db, 0);
            result = Evaluate(ia, ib, residual);
            if (Math.Abs(residual[0]) + Math.Abs(residual[1]) < 1e-12) {
                break;
            }
        }

        return result;
    }

    /// <summary>
    ///     The plate voltages for plate currents (draws included) <paramref name="ia" /> and <paramref name="ib" />, and
    ///     the second grid's voltage: the plate resistors and the divider, a linear 2 × 2 solve.
    /// </summary>
    static (double A, double B, double GridB) Plates(
        FloatingParaphaseDesign design,
        double ia,
        double ib,
        bool dc,
        double junction
    ) {
        var (gp, ga, gb) = (1 / design.PlateResistor, 1 / design.DividerA, 1 / design.DividerB);
        if (dc) {
            // The divider as one resistor between the plates (the coupling cap blocks the leak).
            var g = 1 / (design.DividerA + design.DividerB);
            var (s, t) = (design.Supply * gp - ia, design.Supply * gp - ib);
            var det = (gp + g) * (gp + g) - g * g;
            return (((gp + g) * s + g * t) / det, (g * s + (gp + g) * t) / det, 0);
        }

        // AC: the junction is its rest voltage plus the AC divider loaded by the leak; for the DC currents the junction
        // takes no current through the cap, so the divider's current is (A - m) / R_a = (m - B) / R_b + (m - m0) / R_leak.
        var gl = 1 / design.GridLeak;
        var sum = ga + gb + gl;
        // m = (ga A + gb B + gl m0) / sum
        var (ka, kb, k0) = (ga / sum, gb / sum, gl * junction / sum);
        // Plate A: gp (S - A) = ia + ga (A - m); plate B: gp (S - B) = ib + gb (B - m).
        var a11 = gp + ga - ga * ka;
        var a12 = -ga * kb;
        var a21 = -gb * ka;
        var a22 = gp + gb - gb * kb;
        var b1 = design.Supply * gp - ia + ga * k0;
        var b2 = design.Supply * gp - ib + gb * k0;
        var d = a11 * a22 - a12 * a21;
        var plateA = (b1 * a22 - a12 * b2) / d;
        var plateB = (a11 * b2 - a21 * b1) / d;
        var m = ka * plateA + kb * plateB + k0;
        return (plateA, plateB, m - junction);
    }
}
