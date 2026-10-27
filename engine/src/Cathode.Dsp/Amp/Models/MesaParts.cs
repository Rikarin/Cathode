using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Circuit parts the Mesa/Boogie drawings repeat from one amp to the next: the Mark series' plate-fed tone stack with
///     its treble-shift cap (the Mark IIC+, IV and V, the Subway Blues, the TriAxis, the Bass 400), and the Mark lead
///     cascade (the Mark IIC+, IV and V, and the TriAxis's Lead 2 overdrive).
/// </summary>
/// <remarks>
///     The lead cascade is the same three steps, value for value, on every Mark lead circuit: the drive stage (47 nF
///     with 100k to ground, the feed resistor into the drive, 470k at the drive's wiper, 120 pF from it to the cathode;
///     82k plate, 1.5k ‖ 2.2 µF: <see cref="LeadDrive" />), the lead stage (270k into 68k ‖ 1 nF at the grid; 270k
///     plate; 3.3k with 0.22 µF: <see cref="LeadStage" />) and the divider back into the return stage (47 nF, 220k ‖
///     250 pF into the grid's shunt: <see cref="LeadDivider" />). The Music Electronics Forum thread on the 120 pF
///     grid-to-cathode cap and docs/06 §3.4 describe it. The models differ only in the feed into the drive and in the
///     switch on the 0.22 µF (the parameters); each model's file cites its own drawing. A value that one drawing reads
///     differently (the TriAxis's hand-corrected copy of its sheet, for one) is a parameter of that model, and a
///     correction to the shared values is made here once.
/// </remarks>
static class MesaParts {
    /// <summary>
    ///     The Mark stack driven straight from a plate (no cathode follower): <paramref name="treble" /> from the plate to
    ///     the treble pot's top (<paramref name="shift" /> more while <paramref name="shiftRole" /> is on: Treble Shift,
    ///     Fat), <paramref name="slope" /> to a node S, S through <paramref name="bassCap" /> to the treble pot's foot and
    ///     the bass rheostat, S through <paramref name="middleCap" /> to the bass rheostat's foot and the middle
    ///     rheostat to ground. Returns the treble wiper, S and the treble pot's top.
    /// </summary>
    public static (int Wiper, int Slope, int TrebleTop) Stack(
        StageNetlist net,
        int plate,
        double treble,
        double shift,
        SwitchRole? shiftRole,
        double slope,
        double bassCap,
        double middleCap,
        Pot treblePot,
        Pot bassPot,
        Pot middlePot
    ) {
        var (trebleTop, trebleFoot, node, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        var wiper = Stack(
            net,
            plate,
            trebleTop,
            trebleFoot,
            node,
            middle,
            treble,
            shift,
            shiftRole,
            slope,
            bassCap,
            middleCap,
            treblePot,
            bassPot,
            middlePot
        );
        return (wiper, node, trebleTop);
    }

    /// <summary>
    ///     <see cref="Stack(StageNetlist, int, double, double, SwitchRole?, double, double, double, Pot, Pot, Pot)" />
    ///     on nodes the caller has made (the Mark IIC+, IV and V number theirs in their own order): the treble pot's
    ///     top and foot, the slope node S and the bass rheostat's foot. Returns the treble wiper.
    /// </summary>
    public static int Stack(
        StageNetlist net,
        int plate,
        int trebleTop,
        int trebleFoot,
        int slopeNode,
        int middle,
        double treble,
        double shift,
        SwitchRole? shiftRole,
        double slope,
        double bassCap,
        double middleCap,
        Pot treblePot,
        Pot bassPot,
        Pot middlePot
    ) {
        net.Capacitor(plate, trebleTop, treble, CapacitorRole.Stack);
        if (shiftRole is { } role) {
            net.SwitchedCapacitor(plate, trebleTop, shift, role, CapacitorRole.Stack);
        }

        net.Resistor(plate, slopeNode, slope);
        var wiper = net.Pot(trebleTop, trebleFoot, treblePot, PotRole.Treble);
        net.Capacitor(slopeNode, trebleFoot, bassCap, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middle, bassPot, PotRole.Bass);
        net.Capacitor(slopeNode, middle, middleCap, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, middlePot, PotRole.Middle);
        return wiper;
    }

    /// <summary>
    ///     The Mark lead drive stage: 47 nF from <paramref name="input" /> with 100k to ground (the rhythm channel's
    ///     load), <paramref name="feedCap" /> (0: none; the IIC+ and IV's 20 nF) and <paramref name="feed" /> into the
    ///     drive (<paramref name="drivePot" /> on <paramref name="driveRole" />): a pot to ground with the grid on its
    ///     wiper, or, with <paramref name="shunt" />, a rheostat to ground at the feed resistor's foot (the TriAxis's
    ///     Lead 2 drive LDR); <paramref name="gridLeak" /> from there to ground, <paramref name="gridStopper" /> (0:
    ///     none) to the grid, <paramref name="gridCathode" /> from there to the cathode; an ECC83 with 82k from a
    ///     <paramref name="supply" />-volt rail, 1.5k ‖ 2.2 µF. Returns the plate.
    /// </summary>
    public static int LeadDrive(
        StageNetlist net,
        int input,
        double feed,
        Pot drivePot,
        PotRole driveRole,
        double supply,
        double feedCap = 0,
        bool shunt = false,
        double gridLeak = 470e3,
        double gridStopper = 0,
        double gridCathode = 120e-12
    ) {
        var coupled = net.Node();
        var lead = feedCap > 0 ? net.Node() : coupled;
        var top = net.Node();
        var grid = gridStopper > 0 ? net.Node() : 0;
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        if (feedCap > 0) {
            net.Capacitor(coupled, lead, feedCap);
        }

        net.Resistor(lead, top, feed);
        int drive;
        if (shunt) {
            drive = top;
            net.Rheostat(drive, StageNetlist.Ground, drivePot, driveRole);
        } else {
            drive = net.Pot(top, StageNetlist.Ground, drivePot, driveRole);
        }

        net.Resistor(drive, StageNetlist.Ground, gridLeak);
        if (gridStopper > 0) {
            net.Resistor(drive, grid, gridStopper);
        } else {
            grid = drive;
        }

        net.Capacitor(drive, cathode, gridCathode);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 82e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     The Mark lead stage after the drive: <paramref name="couplingCap" /> from <paramref name="input" />, 270k
    ///     into 68k ‖ 1 nF, <paramref name="gridStopper" /> (0: none) to the grid, <paramref name="gridCathode" /> (0:
    ///     none) from the grid to the cathode; an ECC83 with 270k from a <paramref name="supply" />-volt rail,
    ///     <paramref name="plateCap" /> (0: none) across it; 3.3k with 0.22 µF: fixed without a
    ///     <paramref name="bypassSwitch" />, through <paramref name="seriesResistor" /> to ground that the switch
    ///     shorts with one, or, with a switch and no series resistor, connected only while the switch is on. Returns
    ///     the plate.
    /// </summary>
    public static int LeadStage(
        StageNetlist net,
        int input,
        double supply,
        SwitchRole? bypassSwitch,
        double seriesResistor,
        double couplingCap = 22e-9,
        double gridStopper = 0,
        double gridCathode = 0,
        double plateCap = 0
    ) {
        var (coupled, divider) = (net.Node(), net.Node());
        var grid = gridStopper > 0 ? net.Node() : divider;
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        var bypass = bypassSwitch is not null && seriesResistor > 0 ? net.Node() : 0;
        net.Capacitor(input, coupled, couplingCap);
        net.Resistor(coupled, divider, 270e3);
        net.Resistor(divider, StageNetlist.Ground, 68e3);
        net.Capacitor(divider, StageNetlist.Ground, 1e-9);
        if (gridStopper > 0) {
            net.Resistor(divider, grid, gridStopper);
        }

        if (gridCathode > 0) {
            net.Capacitor(grid, cathode, gridCathode);
        }

        net.Supply(rail, supply);
        net.Resistor(rail, plate, 270e3);
        if (plateCap > 0) {
            net.Capacitor(rail, plate, plateCap);
        }

        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        switch (bypassSwitch) {
            case { } role when seriesResistor > 0:
                net.Capacitor(cathode, bypass, 0.22e-6);
                net.SwitchedResistor(bypass, StageNetlist.Ground, seriesResistor, 1, role);
                break;
            case { } role:
                net.SwitchedCapacitor(cathode, StageNetlist.Ground, 0.22e-6, role);
                break;
            default:
                net.Capacitor(cathode, StageNetlist.Ground, 0.22e-6);
                break;
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     The divider from the lead stage back into the return stage: 47 nF from <paramref name="input" /> to
    ///     <paramref name="coupled" />, 220k ‖ 250 pF to <paramref name="grid" />, <paramref name="shunt" /> ‖
    ///     <paramref name="shuntCap" /> from there to ground (100k ‖ 500 pF, with the return stage's own grid leak and
    ///     47 pF where the model folds them in). The nodes are the caller's.
    /// </summary>
    public static void LeadDivider(StageNetlist net, int input, int coupled, int grid, double shunt, double shuntCap) {
        net.Capacitor(input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Capacitor(coupled, grid, 250e-12);
        net.Resistor(grid, StageNetlist.Ground, shunt);
        net.Capacitor(grid, StageNetlist.Ground, shuntCap);
    }
}
