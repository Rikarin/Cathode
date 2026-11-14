using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Laney VH100R, both channels. Overdrive: the jack's 68k into V1A (100k, 2k7 ‖ 680 nF, 47 pF from the plate to
///     the cathode), 4.7 nF and 4.7 nF with 1M and 220k to ground onto the 1M audio Drive, its wiper through 68k to
///     V1B (100k, 2k7 ‖ 680 nF), 22 nF and 68k into the V2A cold clipper (100k, 10k unbypassed), 22 nF and a
///     470k ‖ 470 pF / 470k divider into V2B (100k, 820 Ω unbypassed), which is DC-coupled to the V5B follower
///     (100k); the overdrive stack on its cathode (470 pF, 33k slope, 220 nF bass, 22 nF middle; 220k linear
///     treble, 1M audio bass, 22k linear middle) and the 1M audio Master Volume through 680k. Clean: the same jack
///     into V3A (100k, 1k5 ‖ 2.2 µF), its own stack on the plate (220 pF ‖ 47 pF, 100k slope, 100 nF bass, 47 nF
///     middle; 220k linear treble, 100k audio bass, 22k linear middle), the Bright switch's 100 pF across the 1M
///     audio Clean Volume, 68k into V3B (100k, 820 Ω unbypassed), and on to the same follower and master with the
///     overdrive stack relayed out. Then the ECC83 long-tail pair (82k/100k, 470 Ω, 10k tail, 1M grid resistors,
///     47 pF between the plates) and four 5881 on 1k5 screens, with the 22k Presence and 100 nF at the tail's foot.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Laney's drawings on schematicheaven.net (<c>laney_vh100.pdf</c>): 2385 issue 6, "VH100R
///         overdrive &amp; output diagram" (26 April 1995), and 2386 issue 4, "VC50/VC100/VH100R clean &amp; FX
///         circuit" (14 May 1995). Every value above is read from them, as are the printed plate voltages (V1A
///         206 V, V1B 211 V, V2A 253 V, V2B 168 V, V5B 320 V, the inverter's 230 V and 220 V on a 350 V rail, and
///         the clean channel's V3A 215 V and V3B 180 V), the −44 V bias the sheet gives for 5881 (−35 V for EL34),
///         the 5k6 grid stoppers, 220k grid leaks, 1k5 screen resistors and the TX6011 transformer's 16, 8 and
///         4 Ω taps.
///     </para>
///     <para>
///         Inferred: that the channel relay RLY2, which the overdrive sheet draws at the tone stack, takes that
///         stack out on the clean channel — the clean channel has a full stack of its own on the other sheet, and
///         the two would otherwise be in series. Also that the clean channel joins the overdrive one at the V5B
///         follower, the only point the two sheets share.
///     </para>
///     <para>
///         Guessed, as neither sheet prints them: B+ (480 V) and the idle current (35 mA a tube at the drawn
///         −44 V), the output transformer's primary (1.9k plate to plate for four 5881 at that B+), the feedback
///         resistor (R58's 100k, read from the 8 Ω tap: the sheet draws the network but not which tap feeds it)
///         and the preamp supply's draw, set so that the 320 V node the sheet prints comes out of the drawn 10k
///         dropper. The two further 10k 4 W droppers below it put V1B and V2A on 310 V and V1A on 300 V, the rails
///         the printed plate voltages imply. Simplified: the reverb and the effects loop are left out (the loop's
///         op-amps are on a third sheet and come to about unity), as are the footswitched Resonance pads across
///         the feedback shunt and the Drive switch's relay; the 5881 is modelled as the 6L6GC of the tube table.
///     </para>
///     <para>
///         Panel: Gain is the Drive (the Clean channel has no gain control of its own; its Clean Volume is
///         Control 1), Master is the shared Master Volume, Bass, Middle and Treble are the playing channel's own
///         (each channel has its own set on the amp), Bright is the clean channel's switch, and <b>Clean Ch</b>
///         selects it.
///     </para>
/// </remarks>
public static class LaneyVH100R {
    /// <summary>The sheet's 320 V node: V2B, the V5B follower and the clean channel.</summary>
    const double RailTop = 320;

    /// <summary>Below the first 10k: V1B and V2A.</summary>
    const double RailTwo = 310;

    /// <summary>Below the second: V1A.</summary>
    const double RailOne = 300;

    public static AmpDesign Design { get; } = new(
        "VH100R",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1A", InputStage(), 68e3) { SupplyNode = 2 },
                    new NetworkStageDesign("V1B", SecondStage()) { SupplyNode = 1 },
                    new NetworkStageDesign("V2A", ColdClipper()) { SupplyNode = 1 },
                    new NetworkStageDesign("V2B", FollowerAndStack(true))
                ],
                [
                    new NetworkStageDesign("V3A", CleanStack(), 68e3),
                    new NetworkStageDesign("V3B", FollowerAndStack(false), 68e3)
                ]
            )
        ],
        new(
            "4 x 5881",
            PentodeModel.SixL6GC,
            2,
            480,
            1.5e3,
            0.035,
            false,
            0,
            100,
            // The 10k tail stands on the 4k7 feedback shunt: 14.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, 350, 82e3, 100e3, 14.7e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 100e3, 4.7e3, new(22e3, PotTaper.Linear), 100e-9),
            true,
            new(1.9e3),
            SpeakerDesign.Closed4x12,
            // The sheet's 10k from the screens to the preamp's 320 V node on 15 µF.
            new(30, 100e-6, 100, 100e-6, 10e3, 15e-6, 15.5e-3),
            InverterInput: new(100e-9, 1e6)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Drive",
            Control1 = "Clean Volume",
            Depth = null,
            Cut = null,
            Switch1 = "Clean Ch"
        },
        // The overdrive sheet's two 10k 4 W droppers below the 320 V node, 15 µF each: node 1 for V1B and V2A,
        // node 2 for V1A, each drawing what the rails the printed plate voltages imply drop across them.
        PreampSupply = new(
            [
                new(10e3, 15e-6, (RailTop - RailTwo) / 10e3 - (RailTwo - RailOne) / 10e3),
                new(10e3, 15e-6, (RailTwo - RailOne) / 10e3, 1)
            ]
        )
    };

    /// <summary>
    ///     V1A (100k, 2k7 ‖ 680 nF, 47 pF from the plate to the cathode), 4.7 nF with 1M to ground, 4.7 nF with
    ///     220k to ground onto the 1M audio Drive, its wiper through 68k. Out at the grid that 68k reaches.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, first, top, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailOne);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 680e-9);
        net.Capacitor(plate, cathode, 47e-12);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        net.Capacitor(plate, first, 4.7e-9);
        net.Resistor(first, StageNetlist.Ground, 1e6);
        net.Capacitor(first, top, 4.7e-9);
        net.Resistor(top, StageNetlist.Ground, 220e3);
        var drive = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(drive, grid, 68e3);
        net.Output = grid;
        return net;
    }

    /// <summary>V1B (100k, 2k7 ‖ 680 nF), 22 nF and 68k on. Out at the grid that 68k reaches.</summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailTwo);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 680e-9);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, grid, 68e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     The V2A cold clipper (100k, 10k unbypassed), 22 nF into the 470k ‖ 470 pF over 470k divider. Out there.
    /// </summary>
    static StageNetlist ColdClipper() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailTwo);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, grid, 470e3);
        net.Capacitor(coupled, grid, 470e-12);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     The last gain stage of the playing channel, the shared V5B follower and the master. With
    ///     <paramref name="overdrive" />: V2B (100k, 820 Ω unbypassed) DC-coupled to the follower (100k), whose
    ///     cathode drives the overdrive stack, and the treble wiper the 1M audio Master. Without it: V3B (100k,
    ///     820 Ω), 22 nF and 1M to ground, the follower, and the master straight on its cathode (the relay takes
    ///     the stack out). Out at the master's wiper, which drives the 680k into the inverter.
    /// </summary>
    static StageNetlist FollowerAndStack(bool overdrive) {
        var net = new StageNetlist();
        var (rail, plate, cathode, follower) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailTop);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var grid = plate;
        if (!overdrive) {
            // The clean channel's own 22 nF and 1M before the shared follower.
            grid = net.Node();
            net.Capacitor(plate, grid, 22e-9);
            net.Resistor(grid, StageNetlist.Ground, 1e6);
        }

        net.Triode(rail, grid, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);

        var top = follower;
        if (overdrive) {
            var (trebleTop, trebleFoot, slope, middle) =
                (net.Node(), net.Node(), net.Node(), net.Node());
            net.Capacitor(follower, trebleTop, 470e-12, CapacitorRole.Stack);
            net.Resistor(follower, slope, 33e3);
            var treble = net.Pot(trebleTop, trebleFoot, new(220e3, PotTaper.Linear), PotRole.Treble);
            net.Capacitor(slope, trebleFoot, 220e-9, CapacitorRole.Stack);
            net.Rheostat(trebleFoot, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
            net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
            net.Rheostat(middle, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);
            top = treble;
        }

        var master = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        var output = net.Node();
        net.Resistor(master, output, 680e3);
        net.Resistor(output, StageNetlist.Ground, 1e6);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The clean channel's V3A (100k, 1k5 ‖ 2.2 µF) with its stack on the plate (220 pF and 47 pF to the
    ///     treble pot's top, 100k slope, 100 nF bass, 47 nF middle; 220k linear treble, 100k audio bass, 22k
    ///     linear middle), and the 1M audio Clean Volume with the Bright switch's 100 pF across its upper half,
    ///     then 68k. Out at the grid that 68k reaches.
    /// </summary>
    static StageNetlist CleanStack() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailTop);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (trebleTop, trebleFoot, slope, middle, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, trebleTop, 220e-12, CapacitorRole.Stack);
        net.Capacitor(plate, trebleTop, 47e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleFoot, 100e-9, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middle, new(100e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.SwitchedCapacitor(treble, volume, 100e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(volume, grid, 68e3);
        net.Output = grid;
        return net;
    }
}
