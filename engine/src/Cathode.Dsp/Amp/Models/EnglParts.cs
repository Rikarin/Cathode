using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     The stages the Engl drawings share (the Savage 120's 610-2, the Powerball's 645-1, the Fireball's 625): the
///     loop's send follower, the return stage with the master, a gain stage's triode and the stack's pots. Each model
///     passes its own drawing's values.
/// </summary>
static class EnglParts {
    /// <summary>
    ///     An ECC83 on <paramref name="grid" />: <paramref name="plateResistor" /> to <paramref name="rail" />,
    ///     <paramref name="cathodeResistor" /> to ground with <paramref name="bypass" /> across it (0: unbypassed).
    ///     Returns the plate.
    /// </summary>
    public static int Triode(
        StageNetlist net,
        int grid,
        double rail,
        double plateResistor,
        double cathodeResistor,
        double bypass
    ) {
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, rail);
        net.Resistor(supply, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        if (bypass > 0) {
            net.Capacitor(cathode, StageNetlist.Ground, bypass);
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     The pots of a stack with one middle pot, from the node after its input cap: <paramref name="slope" /> to
    ///     the slope node; the 250k linear treble from its cap down to the bass's top; <paramref name="bassCap" />
    ///     from the slope to the 1M audio bass rheostat's top; the bass's bottom on the top of the 20k linear middle,
    ///     with <paramref name="middleCap" /> from the slope on its wiper. Returns the treble's and the middle's
    ///     wipers.
    /// </summary>
    public static (int Treble, int Middle) StackPots(
        StageNetlist net,
        (int Coupled, int Slope, int TrebleTop, int BassTop, int MiddleTop) at,
        double slope,
        double bassCap,
        double middleCap
    ) {
        net.Resistor(at.Coupled, at.Slope, slope);
        var treble = net.Pot(at.TrebleTop, at.BassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(at.Slope, at.BassTop, bassCap, CapacitorRole.Stack);
        net.Rheostat(at.BassTop, at.MiddleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(at.MiddleTop, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(at.Slope, middle, middleCap, CapacitorRole.Stack);
        return (treble, middle);
    }

    /// <summary>
    ///     The loop's send follower up to its send cap: <paramref name="series" /> into the grid with
    ///     <paramref name="leak" /> to ground; 100k with <paramref name="plateCap" /> across it to
    ///     <paramref name="rail" />; 1k cathode; <paramref name="sendCap" /> from the cathode to the send, which is
    ///     returned with the netlist.
    /// </summary>
    public static (StageNetlist Net, int Send) SendFollower(
        double rail,
        double series,
        double leak,
        double plateCap,
        double sendCap
    ) {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, send) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, series);
        net.Resistor(grid, StageNetlist.Ground, leak);
        net.Supply(supply, rail);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, plateCap);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, send, sendCap);
        return (net, send);
    }

    /// <summary>
    ///     <see cref="SendFollower" /> into an empty loop with its balance fully dry: 10k from the send into the
    ///     return stage's 100k grid leak.
    /// </summary>
    public static StageNetlist Follower(double rail, double series, double leak, double plateCap, double sendCap) {
        var (net, send) = SendFollower(rail, series, leak, plateCap, sendCap);
        var ret = net.Node();
        net.Resistor(send, ret, 10e3);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Output = ret;
        return net;
    }

    /// <summary>
    ///     The return stage (100k plate, <paramref name="cathode" /> ‖ 22 µF), then 100 nF and
    ///     <paramref name="series" /> into the 1M linear master.
    /// </summary>
    public static StageNetlist ReturnAndMaster(double rail, double cathode, double series) {
        var net = new StageNetlist();
        var (supply, plate, cathodeNode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathodeNode, StageNetlist.Ground, cathode);
        net.Capacitor(cathodeNode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathodeNode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, top, series);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
