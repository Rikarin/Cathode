using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Splawn Quick Rod, the two-channel version with three 12AX7s in the preamp: the overdrive channel with its OD1 /
///     OD2 switch, its "1st/2nd gear" and "3rd gear" switches and the solo boost, and the clean channel. The input (1M,
///     68k) drives V1A (220k plate, 1k5 ‖ 1 µF), whose 22 nF feeds both channels at once. Overdrive: 470k ‖ 1 nF into
///     the 1M Gain pot, 47k from the coupling cap to ground in OD1; the wiper straight to V1B (100k plate), 22 nF and
///     470k ‖ 1 nF into V2A's grid (220k and 1 nF to ground, and 68k in 1st gear); V2A (100k ‖ 1 nF plate, 2k7,
///     bypassed by 1 µF in 3rd gear), 22 nF, 330k into V2B's grid (150k leak); V2B (100k, 2k7 ‖ 1 µF), 100 nF into the
///     1M Master, whose foot is grounded or, with the boost on, sits on the 1M Solo Boost pot wired as a rheostat; the
///     Master's wiper drives the tone stack (470 pF into the 250k Treble with 1 nF from its top to ground; 47k slope,
///     220 nF to the 1M Bass rheostat, 22 nF to the wiper of the 25k Middle, whose top is the Bass's foot) and the
///     Treble's wiper, through 100k, the phase inverter. Clean: 470 pF / 47k / 220 nF / 22 nF stack with 250k Treble,
///     1M Bass and 6k8 in the middle's place, the Treble's wiper onto the 1M Clean Gain pot with 1 nF from its top to
///     its wiper, both halves of V3 in parallel (100k plate), 22 nF into the 1M Volume, whose wiper the channel switch
///     takes to the inverter. V1B and the two V3 halves share one cathode: 1k5 ‖ 1 µF.
/// </summary>
/// <remarks>
///     <para>
///         Source: the preamp drawing "Splawn QuickRod, Sheet 1/1" (a clean CAD drawing, file "Splawn Quickrod Preamp
///         Schematic.pdf"), every value above read from it: three 12AX7 (12AX7_1 to 12AX7_3, heaters on 6.3 V AC), the
///         rail from 420 V DC through two 10k droppers (R7, R20) with 47 µF at each node (C12 and C23 at 420 V for V3,
///         C25 and C26 for V2, C11 for V1), the switches OD1/OD2, 1ST/2ND_GEAR, 3RD_GEAR, BOOST and CHANNEL_SWITCH, and
///         the 1 nF / 500 V across R13 (its designator, C7, is printed over C26's value). It is the two-channel Quick
///         Rod (a clean channel switched in at the inverter's input); with the inverter, the four-12AX7 amp.
///     </para>
///     <para>
///         Guessed (the drawing ends at "OUT_TO_P.I."): the whole power amp, taken from the JCM800 2203 (four EL34,
///         470 V B+, 35 mA a tube, 1k screens, 5k6 stoppers, a 12AX7 long-tail pair with 82k / 100k, 470 and 10k, 22 nF
///         out to 220k, 22 nF and 1M in, 100k of feedback from the 4 Ω tap into 4k7 with the 22k Presence and 0.1 µF,
///         1.7k plate to plate), with the inverter on the drawing's 420 V node, and the dropper from B+ to that node (7k2)
///         sized for 420 V at the preamp's and inverter's 6.5 mA; the 4 × 12 closed load (Vintage 30). Also guessed:
///         which position of each switch is which (OD1 grounds the 47k, 1st gear the 68k; the drawing's two gear
///         switches act independently, so 3rd gear with the first switch in 1st is possible too), the pots' tapers
///         (Gain, Master, Solo Boost, Bass and Volume audio, Treble and Middle linear; the drawing gives none), and the
///         preamp nodes' DC (V2 at 386 V and V1 at 371 V: the droppers' drop at the triodes' own currents, 4.6 mA in
///         all, solved with the library's 12AX7).
///     </para>
///     <para>
///         Simplified: none of the drawn preamp. V1A drives both channels' networks in each, and V1B and V3 share their
///         cathode in each, so each channel's first netlist holds all three: the idle channel's pots sit at noon (its
///         Volume, in the overdrive channel, only loads V3's coupling cap: 1M either way). The two V3 halves are one
///         paralleled triode. The overdrive chain is cut at V2A's grid: with 2nd and 3rd gear V2A's Miller capacitance
///         against that node's reactive source puts the chain −37 and −29 dB from the uncut circuit (−45 dB in 1st
///         gear). V2A in the first netlist too (four triodes) stays within −55 dB but takes about a fifth more CPU on
///         guitar and a third more on loud noise, past the high-gain budget. Both channels' first netlists take chord
///         steps and a 1e-5 Newton tolerance.
///     </para>
///     <para>
///         Panel: Switch 1 selects the clean channel; Bright is OD2, Switch 2 and Switch 3 the "2nd gear" and "3rd
///         gear" switches (both off: 1st gear); Control 1 the Solo Boost pot, which at 0 is the boost switched off (the
///         switch grounds the Master's foot, where the pot at 0 puts its wiper). Gain, Treble and Bass are the playing
///         channel's; Master is the overdrive channel's Master and the clean channel's Volume; Middle is the overdrive
///         channel's only.
///     </para>
/// </remarks>
public static class QuickRod {
    const double Rail = 420;
    const double V2Rail = 386;
    const double V1Rail = 371;
    const double GridPlate = 1.7e-12;

    static readonly Pot Gain = new(1e6, PotTaper.Audio);
    static readonly Pot Treble = new(250e3, PotTaper.Linear);
    static readonly Pot Bass = new(1e6, PotTaper.Audio);

    public static AmpDesign Design { get; } = new(
        "Quick Rod",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1 + V3", Front(false)) {
                        MaxIterations = 64, ReuseFactors = true, Tolerance = 1e-5
                    },
                    new NetworkStageDesign("V2A", SecondStage()),
                    new NetworkStageDesign("V2B", StackStage())
                ],
                [
                    new NetworkStageDesign("V1 + V3", Front(true)) {
                        MaxIterations = 64, ReuseFactors = true, Tolerance = 1e-5
                    }
                ]
            )
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, Rail, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback,
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 100e-6, 7.2e3, 50e-6, 6.5e-3),
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = "OD2",
            Switch1 = "Clean",
            Switch2 = "2nd Gear",
            Switch3 = "3rd Gear",
            Control1 = "Solo Boost"
        }
    };

    /// <summary>
    ///     A pot that no knob turns here (the idle channel's), at half rotation between <paramref name="top" /> and
    ///     <paramref name="bottom" />; returns its wiper.
    /// </summary>
    static int Idle(StageNetlist net, int top, int bottom, Pot pot) {
        var wiper = net.Node();
        var fraction = pot.Fraction(0.5);
        net.Resistor(top, wiper, (1 - fraction) * pot.Ohms);
        net.Resistor(wiper, bottom, fraction * pot.Ohms);
        return wiper;
    }

    /// <summary>
    ///     The three triodes that share circuit: the input (1M, 68k), V1A and its 22 nF onto both channels' networks;
    ///     the overdrive channel's (470k ‖ 1 nF into the Gain pot, the 47k of OD1) into V1B, whose plate couples
    ///     through 22 nF and 470k ‖ 1 nF into V2A's grid (220k and 1 nF to ground, 68k in 1st gear), the output when
    ///     <paramref name="clean" /> is false; the clean channel's stack and Clean Gain pot into the paralleled V3,
    ///     whose plate couples through 22 nF into the 1M Volume, whose wiper is the output when it is true. V1B and V3 on
    ///     their shared 1k5 ‖ 1 µF.
    /// </summary>
    static StageNetlist Front(bool clean) {
        var net = new StageNetlist();
        var (rail, v1Rail, grid, plate, cathode, coupled) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Rail);
        net.Supply(v1Rail, V1Rail);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 68e3);
        net.Resistor(v1Rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Capacitor(plate, coupled, 22e-9);

        // The overdrive channel's input: 470k ‖ 1 nF onto the Gain pot; OD1 grounds the 47k.
        var gainTop = net.Node();
        net.Resistor(coupled, gainTop, 470e3);
        net.Capacitor(coupled, gainTop, 1e-9);
        net.SwitchedResistor(coupled, StageNetlist.Ground, 47e3, 0, SwitchRole.Bright);
        var gain = clean ? Idle(net, gainTop, StageNetlist.Ground, Gain) :
            net.Pot(gainTop, StageNetlist.Ground, Gain, PotRole.Gain);

        // The clean channel's stack: 470 pF, 47k, 220 nF and 22 nF; 6k8 in the middle's place.
        var (trebleTop, slope, bassTop, bassFoot) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 47e3);
        net.Capacitor(slope, bassTop, 220e-9, CapacitorRole.Stack);
        net.Capacitor(slope, bassFoot, 22e-9, CapacitorRole.Stack);
        net.Resistor(bassFoot, StageNetlist.Ground, 6.8e3);
        int treble, cleanGain;
        if (clean) {
            treble = net.Pot(trebleTop, bassTop, Treble, PotRole.Treble);
            net.Rheostat(bassTop, bassFoot, Bass, PotRole.Bass);
            cleanGain = net.Pot(treble, StageNetlist.Ground, Gain, PotRole.Gain);
        } else {
            treble = Idle(net, trebleTop, bassTop, Treble);
            net.Resistor(bassTop, bassFoot, Bass.Fraction(0.5) * Bass.Ohms);
            cleanGain = Idle(net, treble, StageNetlist.Ground, Gain);
        }

        net.Capacitor(treble, cleanGain, 1e-9, CapacitorRole.Bright);

        // V1B and the paralleled V3 on their shared cathode.
        var (odPlate, cleanPlate, shared) = (net.Node(), net.Node(), net.Node());
        net.Resistor(v1Rail, odPlate, 100e3);
        net.Resistor(rail, cleanPlate, 100e3);
        net.CathodeResistor(shared, StageNetlist.Ground, 1.5e3);
        net.Capacitor(shared, StageNetlist.Ground, 1e-6);
        net.Triode(odPlate, gain, shared, TriodeModel.Ecc83);
        net.Capacitor(gain, odPlate, GridPlate);
        net.Triode(cleanPlate, cleanGain, shared, TriodeModel.Ecc83.Paralleled(2));
        net.Capacitor(cleanGain, cleanPlate, 2 * GridPlate);

        // V1B's coupling into V2A's grid.
        var (odCoupled, v2Grid) = (net.Node(), net.Node());
        net.Capacitor(odPlate, odCoupled, 22e-9);
        net.Resistor(odCoupled, v2Grid, 470e3);
        net.Capacitor(odCoupled, v2Grid, 1e-9);
        net.Resistor(v2Grid, StageNetlist.Ground, 220e3);
        net.Capacitor(v2Grid, StageNetlist.Ground, 1e-9);
        net.SwitchedResistor(v2Grid, StageNetlist.Ground, 68e3, 0, SwitchRole.Switch2);

        // V3's coupling into the Volume.
        var volumeTop = net.Node();
        net.Capacitor(cleanPlate, volumeTop, 22e-9);
        if (clean) {
            net.Output = net.Pot(volumeTop, StageNetlist.Ground, Gain, PotRole.Master);
            return net;
        }

        net.Resistor(volumeTop, StageNetlist.Ground, Gain.Ohms);
        net.Output = v2Grid;
        return net;
    }

    /// <summary>
    ///     V2A from its grid: 100k ‖ 1 nF plate, 2k7 and (3rd gear) 1 µF; 22 nF and 330k into V2B's grid, 150k to
    ///     ground (the output).
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, grid) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V2Rail);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 1e-9, CapacitorRole.Plate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 1e-6, SwitchRole.Switch3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, GridPlate);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, grid, 330e3);
        net.Resistor(grid, StageNetlist.Ground, 150e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2B from its grid (100k, 2k7 ‖ 1 µF), 100 nF into the Master, whose foot the boost switch grounds or (on, at
    ///     the Solo Boost pot above 0) lifts onto the Solo Boost rheostat; the tone stack on the Master's wiper; the
    ///     Treble's wiper through 100k (the output, which the channel switch takes to the inverter).
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (grid, rail, plate, cathode, masterTop, masterFoot) =
            (net.Input, net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V2Rail);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Capacitor(plate, masterTop, 100e-9);
        var master = net.Pot(masterTop, masterFoot, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Rheostat(masterFoot, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);

        var (trebleTop, slope, bassTop, middleTop, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(master, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Capacitor(trebleTop, StageNetlist.Ground, 1e-9);
        net.Resistor(master, slope, 47e3);
        net.Capacitor(slope, bassTop, 220e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, Treble, PotRole.Treble);
        net.Rheostat(bassTop, middleTop, Bass, PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Resistor(treble, output, 100e3);
        net.Output = output;
        return net;
    }
}
