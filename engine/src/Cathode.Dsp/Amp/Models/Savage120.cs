using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Savage 120 (E610), lead channel, Rough and Smooth. V1B (100k, 1.5k ‖ 22 µF), 10 nF and 100k into the 1M audio gain; V2A
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
///         is not known; LD16's adds to the 3.3k); Hi Balance has no knob here and sits at noon (900k above the wiper,
///         100k below).
///     </para>
///     <para>
///         Rough is kept as first modelled, three readings short of the drawing, so that existing presets do not
///         move: V3A's grid network still loads the junction (the dark LD17 isolates it), R38 33k is not in series
///         with V3B's grid (relay RE4B puts it there in Lead; it was once read as a crunch-mode pad), and the Crunch 2
///         volume's 250k, whose top is tied to the lead volume's, is not across it.
///     </para>
///     <para>
///         Not modelled: the clean channel (V1A, V4A, V4B and their own stack), Crunch 1, Crunch 2 (RE4 at rest sends
///         V2A through R33 100k straight to V3B's grid and grounds V2B's output), and a Hi Balance knob.
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

    public static AmpDesign Design { get; } = new(
        "Savage 120",
        [
            new NetworkStageDesign("V1B", InputAndGain()) { SupplyNode = 3 },
            new NetworkStageDesign("V2A", SecondStage()) { SupplyNode = 2 },
            new ChannelSwitchStageDesign(
                "V2B",
                SwitchRole.Switch3,
                [new NetworkStageDesign("V2B", ThirdStage()) { SupplyNode = 2 }],
                [new NetworkStageDesign("V2B Smooth", SmoothThirdStage()) { SupplyNode = 2 }]
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
        new(
            "4 x 6550",
            PentodeModel.SixtyFiveFifty,
            2,
            560,
            1e3,
            0.05,
            false,
            0,
            120,
            new LongTailPairDesign(TriodeModel.Ecc83, InverterRail, 82e3, 100e3, 22e3, 470),
            47e-9,
            150e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 26.5e3, 4.8e3, new(50e3, PotTaper.Linear), 10e-9),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            // R108 470 Ω 5 W to the screens' CE20 100 µF, R106 + R105 (2.2k each) to CE21 22 µF.
            new(25, 220e-6, 470, 100e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon),
            InverterInput: new(100e-9, 100e3)
        ),
        -9
    ) {
        Panel = AmpPanel.Standard with {
            Depth = "Depth Boost",
            Cut = null,
            Bright = null,
            Control1 = "Lead",
            Control2 = "Lead Volume",
            Switch1 = "Contour",
            Switch2 = "Lead Boost",
            Switch3 = "Smooth"
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
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, V2Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, top, 100e3);
        net.Resistor(top, StageNetlist.Ground, 2.2e6);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
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
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 220e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 47e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, junction, 100e3);
        net.Resistor(junction, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Resistor(junction, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        return net;
    }

    /// <summary>
    ///     The stack in Smooth: the same stack; the treble wiper into R46 100k; at that junction the Hi Balance pot's
    ///     wiper (P17 1M audio at noon, 900k above and 100k below: C27 220 pF from the stack's input to its top, C28
    ///     10 nF from its bottom to ground) and R48 100k to V3A's grid (R49 470k leak); V3A (100k, 1.5k ‖ 22 µF); C29
    ///     47 nF into R52 100k over R53 4.7k, and across the 4.7k the 250k audio lead volume and the Crunch 2 volume's
    ///     250k. R47 hangs open.
    /// </summary>
    static StageNetlist SmoothStack() {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middle, junction) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (balanceTop, balanceBottom, grid, supply, plate, cathode, pad, top) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 220e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 47e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, junction, 100e3);

        net.Capacitor(coupled, balanceTop, 220e-12);
        net.Resistor(balanceTop, junction, 900e3);
        net.Resistor(junction, balanceBottom, 100e3);
        net.Capacitor(balanceBottom, StageNetlist.Ground, 10e-9);

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
