using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Ampeg models share: the passive Baxandall of the SVT's (<see cref="Svt" />), the SVT-CL's
///     (<see cref="SvtClassic" />) and the B-15-N's channels, which is one network with other treble caps, tapers and
///     mixing resistor and, on the B-15-N, a resistor more; and the diode clamp at the two SVTs' power amp input. The
///     values that differ are the parameters; each model's own file says where its values were read.
/// </summary>
static class AmpegParts {
    /// <summary>
    ///     A channel's passive Baxandall from <paramref name="from" />: 0.1 µF in, 220k to the 1M bass pot's top and
    ///     <paramref name="trebleCap" /> to the 1M treble pot's top, 1 nF and 10 nF from the bass wiper,
    ///     <paramref name="wiperLoad" /> from the bass wiper to ground where the amp has one (0 for none), 22k to
    ///     ground, <paramref name="footCap" /> to the treble pot's bottom, <paramref name="mix" /> (120k on the 1970
    ///     amps) between the wipers. Returns the treble wiper. The tapers are not marked on Piazza's redraws (taken as
    ///     linear, as a Baxandall's usually are); the SVT-CL's are <paramref name="taper" />.
    /// </summary>
    public static int Baxandall(
        StageNetlist net,
        int from,
        in StackNodes nodes,
        PotRole bassRole,
        PotRole trebleRole,
        double trebleCap,
        double footCap,
        double wiperLoad = 0,
        double mix = 120e3,
        PotTaper taper = PotTaper.Linear
    ) {
        var (input, bassTop, bassBottom, trebleTop, trebleBottom) = nodes;
        net.Capacitor(from, input, 0.1e-6);
        net.Resistor(input, bassTop, 220e3);
        net.Capacitor(input, trebleTop, trebleCap, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, taper), bassRole);
        net.Capacitor(bassTop, bass, 1e-9, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 10e-9, CapacitorRole.Stack);
        if (wiperLoad > 0) {
            net.Resistor(bass, StageNetlist.Ground, wiperLoad);
        }

        net.Resistor(bassBottom, StageNetlist.Ground, 22e3);
        net.Capacitor(StageNetlist.Ground, trebleBottom, footCap, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, taper), trebleRole);
        net.Resistor(bass, treble, mix);
        return treble;
    }

    /// <summary>
    ///     The same Baxandall from <paramref name="net" />'s input on the Bass and Treble knobs, its inner nodes made
    ///     here: <paramref name="trebleCap" /> to the treble pot's top, <paramref name="trebleFoot" /> from its bottom
    ///     to ground, <paramref name="mix" /> between the wipers. Returns the treble wiper, its output.
    /// </summary>
    public static int InputBaxandall(
        StageNetlist net,
        double trebleCap,
        double trebleFoot,
        double mix,
        PotTaper taper
    ) =>
        Baxandall(
            net,
            net.Input,
            StackNodes.In(net),
            PotRole.Bass,
            PotRole.Treble,
            trebleCap,
            trebleFoot,
            0,
            mix,
            taper
        );

    /// <summary>
    ///     The last follower's 0.1 µF out with <paramref name="load" /> to ground, and the power amp's input:
    ///     <paramref name="series" /> into antiparallel silicon diodes to ground and its 470k leak. Out at the power
    ///     amp's first grid.
    /// </summary>
    public static NetworkStageDesign Clamp(double load, double series, double tolerance) {
        var net = new StageNetlist();
        var (output, clamp) = (net.Node(), net.Node());
        net.Capacitor(net.Input, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, load);
        net.Resistor(output, clamp, series);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Silicon, 1, 1);
        net.Resistor(clamp, StageNetlist.Ground, 470e3);
        net.Output = clamp;
        return new("Clamp", net) { Tolerance = tolerance };
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
