using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A long-tail-pair phase inverter: two triodes sharing a cathode, the tail (<paramref name="TailResistor" />) to
///     ground and the bias resistor (<paramref name="BiasResistor" />) above it, where both grid leaks return. The
///     second grid is AC-grounded; the first takes the signal.
/// </summary>
public sealed record LongTailPairDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistorA,
    double PlateResistorB,
    double TailResistor,
    double BiasResistor
);

/// <summary>
///     The static transfer of a long-tail pair: the open-circuit AC voltages of both plates as functions of the
///     differential grid voltage, tabulated from the exact DC solution of the two Koren triodes and the tail, and read
///     with cubic Hermite interpolation (C¹, so Newton sees a continuous slope).
/// </summary>
/// <remarks>
///     <para>
///         A justified simplification of the phase inverter: a long-tail pair has no capacitance of its own (its
///         coupling and grid caps are outside it), so at audio frequencies it is a memoryless two-output nonlinearity,
///         and a table makes it about the cost of two polynomial evaluations instead of a four-port Newton solve. Global
///         negative feedback enters as part of the differential input, which a static map lets the power amp's
///         one-unknown Newton solve include without a delay. What it leaves out: grid current at the first grid (the
///         pair is driven from a low-impedance tone stack or master pot, and blocks only under extreme drive) and
///         Miller capacitance; and its supply is taken as constant.
///     </para>
///     <para>
///         The outputs drive the power tubes' grid networks as a two-port Thévenin source: open-circuit voltages from the
///         table behind the output impedance matrix (<see cref="OutputImpedance" />), found from the exact solution
///         with a small current drawn from each plate in turn. The cross terms are large (the plates are coupled
///         through the shared cathode), and with push-pull loading they lower the effective output impedance by a
///         third or more.
///     </para>
/// </remarks>
public sealed class LongTailPair {
    const int Points = 1_025;
    const double Range = 60; // ± volts of differential input; both sides are saturated or cut off beyond

    static readonly Dictionary<LongTailPairDesign, LongTailPair> Cache = [];

    readonly double[] a = new double[Points];
    readonly double[] b = new double[Points];
    readonly double[] slopeA = new double[Points];
    readonly double[] slopeB = new double[Points];
    readonly double step = 2 * Range / (Points - 1);
    readonly double inverseStep = (Points - 1) / (2 * Range);

    public LongTailPairDesign Design { get; }

    /// <summary>Grid DC voltage (both grids), at the bias/tail junction.</summary>
    public double RestGrid { get; }

    public double RestCathode { get; }

    public double RestPlateA { get; }

    public double RestPlateB { get; }

    /// <summary>Plate current of each triode at rest.</summary>
    public (double A, double B) RestCurrents { get; }

    /// <summary>
    ///     The small-signal output impedance matrix at rest: plate A's voltage falls by Zaa per ampere drawn from plate A
    ///     and by Zab per ampere drawn from plate B, and so on.
    /// </summary>
    public (double Zaa, double Zab, double Zba, double Zbb) OutputImpedance { get; }

    LongTailPair(LongTailPairDesign design) {
        Design = design;
        var triode = new Triode(design.Tube);

        // The rest point: the grids return to the bias/tail junction, which depends on the tail current.
        var grid = 0.0;
        var (ia, ib, vk) = (0.0, 0.0, 0.0);
        for (var i = 0; i < 60; i++) {
            (ia, ib, vk) = Solve(triode, design, grid, grid);
            grid = (ia + ib) * design.TailResistor;
        }

        RestGrid = grid;
        RestCathode = vk;
        RestCurrents = (ia, ib);
        RestPlateA = design.Supply - ia * design.PlateResistorA;
        RestPlateB = design.Supply - ib * design.PlateResistorB;

        // The output impedances: draw a small current from each plate in turn and see both plates move.
        const double Probe = 1e-6;
        var (aa, ba, _) = Solve(triode, design, grid, grid, Probe, 0);
        var (ab, bb, _) = Solve(triode, design, grid, grid, 0, Probe);
        OutputImpedance = (
            (RestPlateA - (design.Supply - (aa + Probe) * design.PlateResistorA)) / Probe,
            (RestPlateA - (design.Supply - ab * design.PlateResistorA)) / Probe,
            (RestPlateB - (design.Supply - ba * design.PlateResistorB)) / Probe,
            (RestPlateB - (design.Supply - (bb + Probe) * design.PlateResistorB)) / Probe
        );

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var (pa, pb, _) = Solve(triode, design, grid + vd, grid);
            a[n] = design.Supply - pa * design.PlateResistorA - RestPlateA;
            b[n] = design.Supply - pb * design.PlateResistorB - RestPlateB;
        }

        for (var n = 0; n < Points; n++) {
            var (lo, hi) = (Math.Max(n - 1, 0), Math.Min(n + 1, Points - 1));
            slopeA[n] = (a[hi] - a[lo]) / ((hi - lo) * step);
            slopeB[n] = (b[hi] - b[lo]) / ((hi - lo) * step);
        }
    }

    /// <summary>The (cached) table for <paramref name="design" />. Control thread: builds it on first use.</summary>
    public static LongTailPair For(LongTailPairDesign design) {
        lock (Cache) {
            if (!Cache.TryGetValue(design, out var pair)) {
                pair = new(design);
                Cache[design] = pair;
            }

            return pair;
        }
    }

    /// <summary>
    ///     The plates' AC open-circuit voltages for differential grid voltage <paramref name="vd" />, and their slopes
    ///     d/d<paramref name="vd" />. Flat beyond ±60 V.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (double A, double B) Evaluate(double vd, out double slopeA, out double slopeB) {
        var x = (vd + Range) * inverseStep;
        if (!(x > 0)) {
            slopeA = 0;
            slopeB = 0;
            return (a[0], b[0]);
        }

        if (!(x < Points - 1)) {
            slopeA = 0;
            slopeB = 0;
            return (a[Points - 1], b[Points - 1]);
        }

        var i = (int)x;
        var t = x - i;
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = 3 * t2 - 2 * t3;
        var h11 = t3 - t2;
        var d00 = (6 * t2 - 6 * t) * inverseStep;
        var d10 = (3 * t2 - 4 * t + 1) * inverseStep;
        var d01 = -d00;
        var d11 = (3 * t2 - 2 * t) * inverseStep;
        var (a0, a1, b0, b1) = (a[i], a[i + 1], b[i], b[i + 1]);
        var (ma0, ma1) = (this.slopeA[i] * step, this.slopeA[i + 1] * step);
        var (mb0, mb1) = (this.slopeB[i] * step, this.slopeB[i + 1] * step);
        slopeA = d00 * a0 + d10 * ma0 + d01 * a1 + d11 * ma1;
        slopeB = d00 * b0 + d10 * mb0 + d01 * b1 + d11 * mb1;
        return (h00 * a0 + h10 * ma0 + h01 * a1 + h11 * ma1, h00 * b0 + h10 * mb0 + h01 * b1 + h11 * mb1);
    }

    /// <summary>
    ///     The exact DC solution for grid voltages <paramref name="gridA" /> and <paramref name="gridB" /> (no grid
    ///     current), with <paramref name="drawA" /> and <paramref name="drawB" /> amperes drawn from the plates by a
    ///     load: plate currents and the cathode voltage, by bisection on the cathode voltage with each triode's current
    ///     found from its own load line.
    /// </summary>
    public static (double A, double B, double Cathode) Solve(
        Triode triode,
        LongTailPairDesign design,
        double gridA,
        double gridB,
        double drawA = 0,
        double drawB = 0
    ) {
        var supplyA = design.Supply - drawA * design.PlateResistorA;
        var supplyB = design.Supply - drawB * design.PlateResistorB;
        var tail = design.TailResistor + design.BiasResistor;
        var low = 0.0;
        var high = design.Supply;
        for (var i = 0; i < 60; i++) {
            var vk = 0.5 * (low + high);
            var ia = LoadLine(triode, gridA - vk, supplyA - vk, design.PlateResistorA);
            var ib = LoadLine(triode, gridB - vk, supplyB - vk, design.PlateResistorB);
            if ((ia + ib) * tail > vk) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var cathode = 0.5 * (low + high);
        return (LoadLine(triode, gridA - cathode, supplyA - cathode, design.PlateResistorA),
            LoadLine(triode, gridB - cathode, supplyB - cathode, design.PlateResistorB), cathode);
    }

    /// <summary>
    ///     The current I with I = I_p(V_gk, V - R I): a triode on a plate resistor, by Newton's method kept inside a
    ///     shrinking bracket (bisecting whenever a step would leave it).
    /// </summary>
    static double LoadLine(Triode triode, double vgk, double headroom, double plateResistor) {
        Span<double> v = stackalloc double[2];
        Span<double> i = stackalloc double[2];
        Span<double> j = stackalloc double[4];
        var low = 0.0;
        var high = Math.Max(headroom, 0) / plateResistor;
        var current = 0.5 * high;
        for (var n = 0; n < 100 && high - low > 1e-15; n++) {
            v[0] = vgk;
            v[1] = headroom - current * plateResistor;
            j.Clear();
            triode.Evaluate(v, i, j, 2);
            var residual = i[1] - current;
            if (residual > 0) {
                low = current;
            } else {
                high = current;
            }

            var next = current - residual / (-plateResistor * j[3] - 1);
            if (!(next > low && next < high)) {
                next = 0.5 * (low + high);
            }

            if (Math.Abs(next - current) <= 1e-14 + 1e-12 * current) {
                return next;
            }

            current = next;
        }

        return current;
    }
}
