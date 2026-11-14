using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark I (the 1970s Boogie, 100 W), the four-stage preamp the drawing names: Input 1 (1M to ground)
///     into V1 (100k, 1.5k ‖ 22 µF), 0.01 µF onto the 1M Volume 1, V2 (100k, 1.5k ‖ 22 µF) with the Fender-topology
///     stack on its plate (250 pF treble cap, 100k slope, 0.1 µF bass, 0.047 µF middle; 250k treble, 250k bass, 10k
///     middle, whose foot the Gain Boost footswitch lifts), 100k from the treble wiper into V3 (100k, 1.5k ‖ 22 µF),
///     its plate onto the 1M Volume 2 with 110 pF across its upper half, V4 (100k, 820 Ω ‖ 22 µF, 820k grid leak),
///     0.047 µF and 220k onto the 1M Master; from there 0.001 µF into the 12AX7 long-tail pair (82k/100k, 430 Ω,
///     1M grid leaks) and four 6L6 at 450 V in fixed bias (−52 V, 220k leaks, 2.2k stoppers, 470 Ω screens), with
///     56k of feedback from the 8 Ω tap into 10k and the 10k Presence with 0.1 µF across it.
/// </summary>
/// <remarks>
///     <para>
///         Source: Mesa's own hand-drawn "MARK I" sheet (schematicheaven.net <c>boogie_marki.pdf</c>), which carries
///         the whole amp on one page: the four-stage preamp with its part values and three printed plate voltages
///         (115 V at V1 and V2, 195 V at V4), the driver and output section, the feedback and presence network, the
///         graphic equaliser, the optional reverb, and the supply's four nodes A (450 V, after the standby switch
///         and the reservoir), B (the screens, through the choke), C (6.6k down, the driver's 295 V) and D (10k
///         further down, the preamp's 260 V). Every value above is read from it.
///     </para>
///     <para>
///         Read with doubt, because the sheet is a freehand sketch: the driver's tail resistor and the second grid's
///         return are drawn as a knot of three resistors around the feedback node, of which only the 1M between the
///         grids and the 430 Ω at the cathodes are legible; the tail is taken as 10k, the value this Fender-derived
///         driver carries on every later Boogie sheet that prints it. The 15k the sheet draws between the feedback
///         shunt and that knot carries no DC and is left out. The sheet writes "1N5303 w/12AX7, 1.5K ¼ watt
///         resistors" beside V1's and V2's cathodes, read as the two alternatives Mesa built (a current-regulator
///         diode or a plain cathode resistor); the resistor version is modelled.
///     </para>
///     <para>
///         Inferred: the coupling cap between V3's plate and Volume 2. The sheet draws the plate line running
///         straight into the pot's top with only the 110 pF bright cap on it, which cannot be the coupling cap (a
///         110 pF into 1M would cut everything below 1.4 kHz), so 0.047 µF is used, the value the same sheet gives
///         V4's output cap. Which way the Gain Boost footswitch works follows from what it is wired across: it sits
///         between the middle pot's foot and ground, so the boost is the contact open, the stack's shunt leg
///         lifted.
///     </para>
///     <para>
///         Guessed, as the sheet prints none of them: the idle current (35 mA a tube at the drawn −52 V), the
///         output transformer's primary (2.1k plate to plate for four 6L6 at 450 V), the pot tapers (audio for the
///         three volumes, linear for treble and middle, audio for bass, as the later Mark sheets print them) and
///         the supply's source resistance. Not modelled: the five-band graphic equaliser (an option on this amp, and
///         the later Marks' is a stage of its own), the optional reverb and its 220k/330k feed, the 60/100 W switch
///         (which grounds only one pair of cathodes), the second input and the Fetron variant the sheet notes.
///     </para>
///     <para>
///         Panel: Gain is Volume 1, Control 1 Volume 2, Master the master, and <b>Gain Boost</b> the footswitch that
///         lifts the middle pot's foot. The amp has no bright switch, depth or cut.
///     </para>
/// </remarks>
public static class MarkI {
    /// <summary>The preamp's node D, as the sheet prints it.</summary>
    const double RailD = 260;

    /// <summary>The driver's node C, as the sheet prints it.</summary>
    const double RailC = 295;

    public static AmpDesign Design { get; } = new(
        "Mark I",
        [
            new NetworkStageDesign("V1", InputStage()),
            new NetworkStageDesign("V2", StackStage()),
            new NetworkStageDesign("V3", SecondVolume()),
            new NetworkStageDesign("V4", Recovery())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            450,
            470,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, RailC, 82e3, 100e3, 10e3, 430),
            0.1e-6,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 56e3, 10e3, new(10e3, PotTaper.Linear), 0.1e-6),
            true,
            new(2.1e3),
            SpeakerDesign.Closed4x12,
            // The screens through the sheet's 6.6k and 10k to the preamp's 260 V: 11 mA at the drawn voltages.
            new(30, 60e-6, 100, 50e-6, 16.6e3, 30e-6, 11e-3, RectifierModel.Silicon),
            InverterInput: new(1e-9, 1e6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume 1",
            Control1 = "Volume 2",
            Bright = null,
            Depth = null,
            Cut = null,
            Switch1 = "Gain Boost"
        }
    };

    /// <summary>
    ///     Input 1 (1M to ground) into V1 (100k, 1.5k ‖ 22 µF), 0.01 µF onto the 1M audio Volume 1. Out at its wiper.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, RailD);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 0.01e-6);
        net.Output = net.Pot(coupled, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     V2 (100k, 1.5k ‖ 22 µF) with the stack on its plate, the Gain Boost switch lifting the middle pot's foot,
    ///     and 100k from the treble wiper to V3's grid. Out there.
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailD);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (trebleTop, trebleFoot, slope, middle, foot, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, trebleTop, 250e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleFoot, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 0.047e-6, CapacitorRole.Stack);
        net.Rheostat(middle, foot, new(10e3, PotTaper.Linear), PotRole.Middle);
        net.SwitchedResistor(foot, StageNetlist.Ground, 1, 1e9, SwitchRole.Switch1);
        net.Resistor(treble, grid, 100e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V3 (100k, 1.5k ‖ 22 µF), 0.047 µF onto the 1M audio Volume 2 with 110 pF across its upper half. Out at
    ///     its wiper.
    /// </summary>
    static StageNetlist SecondVolume() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailD);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 0.047e-6);
        var wiper = net.Pot(coupled, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Capacitor(coupled, wiper, 110e-12, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V4 (820k grid leak, 100k, 820 Ω ‖ 22 µF), 0.047 µF and 220k onto the 1M audio Master. Out at its wiper,
    ///     which drives the inverter's 0.001 µF.
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 820e3);
        net.Supply(rail, RailD);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 0.047e-6);
        net.Resistor(coupled, top, 220e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
