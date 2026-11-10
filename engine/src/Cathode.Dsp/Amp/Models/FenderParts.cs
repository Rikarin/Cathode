namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Circuit parts of the later Fender-style combos: the plate-fed tone stack with the middle cap on the middle
///     pot's wiper (the Peavey Classic 30; the Blues Junior's stack is the same circuit, still written out in its own
///     file).
/// </summary>
static class FenderParts {
    /// <summary>
    ///     The stack driven from <paramref name="source" /> (a plate, or the node behind its coupling cap):
    ///     <paramref name="trebleCap" /> to the treble pot's top, <paramref name="slope" /> to a node S, S through
    ///     <paramref name="bassCap" /> to the treble pot's foot and the bass rheostat, the middle pot from the
    ///     rheostat's foot to ground, and S through <paramref name="middleCap" /> to the middle pot's wiper. Returns
    ///     the treble wiper and the treble pot's top.
    /// </summary>
    public static (int Wiper, int TrebleTop) MidWiperStack(
        StageNetlist net,
        int source,
        double trebleCap,
        double slope,
        double bassCap,
        double middleCap,
        Pot treblePot,
        Pot bassPot,
        Pot middlePot
    ) {
        var (top, bottom, node, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(source, top, trebleCap, CapacitorRole.Stack);
        net.Resistor(source, node, slope);
        net.Capacitor(node, bottom, bassCap, CapacitorRole.Stack);
        var wiper = net.Pot(top, bottom, treblePot, PotRole.Treble);
        net.Rheostat(bottom, middle, bassPot, PotRole.Bass);
        var mid = net.Pot(middle, StageNetlist.Ground, middlePot, PotRole.Middle);
        net.Capacitor(node, mid, middleCap, CapacitorRole.Stack);
        return (wiper, top);
    }
}
