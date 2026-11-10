using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     The circuits the Ampeg SVT of 1970 (<see cref="Svt" />) and the SVT-CL of 1994 (<see cref="SvtClassic" />)
///     share: the passive Baxandall and the diode clamp at the power amp's input. The values that differ between the
///     two are the parameters; each model's own file says where its values were read.
/// </summary>
public static class AmpegParts {
    /// <summary>
    ///     The passive Baxandall as a stage of its own (see the other overload); out at the treble wiper.
    /// </summary>
    public static NetworkStageDesign Baxandall(double trebleCap, double trebleFoot, double mix, PotTaper taper) {
        var net = new StageNetlist();
        net.Output = Baxandall(net, trebleCap, trebleFoot, mix, taper);
        return new("Baxandall", net);
    }

    /// <summary>
    ///     The passive Baxandall from <paramref name="net" />'s input: 0.1 µF in, 220k to the bass pot's top and
    ///     <paramref name="trebleCap" /> to the treble pot's, the 1M bass pot with 1 nF and 10 nF from its wiper, 22k
    ///     to ground, <paramref name="trebleFoot" /> from the 1M treble pot's bottom to ground, <paramref name="mix" />
    ///     between the wipers. Returns the treble wiper, its output.
    /// </summary>
    public static int Baxandall(StageNetlist net, double trebleCap, double trebleFoot, double mix, PotTaper taper) {
        var (input, bassTop, bassBottom, trebleTop, trebleBottom) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(net.Input, input, 0.1e-6);
        net.Resistor(input, bassTop, 220e3);
        net.Capacitor(input, trebleTop, trebleCap, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, taper), PotRole.Bass);
        net.Capacitor(bassTop, bass, 1e-9, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 10e-9, CapacitorRole.Stack);
        net.Resistor(bassBottom, StageNetlist.Ground, 22e3);
        net.Capacitor(StageNetlist.Ground, trebleBottom, trebleFoot, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, taper), PotRole.Treble);
        net.Resistor(bass, treble, mix);
        return treble;
    }

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
}
