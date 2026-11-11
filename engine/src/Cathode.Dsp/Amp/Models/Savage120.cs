using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Savage 120 (E610), channel 2: the lead, Rough and Smooth, and Crunch 2 on the Bright switch (channel 1 is
///     <see cref="Clean" />). V1B (100k, 1.5k ‖ 22 µF), 10 nF and 100k into the 1M audio gain; V2A
///     (100k, 1k ‖ 22 µF); 47 nF with 2.2M to ground, 100k into the 250k audio Lead pot (2.2M to ground before it), its
///     wiper on V2B's grid (1 nF to ground with Lead Boost); V2B (330k, 3.3k, bypassed by 10 µF with Lead Boost); 47 nF
///     into V3B's 1M grid leaks; V3B (100k, 1.5k ‖ 22 µF); 47 nF and 2.2M into the stack (470 pF, 220 pF more with
///     Contour; 47k slope; 47 nF, 22 nF; 250k linear treble, 1M audio bass, 20k linear middle); 100k, 100k into V3A's
///     idle grid network (470k) and the 250k audio lead volume; the loop's send follower V5A (1k cathode) and return
///     stage V5B (100k, 1k ‖ 22 µF), 100 nF and 47k into the 1M linear master; a long-tail pair (82k/100k, 470 Ω, 22k
///     tail, 100 nF to the second grid) and four 6550, two a side (2.2k stoppers, 1k screens, 150k bias feeds). Smooth
///     (Switch 3) pads V3B's grid (33k in series, 3.3k to ground) and puts V3A (100k, 1.5k ‖ 22 µF), its 100k / 4.7k
///     output pad and the Hi Balance network between the stack and the lead volume.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Engl factory drawing "Savage 120 type E610", drawing 610-2 (ENGL Gerätebau GmbH, two sheets: the
///         whole amp, then the relay logic),
///         https://el34world.com/charts/Schematics/files/Engl/Engl_savage_120_sch.pdf. Every value above is read from
///         it. An earlier hand-drawn factory sheet
///         (https://el34world.com/charts/Schematics/files/Engl/Engl_savage_120.pdf) was not compared value by value.
///     </para>
///     <para>
///         The output stage is four 6550 in parallel pairs, from Engl's E610 specification (power amp: 4 × 6550, KT88
///         optional); the two-6550 amp of the family is the Savage 60 (E630). It was once modelled as one 6550 a side
///         into 3.4k, half the real stage (issue #49); the full stage peaks 1.4 dB hotter, so the output trim is -9 dB
///         (was -7.5).
///     </para>
///     <para>
///         The supply's decoupling as drawn: R108 470 Ω 5 W to the screens' CE20 100 µF, R106 and R105 (2.2k each) to
///         CE21 22 µF (the inverter), R107 1k to V5 (no cap of its own: here on CE21), R56 2.2k 1 W to CE4 10 µF (V3,
///         V4), R55 2.2k to CE9 10 µF (V2), R54 2.2k to CE6 10 µF (V1).
///     </para>
///     <para>
///         Guesses and simplifications, marked: the drawing prints no rail voltages (B+ 560 V, idle 50 mA a tube, a
///         1.7k plate-to-plate primary and the 100 W class's 25 Ω supply are typical values for four 6550 at 120 W; the
///         preamp's rails, 530 V at CE21 down to 511 V at CE6, are the drawn chain's drops at the assumed draws);
///         the loop is empty, its follower driving V5B through the 10k balance resistor. The feedback network is
///         simplified: the
///         presence pot sits in series with the feedback on the real amp (turning it down lowers the feedback); here
///         1.5k plus half the 50k presence from the 8 Ω tap into a 4.8k shunt (1.5k and 3.3k), the presence's 10 nF
///         across it, and the Depth Boost switch (10 nF across the feedback path) as the depth control.
///     </para>
///     <para>
///         Rough and Smooth (Switch 3; off is Rough). The R/S relay RE6 is drawn at rest with the stack's R47 100k
///         straight on the lead volume and V3A out of the path. Its driver also lights three LT1011 optocouplers (LD15,
///         LD16 and LD17, in series from +24 V to the coil's switched end), the Hi Balance relay RE10 and the panel's
///         R/S LED. Engl's manual says that LED lights in Smooth, so the rest position is Rough and the energised one
///         Smooth (this model once called its one mode Smooth). Smooth as drawn: LD16 puts R40 3.3k from V3B's grid to
///         ground, behind R38 33k from V2B's coupling cap, a 21 dB pad that also loads V2B's plate; LD17 joins V3A's
///         grid (R48 100k, R49 470k leak) to the junction after the treble wiper's R46 100k; V3A (R51 100k, R50 1.5k ‖
///         CE11 22 µF) feeds C29 47 nF into R52 100k over R53 4.7k, which RE6A puts on the volumes; RE10A hangs the Hi
///         Balance pot's wiper on the same junction (P17 1M audio, C27 220 pF from the stack's input to its top, C28
///         10 nF from its bottom to ground). Guesses: a lit optocoupler is taken as closed (the LT1011's on resistance
///         is not known; LD16's adds to the 3.3k). Hi Balance is the Cut knob, shown while Smooth is on; its default,
///         noon (900k above the wiper, 100k below), is where the model had it fixed.
///     </para>
///     <para>
///         Rough is kept as first modelled, three readings short of the drawing, so that existing presets do not
///         move: V3A's grid network still loads the junction (the dark LD17 isolates it), R38 33k is not in series
///         with V3B's grid (relay RE4B puts it there in Lead; it was once read as a crunch-mode pad), and the Crunch 2
///         volume's 250k, whose top is tied to the lead volume's, is not across it.
///     </para>
///     <para>
///         Crunch 2 (the Bright switch; off is Lead). The "CR/LE" relays RE4 and RE5 are drawn at rest in Crunch: RE4A
///         sends V2A's output through R33 100k straight to V3B's grid, past the Lead pot and V2B; RE4B grounds V2B's
///         output; RE5A takes the Crunch 2 volume's wiper (P19, 250k audio, its top tied to the lead volume's) in
///         place of the lead volume's. Here the one Lead Volume knob serves both, and the Lead knob and Lead Boost do
///         nothing in Crunch 2. Rough and Smooth work in it as in the lead.
///     </para>
/// </remarks>
public static class Savage120 {
    // The rails at rest, from the drawn chain at the assumed draws (no voltages are printed): CE21, the inverter and
    // V5, 556.9 V at CE20 less 4.4k × 6 mA; CE4 (V3) less 3.2k × 4 mA; CE9 (V2) less 2.2k × 2 mA; CE6 (V1) less 2.2k ×
    // 1 mA. They were once taken as 380 V for every stage and 400 V for the inverter, which the drawn droppers cannot
    // give from a 560 V B+ at any plausible draw (380 V would need about 30k at 6 mA).
    const double InverterRail = 530;
    const double V3Rail = 518;
    const double V2Rail = 513;
    const double V1Rail = 511;

    /// <summary>The Clean type's output trim, which evens out the models' loudness.</summary>
    const double CleanTrim = -4;

    public static AmpDesign Design { get; } = new(
        "Savage 120",
        [
            new NetworkStageDesign("V1B", InputAndGain()) { SupplyNode = 3 },
            new ChannelSwitchStageDesign(
                "Crunch 2",
                SwitchRole.Bright,
                [
                    new NetworkStageDesign("V2A", SecondStage()) { SupplyNode = 2 },
                    new ChannelSwitchStageDesign(
                        "V2B",
                        SwitchRole.Switch3,
                        [new NetworkStageDesign("V2B", ThirdStage()) { SupplyNode = 2 }],
                        [new NetworkStageDesign("V2B Smooth", SmoothThirdStage()) { SupplyNode = 2 }]
                    ) { SupplyNode = 2 }
                ],
                [new NetworkStageDesign("V2A Crunch 2", CrunchTwoStage()) { SupplyNode = 2 }]
            ) { SupplyNode = 2 },
            new TriodeStageDesign("V3B", TriodeModel.Ecc83, V3Rail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 },
            new ChannelSwitchStageDesign(
                "Stack",
                SwitchRole.Switch3,
                [new NetworkStageDesign("Stack", LeadStack()) { SupplyNode = 1 }],
                [new NetworkStageDesign("Stack and V3A", SmoothStack()) { SupplyNode = 1 }]
            ) { SupplyNode = 1 },
            new NetworkStageDesign("V5A", Follower()),
            new NetworkStageDesign("V5B", ReturnAndMaster())
        ],
        EnglParts.Power(
            new("4 x 6550", PentodeModel.SixtyFiveFifty, 560, 1e3, 0.05, 120, 1.7e3),
            // The 22k tail stands on the 4.8k feedback shunt (1.5k and 3.3k): 26.8k from the bias junction to ground.
            new(InverterRail, 26.8e3, 150e3),
            new(1, 26.5e3, 4.8e3, new(50e3, PotTaper.Linear), 10e-9),
            // R108 470 Ω 5 W to the screens' CE20 100 µF, R106 + R105 (2.2k each) to CE21 22 µF.
            new(25, 220e-6, 470, 100e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon)
        ),
        -9
    ) {
        Panel = AmpPanel.Standard with {
            Depth = "Depth Boost",
            Cut = "Hi Balance",
            Bright = "Crunch 2",
            Control1 = "Lead",
            Control2 = "Lead Volume",
            Switch1 = "Contour",
            Switch2 = "Lead Boost",
            Switch3 = "Smooth",
            // Noon is the network the model had while Hi Balance was fixed: presets from then keep their sound.
            Defaults = new Dictionary<string, float> { ["cut"] = 5 },
            // The Hi Balance relay closes with Smooth.
            BehindSwitch = new Dictionary<string, string> { ["cut"] = "switch3" }
        },
        PreampSupply = Decoupling
    };

    /// <summary>
    ///     Channel 1 of the same amp, Clean and (Switch 3) Crunch 1, from the same drawing: the input through R88 100k
    ///     into V1A (R3 100k, R2 1.5k ‖ CE1 22 µF); C1 47 nF and R4 2.2M; the Preshape switch S3A takes the gain's feed
    ///     from C3 470 pF and R6 22k over the junction of C4 10 nF and R7 1.5k (drawn at rest) or from C2 10 nF; R9
    ///     100k into the 1M audio gain P1, C5 100 pF across both with Bright; V4A (R11 100k, R10 1.5k ‖ CE2 22 µF); C6
    ///     47 nF. Clean (relays RE2 and RE3 at rest) goes from there into the stack; Crunch 1 through R16 100k (R13
    ///     2.2M to ground) and the 250k audio Crunch 1 pot P2 into V4B (R19 100k, R18 1k ‖ CE3 22 µF) and C9 47 nF
    ///     (R20 2.2M) first. The stack has the lead's values (C10 470 pF, C8 220 pF more with Contour; R22 47k; C11
    ///     47 nF, C12 22 nF; 250k linear treble, 1M audio bass, 20k linear middle; R21 2.2M at its input); R24 100k
    ///     from the treble wiper to the 250k linear Clean volume P8, and R25 100k on from there to the 250k audio
    ///     Crunch volume P9; relay RE1B puts the selected wiper on the loop's send follower V5A, shared with channel 2
    ///     as is everything after it.
    /// </summary>
    /// <remarks>
    ///     Guesses and simplifications: Preshape off is taken as the switch's drawn position; one Volume knob for the
    ///     Clean and Crunch volumes, both of which stay in circuit; the Sense switch S1A (R90 47k from V1A's grid to
    ///     ground, an input pad) is left out, as the Input Trim does that; channel 2's V1B grid hangs on the same
    ///     jack and loads nothing. V1A runs on CE6 with V1B, V4 on CE4 with V3.
    /// </remarks>
    public static AmpDesign Clean { get; } = new(
        "Savage 120 Clean",
        [
            new NetworkStageDesign("V1A", CleanInput()) { SupplyNode = 3 },
            new ChannelSwitchStageDesign(
                "Crunch 1",
                SwitchRole.Switch3,
                [new TriodeStageDesign("V4A", TriodeModel.Ecc83, V3Rail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 }],
                [
                    new TriodeStageDesign("V4A", TriodeModel.Ecc83, V3Rail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 },
                    new NetworkStageDesign("V4B", CrunchOneStage()) { SupplyNode = 1 }
                ]
            ) { SupplyNode = 1 },
            new ChannelSwitchStageDesign(
                "Stack",
                SwitchRole.Switch3,
                [new NetworkStageDesign("Clean Stack", ChannelOneStack(false)) { SupplyNode = 1 }],
                [new NetworkStageDesign("Crunch Stack", ChannelOneStack(true)) { SupplyNode = 1 }]
            ) { SupplyNode = 1 },
            new NetworkStageDesign("V5A", Follower()),
            new NetworkStageDesign("V5B", ReturnAndMaster())
        ],
        Design.Power,
        CleanTrim
    ) {
        Panel = AmpPanel.Standard with {
            Depth = "Depth Boost",
            Cut = null,
            Control1 = "Crunch 1 Gain",
            Control2 = "Volume",
            Switch1 = "Contour",
            Switch2 = "Preshape",
            Switch3 = "Crunch 1",
            BehindSwitch = new Dictionary<string, string> { ["control1"] = "switch3" }
        },
        PreampSupply = Decoupling
    };

    /// <summary>
    ///     The drawing's chain below CE21 (the inverter, and V5 behind R107 1k with no cap of its own, here on CE21):
    ///     R107 + R56 (3.2k) to CE4 10 µF (V3, V4), R55 2.2k to CE9 10 µF (V2), R54 2.2k to CE6 10 µF (V1). The drawing
    ///     prints no voltages, so each node draws an equal share of the preamp's 6 mA per triode on it (twelve in all,
    ///     the inverter's two included): 0.5 mA each.
    /// </summary>
    static PreampSupplyDesign Decoupling =>
        new([new(3.2e3, 10e-6, 2e-3), new(2.2e3, 10e-6, 1e-3, 1), new(2.2e3, 10e-6, 1e-3, 2)]);

    /// <summary>V1B and the gain: 1M grid leak; 100k plate; 1.5k ‖ 22 µF; 10 nF, 100k into the 1M audio gain.</summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, V1Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 10e-9);
        net.Resistor(coupled, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     V2A and the Lead pot: 100k plate; 1k ‖ 22 µF; 47 nF, 2.2M to ground, 100k into the 250k audio Lead with 2.2M
    ///     to ground at its top.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (coupled, top) = (SecondTriode(net), net.Node());
        net.Resistor(coupled, top, 100e3);
        net.Resistor(top, StageNetlist.Ground, 2.2e6);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
        return net;
    }

    /// <summary>
    ///     V2A on <paramref name="net" />, its grid the input: R31 100k; R30 1k ‖ CE7 22 µF; C16 47 nF with R32 2.2M to
    ///     ground. Returns the node after the cap.
    /// </summary>
    static int SecondTriode(StageNetlist net) {
        var plate = net.CommonCathode(net.Input, TriodeModel.Ecc83, V2Rail, 100e3, 1e3, 22e-6);
        var coupled = net.Node();
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        return coupled;
    }

    /// <summary>
    ///     V2A in Crunch 2 (the "CR/LE" relay RE4 at rest): the same stage, then R33 100k straight to V3B's grid (R41
    ///     1M to ground; in Smooth also R40 3.3k through the lit LD16), past the Lead pot and V2B, whose output RE4B
    ///     grounds. The optocoupler LD15 between R33 and the grid is taken as closed.
    /// </summary>
    static StageNetlist CrunchTwoStage() {
        var net = new StageNetlist();
        var (coupled, grid) = (SecondTriode(net), net.Node());
        net.Resistor(coupled, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.SwitchedResistor(grid, StageNetlist.Ground, 0, 3.3e3, SwitchRole.Switch3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2B: 1 nF from the grid to ground with Lead Boost; 330k plate; 3.3k, bypassed by 10 µF with Lead Boost; 47
    ///     nF into V3B's grid leaks (1M and 1M).
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedCapacitor(net.Input, StageNetlist.Ground, 1e-9, SwitchRole.Switch2);
        net.Supply(supply, V2Rail);
        net.Resistor(supply, plate, 330e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 10e-6, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 47e-9);
        net.Resistor(output, StageNetlist.Ground, 500e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V2B in Smooth: the same stage (1 nF at the grid and the 10 µF bypass with Lead Boost; 330k; 3.3k); 47 nF,
    ///     R39 1M to ground, R38 33k to V3B's grid, where R41 1M and (through the lit LD16) R40 3.3k go to ground.
    /// </summary>
    static StageNetlist SmoothThirdStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, grid) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedCapacitor(net.Input, StageNetlist.Ground, 1e-9, SwitchRole.Switch2);
        net.Supply(supply, V2Rail);
        net.Resistor(supply, plate, 330e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 10e-6, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, grid, 33e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Resistor(grid, StageNetlist.Ground, 3.3e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     The stack: 47 nF and 2.2M to ground; 470 pF (and 220 pF more with Contour) to the 250k linear treble's top;
    ///     47k slope; 47 nF to the 1M audio bass rheostat, 22 nF to the 20k linear middle; the treble wiper into 100k,
    ///     loaded there by V3A's idle grid network (100k into 470k), and 100k on into the 250k audio lead volume.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middle, junction, grid, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        var treble = ToneStack(net, coupled, slope, trebleTop, bassTop, middle);
        net.Resistor(treble, junction, 100e3);
        net.Resistor(junction, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Resistor(junction, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        return net;
    }

    /// <summary>
    ///     The tone stack on <paramref name="net" />, from its input node <paramref name="coupled" />: 470 pF (and
    ///     220 pF more with Contour) to the 250k linear treble's top; 47k slope; 47 nF to the 1M audio bass rheostat,
    ///     22 nF to the 20k linear middle. Channel 1's stack has the same values (C10, C8, R22, C11, C12, P5, P3, P4).
    ///     Returns the treble wiper.
    /// </summary>
    static int ToneStack(StageNetlist net, int coupled, int slope, int trebleTop, int bassTop, int middle) {
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 220e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 47e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        return treble;
    }

    /// <summary>
    ///     V1A, the Preshape network and the gain (see <see cref="Clean" />).
    /// </summary>
    static StageNetlist CleanInput() {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 100e3);
        var plate = net.CommonCathode(grid, TriodeModel.Ecc83, V1Rail, 100e3, 1.5e3, 22e-6);
        var (coupled, shaped, shunt, full, top, potTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, shaped, 470e-12);
        net.Resistor(shaped, shunt, 22e3);
        net.Capacitor(coupled, shunt, 10e-9);
        net.Resistor(shunt, StageNetlist.Ground, 1.5e3);
        net.Capacitor(coupled, full, 10e-9);
        net.SwitchedResistor(shaped, top, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(full, top, 0, 1, SwitchRole.Switch2);
        net.Resistor(top, potTop, 100e3);
        var wiper = net.Pot(potTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(top, wiper, 100e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     Crunch 1's V4B: C6 47 nF, R13 2.2M to ground, R16 100k into the 250k audio Crunch 1 pot P2, its wiper on the
    ///     grid; R19 100k; R18 1k ‖ CE3 22 µF.
    /// </summary>
    static StageNetlist CrunchOneStage() {
        var net = new StageNetlist();
        var (coupled, top) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, top, 100e3);
        var grid = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
        net.Output = net.CommonCathode(grid, TriodeModel.Ecc83, V3Rail, 100e3, 1e3, 22e-6);
        return net;
    }

    /// <summary>
    ///     Channel 1's stack and volumes: 47 nF (C6 from V4A in Clean, C9 from V4B in Crunch 1) and 2.2M to ground
    ///     (R21, and R20 beside it in Crunch 1); the stack; R24 100k from the treble wiper to the 250k linear Clean
    ///     volume, R25 100k on to the 250k audio Crunch volume. The output is the wiper of the mode that plays.
    /// </summary>
    static StageNetlist ChannelOneStack(bool crunch) {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middle, cleanTop, crunchTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, crunch ? 1.1e6 : 2.2e6);
        var treble = ToneStack(net, coupled, slope, trebleTop, bassTop, middle);
        net.Resistor(treble, cleanTop, 100e3);
        net.Resistor(cleanTop, crunchTop, 100e3);
        var clean = net.Pot(cleanTop, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        var crunchWiper = net.Pot(crunchTop, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        net.Output = crunch ? crunchWiper : clean;
        return net;
    }

    /// <summary>
    ///     The stack in Smooth: the same stack; the treble wiper into R46 100k; at that junction the Hi Balance pot's
    ///     wiper (P17 1M audio, the Cut knob; at noon 900k above and 100k below: C27 220 pF from the stack's input to
    ///     its top, C28 10 nF from its bottom to ground) and R48 100k to V3A's grid (R49 470k leak); V3A (100k, 1.5k ‖
    ///     22 µF);
    ///     C29
    ///     47 nF into R52 100k over R53 4.7k, and across the 4.7k the 250k audio lead volume and the Crunch 2 volume's
    ///     250k. R47 hangs open.
    /// </summary>
    static StageNetlist SmoothStack() {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (balanceTop, balanceBottom, grid, supply, plate, cathode, pad, top) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        var treble = ToneStack(net, coupled, slope, trebleTop, bassTop, middle);
        net.Capacitor(coupled, balanceTop, 220e-12);
        var junction = net.Pot(balanceTop, balanceBottom, new(1e6, PotTaper.Audio), PotRole.Cut);
        net.Capacitor(balanceBottom, StageNetlist.Ground, 10e-9);
        net.Resistor(treble, junction, 100e3);

        net.Resistor(junction, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Supply(supply, V3Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, pad, 47e-9);
        net.Resistor(pad, top, 100e3);
        net.Resistor(top, StageNetlist.Ground, 4.7e3);
        net.Resistor(top, StageNetlist.Ground, 250e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        return net;
    }

    /// <summary>
    ///     V5A, the loop's send follower: 100k grid leak; 100k ‖ 47 nF to the rail; 1k cathode; 1 µF and the balance's
    ///     10k into V5B's 100k grid leak.
    /// </summary>
    static StageNetlist Follower() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, send, ret) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 1);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, 47e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, send, 1e-6);
        net.Resistor(send, ret, 10e3);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Output = ret;
        return net;
    }

    /// <summary>V5B, the return stage (100k, 1k ‖ 22 µF), and 100 nF, 47k into the 1M linear master.</summary>
    static StageNetlist ReturnAndMaster() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, top, 47e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
