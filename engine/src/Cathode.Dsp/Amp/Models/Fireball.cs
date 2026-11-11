using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Fireball 60 (E625): the Ultra (lead) channel, and the clean channel on Switch 3. V1A (1M grid leak, 100k,
///     1k ‖ 22 µF), 47 nF with 2.2M to ground, 1 nF (2 nF with Depth) with 220k to ground, 100k into the 1M audio gain
///     (100 pF from its top to its wiper with Bright, 470k from the wiper to ground), the wiper on V1B's grid; V1B
///     (100k, 1.5k ‖ 22 µF), 47 nF with 2.2M to ground. Ultra: 1 nF and 100k into V2A's grid with 220k ‖ 1 nF to ground
///     (330k ‖ 1 nF plate, 3.3k ‖ 10 µF), 47 nF with 470k to ground, 220k into V2B's grid with 470k to ground. Clean:
///     V2A is out of the path (its output grounded) and 1M ‖ 100 pF over 22k, then the same 220k, feed V2B. V2B (100k,
///     1.5k ‖ 22 µF), 100 nF with 2.2M to ground into the stack (250 pF; 68k slope; 22 nF bass cap, 1M audio bass
///     rheostat; a 22k linear middle under the bass with 15 nF on its wiper; 220k linear treble), 470k from the treble
///     wiper over 100k (and 22k in Ultra), 330k into V3A's grid with 220k to ground; V3A, the loop's send follower
///     (100k ‖ 47 nF plate, 1k cathode), 1 µF into the loop and its balance pot, 10k into V3B's 100k grid leak; V3B
///     (100k, 1.5k ‖ 22 µF), 100 nF and 100k into the 1M linear master; 100 nF into a long-tail pair (100k grid leak to
///     the bias junction; 82k/100k, 470 Ω, 22k tail on a 3.3k feedback shunt) and two 6L6GC (2.2k stoppers, 470 Ω
///     screens, 47 nF, 330k bias feeds) at 430 V. The feedback runs from the 8 Ω tap through the 250k audio presence
///     rheostat and 56k into the 3.3k: the presence is the amount of feedback, with no cap.
/// </summary>
/// <remarks>
///     <para>
///         Two sources. The first is the Engl factory drawing "625" (ENGL Gerätebau GmbH, one sheet, a vector PDF made
///         in March 2003), https://schematicheaven.net/bargainbin/engl_fireball.pdf; the copy at
///         https://el34world.com/charts/Schematics/files/Engl/Engl_fireball.pdf is the same file. It shows two 6L6GC,
///         so it is the Fireball 60; no drawing of the Fireball 100 (E635) was found, and a forum poster says the
///         published Fireball drawings do not match the 100, so there is no 100 variant here. It prints B+ 450 V and
///         430 V for the two positions of the standby switch; 430 V is taken as the running voltage. Its preamp cannot
///         be the amp's, as the Powerball's drawing 645-1 could not (issue #512): V2A's grid is drawn tied to ground,
///         the Depth switch's cap sits across a wire, and four stages are coupled by 47 nF with almost no divider. The
///         model built from it had 66 dB of gain at Gain 5 (the 5150 and the Dual Rectifier have about 38) and played a
///         0.2 mV note within 2 dB of a 0.1 V one (issue #525): a square wave whatever was played.
///     </para>
///     <para>
///         The second is a redraw of the E625's preamp alone, "Engl Fireball E625", dated 7 April 2010 (one sheet,
///         drawn in Illustrator CS3; its author is not named on it). It draws every stage from the input to the masters
///         with the networks 625 leaves out, and they are the ones the hand-corrected copy of the Powerball's drawing
///         found on that amp: 1 nF into 220k ahead of a gain with 470k on its wiper, a 1 nF series cap into V2A's
///         divider with 1 nF across its lower leg, 1 nF across V2A's 330k plate resistor, and the Powerball's Clean
///         divider (1M ‖ 100 pF over 22k). The model follows it from the input to V3A's grid: the values above, the
///         bypassed cathodes of V1A (22 µF) and V2A (10 µF), the Depth switch (C22 1 nF across C13), the stack (250 pF,
///         220k treble, 22 nF, 15 nF, 22k middle; 625 has 1 nF, 250k, 47 nF, 47 nF and 20k), R3 100k after R27 470k,
///         R28 22k in Ultra where 625 has 220k, and C15 22 µF for the preamp's filter cap (625's CE3, 10 µF). Its
///         relay K1 is read with S as Ultra: there S1 takes V2A's output to V2B's 220k and S2 grounds R28; at O, O1
///         takes the clean divider and O2 grounds V2A's output. From V3A on, the redraw and 625 differ in small parts
///         (C17 100 nF, C25 10 µF, no 100k ahead of the masters, 10k loop pots); there 625 is kept. The redraw's rail
///         label, 320 V, is not used: the rails are 625's chain (below).
///     </para>
///     <para>
///         The supply as drawn on 625: CE16 100 µF, R81 470 Ω to the screens' CE17 47 µF, R82 and R83 (2.2k each) to
///         CE18 22 µF (V3 and the inverter), R84 4.7k to the preamp's node (V1 and V2), 22 µF as the redraw has it.
///         Neither prints voltages below B+, so the rails are the chain's rest voltages at assumed draws, 0.75 mA a
///         triode as on the Powerball. With the redraw's preamp the model is silent on that supply after a loud input
///         stops, so the two filter nodes once assumed below it (10k into 22 µF each, docs/06 §3.7) are gone.
///     </para>
///     <para>
///         V4B's grid is drawn on 625 tied straight to the feedback node with R45 150k to the bias junction and no
///         capacitor, which would cut that triode off; the model's long-tail pair has the usual AC-grounded second
///         grid, which needs no value from the missing part.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the idle current (625 gives the bias as 2.2 to 2.7 V between either
///         plate and the centre tap, across a primary resistance it does not give; 31 mA a tube, the middle of the
///         Powerball drawing's 28 to 34 mA, is assumed), the primary (3.8k plate to plate, the same load a tube as the
///         Powerball's assumed 1.9k for four) and the supply's 60 Ω source resistance are typical values; the tapers of
///         the redraw's 220k treble and 22k middle are not printed (linear here, as on 625) nor the bass's (audio, as
///         on 625); which way the presence rheostat turns is not on the drawing (here up is more resistance and less
///         feedback, as on the Mesa Marks); one Master knob for Master A and Master B (two equal 1M linear pots behind
///         a relay); the loop is empty with its balance fully dry (the return jack is normalled to the send, so both of
///         the balance's 10k inputs carry the send: one into the mix, the other into the pot's grounded end). Left out:
///         the J175 mute T1 on the stack's output through R25 10k, which acts only while switching channels.
///     </para>
/// </remarks>
public static class Fireball {
    // The rails at rest, from 625's chain at its assumed draws (neither drawing prints them): CE18, V3 and the
    // inverter, the screens' CE17 less 4.4k × 6 mA; the preamp's node (V1, V2) less 4.7k × 3 mA.
    const double InverterRail = 399;
    const double PreampRail = 385;

    public static AmpDesign Design { get; } = new(
        "Fireball 60",
        [
            new NetworkStageDesign("V1A", InputAndGain()) { SupplyNode = 1 },
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, PreampRail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 },
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch3,
                [
                    new NetworkStageDesign("V2A", UltraStage()) { SupplyNode = 1 },
                    new NetworkStageDesign("V2B", UltraLastStage()) { SupplyNode = 1 }
                ],
                [new NetworkStageDesign("Clean V2B", CleanLastStage()) { SupplyNode = 1 }]
            ) { SupplyNode = 1 },
            new NetworkStageDesign("Stack", Stack()) { SupplyNode = 1 },
            new NetworkStageDesign("V3A", Follower()),
            new NetworkStageDesign("V3B", EnglParts.ReturnAndMaster(InverterRail, 1.5e3, 100e3))
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            430,
            470,
            0.031,
            false,
            0,
            60,
            // The 22k tail stands on the 3.3k feedback shunt: 25.3k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, InverterRail, 82e3, 100e3, 25.3e3, 470),
            47e-9,
            330e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            // R46 56k and the 250k audio presence rheostat P5 in series from the 8 Ω tap into R44 3.3k; no cap (the
            // picofarad is a placeholder).
            new(1, 56e3, 3.3e3, new(250e3, PotTaper.Audio), 1e-12) { Series = new(0, 0, 0) },
            true,
            new(3.8e3),
            SpeakerDesign.Closed4x12,
            // CE16 100 µF, R81 470 Ω to the screens' CE17 47 µF, R82 + R83 (2.2k each) to CE18 22 µF.
            new(60, 100e-6, 470, 47e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon),
            InverterInput: new(100e-9, 100e3)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with { Depth = null, Cut = null, Switch3 = "Clean", Fat = "Depth" },
        // From CE18 (V3 and the inverter): R84 4.7k (the redraw's R15) to the preamp's 22 µF (the redraw's C15; 625's
        // CE3 is 10 µF), V1 and V2. Neither drawing prints voltages, so each of the four triodes draws an equal share
        // of the preamp's 6 mA, 0.75 mA.
        PreampSupply = new([new(4.7e3, 22e-6, 3e-3)])
    };

    /// <summary>
    ///     V1A and the gain: R19 1M grid leak; R1 100k plate; R2 1k ‖ C6 22 µF; C2 47 nF with R23 2.2M to ground; C13
    ///     1 nF (with Depth, C22 1 nF across it) with R5 220k to ground; R4 100k into the top of the 1M audio gain, its
    ///     wiper on V1B's grid with R22 470k to ground; with Bright, C19 100 pF from the top to the wiper.
    /// </summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (coupled, cut, top) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Capacitor(TriodeParts.CommonCathode(net, net.Input, PreampRail, 100e3, 1e3, 22e-6), coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Output = EnglParts.Gain(net, (coupled, cut, top), 1e-9, 220e3, false, 470e3);
        return net;
    }

    /// <summary>
    ///     V1B's coupling, on <paramref name="net" />: C3 47 nF with R18 2.2M to ground. Both channels' networks hang
    ///     on the node after it, which is returned: the Ultra one (C4 1 nF, R7 100k into V2A's grid with R25 220k ‖ C8
    ///     1 nF to ground) and the clean one (R20 1M ‖ C21 100 pF over R21 22k).
    /// </summary>
    static (int Ultra, int Clean) Networks(StageNetlist net) {
        var (coupled, series, ultra, clean) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        EnglParts.Divider(net, (coupled, series, ultra), 1e-9, 100e3, 220e3, 1e-9);
        EnglParts.CleanDivider(net, coupled, clean);
        return (ultra, clean);
    }

    /// <summary>
    ///     V2A, in Ultra: <see cref="Networks" />, the clean divider a load; R17 330k ‖ C20 1 nF plate; R9 3.3k ‖ C14
    ///     10 µF.
    /// </summary>
    static StageNetlist UltraStage() {
        var net = new StageNetlist();
        var (grid, _) = Networks(net);
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Output = EnglParts.ColdClipper(net, (grid, supply, plate, cathode), PreampRail, 10e-6);
        return net;
    }

    /// <summary>V2B in Ultra: C7 47 nF with R24 470k to ground, then <see cref="LastStage" /> from there.</summary>
    static StageNetlist UltraLastStage() {
        var net = new StageNetlist();
        var coupled = net.Node();
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 470e3);
        LastStage(net, coupled);
        return net;
    }

    /// <summary>
    ///     V2B in the clean channel: <see cref="Networks" />, the Ultra one a load (V2A's grid draws nothing), then
    ///     <see cref="LastStage" /> from the clean divider's tap.
    /// </summary>
    static StageNetlist CleanLastStage() {
        var net = new StageNetlist();
        var (_, clean) = Networks(net);
        LastStage(net, clean);
        return net;
    }

    /// <summary>
    ///     V2B from <paramref name="from" />, on <paramref name="net" />: R11 220k into the grid with R10 470k to
    ///     ground; R12 100k plate; R13 1.5k ‖ C1 22 µF. Its plate is the output.
    /// </summary>
    static void LastStage(StageNetlist net, int from) {
        var grid = net.Node();
        net.Resistor(from, grid, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = TriodeParts.CommonCathode(net, grid, PreampRail, 100e3, 1.5e3, 22e-6);
    }

    /// <summary>
    ///     The stack: C9 100 nF with R26 2.2M to ground; C10 250 pF to the 220k linear treble's top; R14 68k slope;
    ///     C11 22 nF to the 1M audio bass rheostat's top (the treble's bottom); the bass's bottom on the top of the 22k
    ///     linear middle, with C12 15 nF from the slope on its wiper; R27 470k from the treble wiper over R3 100k, and
    ///     in Ultra R28 22k from there to ground.
    /// </summary>
    static StageNetlist Stack() {
        var net = new StageNetlist();
        var at = (Coupled: net.Node(), Slope: net.Node(), TrebleTop: net.Node(), BassTop: net.Node(),
            MiddleTop: net.Node());
        var (coupled, trebleTop, output) = (at.Coupled, at.TrebleTop, net.Node());
        net.Capacitor(net.Input, coupled, 100e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, trebleTop, 250e-12, CapacitorRole.Stack);
        var (treble, _) = EnglParts.StackPots(
            net,
            at,
            68e3,
            22e-9,
            15e-9,
            220e3,
            22e3
        );
        net.Resistor(treble, output, 470e3);
        net.Resistor(output, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(output, StageNetlist.Ground, 22e3, 0, SwitchRole.Switch3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V3A, the loop's send follower: R26 330k into the grid with R27 220k to ground; R29 100k ‖ C17 47 nF to the
    ///     rail; R28 1k cathode; CE6 1 µF to the send. The empty loop with the balance fully dry: R31 10k into the mix
    ///     (the two 50k tracks from there to ground), R32 10k to ground, R33 10k into V3B's grid with R34 100k to
    ///     ground. (625's part numbers.)
    /// </summary>
    static StageNetlist Follower() {
        var (net, send) = EnglParts.SendFollower(InverterRail, 330e3, 220e3, 47e-9, 1e-6);
        var (mix, ret) = (net.Node(), net.Node());
        net.Resistor(send, StageNetlist.Ground, 10e3);
        net.Resistor(send, mix, 10e3);
        net.Resistor(mix, StageNetlist.Ground, 25e3);
        net.Resistor(mix, ret, 10e3);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Output = ret;
        return net;
    }
}
