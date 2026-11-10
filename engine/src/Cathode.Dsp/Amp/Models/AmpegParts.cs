namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Ampeg models share: the passive Baxandall of the SVT's and the B-15-N's channels, which is one network
///     with other treble caps and, on the B-15-N, a resistor more.
/// </summary>
static class AmpegParts {
    /// <summary>
    ///     A channel's passive Baxandall from <paramref name="from" />: 0.1 µF in, 220k to the 1M bass pot's top and
    ///     <paramref name="trebleCap" /> to the 1M treble pot's top, 1 nF and 10 nF from the bass wiper,
    ///     <paramref name="wiperLoad" /> from the bass wiper to ground where the amp has one (0 for none), 22k to
    ///     ground, <paramref name="footCap" /> to the treble pot's bottom, 120k between the wipers. Returns the treble
    ///     wiper. The tapers are not marked on Piazza's redraws (taken as linear, as a Baxandall's usually are).
    /// </summary>
    public static int Baxandall(
        StageNetlist net,
        int from,
        in StackNodes nodes,
        PotRole bassRole,
        PotRole trebleRole,
        double trebleCap,
        double footCap,
        double wiperLoad = 0
    ) {
        var (input, bassTop, bassBottom, trebleTop, trebleBottom) = nodes;
        net.Capacitor(from, input, 0.1e-6);
        net.Resistor(input, bassTop, 220e3);
        net.Capacitor(input, trebleTop, trebleCap, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, PotTaper.Linear), bassRole);
        net.Capacitor(bassTop, bass, 1e-9, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 10e-9, CapacitorRole.Stack);
        if (wiperLoad > 0) {
            net.Resistor(bass, StageNetlist.Ground, wiperLoad);
        }

        net.Resistor(bassBottom, StageNetlist.Ground, 22e3);
        net.Capacitor(StageNetlist.Ground, trebleBottom, footCap, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Linear), trebleRole);
        net.Resistor(bass, treble, 120e3);
        return treble;
    }

    /// <summary>
    ///     A Baxandall's inner nodes, made on their own (<see cref="In" />) so a stage can make them before its other
    ///     nodes: the one-channel models' netlists keep the node order they always had, and with it their renders.
    /// </summary>
    public readonly record struct StackNodes(int Input, int BassTop, int BassBottom, int TrebleTop, int TrebleBottom) {
        public static StackNodes In(StageNetlist net) =>
            new(net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
    }
}
