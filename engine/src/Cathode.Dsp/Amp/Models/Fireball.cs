using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Fireball 60 (E625): the Ultra (lead) channel, and the clean channel on Switch 3. V1A (1M grid leak, 100k,
///     1k unbypassed), 47 nF into the 1M audio gain (100 pF from its top to its wiper with Bright), the wiper on V1B's
///     grid; V1B (100k, 1.5k ‖ 22 µF), 47 nF with 2.2M to ground. Ultra: 100k into V2A's grid (330k, 3.3k
///     unbypassed), 47 nF with 470k to ground, 330k into V2B's grid with 470k to ground. Clean: V2A is out of the path
///     (its output is grounded at V2B's bypassed cathode) and 1M joins V1B's coupling cap to the same 330k. V2B (100k,
///     1.5k ‖ 22 µF), 100 nF with 2.2M to ground into the stack (1 nF; 68k slope; 47 nF bass cap, 1M audio bass
///     rheostat; a 20k linear middle under the bass with 47 nF on its wiper; 250k linear treble), 470k from the treble
///     wiper (220k to ground from there in Ultra), 330k into V3A's grid with 220k to ground; V3A, the loop's send
///     follower (100k ‖ 47 nF plate, 1k cathode), 1 µF into the loop and its balance pot, 10k into V3B's 100k grid
///     leak; V3B (100k, 1.5k ‖ 22 µF), 100 nF and 100k into the 1M linear master; 100 nF into a long-tail pair (100k
///     grid leak to the bias junction; 82k/100k, 470 Ω, 22k tail on a 3.3k feedback shunt) and two 6L6GC (2.2k
///     stoppers, 470 Ω screens, 47 nF, 330k bias feeds) at 430 V. The feedback runs from the 8 Ω tap through the 250k
///     audio presence rheostat and 56k into the 3.3k: the presence is the amount of feedback, with no cap.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Engl factory drawing "625" (ENGL Gerätebau GmbH, one sheet, a vector PDF made in March 2003),
///         https://schematicheaven.net/bargainbin/engl_fireball.pdf; the copy at
///         https://el34world.com/charts/Schematics/files/Engl/Engl_fireball.pdf is the same file. It shows two 6L6GC,
///         so it is the Fireball 60; no drawing of the Fireball 100 (E635) was found, and a forum poster says the
///         published Fireball drawings do not match the 100, so there is no 100 variant here. Every value above is
///         read from the drawing except the two assumptions marked below. It prints B+ 450 V and 430 V for the two
///         positions of the standby switch; 430 V is taken as the running voltage.
///     </para>
///     <para>
///         The drawing leaves parts out, as forum posters say of Engl's published drawings. Three places:
///     </para>
///     <para>
///         <b>Assumption, not on 625:</b> V2A's grid is drawn tied straight to ground below R12 100k, which would
///         silence the Ultra channel, so the lower leg of that divider is missing. The model puts 470k there, the
///         lower leg of the Hi divider (100k over 470k) ahead of the same stage (330k plate, 3.3k cathode, 47 nF and
///         2.2M before it) on Engl's Powerball drawing 645-1. The Ultra channel's gain depends on it: 100k over 470k
///         passes 0.82 of V1B's output.
///     </para>
///     <para>
///         The Depth switch S2A is drawn with C3 1.5 nF between two points of the same wire (V1A's coupling cap and
///         the gain pot's top), so as drawn it does nothing; it is left out and the panel has no switch for it. V4B's
///         grid is drawn tied straight to the feedback node with R45 150k to the bias junction and no capacitor, which
///         would cut that triode off; the model's long-tail pair has the usual AC-grounded second grid, which needs
///         no value from the missing part.
///     </para>
///     <para>
///         The supply as drawn: CE16 100 µF, R81 470 Ω to the screens' CE17 47 µF, R82 and R83 (2.2k each) to CE18
///         22 µF (V3 and the inverter), R84 4.7k to CE3 10 µF (V1 and V2). The drawing prints no voltages below B+, so
///         the rails are the chain's rest voltages at assumed draws, 0.75 mA a triode as on the Powerball.
///     </para>
///     <para>
///         <b>Assumption, not on 625:</b> as drawn, with V1 and V2 all on CE3, the model motorboats after a loud
///         input stops, in both channels and down to gain 1 with the master full (-16 to -29 dB two seconds after
///         the stop), as the Powerball model did on its drawing's single node. The gain pot does not break that
///         loop: V1B, V2A and V2B come after it. So the model adds the Powerball model's assumed filter: two RC
///         nodes below CE3, each 10k into 22 µF, V1B and V2A on the first, V1A on the second, V2B left on CE3. With
///         them every case of the after-the-stop test is silent; 10 µF nodes still pass, 4.7 µF fail, and a single
///         extra node fails at full gain.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the idle current (the drawing gives the bias as 2.2 to 2.7 V between
///         either plate and the centre tap, across a primary resistance it does not give; 31 mA a tube, the middle of
///         the Powerball drawing's 28 to 34 mA, is assumed), the primary (3.8k plate to plate, the same load a tube as
///         the Powerball's assumed 1.9k for four) and the supply's 60 Ω source resistance are typical values; which
///         way the presence rheostat turns is not on the drawing (here up is more resistance and less feedback, as on
///         the Mesa Marks); the Ultra relay RE1B puts R24 220k on V2B's cathode, an AC ground through its 22 µF, here
///         on ground; one Master knob for Master A and Master B (two equal 1M linear pots behind a relay); the loop is
///         empty with its balance fully dry (the return jack is normalled to the send, so both of the balance's 10k
///         inputs carry the send: one into the mix, the other into the pot's grounded end). Left out: the J175 mute T1
///         on the stack's output through R25 10k, which acts only while switching channels.
///     </para>
/// </remarks>
public static class Fireball {
    // The rails at rest, from the chain at its assumed draws (the drawing prints none below B+): CE18, V3 and the
    // inverter, the screens' CE17 less 4.4k × 6 mA; CE3 (V2B) less 4.7k × 3 mA; the first assumed node (V1B, V2A) less
    // 10k × 2.25 mA; the second (V1A) less 10k × 0.75 mA.
    const double InverterRail = 399;
    const double V2BRail = 385;
    const double SecondRail = 362;
    const double V1ARail = 355;

    public static AmpDesign Design { get; } = new(
        "Fireball 60",
        [
            new NetworkStageDesign("V1A", InputAndGain()) { SupplyNode = 3 },
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, SecondRail, 100e3, 1.5e3, 22e-6) { SupplyNode = 2 },
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch3,
                [
                    new NetworkStageDesign("V2A", UltraStage()) { SupplyNode = 2 },
                    // R16 470k loads V2A's coupling cap; R18 330k into the grid.
                    new NetworkStageDesign("V2B", LastStage(470e3, 330e3)) { SupplyNode = 1 }
                ],
                [
                    // R9 2.2M and V2A's idle divider (R12 100k over the assumed 470k) load V1B's coupling cap; R10 1M
                    // and R18 330k into the grid.
                    new NetworkStageDesign("Clean V2B", LastStage(1 / (1 / 2.2e6 + 1 / 570e3), 1.33e6)) {
                        SupplyNode = 1
                    }
                ]
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
        Panel = AmpPanel.Standard with { Depth = null, Cut = null, Switch3 = "Clean" },
        // From CE18 (V3 and the inverter): R84 4.7k to CE3 10 µF (V1, V2 as drawn). The drawing prints no voltages, so
        // each of the four triodes below CE18 draws an equal share of the preamp's 6 mA, 0.75 mA.
        // ASSUMPTION, not on Engl 625: two more nodes, 10k into 22 µF under CE3 for V1B and V2A, and 10k into 22 µF
        // under that for V1A (CE3 keeps V2B), the filter the Powerball model assumes for the same reason. As drawn,
        // with all four on CE3, the model motorboats after loud playing in both channels (docs/06 §3.7). With these
        // nodes 10 µF still passes and 4.7 µF fails; one extra node alone fails at full gain.
        PreampSupply = new(
            [
                new(4.7e3, 10e-6, 0.75e-3), new(10e3, 22e-6, 1.5e-3, 1), new(10e3, 22e-6, 0.75e-3, 2)
            ]
        )
    };

    /// <summary>
    ///     V1A and the gain: R1 1M grid leak; R3 100k plate; R2 1k cathode, unbypassed; C1 47 nF into the top of the 1M
    ///     audio gain P1, its wiper on V1B's grid; with Bright, C4 100 pF from the top to the wiper.
    /// </summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var top = net.Node();
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Capacitor(EnglParts.Triode(net, net.Input, V1ARail, 100e3, 1e3, 0), top, 47e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(top, wiper, 100e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V2A, in Ultra only: C5 47 nF with R9 2.2M to ground; R12 100k into the grid over the assumed 470k (see the
    ///     class remarks: the drawing ties the grid to ground); R15 330k plate; R14 3.3k cathode, unbypassed. R10 1M
    ///     hangs open on the relay.
    /// </summary>
    static StageNetlist UltraStage() => PaddedStage(2.2e6, 100e3, (SecondRail, 330e3, 3.3e3, 0));

    /// <summary>
    ///     V2B: 47 nF with <paramref name="load" /> to ground, <paramref name="series" /> into the grid with R17 470k
    ///     to ground; R20 100k plate; R19 1.5k ‖ CE5 22 µF.
    /// </summary>
    static StageNetlist LastStage(double load, double series) =>
        PaddedStage(load, series, (V2BRail, 100e3, 1.5e3, 22e-6));

    /// <summary>
    ///     The stack: C11 100 nF with R21 2.2M to ground; C13 1 nF to the 250k linear treble's top; R22 68k slope; C14
    ///     47 nF to the 1M audio bass rheostat's top (the treble's bottom); the bass's bottom on the top of the 20k
    ///     linear middle, with C16 47 nF from the slope on its wiper; R23 470k from the treble wiper, and in Ultra R24
    ///     220k from there to ground.
    /// </summary>
    static StageNetlist Stack() {
        var net = new StageNetlist();
        var at = (Coupled: net.Node(), Slope: net.Node(), TrebleTop: net.Node(), BassTop: net.Node(),
            MiddleTop: net.Node());
        net.Capacitor(net.Input, at.Coupled, 100e-9);
        net.Resistor(at.Coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(at.Coupled, at.TrebleTop, 1e-9, CapacitorRole.Stack);
        var (treble, _) = EnglParts.StackPots(net, at, 68e3, 47e-9, 47e-9);
        var output = net.Node();
        net.Resistor(treble, output, 470e3);
        net.SwitchedResistor(output, StageNetlist.Ground, 220e3, 0, SwitchRole.Switch3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V3A, the loop's send follower: R26 330k into the grid with R27 220k to ground; R29 100k ‖ C17 47 nF to the
    ///     rail; R28 1k cathode; CE6 1 µF to the send. The empty loop with the balance fully dry: R31 10k into the mix
    ///     (the two 50k tracks from there to ground), R32 10k to ground, R33 10k into V3B's grid with R34 100k to
    ///     ground.
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

    /// <summary>
    ///     A stage behind its coupling cap and grid divider: 47 nF with <paramref name="load" /> to ground,
    ///     <paramref name="series" /> into the grid with 470k to ground, and the triode on <paramref name="tube" />'s
    ///     rail, plate resistor, cathode resistor and bypass cap.
    /// </summary>
    static StageNetlist PaddedStage(
        double load,
        double series,
        (double Rail, double Plate, double Cathode, double Bypass) tube
    ) {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, load);
        net.Resistor(coupled, grid, series);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = EnglParts.Triode(net, grid, tube.Rail, tube.Plate, tube.Cathode, tube.Bypass);
        return net;
    }
}
