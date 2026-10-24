using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A cathodyne driving a push-pull voltage amplifier straight into the output tubes, with no cathode followers (the
///     Marshall Major): the cathodyne's plate and junction (the top of <paramref name="CathodeResistor" />, where its grid
///     leak returns) each AC-coupled into one grid of a triode pair sharing an unbypassed cathode resistor
///     <paramref name="DriverCathode" /> to ground, the pair's grids on <paramref name="DriverGridLeak" /> to ground. The
///     outputs are the pair's plates, RC-coupled to the output tubes' grid networks as a long-tail pair's are.
/// </summary>
/// <param name="Tube">The cathodyne's triode.</param>
/// <param name="Supply">The cathodyne's plate supply.</param>
/// <param name="PlateResistor">The cathodyne's plate load.</param>
/// <param name="CathodeResistor">The cathodyne's cathode load, from the junction to ground.</param>
/// <param name="BiasResistor">From the cathode down to the junction.</param>
/// <param name="DriverTube">The pair's triode.</param>
/// <param name="DriverSupply">The pair's plate supply.</param>
/// <param name="DriverPlate">The pair's plate resistors.</param>
/// <param name="DriverCathode">The pair's shared cathode resistor to ground, unbypassed.</param>
/// <param name="DriverGridLeak">The pair's grid leaks to ground: the AC load on each of the cathodyne's outputs.</param>
public sealed record CathodyneDriverDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeResistor,
    double BiasResistor,
    TriodeModel DriverTube,
    double DriverSupply,
    double DriverPlate,
    double DriverCathode,
    double DriverGridLeak
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     Two differential stages in cascade (the Mesa/Boogie Bass 400): a long-tail pair whose tail returns to a negative
///     supply (<paramref name="TailReturn" />), its first grid driven and its second at ground, both grids' DC at ground;
///     each plate AC-coupled onto <paramref name="CouplingLoad" /> to ground and, through the divider
///     <paramref name="DividerSeries" /> / <paramref name="DividerShunt" />, into one grid of a second pair that shares an
///     unbypassed cathode resistor <paramref name="DriverCathode" /> to ground. The outputs are the second pair's plates.
/// </summary>
/// <param name="Tube">The first pair's triode.</param>
/// <param name="Supply">The first pair's plate supply.</param>
/// <param name="PlateResistor">The first pair's plate resistors.</param>
/// <param name="TailResistor">The first pair's tail, from the joined cathodes to <paramref name="TailReturn" />.</param>
/// <param name="TailReturn">The (negative) supply the tail returns to.</param>
/// <param name="CouplingLoad">From each coupling cap's far side to ground.</param>
/// <param name="DividerSeries">From the coupling cap's far side to the second pair's grid.</param>
/// <param name="DividerShunt">From the second pair's grid to ground.</param>
/// <param name="DriverTube">The second pair's triode.</param>
/// <param name="DriverSupply">The second pair's plate supply.</param>
/// <param name="DriverPlate">The second pair's plate resistors.</param>
/// <param name="DriverCathode">The second pair's shared cathode resistor to ground.</param>
/// <param name="InputRange">The table's span (see <see cref="PhaseInverter.Range" />).</param>
public sealed record DifferentialDriverDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double TailResistor,
    double TailReturn,
    double CouplingLoad,
    double DividerSeries,
    double DividerShunt,
    TriodeModel DriverTube,
    double DriverSupply,
    double DriverPlate,
    double DriverCathode,
    double InputRange = 12
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     A <see cref="CathodyneDriverDesign" />'s table: the pair's plates (AC, open circuit) as functions of the
///     cathodyne's grid voltage (AC), from the exact DC solution of the cathodyne (each output loaded by the pair's grid
///     leak) and then of the pair.
/// </summary>
/// <remarks>
///     As in <see cref="DriverChain" />, the coupling caps between the cathodyne and the pair are shorts for the signal,
///     so the chain is a memoryless map; what that leaves out is their low-frequency roll-off and the pair's grid current,
///     which flows once a grid rises above the pair's shared cathode (deep overdrive) and would charge them. Output A is
///     the plate driven from the junction, so that it falls as the input rises, as every inverter's does.
/// </remarks>
public sealed class CathodyneDriver : PhaseInverter {
    public CathodyneDriverDesign Design { get; }

    /// <summary>The cathodyne's current at rest.</summary>
    public double RestCurrent { get; }

    /// <summary>The cathodyne's plate and junction at rest.</summary>
    public (double Plate, double Junction) RestSplitter { get; }

    /// <summary>The pair's shared cathode at rest.</summary>
    public double RestDriverCathode { get; }

    internal CathodyneDriver(CathodyneDriverDesign design) {
        Design = design;
        var splitter = new Triode(design.Tube);
        var driver = new Triode(design.DriverTube);
        var (rp, rk, rb) = (design.PlateResistor, design.CathodeResistor, design.BiasResistor);

        // The rest point: the grid returns to the junction, so V_gk = -I R_bias.
        var (low, high) = (0.0, design.Supply / (rp + rk + rb));
        for (var n = 0; n < 100; n++) {
            var trial = 0.5 * (low + high);
            var current = OperatingPoint.PlateCurrent(splitter, -trial * rb, design.Supply - trial * (rp + rk + rb));
            (low, high) = current > trial ? (trial, high) : (low, trial);
        }

        RestCurrent = 0.5 * (low + high);
        var restPlate = design.Supply - RestCurrent * rp;
        var restJunction = RestCurrent * rk;
        RestSplitter = (restPlate, restJunction);
        RestGrid = restJunction;
        RestCathode = RestCurrent * (rk + rb);

        var pair = Pair(design);
        var (ia, ib, vk) = LongTailPair.Solve(driver, pair, 0, 0);
        RestDriverCathode = vk;
        RestPlateA = design.DriverSupply - ia * design.DriverPlate;
        RestPlateB = design.DriverSupply - ib * design.DriverPlate;
        OutputImpedance = PairImpedance(driver, pair, 0, 0, RestPlateA, RestPlateB);

        // Each output loaded by a grid leak to its rest voltage, as a Thévenin source.
        var leak = design.DriverGridLeak;
        var plateResistance = Parallel(rp, leak);
        var plateSource = (design.Supply * leak + restPlate * rp) / (rp + leak);
        var junctionResistance = Parallel(rk, leak);
        var junctionSource = restJunction * rk / (rk + leak);
        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var grid = RestGrid + vd;
            var current = Split(splitter, grid, plateSource, plateResistance, junctionSource, junctionResistance, rb);
            var plate = plateSource - current * plateResistance;
            var junction = junctionSource + current * junctionResistance;
            var (pa, pb, _) = LongTailPair.Solve(driver, pair, junction - restJunction, plate - restPlate);
            a[n] = design.DriverSupply - pa * design.DriverPlate - RestPlateA;
            b[n] = design.DriverSupply - pb * design.DriverPlate - RestPlateB;
            gridVoltages[n] = grid - junction - current * rb;
            leakReturns[n] = junction - restJunction;
            cathodeResistances[n] = rb + junctionResistance;
        }

        Differentiate();
    }

    /// <summary>The pair as a long-tail pair whose tail is its shared cathode resistor (no bias resistor).</summary>
    internal static LongTailPairDesign Pair(CathodyneDriverDesign design) =>
        new(design.DriverTube, design.DriverSupply, design.DriverPlate, design.DriverPlate, design.DriverCathode, 0);

    /// <summary>
    ///     The cathodyne's current with its grid at <paramref name="grid" /> volts, its plate on a Thévenin source and its
    ///     junction on another (both outputs loaded): bisection on the current.
    /// </summary>
    static double Split(
        Triode triode,
        double grid,
        double plateSource,
        double plateResistance,
        double junctionSource,
        double junctionResistance,
        double bias
    ) {
        var low = 0.0;
        var high = Math.Max(plateSource - junctionSource, 0) / (plateResistance + junctionResistance + bias);
        for (var n = 0; n < 100; n++) {
            var current = 0.5 * (low + high);
            var cathode = junctionSource + current * (junctionResistance + bias);
            var plate = plateSource - current * plateResistance;
            if (OperatingPoint.PlateCurrent(triode, grid - cathode, plate - cathode) > current) {
                low = current;
            } else {
                high = current;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>
    ///     A pair's output impedance matrix with its grids at <paramref name="gridA" /> / <paramref name="gridB" />: a small
    ///     current drawn from each plate in turn.
    /// </summary>
    internal static (double, double, double, double) PairImpedance(
        Triode triode,
        LongTailPairDesign pair,
        double gridA,
        double gridB,
        double restA,
        double restB
    ) {
        const double Probe = 1e-6;
        var (aa, ba, _) = LongTailPair.Solve(triode, pair, gridA, gridB, Probe);
        var (ab, bb, _) = LongTailPair.Solve(triode, pair, gridA, gridB, 0, Probe);
        return (
            (restA - (pair.Supply - (aa + Probe) * pair.PlateResistorA)) / Probe,
            (restA - (pair.Supply - ab * pair.PlateResistorA)) / Probe,
            (restB - (pair.Supply - ba * pair.PlateResistorB)) / Probe,
            (restB - (pair.Supply - (bb + Probe) * pair.PlateResistorB)) / Probe
        );
    }
}

/// <summary>
///     A <see cref="DifferentialDriverDesign" />'s table: the second pair's plates (AC, open circuit) as functions of the
///     first pair's driven grid (AC), from the exact DC solution of the first pair (each plate loaded by its coupling
///     load and divider) and then of the second.
/// </summary>
/// <remarks>
///     The coupling caps are shorts for the signal, as in the other driver chains; the second pair's grids sit on
///     dividers (a third of the first pair's swing reaches them), so they draw grid current only in deep overdrive, which
///     the table leaves out. Output A is the plate driven from the first pair's undriven side, so that it falls as the
///     input rises.
/// </remarks>
public sealed class DifferentialDriver : PhaseInverter {
    public DifferentialDriverDesign Design { get; }

    /// <summary>The first pair's plates at rest (both alike).</summary>
    public double RestFirstPlate { get; }

    /// <summary>The second pair's shared cathode at rest.</summary>
    public double RestDriverCathode { get; }

    /// <summary>Each triode's plate current at rest, first pair and second.</summary>
    public (double First, double Second) RestCurrents { get; }

    internal DifferentialDriver(DifferentialDriverDesign design) : base(design.InputRange) {
        Design = design;
        var first = new Triode(design.Tube);
        var second = new Triode(design.DriverTube);
        var rp = design.PlateResistor;

        var (ia, ib, vk) = SolvePair(first, 0, 0, design.Supply, rp, design.Supply, rp, design.TailResistor,
            design.TailReturn);
        RestFirstPlate = design.Supply - ia * rp;
        var restFirstB = design.Supply - ib * rp;
        RestGrid = 0;
        RestCathode = vk;

        var (ja, jb, jk) = SolvePair(second, 0, 0, design.DriverSupply, design.DriverPlate, design.DriverSupply,
            design.DriverPlate, design.DriverCathode, 0);
        RestDriverCathode = jk;
        RestCurrents = (ia, ja);

        // Output A is the second pair's triode fed from the first pair's B side (see the remarks).
        RestPlateA = design.DriverSupply - jb * design.DriverPlate;
        RestPlateB = design.DriverSupply - ja * design.DriverPlate;
        const double Probe = 1e-6;
        var withA = SolvePair(second, 0, 0, design.DriverSupply - Probe * design.DriverPlate, design.DriverPlate,
            design.DriverSupply, design.DriverPlate, design.DriverCathode, 0);
        var withB = SolvePair(second, 0, 0, design.DriverSupply, design.DriverPlate,
            design.DriverSupply - Probe * design.DriverPlate, design.DriverPlate, design.DriverCathode, 0);
        // A draw from output A is a draw from the second pair's B plate (and the other way round).
        OutputImpedance = (
            (RestPlateA - (design.DriverSupply - (withB.B + Probe) * design.DriverPlate)) / Probe,
            (RestPlateA - (design.DriverSupply - withA.B * design.DriverPlate)) / Probe,
            (RestPlateB - (design.DriverSupply - withB.A * design.DriverPlate)) / Probe,
            (RestPlateB - (design.DriverSupply - (withA.A + Probe) * design.DriverPlate)) / Probe
        );

        // The first pair's plates loaded by the coupling load beside the divider, to their rest voltages.
        var load = Parallel(design.CouplingLoad, design.DividerSeries + design.DividerShunt);
        var loaded = Parallel(rp, load);
        var sourceA = (design.Supply * load + RestFirstPlate * rp) / (rp + load);
        var sourceB = (design.Supply * load + restFirstB * rp) / (rp + load);
        var ratio = design.DividerShunt / (design.DividerSeries + design.DividerShunt);
        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var (pa, pb, cathode) = SolvePair(first, vd, 0, sourceA, loaded, sourceB, loaded, design.TailResistor,
                design.TailReturn);
            var gridA = (sourceA - pa * loaded - RestFirstPlate) * ratio;
            var gridB = (sourceB - pb * loaded - restFirstB) * ratio;
            var (qa, qb, _) = SolvePair(second, gridA, gridB, design.DriverSupply, design.DriverPlate,
                design.DriverSupply, design.DriverPlate, design.DriverCathode, 0);
            a[n] = design.DriverSupply - qb * design.DriverPlate - RestPlateA;
            b[n] = design.DriverSupply - qa * design.DriverPlate - RestPlateB;
            gridVoltages[n] = vd - cathode;
            leakReturns[n] = 0;
            cathodeResistances[n] = design.TailResistor;
        }

        Differentiate();
    }

    /// <summary>
    ///     A pair with its cathodes joined and returned through <paramref name="tail" /> to <paramref name="tailReturn" />,
    ///     each plate on a Thévenin source (<paramref name="supplyA" /> behind <paramref name="plateA" />, and so on), its
    ///     grids at <paramref name="gridA" /> and <paramref name="gridB" /> (no grid current): the plate currents and the
    ///     cathode, by bisection on the cathode with each triode's current from its own load line.
    /// </summary>
    public static (double A, double B, double Cathode) SolvePair(
        Triode triode,
        double gridA,
        double gridB,
        double supplyA,
        double plateA,
        double supplyB,
        double plateB,
        double tail,
        double tailReturn
    ) {
        var low = tailReturn;
        var high = Math.Max(supplyA, supplyB);
        for (var i = 0; i < 80; i++) {
            var vk = 0.5 * (low + high);
            var ia = LoadLine(triode, gridA - vk, supplyA - vk, plateA);
            var ib = LoadLine(triode, gridB - vk, supplyB - vk, plateB);
            if ((ia + ib) * tail > vk - tailReturn) {
                low = vk;
            } else {
                high = vk;
            }
        }

        var cathode = 0.5 * (low + high);
        return (LoadLine(triode, gridA - cathode, supplyA - cathode, plateA),
            LoadLine(triode, gridB - cathode, supplyB - cathode, plateB), cathode);
    }
}
