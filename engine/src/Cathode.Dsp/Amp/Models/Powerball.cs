using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Powerball (E645), channel 2 (lead) in its Lo and Hi modes. V5A (100k, 1k ‖ 22 µF) into 47 nF and 2.2M, the
///     channel-2 gain (100k into 1M audio; channel 1's 15 nF, 100k and 1M gain network loads the same node) and V5B's
///     1M grid leak; V5B (100k, 1.5k ‖ 22 µF); 47 nF and 2.2M, the Lo/Hi divider (220k/470k, or 100k/470k in Hi;
///     channel 1's 1M + 220k and 680k + 330k branches load it) into V6A (330k, 3.3k ‖ 1 µF); 47 nF and 220k into V6B
///     (1M leak; 100k, 1.5k ‖ 22 µF); 100 nF and 1M (into the noise gate's 82k) into the lead stack (470 pF, 940 pF in
///     Focused; 100k slope; 220 nF bass cap, 1M audio bass rheostat; the middle: a 20k linear pot under the bass with
///     22 nF on its wiper, Open, or its own 20k with 47 nF, Focused; 250k linear treble), 470k into the 250k linear
///     lead volume, 220k into 100k at the V7A cathode follower (100k ‖ 10 nF plate, 1k cathode), 4.7 µF and the loop's
///     10k into V7B (100k leak; 100k, 1.5k ‖ 22 µF), 100 nF and 100k into the 1M linear master (1M from its wiper to
///     ground), 100 nF into a long-tail pair (100k grid leak to the bias junction; 82k/100k, 470 Ω, 22k tail, a 4.7k
///     feedback shunt) and four 6L6GC (2.2k stoppers, 470 Ω screens, 47 nF, 330k bias feeds) at 440 V.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Engl factory drawing "645-1" (ENGL Gerätebau GmbH, two sheets: the whole amp, then the switching
///         logic), https://el34world.com/charts/Schematics/files/Engl/Engl_powerball.pdf; a poster on the Music
///         Electronics Forum (https://music-electronics-forum.com/filedata/fetch?id=812301) says it came from Engl.
///         Every value above is read from it (B+ 460 V in standby, 440 V running; bias 28–34 mA a tube). Forum posters
///         say Engl's published drawings have omissions; this one is internally consistent for channel 2.
///     </para>
///     <para>
///         The supply's decoupling as drawn: R136 470 Ω to the 430 V screens' CE9 47 µF, R137 and R138 (2.2k each) to
///         CE10 22 µF (V7, the inverter; three ZY150 zeners across it hold it under 450 V in standby), R26 4.7k 1 W to
///         CE3 10 µF (V5, V6).
///     </para>
///     <para>
///         As drawn, the model motorboats (16–22 Hz) after a loud input stops, down to gain 1: four fully bypassed
///         stages (34, 36, 32 and 34 dB at 1 kHz, as their values predict) share CE3, and the loop through the supply
///         has about 14 dB of small-signal gain at 18 Hz at gain 10 (the Savage 120 latches only with a tenth of its
///         chain's caps). Nothing on the drawing breaks that loop (docs/06 §3.7), so the drawing likely leaves out part
///         of the real amp's preamp filtering.
///     </para>
///     <para>
///         <b>Assumption, not on 645-1:</b> the model adds two RC nodes below CE3, each 10k into 22 µF (the generic
///         chain's step): V5B and V6A on the first, V5A on the second, V6B left on CE3. They are the smallest values
///         that a search (docs/06 §3.7) found silent in every case of the after-the-stop test and at gain 1 with
///         master 5, with room to spare.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the preamp rails carry no voltages on the drawing (here 400 V for V5 and
///         V6, 410 V for V7, 420 V for the inverter, from the chain above); Hi mode is
///         taken as the energised position of the "Gain" relays RE3 and RE4 (the hotter divider and the Hi Lead volume;
///         one Lead Volume knob here for both); the low/high lead bright switches S3/S4 (330k + 680k from the bass pot's
///         top into the volume) are off; the parallel loop is empty with its balance fully dry (the 10k series only).
///         Left out: the lead channel's noise gate (IC4, an LM324 full-wave detector on V6B's output through 1M and
///         82k, a comparator against the 20k threshold trimmer P20, and the J175 T8 shunting the lead volume's wiper
///         to ground through 4.7k below the threshold), whose setting the drawing does not give, and the J175 soft
///         mute T1 on the same node, which acts only while switching channels. Relay RE1B (once read here as switching
///         V6B's cathode) grounds V6A's output in channel 1 and lights channel 2's LDR. The feedback network is
///         simplified to 22k + 1.5k from the speaker into the 4.7k shunt with the 50k presence and 100 nF across it,
///         the depth punch (1M with 10 nF) as the depth control; the tap and primary (1.9k) are typical values.
///     </para>
/// </remarks>
public static class Powerball {
    public static AmpDesign Design { get; } = new(
        "Powerball",
        [
            new NetworkStageDesign("V5A", InputAndGain()) { SupplyNode = 3 },
            new TriodeStageDesign("V5B", TriodeModel.Ecc83, 400, 100e3, 1.5e3, 22e-6) { SupplyNode = 2 },
            new NetworkStageDesign("V6A", ThirdStage()) { SupplyNode = 2 },
            new NetworkStageDesign("V6B", FourthStage()) { SupplyNode = 1 },
            new NetworkStageDesign("Stack", LeadStack()) { SupplyNode = 1 },
            new NetworkStageDesign("V7A", Follower()),
            new NetworkStageDesign("V7B", ReturnAndMaster())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            440,
            470,
            0.031,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 420, 82e3, 100e3, 22e3, 470),
            47e-9,
            330e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 23.5e3, 4.7e3, new(50e3, PotTaper.ReverseAudio), 0.1e-6),
            true,
            new(1.9e3),
            SpeakerDesign.Closed4x12,
            new(30, 220e-6, 470, 47e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon),
            InverterInput: new(100e-9, 100e3)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Middle = "Mid Open",
            Depth = "Depth Punch",
            Cut = null,
            Bright = null,
            Control1 = "Mid Focused",
            Control2 = "Lead Volume",
            Switch1 = "Focused",
            Switch2 = "Hi Lead"
        },
        // From CE10 (V7 and the inverter): R26 4.7k to CE3 10 µF (V5, V6 as drawn). The drawing prints no voltages,
        // so each of the four triodes below CE3 draws an equal share of the preamp's 6 mA, 0.75 mA.
        // ASSUMPTION, not on Engl 645-1: two more nodes, 10k into 22 µF under CE3 for V5B and V6A, and 10k into
        // 22 µF under that for V5A (CE3 keeps V6B). As drawn, with all four on CE3, the model motorboats after loud
        // playing down to gain 1, and no drawn part can stop it (docs/06 §3.7), so the real amp must filter its first
        // stages more than the drawing shows. 10k / 22 µF is the generic chain's step and the smallest search value
        // with a margin: 18 µF still passes, 15 µF and 10 µF fail; one extra node fails or passes erratically.
        PreampSupply = new(
            [
                new(4.7e3, 10e-6, 0.75e-3), new(10e3, 22e-6, 1.5e-3, 1), new(10e3, 22e-6, 0.75e-3, 2)
            ]
        )
    };

    /// <summary>
    ///     V5A and channel 2's gain: 1M grid leak; 100k plate; 1k ‖ 22 µF; 47 nF, 2.2M to ground, channel 1's C2 15 nF
    ///     into its 100k + 1M (Bottom off) to ground, 100k into the 1M audio gain, its wiper on V5B's grid with 1M to
    ///     ground.
    /// </summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, clean, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 400);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, clean, 15e-9);
        net.Resistor(clean, StageNetlist.Ground, 1.1e6);
        net.Resistor(coupled, top, 100e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V6A: 47 nF; 2.2M, channel 1's 1M + 220k and 680k + 330k to ground; the Lo divider (220k into 470k) or, in Hi,
    ///     the Hi divider (100k into 470k), both always loading; 330k plate; 3.3k ‖ 1 µF.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (coupled, lo, hi, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1 / (1 / 2.2e6 + 1 / 1.22e6 + 1 / 1.01e6));
        net.Resistor(coupled, lo, 220e3);
        net.Resistor(lo, StageNetlist.Ground, 470e3);
        net.Resistor(coupled, hi, 100e3);
        net.Resistor(hi, StageNetlist.Ground, 470e3);
        net.SwitchedResistor(lo, grid, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(hi, grid, 0, 1, SwitchRole.Switch2);
        net.Supply(supply, 400);
        net.Resistor(supply, plate, 330e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V6B: 47 nF, 220k into the grid with 1M to ground; 100k plate; 1.5k ‖ 22 µF.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 400);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead stack: 100 nF, then R25 1M into the noise gate's R76 82k (its 100 nF and 470k across it) to ground;
    ///     470 pF (and 470 pF more in Focused) to the 250k linear treble's top; 100k slope; 220 nF to the 1M audio bass
    ///     rheostat's top; the bass's bottom on the top of the Open middle pot (20k linear, 22 nF from the slope on its
    ///     wiper) or of the Focused one (its own 20k, 47 nF); 470k from the treble wiper into the 250k linear lead
    ///     volume.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middleTop, open, focused, openCap, focusedCap, top) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Capacitor(net.Input, coupled, 100e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6 + 82e3 * 470e3 / (82e3 + 470e3));
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 470e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 100e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 220e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);

        net.SwitchedResistor(middleTop, open, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(middleTop, focused, 0, 1, SwitchRole.Switch1);
        net.SwitchedResistor(slope, openCap, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(slope, focusedCap, 0, 1, SwitchRole.Switch1);
        var openWiper = net.Pot(open, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        var focusedWiper = net.Pot(focused, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Control1);
        net.Capacitor(openCap, openWiper, 22e-9, CapacitorRole.Stack);
        net.Capacitor(focusedCap, focusedWiper, 47e-9, CapacitorRole.Stack);

        net.Resistor(treble, top, 470e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
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
        net.Supply(supply, 410);
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
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
