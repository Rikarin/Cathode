namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Circuit parts the Mesa/Boogie drawings repeat from one amp to the next (the Subway Blues, the TriAxis, the Bass
///     400): the Mark series' plate-fed tone stack with its treble-shift cap.
/// </summary>
/// <remarks>
///     The Mark IIC+, Mark IV and Mark V models wire the same stack inline (their own values); this helper is for the
///     models that came after them, so that the topology is written once.
/// </remarks>
static class MesaParts {
    /// <summary>
    ///     The Mark stack driven straight from a plate (no cathode follower): <paramref name="treble" /> from the plate to
    ///     the treble pot's top (<paramref name="shift" /> more while <paramref name="shiftRole" /> is on: Treble Shift,
    ///     Fat), <paramref name="slope" /> to a node S, S through <paramref name="bassCap" /> to the treble pot's foot and
    ///     the bass rheostat, S through <paramref name="middleCap" /> to the bass rheostat's foot and the middle rheostat
    ///     to ground. Returns the treble wiper, S and the treble pot's top.
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
        net.Capacitor(plate, trebleTop, treble, CapacitorRole.Stack);
        if (shiftRole is { } role) {
            net.SwitchedCapacitor(plate, trebleTop, shift, role, CapacitorRole.Stack);
        }

        net.Resistor(plate, node, slope);
        var wiper = net.Pot(trebleTop, trebleFoot, treblePot, PotRole.Treble);
        net.Capacitor(node, trebleFoot, bassCap, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middle, bassPot, PotRole.Bass);
        net.Capacitor(node, middle, middleCap, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, middlePot, PotRole.Middle);
        return (wiper, node, trebleTop);
    }
}
