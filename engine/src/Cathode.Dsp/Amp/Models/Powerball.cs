using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Powerball (E645): channel 2 (lead) in its Lo and Hi modes with its noise gate, and channel 1 (Clean and
///     Crunch) on Switch 3. Channel 2: V5A (100k, 1k ‖ 22 µF) into 47 nF and 2.2M, then 1 nF with 470k to ground into
///     the channel-2 gain (100k into 1M audio, 100 pF from the 100k's top to the wiper, 470k from the wiper to ground)
///     and V5B's 1M grid leak; V5B (100k, 1.5k ‖ 22 µF); 47 nF and 2.2M, then the Lo network (470 pF, 220k over 470k
///     with about 330 pF across it) or the Hi one (2.2 nF, 100k over 470k with 1 nF across it) into V6A (470k grid
///     leak; 330k ‖ 1 nF, 3.3k ‖ 1 µF); 47 nF, 470k to ground and 220k into V6B (1M leak; 100k, 1.5k ‖ 22 µF); 100 nF
///     and 2.2M (and 1M into the noise gate's detector) into the lead stack (220 pF, 440 pF in Focused; 100k slope,
///     220k across it in Focused; 22 nF bass cap, 1M audio bass rheostat; the middle: a 20k linear pot under the bass
///     with 22 nF on its wiper, Open, or its own 20k with 10 nF, Focused; 250k linear treble), 470k into the 250k
///     linear lead volume with 100k across it, 220k into 100k at the V7A cathode follower (100k ‖ 10 nF plate, 1k
///     cathode), 4.7 µF and the loop's 10k into V7B (100k leak; 100k, 1.5k ‖ 22 µF), 100 nF and 100k into the 1M linear
///     master (1M from its wiper to ground), 100 nF into a long-tail pair (100k grid leak to the bias junction;
///     82k/100k, 470 Ω, 22k tail, a 4.7k feedback shunt) and four 6L6GC (2.2k stoppers, 470 Ω screens, 47 nF, 330k bias
///     feeds) at 440 V. Channel 1 uses three of the four stages: V5A into 1.5 nF (3 nF with Bottom) with 470k to
///     ground, 100k and the 1M audio channel-1 gain (100 pF across the 100k and the pot's upper part with Bright, 470k
///     from the wiper to ground), V5B, the Clean divider (1M ‖ 100 pF over 22k) or the Crunch one (1 nF, 470k over
///     470k) and 220k into V6B, then its own stack (470 pF, 940 pF in Focused; 100k slope; 22 nF bass cap; 10 nF, and
///     10 nF more in Open, on the 20k middle's wiper; 250k linear treble) and 220k into the 250k linear Clean volume or
///     330k into the 250k audio Crunch volume.
/// </summary>
/// <remarks>
///     <para>
///         Two sources. The first is the Engl factory drawing "645-1" (ENGL Gerätebau GmbH, two sheets: the whole amp,
///         then the switching logic), https://el34world.com/charts/Schematics/files/Engl/Engl_powerball.pdf; a poster
///         on the Music Electronics Forum (https://music-electronics-forum.com/filedata/fetch?id=812301) says it came
///         from Engl (B+ 460 V in standby, 440 V running; bias 28–34 mA a tube). Its preamp cannot be the amp's: as
///         drawn, four fully bypassed stages are coupled by 47 nF with no divider to speak of. The model built from it
///         alone had 65 dB of gain at Gain 5 (the 5150 and the Dual Rectifier have about 40), turned a note into a
///         square wave down to 0.2 mV of input (issue #512, "fuzzy") and motorboated on the drawn supply. Others found
///         the same: a poster who opened an amp says of the factory drawing that its low-frequency response shows the
///         circuit would not work (the forum's thread 8389, "Engl P-ball Lo lead channel schematic?"), and a thread on
///         guitar-gear.ru calls its preamp on the first two tubes a fake.
///     </para>
///     <para>
///         The second source is a copy of 645-1 corrected by hand in red from photographs of a disassembled amp, posted
///         in that thread on 13 May 2016 by its author (user "staff", post 22: "I drew it myself, from photos of the
///         disassembled unit"; https://guitar-gear.ru/forum/topic/4085-cхема-engl-powerball/, attachment
///         powerball-1.png). It is one person's trace, questioned in the thread, and marks its own doubts with "?".
///         Where it corrects the factory drawing the model follows it: the 1 nF and 470k ahead of the channel-2 gain,
///         the 100 pF and 470k on that pot, the Lo and Hi networks (470 pF, 2.2 nF, 1 nF, and "?~330p", kept as
///         330 pF), V6A's 470k grid leak and the 1 nF across its plate resistor, the 470k after its coupling cap, the
///         2.2M after V6B's, the lead stack's caps (220 pF for both treble caps, 22 nF for the bass cap, 10 nF on the
///         Focused middle), the 100k across each lead volume, the Bottom network (10 nF to ground after R32), channel
///         1's 1.5 nF coupling cap with a second 1.5 nF on the Bottom switch, its 470k resistors, its dividers (100 pF
///         across R15, R17 22k, 1 nF ahead of R18 470k, R16 470k), its stack's 22 nF bass cap and 10 nF middle caps,
///         and the gate's detector (R76 100k, the reference rail at half of 24 V, 1 µF and 100k on the rectifier). Its
///         printed sheet also differs from the el34world copy in two places it leaves unmarked: R30 220k on a second
///         Style contact (read here as across the 100k slope in Focused; the junction is not clear in the scan) and
///         C11 10 nF. Where it marks "?" without a value (channel 1's treble caps) the factory values stay. Its
///         corrections to the feedback network (680 nF, 6.8k, a 250k depth pot, 2.2 nF and 4.7 nF) are not used: the
///         scan does not show how they connect.
///     </para>
///     <para>
///         The supply's decoupling as drawn: R136 470 Ω to the 430 V screens' CE9 47 µF, R137 and R138 (2.2k each) to
///         CE10 22 µF (V7, the inverter; three ZY150 zeners across it hold it under 450 V in standby), R26 4.7k 1 W to
///         CE3 10 µF (V5, V6). With the corrected preamp the model is silent on that supply after a loud input stops,
///         so the two filter nodes it once assumed below CE3 (10k into 22 µF each, docs/06 §3.7) are gone.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the preamp rails carry no voltages on the drawing (here the chain's own
///         rest voltages at its assumed draws: 410 V at CE10 for V7 and the inverter, 396 V at CE3 for V5 and V6); Hi
///         mode is taken as the energised position of the "Gain" relays RE3 and RE4 (the Hi network and the Hi Lead
///         volume; one Channel Volume knob here for both volumes, both of which load the treble wiper); the amp's three
///         Bottom buttons are one Bottom switch here (the Circuit Amp's Fat), which acts on the mode that plays: S2 in
///         channel 1 (the second 1.5 nF), S3 in Lo Lead and S4 in Hi Lead (R32 330k from the bass pot's top, 10 nF to
///         ground, then 680k into the volume's top: a low boost); the parallel loop is empty with its balance fully dry
///         (the 10k series only). Relay RE1B grounds V6A's output in channel 1 and lights channel 2's LDR. Left out:
///         the J175 soft mute T1, which acts only while switching channels. The feedback network is simplified to 22k
///         + 1.5k from the speaker into the 4.7k shunt with the 50k presence and 100 nF across it, the depth punch (1M
///         with 10 nF) as the depth control; the tap and primary (1.9k) are typical values.
///     </para>
///     <para>
///         The noise gate (the Cut knob, "Gate Threshold": the rear panel's Noise Gate Threshold, P20). From 645-1:
///         V6B's output through R25 1M into R76 and C26 100 nF with R77 470k; IC4A (LM324) amplifies it by 1 + R79 /
///         R78 (10k, 4.7k) about the reference rail, IC4B inverts that, and each output charges the hold capacitor
///         through a diode; R82 10k takes the held voltage to comparator IC4C, whose other input stands on the divider
///         from +24 V through P20 (20k linear, a rheostat) and R83 4.7k into R85 22k; its output drives the gate of T8
///         (J175) through R38 22k, and T8 shunts the lead volume's wiper to ground through R39 4.7k. From the corrected
///         copy: R76 100k, the rail at 12 V (10k from +24 V into R84 10k), 1 µF on the rectifier with 100k to the rail
///         after R82 (a release of 0.11 s), and 100 nF with 1M on T8's gate (2.2 ms). The divider gives 11.3 V with P20
///         all in and 19.8 V with it out, so the comparator never falls below 13 % of the knob's travel: Engl's manual
///         says the gate works "past about 9 o'clock" and that turning further right raises the level it engages at.
///         The J175's 125 Ω on resistance and 3 to 6 V pinch-off (4.5 V here) are from Vishay's J/SST174 series data
///         sheet. Guesses: 0.5 V for the diode, the LM324's output reaching 22 V (2 V under its supply), so 10 V above
///         the rail. The knob's default is 0, the gate off.
///     </para>
///     <para>
///         Channel 1 (Switch 3), from the same sheets. The channel relays are drawn at rest in channel 1: RE1A feeds
///         V6B's 220k from the "Gain" relay RE3A's divider in place of V6A's output, RE1B grounds V6A's output, RE2B
///         sends V6B's output to channel 1's stack and RE2A takes the follower's input from its volumes. The "Gain"
///         relays at rest give Clean (R15 over R17, the Clean Treble pot P4 and R28 220k into the Clean volume P6, 250k
///         linear), energised Crunch (R18 over R16, the Crunch Treble pot P5 and R29 330k into the Crunch volume P7,
///         250k audio); Switch 2 is that relay in both channels. The "Style" relays RE6 do in channel 1 what they do in
///         the lead: Focused adds C7 to the treble cap and takes C11 off the middle's wiper, leaving C10. Simplified as
///         in the lead: one Treble knob for the Clean and Crunch Treble pots (only the selected one is in circuit: the
///         other's bottom end is open) and one Channel Volume knob for the two volumes, both of which load the treble
///         wiper; a lit LT30 optocoupler (LD1, between the gain's wiper and V5B's grid) is closed and the dark one
///         (LD2, channel 2's) open; the idle channel's gain network and dividers stay as loads. The Mid Focused knob
///         does nothing in channel 1, which has one middle pot, and the gate works on the lead only.
///     </para>
/// </remarks>
public static class Powerball {
    // The rails at rest, from the chain at its assumed draws (the drawing prints none below the screens): CE10, the
    // inverter and V7, 436.5 V at the screens' CE9 less 4.4k × 6 mA; CE3 (V5, V6) less 4.7k × 3 mA. They were once
    // taken as 420 V for the inverter, 410 V for V7 on the same node, and 400 V for V5 and V6 on three different nodes
    // (#296), then as 396, 374 and 366 V on CE3 and two assumed nodes below it.
    const double InverterRail = 410;
    const double PreampRail = 396;

    /// <summary>
    ///     The lead channel's noise gate (see the remarks): the detector's divider (R76 100k ‖ R77 470k under R25 1M)
    ///     times IC4A's gain, its coupling (C6 100 nF into R25 and the divider), and the comparator, JFET and timing
    ///     values listed there.
    /// </summary>
    static NoiseGateDesign Gate { get; } = new(
        82.5e3 / (1e6 + 82.5e3) * (1 + 10e3 / 4.7e3),
        1 / (2 * Math.PI * 100e-9 * (1e6 + 82.5e3)),
        0.5,
        10,
        1e-6 * (10e3 + 100e3),
        24,
        12,
        new(20e3, PotTaper.Linear),
        4.7e3,
        22e3,
        4.7e3,
        125,
        4.5,
        22,
        100e-9 * (22e3 * 1e6 / (22e3 + 1e6))
    );

    /// <summary>
    ///     Four 6L6GC at 440 V and 31 mA each (470 Ω screens, 330k bias feeds) into an assumed 1.9k primary, the
    ///     inverter on CE10, and the simplified feedback network (see the remarks).
    /// </summary>
    static PowerAmpDesign Power { get; } = EnglParts.Power(
        new("4 x 6L6GC", PentodeModel.SixL6GC, 440, 470, 0.031, 100, 1.9e3),
        // The 22k tail stands on the 4.7k feedback shunt: 26.7k from the bias junction to ground.
        new(InverterRail, 26.7e3, 330e3),
        new(1, 23.5e3, 4.7e3, new(50e3, PotTaper.ReverseAudio), 0.1e-6),
        new(30, 220e-6, 470, 47e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon)
    );

    public static AmpDesign Design { get; } = new(
        "Powerball",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch3,
                [
                    new NetworkStageDesign("V5A", InputAndGain()) { SupplyNode = 1 },
                    new TriodeStageDesign("V5B", TriodeModel.Ecc83, PreampRail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 },
                    new NetworkStageDesign("V6A", ThirdStage()) { SupplyNode = 1 },
                    new NetworkStageDesign("V6B", FourthStage()) { SupplyNode = 1 }
                ],
                [
                    new NetworkStageDesign("Ch 1 V5A", ChannelOneGain()) { SupplyNode = 1 },
                    new TriodeStageDesign("Ch 1 V5B", TriodeModel.Ecc83, PreampRail, 100e3, 1.5e3, 22e-6) {
                        SupplyNode = 1
                    },
                    new NetworkStageDesign("Ch 1 V6B", ChannelOneThirdStage()) { SupplyNode = 1 }
                ]
            ) { SupplyNode = 1 },
            // Its own switch, so that the stack stays a stage the Tone Stack Location can move.
            new ChannelSwitchStageDesign(
                "Stack",
                SwitchRole.Switch3,
                [new NetworkStageDesign("Stack", LeadStack()) { SupplyNode = 1, Gate = Gate }],
                [new NetworkStageDesign("Ch 1 Stack", ChannelOneStack()) { SupplyNode = 1 }]
            ) { SupplyNode = 1 },
            new NetworkStageDesign("V7A", Follower()),
            new NetworkStageDesign("V7B", ReturnAndMaster())
        ],
        Power,
        OutputTrim
    ) {
        Panel = AmpPanel.Standard with {
            Middle = "Mid Open",
            Depth = "Depth Punch",
            Cut = "Gate Threshold",
            Control1 = "Mid Focused",
            Control2 = "Channel Volume",
            Switch1 = "Focused",
            Switch2 = "Gain Hi",
            Switch3 = "Channel 1",
            Fat = "Bottom"
        },
        // From CE10 (V7 and the inverter): R26 4.7k to CE3 10 µF (V5, V6), as drawn. The drawing prints no voltages,
        // so each of the four triodes on CE3 draws an equal share of the preamp's 6 mA, 0.75 mA.
        PreampSupply = new([new(4.7e3, 10e-6, 3e-3)])
    };

    /// <summary>The block's output trim, which evens out the models' loudness.</summary>
    const double OutputTrim = -7;

    /// <summary>
    ///     V5A and channel 2's gain: 1M grid leak; 100k plate; 1k ‖ 22 µF; 47 nF, 2.2M to ground; 1 nF, 470k to
    ///     ground, 100k into the 1M audio gain, 100 pF from the 100k's top to the wiper, the wiper on V5B's grid with
    ///     470k and 1M to ground. Channel 1's network loads the 47 nF: 1.5 nF into 470k and its 100k + 1M.
    /// </summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (coupled, idle, cut, top) = (FirstStage(net), net.Node(), net.Node(), net.Node());
        net.Capacitor(coupled, idle, 1.5e-9);
        net.Resistor(idle, StageNetlist.Ground, Parallel(470e3, 1.1e6));
        net.Capacitor(coupled, cut, 1e-9);
        net.Resistor(cut, StageNetlist.Ground, 470e3);
        net.Resistor(cut, top, 100e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(cut, wiper, 100e-12);
        net.Resistor(wiper, StageNetlist.Ground, Parallel(470e3, 1e6));
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V5A up to its coupling cap, on <paramref name="net" />: R1 1M grid leak; R3 100k; R2 1k ‖ CE1 22 µF; C1
    ///     47 nF with R4 2.2M to ground. Returns the node after the cap.
    /// </summary>
    static int FirstStage(StageNetlist net) {
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        var plate = net.CommonCathode(net.Input, TriodeModel.Ecc83, PreampRail, 100e3, 1e3, 22e-6);
        var coupled = net.Node();
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        return coupled;
    }

    /// <summary>
    ///     V6A: 47 nF and 2.2M; the Lo network (470 pF, R11 220k over R12 470k with about 330 pF across it) or, in Hi,
    ///     the Hi one (2.2 nF, R13 100k over R14 470k with 1 nF across it), both always loading; channel 1's dividers
    ///     as loads (1M ‖ 100 pF over 22k; 1 nF into 470k + 470k); 470k grid leak; 330k ‖ 1 nF plate; 3.3k ‖ 1 µF.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (coupled, loTop, lo, hiTop, hi, clean, crunch) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, loTop, 470e-12);
        net.Resistor(loTop, lo, 220e3);
        net.Resistor(lo, StageNetlist.Ground, 470e3);
        net.Capacitor(lo, StageNetlist.Ground, 330e-12);
        net.Capacitor(coupled, hiTop, 2.2e-9);
        net.Resistor(hiTop, hi, 100e3);
        net.Resistor(hi, StageNetlist.Ground, 470e3);
        net.Capacitor(hi, StageNetlist.Ground, 1e-9);
        net.Resistor(coupled, clean, 1e6);
        net.Capacitor(coupled, clean, 100e-12);
        net.Resistor(clean, StageNetlist.Ground, 22e3);
        net.Capacitor(coupled, crunch, 1e-9);
        net.Resistor(crunch, StageNetlist.Ground, 940e3);
        net.SwitchedResistor(lo, grid, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(hi, grid, 0, 1, SwitchRole.Switch2);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Supply(supply, PreampRail);
        net.Resistor(supply, plate, 330e3);
        net.Capacitor(supply, plate, 1e-9, CapacitorRole.Plate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V6B: 47 nF, 470k to ground, 220k into the grid with 1M to ground; 100k plate; 1.5k ‖ 22 µF.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 470e3);
        net.Resistor(coupled, grid, 220e3);
        LastStage(net, grid);
        return net;
    }

    /// <summary>
    ///     V6B from its grid, on <paramref name="net" />: R22 1M grid leak; R24 100k; R23 1.5k ‖ CE5 22 µF. Its plate
    ///     is the output.
    /// </summary>
    static void LastStage(StageNetlist net, int grid) {
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Output = net.CommonCathode(grid, TriodeModel.Ecc83, PreampRail, 100e3, 1.5e3, 22e-6);
    }

    /// <summary>
    ///     A stack's input, on <paramref name="net" />: C6 100 nF, 2.2M to ground, and R25 1M into the noise gate's
    ///     detector (R76 100k ‖ R77 470k). Returns the node after the cap.
    /// </summary>
    static int StackInput(StageNetlist net) {
        var coupled = net.Node();
        net.Capacitor(net.Input, coupled, 100e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, StageNetlist.Ground, 1e6 + 82.5e3);
        return coupled;
    }

    /// <summary>
    ///     The lead stack: the input (<see cref="StackInput" />); C13 220 pF (and C12 220 pF more in Focused) to the
    ///     250k linear treble's top; R31 100k slope (R30 220k across it in Focused); C14 22 nF to the 1M audio bass
    ///     rheostat's top; the bass's bottom on the top of the Open middle pot (20k linear, C16 22 nF from the slope on
    ///     its wiper) or of the Focused one (its own 20k, C15 10 nF); from the treble wiper R33 470k into the Lo Lead
    ///     volume and R35 470k into the Hi Lead volume (250k linear each, 100k across each), of which Switch 2 takes
    ///     one; with Bottom, R32 330k from the bass pot's top, 10 nF to ground, and R34 and R36 (680k each) into the
    ///     volumes' tops.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var coupled = StackInput(net);
        var (slope, trebleTop, bassTop, middleTop, open, focused, openCap, focusedCap) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        var (bottom, loTop, hiTop, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(coupled, trebleTop, 220e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 220e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 100e3);
        net.SwitchedResistor(coupled, slope, 0, 220e3, SwitchRole.Switch1);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 22e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);

        net.SwitchedResistor(middleTop, open, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(middleTop, focused, 0, 1, SwitchRole.Switch1);
        net.SwitchedResistor(slope, openCap, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(slope, focusedCap, 0, 1, SwitchRole.Switch1);
        var openWiper = net.Pot(open, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        var focusedWiper = net.Pot(focused, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Control1);
        net.Capacitor(openCap, openWiper, 22e-9, CapacitorRole.Stack);
        net.Capacitor(focusedCap, focusedWiper, 10e-9, CapacitorRole.Stack);

        net.Resistor(bassTop, bottom, 330e3);
        net.Capacitor(bottom, StageNetlist.Ground, 10e-9);
        foreach (var top in new[] { loTop, hiTop }) {
            net.Resistor(treble, top, 470e3);
            net.Resistor(top, StageNetlist.Ground, 100e3);
            net.SwitchedResistor(bottom, top, 0, 680e3, SwitchRole.Fat);
        }

        var lo = net.Pot(loTop, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        var hi = net.Pot(hiTop, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        net.SwitchedResistor(lo, output, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(hi, output, 0, 1, SwitchRole.Switch2);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     Channel 1's V5A and gain: V5A up to its coupling cap; channel 2's 1 nF into its 470k and 100k + 1M as a
    ///     load; C2 1.5 nF (a second 1.5 nF across it with Bottom), 470k to ground, R5 100k and the 1M audio gain P1,
    ///     its wiper on V5B's grid with 470k and 1M to ground; with Bright, C3 100 pF from the 1.5 nF to the wiper.
    /// </summary>
    static StageNetlist ChannelOneGain() {
        var net = new StageNetlist();
        var (coupled, idle, cut, top) = (FirstStage(net), net.Node(), net.Node(), net.Node());
        net.Capacitor(coupled, idle, 1e-9);
        net.Resistor(idle, StageNetlist.Ground, Parallel(470e3, 1.1e6));
        net.Capacitor(coupled, cut, 1.5e-9);
        net.SwitchedCapacitor(coupled, cut, 1.5e-9, SwitchRole.Fat);
        net.Resistor(cut, StageNetlist.Ground, 470e3);
        net.Resistor(cut, top, 100e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(cut, wiper, 100e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(wiper, StageNetlist.Ground, Parallel(470e3, 1e6));
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     Channel 1's V6B: 47 nF and 2.2M; channel 2's networks as loads (470 pF into 220k + 470k, 2.2 nF into 100k +
    ///     470k); the Clean divider (R15 1M ‖ 100 pF over R17 22k) or, with Switch 2, the Crunch one (1 nF, R18 470k
    ///     over R16 470k), both always loading; R21 220k from the selected tap into the grid with 1M to ground; 100k
    ///     plate; 1.5k ‖ 22 µF.
    /// </summary>
    static StageNetlist ChannelOneThirdStage() {
        var net = new StageNetlist();
        var (coupled, lo, hi, clean, crunchTop, crunch, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, lo, 470e-12);
        net.Resistor(lo, StageNetlist.Ground, 690e3);
        net.Capacitor(coupled, hi, 2.2e-9);
        net.Resistor(hi, StageNetlist.Ground, 570e3);
        net.Resistor(coupled, clean, 1e6);
        net.Capacitor(coupled, clean, 100e-12);
        net.Resistor(clean, StageNetlist.Ground, 22e3);
        net.Capacitor(coupled, crunchTop, 1e-9);
        net.Resistor(crunchTop, crunch, 470e3);
        net.Resistor(crunch, StageNetlist.Ground, 470e3);
        net.SwitchedResistor(clean, grid, 220e3, 0, SwitchRole.Switch2);
        net.SwitchedResistor(crunch, grid, 0, 220e3, SwitchRole.Switch2);
        LastStage(net, grid);
        return net;
    }

    /// <summary>
    ///     Channel 1's stack: the input (<see cref="StackInput" />); C8 470 pF (and C7 470 pF more in Focused) to the
    ///     250k linear treble's top; R27 100k slope; C9 22 nF to the 1M audio bass rheostat's top; the bass's bottom on
    ///     the top of the 20k linear middle, with C10 10 nF (and C11 10 nF more in Open) from the slope on its wiper;
    ///     from the treble wiper R28 220k into the 250k linear Clean volume and R29 330k into the 250k audio Crunch
    ///     volume, of which Switch 2 takes one.
    /// </summary>
    static StageNetlist ChannelOneStack() {
        var net = new StageNetlist();
        var coupled = StackInput(net);
        var (slope, trebleTop, bassTop, middleTop, cleanTop, crunchTop, output) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 470e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 100e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 22e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 10e-9, CapacitorRole.Stack);
        net.SwitchedCapacitor(slope, middle, 0, SwitchRole.Switch1, CapacitorRole.Stack, 10e-9);

        net.Resistor(treble, cleanTop, 220e3);
        net.Resistor(treble, crunchTop, 330e3);
        var clean = net.Pot(cleanTop, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        var crunch = net.Pot(crunchTop, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        net.SwitchedResistor(clean, output, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(crunch, output, 0, 1, SwitchRole.Switch2);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V7A, the loop's send follower: 220k into the grid with 100k to ground; 100k ‖ 10 nF to the 410 V rail; 1k
    ///     cathode; 4.7 µF and 10k into V7B's 100k grid leak.
    /// </summary>
    static StageNetlist Follower() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, send, ret) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, 10e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, send, 4.7e-6);
        net.Resistor(send, ret, 10e3);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Output = ret;
        return net;
    }

    /// <summary>V7B, the return stage (100k, 1.5k ‖ 22 µF), and 100 nF, 100k into the 1M linear master.</summary>
    static StageNetlist ReturnAndMaster() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        return net;
    }

    static double Parallel(double a, double b) => a * b / (a + b);
}
