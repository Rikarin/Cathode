using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Engl models share. The Powerball's and the Savage 120's power amps on their factory drawings (645-1
///     and 610-2): two power tubes a side in fixed bias, 2.2k stoppers, 47 nF from a long-tail pair with 82k and 100k
///     plate resistors and 470 Ω under its cathodes, 100 nF into its first grid with 100k to the bias junction, and a
///     closed 4x12. And the stages those two drawings and the Fireball's (625) share: the loop's send follower, the
///     return stage with the master, a gain stage's triode and the stack's pots. Each model passes its own drawing's
///     values.
/// </summary>
static class EnglParts {
    /// <summary>
    ///     The output stage: its label and tube, B+ in volts, the screen resistors, the idle current per tube in
    ///     amperes, the rated power in watts and the transformer's plate-to-plate primary in ohms.
    /// </summary>
    public readonly record struct Output(
        string Name,
        PentodeModel Tube,
        double Supply,
        double ScreenResistor,
        double IdleCurrent,
        double RatedPower,
        double Primary
    );

    /// <summary>
    ///     The inverter: its rail in volts, the whole path from its bias junction to ground (the tail resistor and the
    ///     feedback shunt under it), and the power tubes' bias feed resistors.
    /// </summary>
    public readonly record struct Inverter(double Rail, double Tail, double BiasFeed);

    /// <summary>The power amp of <paramref name="output" /> behind <paramref name="inverter" />.</summary>
    public static PowerAmpDesign Power(
        Output output,
        Inverter inverter,
        FeedbackDesign feedback,
        PowerSupplyDesign supply
    ) =>
        new(
            output.Name,
            output.Tube,
            2,
            output.Supply,
            output.ScreenResistor,
            output.IdleCurrent,
            false,
            0,
            output.RatedPower,
            new LongTailPairDesign(TriodeModel.Ecc83, inverter.Rail, 82e3, 100e3, inverter.Tail, 470),
            47e-9,
            inverter.BiasFeed,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            feedback,
            true,
            new(output.Primary),
            SpeakerDesign.Closed4x12,
            supply,
            InverterInput: new(100e-9, 100e3)
        );

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
