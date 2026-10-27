using Cathode.Dsp.Wdf;
using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A phase inverter's design: its triode and supply; <see cref="PhaseInverter.For" /> builds its table.
/// </summary>
public abstract record PhaseInverterDesign(TriodeModel Tube, double Supply);

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
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     A cathodyne (split-load) phase inverter: one triode with <paramref name="PlateResistor" /> from the supply and,
///     from the cathode, <paramref name="BiasResistor" /> down to a junction and <paramref name="CathodeResistor" /> to
///     ground; the grid leak returns to the junction. Output A is the plate, output B the junction (the top of the
///     cathode load, so that equal plate and cathode loads give equal swings).
/// </summary>
public sealed record CathodyneDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeResistor,
    double BiasResistor
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     The static transfer of a phase inverter: the open-circuit AC voltages of both outputs as functions of the
///     driven grid's AC voltage, tabulated from the exact DC solution of its Koren triodes, and read with cubic Hermite
///     interpolation (C¹, so Newton sees a continuous slope); with the driven grid's grid-cathode voltage, for grid
///     current (<see cref="InverterInputDesign" />), and the outputs' 2 × 2 output impedance.
/// </summary>
/// <remarks>
///     <para>
///         A justified simplification: a long-tail pair or a cathodyne has no capacitance of its own (its coupling and
///         grid caps are outside it), so at audio frequencies it is a memoryless two-output nonlinearity, and a table
///         makes it about the cost of two polynomial evaluations instead of a multi-port Newton solve. Global negative
///         feedback enters as part of the differential input, which a static map lets the power amp's one-unknown
///         Newton solve include without a delay. What the table leaves out: grid current (modelled outside it, at the
///         driven grid, where a design asks for it) and Miller capacitance; and its supply is taken as constant.
///     </para>
///     <para>
///         The outputs drive the power tubes' grid networks as a two-port Thévenin source: open-circuit voltages from
///         the table behind the output impedance matrix (<see cref="OutputImpedance" />), found from the exact solution
///         with a small current drawn from each output in turn. The cross terms are large (a long-tail pair's plates
///         are coupled through the shared cathode; a cathodyne's plate output is high-impedance and its cathode output
///         low), and with push-pull loading they matter.
///     </para>
/// </remarks>
public abstract class PhaseInverter {
    protected const int Points = 1_025;

    internal double[] a = new double[Points];
    internal double[] b = new double[Points];
    internal double[] slopeA = new double[Points];
    internal double[] slopeB = new double[Points];
    internal double[] gridVoltages = new double[Points];
    internal double[] leakReturns = new double[Points];
    internal double[] cathodeResistances = new double[Points];
    internal readonly double step;

    // Per point: a, b and their slopes times the step, side by side for Evaluate (packed by Differentiate).
    readonly double[] outputs = new double[4 * Points];
    internal readonly double inverseStep;

    static readonly Dictionary<(PhaseInverterDesign, double), PhaseInverter> Cache = [];

    /// <summary>± volts of input the table spans; flat beyond.</summary>
    public double Range { get; }

    /// <summary>The driven grid's DC voltage, where its grid leak returns.</summary>
    public double RestGrid { get; protected init; }

    public double RestCathode { get; protected init; }

    /// <summary>Output A's DC voltage (a plate).</summary>
    public double RestPlateA { get; protected init; }

    /// <summary>Output B's DC voltage (the other plate, or a cathodyne's cathode output).</summary>
    public double RestPlateB { get; protected init; }

    /// <summary>
    ///     The small-signal output impedance matrix at rest: output A's voltage falls by Zaa per ampere drawn from A
    ///     and by Zab per ampere drawn from B, and so on.
    /// </summary>
    public (double Zaa, double Zab, double Zba, double Zbb) OutputImpedance { get; protected init; }

    /// <param name="range">
    ///     The table's span, ± volts of input: both sides are saturated or cut off beyond it (60 V for a single
    ///     inverter stage; less for a chain with gain ahead of its splitter, whose linear region is narrower).
    /// </param>
    protected PhaseInverter(double range = 60) {
        Range = range;
        step = 2 * range / (Points - 1);
        inverseStep = (Points - 1) / (2 * range);
    }

    /// <summary>
    ///     The (cached) table for <paramref name="design" />. Control thread: builds it on first use. With a
    ///     <paramref name="load" />, the driven grid's tables (<see cref="GridToCathode" />, <see cref="LeakReturn" />,
    ///     <see cref="CathodeResistance" />) are those of the inverter with each plate loaded by that resistance to its
    ///     rest voltage (the power tubes' grid leaks through their coupling caps): the load lowers the plate swing, so
    ///     the driven triode carries more current and its cathode rises further, which is what keeps its grid current
    ///     small. The output tables stay open-circuit (the grid networks load them as a Thévenin source).
    /// </summary>
    public static PhaseInverter For(PhaseInverterDesign design, double load = 0) {
        lock (Cache) {
            if (!Cache.TryGetValue((design, load), out var inverter)) {
                inverter = design switch {
                    LongTailPairDesign pair => new LongTailPair(pair, load),
                    CathodyneDesign cathodyne => new Cathodyne(cathodyne, load),
                    DriverChainDesign chain => new DriverChain(chain),
                    FloatingParaphaseDesign paraphase => new FloatingParaphase(paraphase),
                    CathodyneDriverDesign driver => new CathodyneDriver(driver),
                    DifferentialDriverDesign driver => new DifferentialDriver(driver),
                    _ => throw new ArgumentException($"Unknown phase inverter {design}.", nameof(design))
                };
                Cache[(design, load)] = inverter;
            }

            return inverter;
        }
    }

    /// <summary>
    ///     The outputs' AC open-circuit voltages for the driven grid's AC voltage <paramref name="vd" />, and their
    ///     slopes d/d<paramref name="vd" />. Flat beyond ±<see cref="Range" />.
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
        var point = outputs.AsSpan(4 * i, 8);
        var (a0, b0, ma0, mb0) = (point[0], point[1], point[2], point[3]);
        var (a1, b1, ma1, mb1) = (point[4], point[5], point[6], point[7]);
        slopeA = d00 * a0 + d10 * ma0 + d01 * a1 + d11 * ma1;
        slopeB = d00 * b0 + d10 * mb0 + d01 * b1 + d11 * mb1;
        return (h00 * a0 + h10 * ma0 + h01 * a1 + h11 * ma1, h00 * b0 + h10 * mb0 + h01 * b1 + h11 * mb1);
    }

    /// <summary>
    ///     The output tables side by side, as <see cref="Evaluate" /> reads them (each slope times the step).
    /// </summary>
    void Pack() {
        for (var i = 0; i < Points; i++) {
            outputs[4 * i] = a[i];
            outputs[4 * i + 1] = b[i];
            outputs[4 * i + 2] = slopeA[i] * step;
            outputs[4 * i + 3] = slopeB[i] * step;
        }
    }

    /// <summary>
    ///     The driven grid's grid-to-cathode voltage (absolute, negative at rest) for its AC voltage
    ///     <paramref name="vd" />, linearly interpolated, and its slope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GridToCathode(double vd, out double slope) {
        var x = (vd + Range) * inverseStep;
        if (!(x > 0)) {
            slope = 0;
            return gridVoltages[0];
        }

        if (!(x < Points - 1)) {
            slope = 1; // beyond the table the cathode no longer follows
            return gridVoltages[Points - 1] + (x - (Points - 1)) * step;
        }

        var i = (int)x;
        var t = x - i;
        slope = (gridVoltages[i + 1] - gridVoltages[i]) * inverseStep;
        return gridVoltages[i] + t * (gridVoltages[i + 1] - gridVoltages[i]);
    }

    /// <summary>
    ///     The resistance the cathode presents to a current injected into it (the driven grid's grid current, which
    ///     leaves through the cathode and raises it) at the driven grid's AC voltage <paramref name="vd" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double CathodeResistance(double vd) {
        var x = Math.Clamp((vd + Range) * inverseStep, 0, Points - 1 - 1e-9);
        var i = (int)x;
        return cathodeResistances[i] + (x - i) * (cathodeResistances[i + 1] - cathodeResistances[i]);
    }

    /// <summary>
    ///     The AC voltage of the node the driven grid's leak returns to (the tail or cathode-load junction, which
    ///     follows the input: the leak is bootstrapped) for the grid's AC voltage <paramref name="vd" />, and its
    ///     slope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double LeakReturn(double vd, out double slope) {
        var x = Math.Clamp((vd + Range) * inverseStep, 0, Points - 1 - 1e-9);
        var i = (int)x;
        var t = x - i;
        slope = vd > -Range && vd < Range ? (leakReturns[i + 1] - leakReturns[i]) * inverseStep : 0;
        return leakReturns[i] + t * (leakReturns[i + 1] - leakReturns[i]);
    }

    protected static double Parallel(double a, double b) => a * b / (a + b);

    /// <summary>
    ///     The current I with I = I_p(V_gk, V - R I): a triode on a plate resistor, by Newton's method kept inside a
    ///     shrinking bracket (bisecting whenever a step would leave it).
    /// </summary>
    protected static double LoadLine(Triode triode, double vgk, double headroom, double plateResistor) {
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

    /// <summary>
    ///     Central-difference slopes of the tables (for the Hermite reads), and the output tables packed for
    ///     <see cref="Evaluate" />: the last step of building every table.
    /// </summary>
    protected void Differentiate() {
        for (var n = 0; n < Points; n++) {
            var (lo, hi) = (Math.Max(n - 1, 0), Math.Min(n + 1, Points - 1));
            slopeA[n] = (a[hi] - a[lo]) / ((hi - lo) * step);
            slopeB[n] = (b[hi] - b[lo]) / ((hi - lo) * step);
        }

        Pack();
    }
}

/// <summary>
///     A <see cref="LongTailPairDesign" />'s table: the plates' AC voltages as functions of the differential grid
///     voltage. With push-pull loading, the cross terms of its output impedance lower the effective output impedance
///     by a third or more.
/// </summary>
public sealed class LongTailPair : PhaseInverter {
    public LongTailPairDesign Design { get; }

    /// <summary>Plate current of each triode at rest.</summary>
    public (double A, double B) RestCurrents { get; }

    internal LongTailPair(LongTailPairDesign design, double load = 0) {
        Design = design;
        var triode = new Triode(design.Tube);

        // The rest point: the grids return to the bias/tail junction, which depends on the tail current. The junction
        // follows the grids almost one to one (R_tail / (R_tail + R_bias + 1/g_m), 0.96 with a 47k tail), so iterating
        // grid = I R_tail converges too slowly; bisect on I(grid) R_tail - grid, which falls with the grid voltage.
        var (low, high) = (0.0, design.Supply);
        var (ia, ib, vk) = (0.0, 0.0, 0.0);
        for (var i = 0; i < 80; i++) {
            var middle = 0.5 * (low + high);
            (ia, ib, vk) = Solve(triode, design, middle, middle);
            if ((ia + ib) * design.TailResistor > middle) {
                low = middle;
            } else {
                high = middle;
            }
        }

        var grid = 0.5 * (low + high);
        (ia, ib, vk) = Solve(triode, design, grid, grid);

        RestGrid = grid;
        RestCathode = vk;
        RestCurrents = (ia, ib);
        RestPlateA = design.Supply - ia * design.PlateResistorA;
        RestPlateB = design.Supply - ib * design.PlateResistorB;

        // The output impedances: draw a small current from each plate in turn and see both plates move.
        const double Probe = 1e-6;
        var (aa, ba, _) = Solve(triode, design, grid, grid, Probe);
        var (ab, bb, _) = Solve(triode, design, grid, grid, 0, Probe);
        OutputImpedance = (
            (RestPlateA - (design.Supply - (aa + Probe) * design.PlateResistorA)) / Probe,
            (RestPlateA - (design.Supply - ab * design.PlateResistorA)) / Probe,
            (RestPlateB - (design.Supply - ba * design.PlateResistorB)) / Probe,
            (RestPlateB - (design.Supply - (bb + Probe) * design.PlateResistorB)) / Probe
        );

        // A load on each plate as its Thévenin equivalent: the plate resistor in parallel with the load, and a supply
        // that, through it, gives the plate its open-circuit rest voltage (as draws from the plates, which the solve
        // takes off the supply through the plate resistors).
        var loaded = design;
        var (drawA, drawB) = (0.0, 0.0);
        if (load > 0) {
            var (ra, rb) = (Parallel(design.PlateResistorA, load), Parallel(design.PlateResistorB, load));
            loaded = design with { PlateResistorA = ra, PlateResistorB = rb };
            drawA = (design.Supply
                    - (design.Supply * load + RestPlateA * design.PlateResistorA) / (design.PlateResistorA + load))
                / ra;
            drawB = (design.Supply
                    - (design.Supply * load + RestPlateB * design.PlateResistorB) / (design.PlateResistorB + load))
                / rb;
        }

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var (pa, pb, cathode) = Solve(triode, design, grid + vd, grid);
            a[n] = design.Supply - pa * design.PlateResistorA - RestPlateA;
            b[n] = design.Supply - pb * design.PlateResistorB - RestPlateB;
            if (load > 0) {
                cathode = Solve(triode, loaded, grid + vd, grid, drawA, drawB).Cathode;
            }

            gridVoltages[n] = grid + vd - cathode;
            leakReturns[n] = cathode * design.TailResistor / (design.TailResistor + design.BiasResistor) - grid;
            var injected = Solve(triode, loaded, grid + vd, grid, drawA, drawB, Probe).Cathode;
            cathodeResistances[n] = (injected - cathode) / Probe;
        }

        Differentiate();
    }

    /// <summary>The (cached) table for <paramref name="design" />. Control thread: builds it on first use.</summary>
    public static LongTailPair For(LongTailPairDesign design) => (LongTailPair)PhaseInverter.For(design);

    /// <summary>
    ///     The exact DC solution for grid voltages <paramref name="gridA" /> and <paramref name="gridB" /> (no grid
    ///     current), with <paramref name="drawA" /> and <paramref name="drawB" /> amperes drawn from the plates by a
    ///     load and <paramref name="inject" /> injected into the cathode (grid current leaving through it): plate
    ///     currents and the cathode voltage, by bisection on the cathode voltage with each triode's current found from
    ///     its own load line.
    /// </summary>
    public static (double A, double B, double Cathode) Solve(
        Triode triode,
        LongTailPairDesign design,
        double gridA,
        double gridB,
        double drawA = 0,
        double drawB = 0,
        double inject = 0
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
            if ((ia + ib + inject) * tail > vk) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var cathode = 0.5 * (low + high);
        return (LoadLine(triode, gridA - cathode, supplyA - cathode, design.PlateResistorA),
            LoadLine(triode, gridB - cathode, supplyB - cathode, design.PlateResistorB), cathode);
    }
}

/// <summary>
///     A <see cref="CathodyneDesign" />'s table: the plate's and the cathode load's AC voltages as functions of the
///     grid's AC voltage. Its gain is a little under one on each side; the plate output's impedance is high and the
///     cathode output's low, and as the plate falls to meet the cathode the plate side clips first, which is the
///     cathodyne's sound.
/// </summary>
public sealed class Cathodyne : PhaseInverter {
    public CathodyneDesign Design { get; }

    /// <summary>The triode's current at rest.</summary>
    public double RestCurrent { get; }

    internal Cathodyne(CathodyneDesign design, double load = 0) {
        Design = design;
        var triode = new Triode(design.Tube);

        // The rest point: the grid returns to the junction, so V_gk = -I R_bias; bisection on the current.
        Span<double> v = stackalloc double[2];
        Span<double> c = stackalloc double[2];
        Span<double> j = stackalloc double[4];
        var (low, high) = (0.0, design.Supply / (design.PlateResistor + design.CathodeResistor + design.BiasResistor));
        for (var n = 0; n < 100; n++) {
            var trial = 0.5 * (low + high);
            v[0] = -trial * design.BiasResistor;
            v[1] = design.Supply - trial * (design.PlateResistor + design.CathodeResistor + design.BiasResistor);
            j.Clear();
            triode.Evaluate(v, c, j, 2);
            (low, high) = c[1] > trial ? (trial, high) : (low, trial);
        }

        var grid = 0.5 * (low + high) * design.CathodeResistor;
        var current = Solve(triode, design, grid);
        RestGrid = grid;
        RestCurrent = current;
        RestCathode = current * (design.CathodeResistor + design.BiasResistor);
        RestPlateA = design.Supply - current * design.PlateResistor;
        RestPlateB = current * design.CathodeResistor;

        const double Probe = 1e-6;
        var withA = Solve(triode, design, grid, Probe);
        var withB = Solve(triode, design, grid, 0, Probe);
        OutputImpedance = (
            (RestPlateA - (design.Supply - (withA + Probe) * design.PlateResistor)) / Probe,
            (RestPlateA - (design.Supply - withB * design.PlateResistor)) / Probe,
            (RestPlateB - withA * design.CathodeResistor) / Probe,
            (RestPlateB - (withB - Probe) * design.CathodeResistor) / Probe
        );

        // The plate's load as in the long-tail pair (the cathode output's is left out: its low impedance hardly moves).
        var loaded = design;
        var drawA = 0.0;
        if (load > 0) {
            var r = design.PlateResistor * load / (design.PlateResistor + load);
            loaded = design with { PlateResistor = r };
            drawA = (design.Supply
                    - (design.Supply * load + RestPlateA * design.PlateResistor) / (design.PlateResistor + load))
                / r;
        }

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var i = Solve(triode, design, grid + vd);
            a[n] = design.Supply - i * design.PlateResistor - RestPlateA;
            b[n] = i * design.CathodeResistor - RestPlateB;
            var held = load > 0 ? Solve(triode, loaded, grid + vd, drawA) : i;
            gridVoltages[n] = grid + vd - held * (design.CathodeResistor + design.BiasResistor);
            leakReturns[n] = held * design.CathodeResistor - grid;
            var injected = Solve(triode, loaded, grid + vd, drawA, 0, Probe);
            cathodeResistances[n] = (injected + Probe - held) * (design.CathodeResistor + design.BiasResistor) / Probe;
        }

        Differentiate();
    }

    /// <summary>
    ///     The exact DC solution for grid voltage <paramref name="grid" /> (no grid current), with
    ///     <paramref name="drawA" /> amperes drawn from the plate and <paramref name="drawB" /> from the junction by a
    ///     load and <paramref name="inject" /> injected into the cathode: the triode's current, by bisection.
    /// </summary>
    public static double Solve(
        Triode triode,
        CathodyneDesign design,
        double grid,
        double drawA = 0,
        double drawB = 0,
        double inject = 0
    ) {
        Span<double> v = stackalloc double[2];
        Span<double> i = stackalloc double[2];
        Span<double> j = stackalloc double[4];
        var low = 0.0;
        var high = design.Supply / (design.PlateResistor + design.CathodeResistor + design.BiasResistor) + drawB;
        for (var n = 0; n < 100; n++) {
            var current = 0.5 * (low + high);
            var plate = design.Supply - (current + drawA) * design.PlateResistor;
            var cathode = (current - drawB + inject) * design.CathodeResistor
                + (current + inject) * design.BiasResistor;
            v[0] = grid - cathode;
            v[1] = plate - cathode;
            j.Clear();
            triode.Evaluate(v, i, j, 2);
            if (i[1] > current) {
                low = current;
            } else {
                high = current;
            }
        }

        return 0.5 * (low + high);
    }
}
