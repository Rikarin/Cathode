using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A driver chain in place of a single phase inverter (the Ampeg SVT): an input stage that takes the global feedback
///     at its cathode, a cathodyne splitting its output, a voltage amplifier on each side, and a cathode follower on each
///     side DC-coupled to the output tubes' grids. The outputs are the followers' cathodes.
/// </summary>
/// <param name="Tube">The input stage's triode.</param>
/// <param name="Supply">The input stage's plate supply.</param>
/// <param name="InputPlate">The input stage's plate resistor.</param>
/// <param name="InputCathode">Its cathode resistor, unbypassed (the feedback shunt included).</param>
/// <param name="Splitter">
///     The cathodyne. Its outputs are its plate and its cathode (the top of <see cref="CathodyneDesign.BiasResistor" />,
///     not the grid leak's junction as in a <see cref="Cathodyne" />).
/// </param>
/// <param name="DriverTube">The voltage amplifiers' triode (and the followers').</param>
/// <param name="DriverSupply">The voltage amplifiers' plate supply.</param>
/// <param name="DriverPlate">Their plate resistors.</param>
/// <param name="DriverCathode">Their cathode resistors, unbypassed.</param>
/// <param name="FollowerSupply">The followers' plate supply.</param>
/// <param name="FollowerLoad">The followers' cathode resistors, to <paramref name="FollowerReturn" />.</param>
/// <param name="FollowerReturn">The (negative) bias supply the followers' cathode resistors return to.</param>
/// <param name="FollowerGrid">The followers' grids' DC voltage (from the bias network).</param>
/// <param name="InputRange">The table's span (see <see cref="PhaseInverter.Range" />).</param>
public sealed record DriverChainDesign(
    TriodeModel Tube,
    double Supply,
    double InputPlate,
    double InputCathode,
    CathodyneDesign Splitter,
    TriodeModel DriverTube,
    double DriverSupply,
    double DriverPlate,
    double DriverCathode,
    double FollowerSupply,
    double FollowerLoad,
    double FollowerReturn,
    double FollowerGrid,
    double InputRange = 16
) : PhaseInverterDesign(Tube, Supply);

/// <summary>
///     A <see cref="DriverChainDesign" />'s table: the followers' cathode voltages (AC) as functions of the input stage's
///     grid voltage (AC, the feedback subtracted), from the exact DC solution of each triode in turn.
/// </summary>
/// <remarks>
///     <para>
///         The coupling caps between the stages are taken as shorts for the signal (each stage's grid at its own rest
///         voltage plus the previous stage's AC output), so the chain is a memoryless map like the other inverters; what
///         that leaves out is their low-frequency roll-off and the voltage amplifiers' grid current, which charges them
///         under hard drive (blocking). The loads between the stages (the cathodyne's grid leak, the amplifiers' 470k
///         leaks,
///         the followers' grids) are small beside the plate resistors driving them and are left out too.
///     </para>
///     <para>
///         The followers' output impedance is their cathode's small-signal resistance, a few hundred ohms: the grid
///         stoppers into the output tubes' grids dominate. The driven grid's tables (for an
///         <see cref="InverterInputDesign" />) are the input stage's grid-to-cathode voltage at its unbypassed cathode.
///     </para>
/// </remarks>
public sealed class DriverChain : PhaseInverter {
    public DriverChainDesign Design { get; }

    /// <summary>The followers' cathodes at rest (the output tubes' grid bias).</summary>
    public double RestFollower { get; }

    internal DriverChain(DriverChainDesign design) : base(design.InputRange) {
        Design = design;
        var input = new Triode(design.Tube);
        var splitter = new Triode(design.Splitter.Tube);
        var driver = new Triode(design.DriverTube);
        var splitterRest = For(design.Splitter);

        var restInput = InputPlate(input, design, 0, out _);
        var restSplitterGrid = splitterRest.RestGrid;
        var restCurrent = Cathodyne.Solve(splitter, design.Splitter, restSplitterGrid);
        var (restSplitA, restSplitB) = SplitterOutputs(design.Splitter, restCurrent);
        var restDriver = DriverPlate(driver, design, 0);
        RestFollower = Follower(driver, design, design.FollowerGrid, 0);
        RestGrid = 0;
        RestCathode = InputCathodeVoltage(input, design, 0);
        RestPlateA = RestFollower;
        RestPlateB = RestFollower;

        // The followers' output impedance from a small current drawn at rest; the sides are independent.
        const double Probe = 1e-6;
        var z = (RestFollower - Follower(driver, design, design.FollowerGrid, Probe)) / Probe;
        OutputImpedance = (z, 0, 0, z);

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var plate = InputPlate(input, design, vd, out var current);
            var grid = restSplitterGrid + plate - restInput;
            var i = Cathodyne.Solve(splitter, design.Splitter, grid);
            var (splitA, splitB) = SplitterOutputs(design.Splitter, i);
            var driverA = DriverPlate(driver, design, splitA - restSplitA) - restDriver;
            var driverB = DriverPlate(driver, design, splitB - restSplitB) - restDriver;
            a[n] = Follower(driver, design, design.FollowerGrid + driverA, 0) - RestFollower;
            b[n] = Follower(driver, design, design.FollowerGrid + driverB, 0) - RestFollower;
            gridVoltages[n] = vd - current * design.InputCathode;
            leakReturns[n] = 0;
            cathodeResistances[n] = design.InputCathode;
        }

        Differentiate();
    }

    /// <summary>The input stage's plate for grid voltage <paramref name="grid" />, and its current.</summary>
    static double InputPlate(Triode triode, DriverChainDesign design, double grid, out double current) {
        current = Current(triode, grid, design.Supply, design.InputPlate, design.InputCathode);
        return design.Supply - current * design.InputPlate;
    }

    static double InputCathodeVoltage(Triode triode, DriverChainDesign design, double grid) =>
        Current(triode, grid, design.Supply, design.InputPlate, design.InputCathode) * design.InputCathode;

    /// <summary>A voltage amplifier's plate for its grid at <paramref name="grid" /> volts (its leak returns to ground).</summary>
    static double DriverPlate(Triode triode, DriverChainDesign design, double grid) =>
        design.DriverSupply
        - Current(triode, grid, design.DriverSupply, design.DriverPlate, design.DriverCathode) * design.DriverPlate;

    /// <summary>The cathodyne's plate and cathode for its current.</summary>
    static (double Plate, double Cathode) SplitterOutputs(CathodyneDesign design, double current) =>
        (design.Supply - current * design.PlateResistor, current * (design.CathodeResistor + design.BiasResistor));

    /// <summary>
    ///     A common-cathode triode's current with its grid at <paramref name="grid" />: bisection on the current, the
    ///     cathode at I R_k and the plate at the supply less I R_p.
    /// </summary>
    static double Current(Triode triode, double grid, double supply, double plate, double cathode) {
        var low = 0.0;
        var high = supply / (plate + cathode);
        for (var i = 0; i < 100; i++) {
            var current = 0.5 * (low + high);
            var vk = current * cathode;
            if (OperatingPoint.PlateCurrent(triode, grid - vk, supply - current * plate - vk) > current) {
                low = current;
            } else {
                high = current;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>
    ///     A follower's cathode voltage with its grid at <paramref name="grid" /> and <paramref name="draw" /> amperes
    ///     drawn from the cathode: bisection on the cathode voltage between the return supply and the grid's reach.
    /// </summary>
    static double Follower(Triode triode, DriverChainDesign design, double grid, double draw) {
        var low = design.FollowerReturn;
        var high = design.FollowerSupply;
        for (var i = 0; i < 100; i++) {
            var vk = 0.5 * (low + high);
            var current = OperatingPoint.PlateCurrent(triode, grid - vk, design.FollowerSupply - vk);
            if (current > (vk - design.FollowerReturn) / design.FollowerLoad + draw) {
                low = vk;
            } else {
                high = vk;
            }
        }

        return 0.5 * (low + high);
    }
}
