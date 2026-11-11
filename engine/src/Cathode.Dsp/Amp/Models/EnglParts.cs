using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the Engl models share. The Powerball's and the Savage 120's power amps on their factory drawings (645-1
///     and 610-2): two power tubes a side in fixed bias, 2.2k stoppers, 47 nF from a long-tail pair with 82k and 100k
///     plate resistors and 470 Ω under its cathodes, 100 nF into its first grid with 100k to the bias junction, and a
///     closed 4x12. And the stages those two drawings and the Fireball's (625) share: the loop's send follower, the
///     return stage with the master and the stack's pots (a gain stage's triode is
///     <see cref="TriodeParts.CommonCathode" />). Each model passes its own drawing's values.
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
    ///     The pots of a stack with one middle pot, from the node after its input cap: <paramref name="slope" /> to
    ///     the slope node; the <paramref name="treble" /> linear treble (250k unless given) from its cap down to the
    ///     bass's top; <paramref name="bassCap" /> from the slope to the 1M audio bass rheostat's top; the bass's
    ///     bottom on the top of the <paramref name="middlePot" /> linear middle (20k unless given), with
    ///     <paramref name="middleCap" /> from the slope on its wiper. Returns the treble's and the middle's wipers.
    /// </summary>
    public static (int Treble, int Middle) StackPots(
        StageNetlist net,
        (int Coupled, int Slope, int TrebleTop, int BassTop, int MiddleTop) at,
        double slope,
        double bassCap,
        double middleCap,
        double treble = 250e3,
        double middlePot = 20e3
    ) {
        net.Resistor(at.Coupled, at.Slope, slope);
        var trebleWiper = net.Pot(at.TrebleTop, at.BassTop, new(treble, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(at.Slope, at.BassTop, bassCap, CapacitorRole.Stack);
        net.Rheostat(at.BassTop, at.MiddleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(at.MiddleTop, StageNetlist.Ground, new(middlePot, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(at.Slope, middle, middleCap, CapacitorRole.Stack);
        return (trebleWiper, middle);
    }

    /// <summary>
    ///     A gain pot behind its high-pass, on <paramref name="net" />: <paramref name="cap" /> from the coupled node
    ///     to the cut one (a second one across it with Fat), and <paramref name="shunt" /> from there to ground; 100k
    ///     on to the top, the 1M audio gain's; with Bright, 100 pF to the wiper from the top or, with
    ///     <paramref name="brightFromCut" />, from the cut node; <paramref name="wiperLoad" /> from the wiper to
    ///     ground. Returns the wiper.
    /// </summary>
    public static int Gain(
        StageNetlist net,
        (int Coupled, int Cut, int Top) at,
        double cap,
        double shunt,
        bool brightFromCut,
        double wiperLoad
    ) {
        net.Capacitor(at.Coupled, at.Cut, cap);
        net.SwitchedCapacitor(at.Coupled, at.Cut, cap, SwitchRole.Fat);
        net.Resistor(at.Cut, StageNetlist.Ground, shunt);
        net.Resistor(at.Cut, at.Top, 100e3);
        var wiper = net.Pot(at.Top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(brightFromCut ? at.Cut : at.Top, wiper, 100e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(wiper, StageNetlist.Ground, wiperLoad);
        return wiper;
    }

    /// <summary>
    ///     A divider behind a series cap, on <paramref name="net" />: <paramref name="cap" /> from the node it is fed
    ///     from to its top, <paramref name="series" /> on to its tap, and <paramref name="shunt" /> with
    ///     <paramref name="shuntCap" /> across it from there to ground (the Powerball's Lo and Hi networks, the
    ///     Fireball's Ultra one).
    /// </summary>
    public static void Divider(
        StageNetlist net,
        (int From, int Top, int Tap) at,
        double cap,
        double series,
        double shunt,
        double shuntCap
    ) {
        net.Capacitor(at.From, at.Top, cap);
        net.Resistor(at.Top, at.Tap, series);
        net.Resistor(at.Tap, StageNetlist.Ground, shunt);
        net.Capacitor(at.Tap, StageNetlist.Ground, shuntCap);
    }

    /// <summary>
    ///     The Clean divider, on <paramref name="net" />: 1M with 100 pF across it from <paramref name="from" /> to
    ///     <paramref name="tap" />, 22k from there to ground.
    /// </summary>
    public static void CleanDivider(StageNetlist net, int from, int tap) {
        net.Resistor(from, tap, 1e6);
        net.Capacitor(from, tap, 100e-12);
        net.Resistor(tap, StageNetlist.Ground, 22e3);
    }

    /// <summary>
    ///     The cold clipper, on <paramref name="net" />: a 12AX7 on <paramref name="at" />'s grid with 330k and 1 nF
    ///     across it from <paramref name="rail" />, 3.3k with <paramref name="bypass" /> across it under the cathode.
    ///     Returns the plate.
    /// </summary>
    public static int ColdClipper(
        StageNetlist net,
        (int Grid, int Supply, int Plate, int Cathode) at,
        double rail,
        double bypass
    ) {
        net.Supply(at.Supply, rail);
        net.Resistor(at.Supply, at.Plate, 330e3);
        net.Capacitor(at.Supply, at.Plate, 1e-9, CapacitorRole.Plate);
        net.CathodeResistor(at.Cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(at.Cathode, StageNetlist.Ground, bypass);
        net.Triode(at.Plate, at.Grid, at.Cathode, TriodeModel.Ecc83);
        return at.Plate;
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
