using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall AFD100 (Slash signature, 100 W), both modes. The High input (1M at the jack, both 68k in parallel) or
///     the Low input (68k, with the other 68k to ground) into V5, both triodes of an ECC83 in parallel (4M7 grid leak,
///     51k plate, 1k33 with 2 × 680 nF, 220 pF plate to cathode), 22 nF and 470k ‖ 2.2 nF onto the 1M audio Gain with
///     its 1 nF bright cap; V6A (100k, 10k), 22 nF into 470k ‖ 470 pF over 470k; V6B (100k, 10k with 10k + 100 nF
///     across it), 22 nF into 470k over 470k; V7A (4M7 leak, 100k with 1 nF across it, 820 Ω with 680 nF), DC-coupled
///     to the V7B follower (100k), whose cathode drives the stack directly (470 pF, 33k, 22 nF, 22 nF; 250k linear
///     treble, 1M audio bass, 25k linear middle with the middle cap on its wiper); the treble wiper's 300k onto 560k ‖
///     100 pF, 22 nF into the V8A follower (100k grid leak bootstrapped from the junction of its 1k and 100k cathode
///     resistors); 330 nF to the send divider (15k over 1k), 100k into V8B (1M leak; 150k, 1k5 with 22 µF), 150 nF out
///     and back through 470 nF, 100k ‖ 22 pF to V8B's grid (4M7 to ground at the 470 nF): a unity-gain shunt-feedback
///     recovery stage; 120k onto the 1M audio Master. A 12AX7 long-tail pair (22 nF in, 1M grid leaks, 470 Ω and 22k
///     over 4k7, 82k / 100k with 470 pF between the plates, the second grid AC-coupled by 100 nF to the feedback node),
///     22 nF out, 220k bias feeds, 5k6 stoppers, four 6550 with 1k screens; feedback through 10 µF, 10k and the Power
///     pot's 100k track into the 4k7, the 25k linear Presence with 100 nF across it.
/// </summary>
/// <remarks>
///     <para>
///         Source: Marshall drawing AFD100-60-02 issue 1, "MAIN" (drawn 10-6-10), the main board's whole circuit:
///         marshall_afd100_main_sch.pdf, a vector PDF. Every value above is read from it. The modes are relays: the
///         front-panel AFD switch drives CTL1, whose transistors (TR20, TR21) energise RL1 and RL3 together. Contacts
///         are drawn at rest. At rest RL1B feeds V7A from V6B's divider and RL3B leaves the node between R80 (150k,
///         from V6A's grid) and C57 (100 nF, to V6A's cathode) floating: R80 and C57 in series bootstrap V6A's grid,
///         whose 10k cathode is unbypassed. Energised, RL1B takes V7A's grid to V6A's divider, past V6B, RL1C shorts
///         V6B's divider with 47 Ω, and RL3B grounds that node, so R80 loads the Gain's wiper with 150k and C57
///         bypasses V6A's cathode above 160 Hz. The model takes the energised state as the AFD mode (the switch and its
///         LED are labelled AFD, the rest state #34): #34 then has the extra gain stage, about 4.5 dB more gain at 1
///         kHz below clipping and one more clipping stage, and AFD is the brighter (its partial bypass). That
///         assignment is the one inference here; the controller between the switch and CTL1 is not on the drawing.
///     </para>
///     <para>
///         Stages and cuts: the coupling network after each clipping plate belongs to the next stage (so a grid's
///         current charges its own coupling cap); V7A and the V7B follower with the stack are one circuit (the
///         follower's grid current and cut-off into the stack); the stack is cut at its 300k / 560k node, ahead of the
///         V8A follower; the send is cut ahead of the 100k into V8B (the stage's input resistance). Against the whole
///         chain in one circuit: −27 dB (#34) and −23 dB (AFD) at small signals, −24 and −20 dB at 50 mV. The AFD
///         mode's V6A plate, its cathode bypassed above 160 Hz, has an output resistance that moves with frequency;
///         V8A's bootstrapped grid (about 1.4M) loads the stack. Folding V8A into the V7 circuit (three triodes)
///         brought #34 to −34 dB but cost 40 % more CPU; splitting V7A from its follower saved 15 % and lost 10 dB in
///         clipping. CPU (48 kHz, guitar / loud noise, with the Plexi at 8.4 / 14.8 %): #34 17.4 / 29.0 %, AFD 16.4 /
///         25.5 %, with Newton's tolerance at 1e-5 and chord steps in every stage.
///     </para>
///     <para>
///         The effects loop's relay (RL2, CTL2) is at rest, the loop off: RL2B feeds V8B from the 100k, and RL2C shorts
///         R96 (4M7), so the 100k ‖ 22 pF feedback reaches V8B's grid directly. Loop on, the return would drive the
///         grid and R96 would lift the feedback; Cathode's own loop blocks stand in for it.
///     </para>
///     <para>
///         Not on the drawing, so guessed: B+ (470 V, as the JCM800 2203; the drawing gives no voltages), the idle
///         current (40 mA a tube: the bias is a servo per tube, TL072s holding each 1 Ω cathode sense resistor at a set
///         point the drawing does not give), the output transformer (1.7k plate to plate, as the 2203) and the feedback
///         tap (4 Ω, as the 2203's 100k), the reservoir's source resistance. The preamp rails follow from the drawing's
///         dropping chain (4k7 to the inverter's HT3, then 10k to each of HT4 to HT7, 47 µF on each) with the stages'
///         idle currents through the model's tubes: 423, 351, 303, 284 and 272 V at 470 V and about 10 mA in all.
///     </para>
///     <para>
///         Simplified: the Power control (VR7, 100k linear, two gangs: one in the feedback path, the other setting a
///         MOSFET regulator, M1, that feeds the screens) is at full power, its whole track in the feedback (110k in
///         all) and the regulator in dropout, so the screens sit on the main supply (through the standby contacts, a
///         few ohms); turned down, the screen voltage and the output fall and the feedback rises, which is not
///         modelled. The 470 pF between the inverter's plates is the power amp's cut cap with no pot; the feedback
///         reaches the second grid through 100 nF (the drawing's C73) as the library's differential feedback; 10 µF in
///         the feedback path (a corner below 1 Hz) is left out. The High jack's 1M sits across the ideal source and is
///         left out. In the AFD mode V6B still idles with its grid on V6A's divider; its grid would draw current only
///         above about 2.7 V there, where V7A's (1 V bias) already holds the node, so it is left out.
///     </para>
///     <para>
///         Panel: Switch 1 is the AFD switch (off: #34), Switch 2 the Low input. The board calls the Gain "VOL". The
///         Power control and the loop switch are not on the panel.
///     </para>
/// </remarks>
public static class MarshallAfd100 {
    const double Inverter = 423;
    const double Ht4 = 351;
    const double Ht5 = 303;
    const double Ht6 = 284;
    const double Ht7 = 272;

    public static AmpDesign Design { get; } = new(
        "AFD100",
        [
            Stage("V5", FirstStage()),
            new ChannelSwitchStageDesign(
                "Mode",
                SwitchRole.Switch1,
                [
                    Stage("V6A #34", SecondStage(false)),
                    Stage("V6B", ThirdStage()),
                    Stage("V7 #34", FourthStage(false))
                ],
                [
                    Stage("V6A AFD", SecondStage(true)),
                    Stage("V7 AFD", FourthStage(true))
                ]
            ),
            Stage("V8A", SendStage()),
            Stage("V8B", RecoveryStage(), 100e3)
        ],
        new(
            "4 x 6550",
            PentodeModel.SixtyFiveFifty,
            2,
            470,
            1e3,
            0.040,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, Inverter, 82e3, 100e3, 22e3, 470),
            22e-9,
            220e3,
            5.6e3,
            470e-12,
            new(1, PotTaper.Linear),
            new(Math.Sqrt(0.5), 110e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 50e-6, 10, 50e-6, 4.7e3, 47e-6, 10e-3, RectifierModel.Silicon),
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "AFD",
            Switch2 = "Low Input"
        }
    };

    /// <summary>
    ///     A stage with the solver shortcuts of docs/06 §5.3: Newton's step tolerance at 1e-5 and chord steps on the
    ///     last factorisation.
    /// </summary>
    static NetworkStageDesign Stage(string name, StageNetlist net, double inputResistance = 0) =>
        new(name, net, inputResistance) { Tolerance = 1e-5, ReuseFactors = true };

    /// <summary>
    ///     The input jacks and V5, both triodes in parallel (4M7 grid leak, 51k, 1k33 with 1.36 µF, 220 pF plate to
    ///     cathode). Out at the plate. The High input is 68k ‖ 68k to the grid (the jack's 1M sits across the ideal
    ///     source, so it is left out); the Low input (switch 2) 68k, with the High jack's 68k from the grid to ground.
    /// </summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedResistor(net.Input, grid, 34e3, 68e3, SwitchRole.Switch2);
        net.SwitchedResistor(grid, StageNetlist.Ground, 0, 68e3, SwitchRole.Switch2);
        net.Resistor(grid, StageNetlist.Ground, 4.7e6);
        net.Supply(rail, Ht7);
        net.Resistor(rail, plate, 51e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.33e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1.36e-6);
        net.Capacitor(plate, cathode, 220e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83.Paralleled(2));
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     From V5's plate: 22 nF, 470k ‖ 2.2 nF onto the 1M audio Gain (1 nF from its top to its wiper), V6A on the
    ///     wiper (100k, 10k), with R80 (150k) and C57 (100 nF) in series from its grid to its cathode (#34), or their
    ///     junction grounded (<paramref name="afd" />: 150k on the wiper, 100 nF across the 10k). Out at the plate.
    /// </summary>
    static StageNetlist SecondStage(bool afd) {
        var net = new StageNetlist();
        var (coupled, top, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, top, 470e3);
        net.Capacitor(coupled, top, 2.2e-9);
        var grid = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, grid, 1e-9, CapacitorRole.Bright);
        net.Supply(rail, Ht6);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        if (afd) {
            net.Resistor(grid, StageNetlist.Ground, 150e3);
            net.Capacitor(cathode, StageNetlist.Ground, 0.1e-6);
        } else {
            var junction = net.Node();
            net.Resistor(grid, junction, 150e3);
            net.Capacitor(junction, cathode, 0.1e-6);
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     #34's extra stage: from V6A's plate, 22 nF into 470k ‖ 470 pF over 470k, V6B on the tap (100k, 10k with 10k
    ///     + 100 nF across it). Out at the plate.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, bypass) = (net.Node(), net.Node(), net.Node(), net.Node());
        var grid = Divider(net, net.Input, 470e-12, 470e3);
        net.Supply(rail, Ht6);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        net.Capacitor(cathode, bypass, 0.1e-6);
        net.Resistor(bypass, StageNetlist.Ground, 10e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     22 nF from <paramref name="plate" /> into 470k (with <paramref name="bypass" /> across it, if any) over
    ///     <paramref name="leak" /> (470k, or 470k ‖ 4M7 at V7A's grid); returns the tap.
    /// </summary>
    static int Divider(StageNetlist net, int plate, double bypass, double leak) {
        var (coupled, tap) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, tap, 470e3);
        if (bypass > 0) {
            net.Capacitor(coupled, tap, bypass);
        }

        net.Resistor(tap, StageNetlist.Ground, leak);
        return tap;
    }

    /// <summary>
    ///     V7A on its divider, from V6B's plate (#34: 22 nF into 470k over 470k) or V6A's (<paramref name="afd" />: 22
    ///     nF into 470k ‖ 470 pF over 470k), its 4M7 grid leak; V7A (100k with 1 nF across it, 820 Ω with 680 nF) and
    ///     the V7B follower DC-coupled to its plate (<see cref="Follower" />). Out at the stack's 300k / 560k node.
    /// </summary>
    static StageNetlist FourthStage(bool afd) {
        var net = new StageNetlist();
        var grid7 = Divider(net, net.Input, afd ? 470e-12 : 0, 470e3 * 4.7e6 / (470e3 + 4.7e6));
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht5);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 1e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 680e-9);
        net.Triode(plate, grid7, cathode, TriodeModel.Ecc83);
        net.Output = Follower(net, rail, plate);
        return net;
    }

    /// <summary>
    ///     The V7B follower, its grid on V7A's <paramref name="plate" />, 100k from its cathode, which drives the stack
    ///     directly (470 pF, 33k, 22 nF, 22 nF; 250k linear treble, 1M audio bass, 25k linear middle with the middle
    ///     cap on its wiper); the treble wiper's 300k onto 560k ‖ 100 pF. Returns that node.
    /// </summary>
    static int Follower(StageNetlist net, int rail, int plate) {
        var follower = net.Node();
        net.Triode(rail, plate, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);

        var (trebleTop, slope, bassTop, middleTop, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(follower, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(follower, slope, 33e3);
        net.Capacitor(slope, bassTop, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Resistor(treble, output, 300e3);
        net.Resistor(output, StageNetlist.Ground, 560e3);
        net.Capacitor(output, StageNetlist.Ground, 100e-12);
        return output;
    }

    /// <summary>
    ///     22 nF into the V8A follower (1k and 100k from its cathode, the 100k grid leak from their junction), 330 nF
    ///     to the send divider (15k over 1k). Out at the send.
    /// </summary>
    static StageNetlist SendStage() {
        var net = new StageNetlist();
        var (rail, grid, cathode, junction, send) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht4);
        net.Capacitor(net.Input, grid, 22e-9);
        net.Resistor(grid, junction, 100e3);
        net.Resistor(cathode, junction, 1e3);
        net.Resistor(junction, StageNetlist.Ground, 100e3);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, send, 330e-9);
        net.Resistor(send, StageNetlist.Ground, 16e3);
        net.Output = send;
        return net;
    }

    /// <summary>
    ///     From the send, after the 100k (the stage's input resistance): V8B (1M grid leak; 150k, 1k5 with 22 µF); 150
    ///     nF from its plate to node N, from which 470 nF (4M7 to ground) and 100k ‖ 22 pF return to its grid; 120k
    ///     onto the 1M audio Master. Out at the Master's wiper.
    /// </summary>
    static StageNetlist RecoveryStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, n, m, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht4);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(rail, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, n, 150e-9);
        net.Capacitor(n, m, 470e-9);
        net.Resistor(m, StageNetlist.Ground, 4.7e6);
        net.Resistor(m, net.Input, 100e3);
        net.Capacitor(m, net.Input, 22e-12);
        net.Resistor(n, top, 120e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
