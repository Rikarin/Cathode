using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A fixed-divider paraphase inverter (Valco's: the Supro 1624T): the first triode drives one output tube through
///     its coupling cap, that tube's grid leak is a divider (<paramref name="DividerTop" /> over
///     <paramref name="DividerBottom" />) whose tap feeds the second triode's grid, and the second triode, on an
///     unbypassed <paramref name="CathodeResistor" />, drives the other output tube. Nothing balances it: the second
///     side gets the first side's signal times the divider times the second triode's gain.
/// </summary>
/// <remarks>
///     <para>
///         The first triode is not in the table. It is the preamp's last stage (a <see cref="NetworkStageDesign" />
///         whose output is its plate), so that its grid network, its cathode's bypass cap and its own clipping are
///         solved there; here it is the source: its plate resistor <paramref name="PlateResistor" /> and cathode
///         resistor <paramref name="DriverCathode" /> give the resistance the first output's grid network sees. The
///         power amp's <see cref="PowerAmpDesign.GridLeak" /> must be the divider's whole resistance, and it gets no
///         <see cref="InverterInputDesign" /> (there is no driven grid here).
///     </para>
///     <para>
///         The table's input is the first plate's AC voltage. Output B is that voltage itself and output A the second
///         triode's plate, so that the table keeps the other inverters' sense (A falls and B rises for a rising
///         input); the output tubes are alike, so which of them is called A changes nothing.
///     </para>
/// </remarks>
/// <param name="Tube">Both triodes.</param>
/// <param name="Supply">The plates' supply.</param>
/// <param name="PlateResistor">Each plate's resistor.</param>
/// <param name="DriverCathode">
///     The first triode's cathode resistor (taken as unbypassed for its plate's resistance).
/// </param>
/// <param name="CathodeResistor">The second triode's cathode resistor, unbypassed.</param>
/// <param name="DividerTop">From the first output tube's grid to the tap.</param>
/// <param name="DividerBottom">From the tap to ground; the second triode's grid is on the tap.</param>
/// <param name="GridLeak">The second output tube's grid leak.</param>
public sealed record FixedParaphaseDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double DriverCathode,
    double CathodeResistor,
    double DividerTop,
    double DividerBottom,
    double GridLeak
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     A <see cref="FixedParaphaseDesign" />'s table: the second triode's plate (output A), from its exact solution
///     with its grid on the divider's tap, and the first plate passed through (output B).
/// </summary>
/// <remarks>
///     <para>
///         The tap is on the first output tube's grid, after the coupling cap, so what it carries is the first plate's
///         swing as the divider loads it: the plate's resistance against the divider, a fixed ratio here (the coupling
///         cap as a short). Left out with that: the cap's roll-off ahead of the tap (the second side loses bass
///         twice in the amp) and the first output tube's grid current, which clamps the tap's positive peaks; those
///         fall where the second output tube is being driven into cut-off.
///     </para>
///     <para>
///         The power amp runs both grid networks on one grid-leak value, the divider's. The second output tube's own
///         leak is larger, so the second plate's output is scaled by what that leak would pass over what the divider's
///         value passes (each against the second plate's resistance): the second grid then gets the swing the
///         drawing's parts give it. The outputs' cross impedance is left at zero, as in
///         <see cref="FloatingParaphase" />.
///     </para>
/// </remarks>
public sealed class FixedParaphase : PhaseInverter {
    public FixedParaphaseDesign Design { get; }

    /// <summary>The second triode's plate current at rest.</summary>
    public double RestCurrent { get; }

    /// <summary>The second triode's grid voltage per volt at the first plate (open circuit).</summary>
    public double TapRatio { get; }

    internal FixedParaphase(FixedParaphaseDesign design) : base(design.Supply) {
        Design = design;
        var triode = new Triode(design.Tube);

        // The first triode, for its rest point and its plate's resistance: R_L || (r_p + (mu + 1) R_k).
        var (_, driverPlate, driverCathode) = OperatingPoint.CommonCathode(
            design.Tube,
            0,
            design.Supply,
            design.PlateResistor,
            design.DriverCathode
        );
        var (rp, mu) = OperatingPoint.SmallSignal(design.Tube, -driverCathode, driverPlate - driverCathode);
        var source = Parallel(design.PlateResistor, rp + (mu + 1) * design.DriverCathode);
        var divider = design.DividerTop + design.DividerBottom;
        TapRatio = design.DividerBottom / (divider + source);

        var current = Solve(triode, design, 0, 0);
        var rest = design.Supply - current * design.PlateResistor;
        RestCurrent = current;
        RestGrid = 0;
        RestCathode = current * design.CathodeResistor;
        RestPlateA = rest;
        RestPlateB = driverPlate;

        const double Probe = 1e-6;
        var drawn = design.Supply - (Solve(triode, design, 0, Probe) + Probe) * design.PlateResistor;
        var second = (rest - drawn) / Probe;
        OutputImpedance = (second, 0, 0, source);
        var scale = design.GridLeak / (design.GridLeak + second) * (divider + second) / divider;

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            a[n] = scale * (design.Supply - Solve(triode, design, TapRatio * vd, 0) * design.PlateResistor - rest);
            b[n] = vd;
            // No driven grid: far from conduction, should an input network ever ask.
            gridVoltages[n] = -design.Supply;
            leakReturns[n] = 0;
            cathodeResistances[n] = 0;
        }

        Differentiate();
    }

    /// <summary>
    ///     The second triode's plate current with its grid at <paramref name="grid" /> volts and
    ///     <paramref name="draw" /> amperes drawn from its plate, by bisection.
    /// </summary>
    static double Solve(Triode triode, FixedParaphaseDesign design, double grid, double draw) {
        var (low, high) = (0.0, design.Supply / (design.PlateResistor + design.CathodeResistor));
        for (var n = 0; n < 100; n++) {
            var current = 0.5 * (low + high);
            var cathode = current * design.CathodeResistor;
            var plate = design.Supply - (current + draw) * design.PlateResistor;
            if (OperatingPoint.PlateCurrent(triode, grid - cathode, plate - cathode) > current) {
                low = current;
            } else {
                high = current;
            }
        }

        return 0.5 * (low + high);
    }
}
