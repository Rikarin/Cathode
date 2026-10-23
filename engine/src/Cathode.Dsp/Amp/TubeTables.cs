using System.Runtime.CompilerServices;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A smooth function of one variable on [min, max], tabulated with its exact slope at uniform points and read by
///     cubic Hermite interpolation: C¹, and accurate to O(h⁴) (about 1e-9 relative for the tube laws here).
/// </summary>
public sealed class HermiteTable {
    readonly double[] values;
    readonly double[] slopes; // pre-multiplied by the step
    readonly double inverseStep;

    public double Min { get; }

    public double Max { get; }

    /// <summary>Tabulates <paramref name="function" />, which returns the value and the slope at a point.</summary>
    public HermiteTable(Func<double, (double Value, double Slope)> function, double min, double max, int points) {
        Min = min;
        Max = max;
        values = new double[points];
        slopes = new double[points];
        var step = (max - min) / (points - 1);
        inverseStep = 1 / step;
        for (var i = 0; i < points; i++) {
            var (value, slope) = function(min + i * step);
            values[i] = value;
            slopes[i] = slope * step;
        }
    }

    /// <summary>The value at <paramref name="x" /> (within [Min, Max]) and its slope.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Evaluate(double x, out double slope) {
        var position = (x - Min) * inverseStep;
        var i = Math.Min((int)position, values.Length - 2);
        var t = position - i;
        var (y0, y1, m0, m1) = (values[i], values[i + 1], slopes[i], slopes[i + 1]);
        var delta = y1 - y0;

        // Hermite in the form y0 + t (m0 + t (c2 + t c3)).
        var c2 = 3 * delta - 2 * m0 - m1;
        var c3 = m0 + m1 - 2 * delta;
        slope = (m0 + t * (2 * c2 + 3 * t * c3)) * inverseStep;
        return y0 + t * (m0 + t * (c2 + t * c3));
    }

    /// <summary>
    ///     softplus(u)^ex and its slope, without the underflow of ln(1 + e^u) to zero far below zero (where
    ///     softplus(u) = e^u (1 - e^u / 2 + ...)).
    /// </summary>
    internal static (double Value, double Slope) SoftplusPower(double u, double ex) {
        double s, logistic;
        if (u < -20) {
            var e = Math.Exp(u);
            s = e * (1 - 0.5 * e);
            logistic = e / (1 + e);
        } else {
            s = DeviceMath.Softplus(u, out logistic);
        }

        var value = Math.Pow(s, ex);
        return (value, ex * value / s * logistic);
    }
}

/// <summary>
///     Koren's triode law split into functions of one variable, each tabulated: with
///     u = K_p (1/μ + V_gk / √(K_vb + V_pk²)), I_p = (2 / K_g1) P(V_pk) G(u) where P(v) = (v / K_p)^Ex and
///     G(u) = softplus(u)^Ex; Dempwolf's grid current is a function of V_gk alone. Three table reads and a square root
///     replace two exponentials, two logarithms and two powers; outside the tables the exact expressions take over.
/// </summary>
public sealed class TriodeTables {
    const double LowestU = -40; // G(u) < 1e-24 below: no current
    const double HighestU = 40;
    const double LowestPlate = 4; // (v / K_p)^Ex has unbounded curvature at 0: computed exactly below
    const double HighestPlate = 1_024;
    const double HighestGrid = 10;

    static readonly Dictionary<TriodeModel, TriodeTables> Cache = [];

    readonly HermiteTable g;
    readonly HermiteTable p;
    readonly HermiteTable grid;
    readonly double lowestGrid;

    public TriodeModel Model { get; }

    TriodeTables(TriodeModel model) {
        Model = model;
        var ex = model.Exponent;
        g = new(u => HermiteTable.SoftplusPower(u, ex), LowestU, HighestU, 8_193);
        p = new(
            v => {
                var value = Math.Pow(v / model.Kp, ex);
                return (value, ex * value / v);
            },
            LowestPlate,
            HighestPlate,
            8_193
        );
        lowestGrid = -20 / model.GridSharpness; // where Tube.GridCurrent switches off
        grid = new(
            v => (Tube.GridCurrent(v, model.GridConductance, model.GridSharpness, model.GridExponent, out var slope), slope),
            lowestGrid,
            HighestGrid,
            8_193
        );
    }

    /// <summary>The (cached) tables for <paramref name="model" />. Builds them on first use: control thread.</summary>
    public static TriodeTables For(TriodeModel model) {
        lock (Cache) {
            if (!Cache.TryGetValue(model, out var tables)) {
                tables = new(model);
                Cache[model] = tables;
            }

            return tables;
        }
    }

    /// <summary>Grid current at <paramref name="vgk" /> and its slope.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GridCurrent(double vgk, out double slope) {
        if (vgk <= lowestGrid) {
            slope = 0;
            return 0;
        }

        return vgk < HighestGrid
            ? grid.Evaluate(vgk, out slope)
            : Tube.GridCurrent(vgk, Model.GridConductance, Model.GridSharpness, Model.GridExponent, out slope);
    }

    /// <summary>Plate current at (<paramref name="vgk" />, <paramref name="vpk" />) and its two slopes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double PlateCurrent(double vgk, double vpk, out double gridSlope, out double plateSlope) {
        if (!(vpk > 0)) {
            gridSlope = 0;
            plateSlope = 0;
            return 0;
        }

        var m = Model;
        var squared = m.Kvb + vpk * vpk;
        var root = Math.Sqrt(squared);
        var u = m.Kp * (1 / m.Mu + vgk / root);
        if (!(u > LowestU)) {
            gridSlope = 0;
            plateSlope = 0;
            return 0;
        }

        double gu, du;
        if (u < HighestU) {
            gu = g.Evaluate(u, out du);
        } else {
            gu = Math.Pow(u, m.Exponent); // softplus(u) = u here
            du = m.Exponent * gu / u;
        }

        double pv, dv;
        if (vpk >= LowestPlate && vpk < HighestPlate) {
            pv = p.Evaluate(vpk, out dv);
        } else {
            pv = Math.Pow(vpk / m.Kp, m.Exponent);
            dv = m.Exponent * pv / vpk;
        }

        var scale = 2 / m.Kg1;
        gridSlope = scale * pv * du * m.Kp / root;
        plateSlope = scale * (dv * gu - pv * du * m.Kp * vgk * vpk / (squared * root));
        return scale * pv * gu;
    }
}

/// <summary>
///     <see cref="Triode" /> with tabulated equations (<see cref="TriodeTables" />): the same device to about 1e-9
///     relative, at a third of the cost.
/// </summary>
public readonly struct FastTriode(TriodeModel model) : INonlinearDevice {
    readonly TriodeTables tables = TriodeTables.For(model);

    public int Ports => 2;

    public double Conductance(int port) => 1e-5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Evaluate(ReadOnlySpan<double> voltages, Span<double> currents, Span<double> jacobian, int stride) {
        var vgk = voltages[0];
        currents[0] = tables.GridCurrent(vgk, out jacobian[0]);
        currents[1] = tables.PlateCurrent(vgk, voltages[1], out jacobian[stride], out jacobian[stride + 1]);
    }

    /// <summary>The plate voltage may fall to a tenth per Newton step (<see cref="TubeLimit.Plate" />).</summary>
    public void Limit(ReadOnlySpan<double> previous, Span<double> proposed) =>
        proposed[1] = TubeLimit.Plate(previous[1], proposed[1]);

    public void Commit(ReadOnlySpan<double> voltages) { }

    public void Reset() { }
}

/// <summary>Newton step limits for the amp's tubes.</summary>
public static class TubeLimit {
    /// <summary>How far a plate may fall in one Newton step, as a fraction of where it was.</summary>
    public const double Floor = 0.1;

    /// <summary>
    ///     A plate voltage may fall to a tenth of its previous value per Newton step (<see cref="Tube.LimitPlate" />
    ///     allows half): enough to keep Newton out of the flat region below zero, where it would cycle, while a plate
    ///     slammed from B+ to its knee by a hard transient gets there in two or three steps instead of five or six (on
    ///     loud noise, the power stage's mean iterations drop from 9.4 to 5.3 per sample).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Plate(double previous, double proposed) =>
        previous > 0 && proposed < Floor * previous ? Floor * previous : proposed;
}

/// <summary>
///     Koren's pentode law in tabulated pieces, for the power stage: with u = K_p (1/μ + V_g1k / V_g2k),
///     I_p = (2 / K_g1) (V_g2k / K_p)^Ex G(u) atan(V_pk / K_vb), where the screen factor changes slowly (it is computed
///     once per sample, <see cref="ScreenFactor" />) and G and the arctangent knee are tables. The screen current
///     (V_g2k / μ + V_g1k)^Ex / K_g2 is a table of its one argument.
/// </summary>
public sealed class PentodeTables {
    const double LowestU = -40;
    const double HighestU = 40;
    const double HighestPlate = 2_000;
    const double LowestScreenArgument = 4; // like the triode's plate law, exact below
    const double HighestScreenArgument = 256;

    static readonly Dictionary<PentodeModel, PentodeTables> Cache = [];

    readonly HermiteTable g;
    readonly HermiteTable knee;
    readonly HermiteTable screen;

    public PentodeModel Model { get; }

    PentodeTables(PentodeModel model) {
        Model = model;
        var ex = model.Exponent;
        g = new(u => HermiteTable.SoftplusPower(u, ex), LowestU, HighestU, 8_193);
        knee = new(
            v => {
                var ratio = v / model.Kvb;
                return (Math.Atan(ratio), 1 / (model.Kvb * (1 + ratio * ratio)));
            },
            0,
            HighestPlate,
            8_193
        );
        screen = new(
            w => {
                var value = Math.Pow(w, ex) / model.Kg2;
                return (value, ex * value / w);
            },
            LowestScreenArgument,
            HighestScreenArgument,
            8_193
        );
    }

    /// <summary>The (cached) tables for <paramref name="model" />. Builds them on first use: control thread.</summary>
    public static PentodeTables For(PentodeModel model) {
        lock (Cache) {
            if (!Cache.TryGetValue(model, out var tables)) {
                tables = new(model);
                Cache[model] = tables;
            }

            return tables;
        }
    }

    /// <summary>(2 / K_g1) (V_g2k / K_p)^Ex, the plate current's dependence on the screen voltage.</summary>
    public double ScreenFactor(double vg2) => vg2 > 1e-3 ? 2 * Math.Pow(vg2 / Model.Kp, Model.Exponent) / Model.Kg1 : 0;

    /// <summary>
    ///     Plate current for screen factor <paramref name="factor" /> (from <see cref="ScreenFactor" /> at
    ///     <paramref name="vg2" />), and its slopes with respect to the control grid and the plate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double PlateCurrent(
        double factor,
        double vg1,
        double vg2,
        double vp,
        out double gridSlope,
        out double plateSlope
    ) {
        gridSlope = 0;
        plateSlope = 0;
        if (!(factor > 0) || !(vp > 0)) {
            return 0;
        }

        var m = Model;
        var u = m.Kp * (1 / m.Mu + vg1 / vg2);
        if (!(u > LowestU)) {
            return 0;
        }

        double gu, du;
        if (u < HighestU) {
            gu = g.Evaluate(u, out du);
        } else {
            gu = Math.Pow(u, m.Exponent);
            du = m.Exponent * gu / u;
        }

        double k, dk;
        if (vp < HighestPlate) {
            k = knee.Evaluate(vp, out dk);
        } else {
            var ratio = vp / m.Kvb;
            k = Math.Atan(ratio);
            dk = 1 / (m.Kvb * (1 + ratio * ratio));
        }

        gridSlope = factor * du * m.Kp / vg2 * k;
        plateSlope = factor * gu * dk;
        return factor * gu * k;
    }

    /// <summary>Screen current at (<paramref name="vg1" />, <paramref name="vg2" />).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ScreenCurrent(double vg1, double vg2) {
        var w = vg2 / Model.Mu + vg1;
        if (!(w > 0)) {
            return 0;
        }

        return w is >= LowestScreenArgument and < HighestScreenArgument
            ? screen.Evaluate(w, out _)
            : Math.Pow(w, Model.Exponent) / Model.Kg2;
    }
}
