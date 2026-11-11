using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Ampeg SVT-CL (SVT Classic, 1994, 300 W): V1A (12AX7 half, 100k, 1.5k with 22 µF) behind the 0 dB and −15 dB
///     inputs; the Ultra Lo network into the 1M Gain; V1B (100k, 3k unbypassed, which Ultra Hi bypasses with 33 nF);
///     the passive Baxandall; V2A (100k, 3.7k unbypassed) with the Midrange pot between its cathode and its output
///     divider, the pot's wiper to ground through the tapped 800 mH coil and the cap the five-way Frequency switch
///     picks; the 50k Master; V2B as a cathode follower. The power amp: a 12AX7 input stage taking the feedback at its
///     cathode, a 12AX7 cathodyne, 12AU7 voltage amplifiers and 12AU7 cathode followers DC-coupled through 47k to six
///     6550 (three a side) in fixed bias, on a silicon bridge.
/// </summary>
/// <remarks>
///     <para>
///         Sources: SLM Electronics' factory drawings. Preamp 07S519-11 rev 1 (plotted 05/18/94) and the pictorial's
///         parts list, in thetubestore.com's SVT-Classic-Audio-Schematic.pdf (a 150 dpi scan, read at 300 to 600 dpi):
///         every preamp value, the 15 mV / 0.9 V signal voltages and the tubes' DC voltages. Power amp 07S419 (sheet
///         "part no. 07S419-12", the board with Slave, Preamp Out and Power Amp In jacks; rev B, 2000), its supply
///         sheet and the tube board 07S419-41, a vector PDF at el34world.com ("Ampeg_SVT-2PRO _ IIPRO _ SVT-CL.pdf"
///         and "..._2.pdf"); thetubestore's scan carries the same power amp as the SVT-CL's own sheet 07S419-03. The
///         feedback network is on the alternate speaker board, 07P319-14 (el34world, "Ampeg_svt_cl_altskrbrd.pdf"):
///         68k with 100 pF across it, from the 4 Ω tap. The owner's manual gives the Frequency switch's centres (220
///         Hz, 450 Hz, 800 Hz, 1.6 kHz, 3 kHz) and the Ultra switches' figures (Ultra Lo +2 dB at 40 Hz and −10 dB at
///         500 Hz, Ultra Hi +9 dB at 8 kHz).
///     </para>
///     <para>
///         The inputs: the 0 dB jack's tip goes through R1 (22k) to the grid, and R3 (100k) joins the grid to the −15
///         dB jack's tip. Each jack's switch contact acts when it is empty: the −15 dB jack's ties its tip to the 0 dB
///         tip (R3 across R1: 18k in series), the 0 dB jack's grounds its tip (so a guitar in the −15 dB jack sees
///         100k in series and 22k to ground, −14.9 dB).
///     </para>
///     <para>
///         Not on the drawings: the coil's tap inductances and its winding resistance. The Frequency switch puts 0.68
///         µF or 0.15 µF on tap 1 (the whole 800 mH: 216 Hz and 459 Hz), 0.1 µF or 0.033 µF on tap 2 and 0.033 µF on
///         tap 3. The model takes the taps at two thirds and one third of the turns (356 mH and 89 mH: 844 Hz, 1.47
///         kHz and 2.94 kHz), which is the even spacing that comes nearest the manual's 800 Hz, 1.6 kHz and 3 kHz; no
///         winding resistance. The 470k from each cap's switched end to ground (they keep the switch from popping) are
///         left out. The output transformer's primary impedance is not printed either: 1.85k plate to plate follows
///         from the tube board's 372 V rms on each plate at full output and 300 W. Its other figures (inductances,
///         copper, capacitance) are the 1970 SVT model's guesses, as is the supply's source resistance.
///     </para>
///     <para>
///         The supply: 660 V on the plates from a bridge into 100 µF + 100 µF in series; the screens from a winding of
///         their own (380 V on 47 µF, through a diode and 22 Ω a tube; the tube board's sheet labels that line 345 V,
///         the power amp's sheet draws it from the 380 V node). As in the 1970 SVT model the screen winding is a
///         stand-in, a dropping resistor from B+ into a very large cap, and the preamp's node (drawn at 343 V, off B+
///         through 15k and 18k) hangs from it through a resistor that drops the difference, so the preamp does not
///         sag with B+ as the real one does. The driver's rails (345 V, 365 V, 380 V, −180 V) and the 6550s' 24 mA
///         (0.24 V on their 10 Ω cathode resistors) are the drawing's. The followers' grids rest on the bias pots
///         (−51 to −89 V at the wipers); the model sets them at −70 V, which puts its followers' cathodes at the
///         drawing's "approx. −45 V".
///     </para>
///     <para>
///         Not modelled: the 56 pF from V1A's plate signal to ground is in, but the 50 pF from the cathodyne's grid to
///         its plate and the 100 pF across the 68k feedback resistor are not (<see cref="DriverChainDesign" /> and
///         <see cref="FeedbackDesign" /> have no cap there; both act above the audio band, the second from 23 kHz); the
///         op-amp on the preamp board (standby and fault muting, not in the signal path); the fault and bias-monitor
///         circuits. The power amp's sheet marks 5.7 V at the input stage's plate for 0.4 V in, less than half of what
///         the stage gives without feedback, which is more feedback than 68k into 220 Ω from the 4 Ω tap accounts
///         for; the first speaker board's drawing (07-339-03) was not found, and its resistor may differ.
///     </para>
///     <para>
///         The SVT-2 Pro (the same power amp, tube board and transformers) is not built: its preamp (drawing
///         07S519-03, in the same el34world set) adds a Drive control with a tube stage of its own, a Bright switch,
///         op-amp buffers and a nine-band graphic equaliser, too much for the block's controls as a variant of this
///         model.
///     </para>
/// </remarks>
public static class SvtClassic {
    /// <summary>A closed contact.</summary>
    const double Wire = 1;

    /// <summary>An open contact (<see cref="StageNetlist.SwitchedResistor" />).</summary>
    const double Open = 0;

    /// <summary>The preamp's node (the power amp's input stage and cathodyne run at the same voltage).</summary>
    const double Preamp = 345;

    /// <summary>V1A's node, behind R6 4.7k and C1 10 µF.</summary>
    const double FirstStage = 340;

    /// <summary>The coil between the Midrange wiper and tap 1, tap 2 and tap 3 (see the remarks).</summary>
    const double Coil = 0.8;

    const double CoilTap2 = Coil * 4 / 9;
    const double CoilTap3 = Coil / 9;

    public static AmpDesign Design { get; } = new(
        "SVT-CL",
        [
            InputStage(), GainStage(), MidrangeStage(), FollowerStage(),
            // C26 0.1 µF and R33 1M on the preamp board; R92 220 Ω and R7 1k into D55 / D56 on the power amp's.
            AmpegParts.Clamp(1e6, 220 + 1e3, 1e-6)
        ],
        new(
            "6 x 6550",
            PentodeModel.SixtyFiveFifty,
            3,
            660,
            22,
            0.024,
            false,
            0,
            300,
            new DriverChainDesign(
                TriodeModel.Ecc83,
                Preamp,
                100e3,
                1e3 + 220,
                new(TriodeModel.Ecc83, Preamp, 68e3, 68e3, 820),
                TriodeModel.Ecc82,
                365,
                47e3,
                1.8e3,
                380,
                47e3,
                -180,
                FollowerGrid
            ),
            1, // DC-coupled from the followers
            10e6, // none: the followers' cathodes hold the grids
            47e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 68e3, 220, new(1e3, PotTaper.Linear), 1e-15),
            true,
            new(1.85e3, 15, 4e-3, 60, 2e-9, 100e3),
            SpeakerDesign.Closed8x10,
            new(40, 50e-6, ScreenDropper, 0.5, PreampDropper, 47e-6, PreampCurrent, RectifierModel.Silicon),
            ScreenSupply: 380
        ),
        -5
    ) {
        // R6 4.7k and C1 10 µF ahead of V1A's plate resistor, at its 1.05 mA.
        PreampSupply = new([new(4.7e3, 10e-6, 1.05e-3)]),
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Ultra Hi",
            Switch2 = "Ultra Lo",
            Switch3 = "-15 dB Input",
            Control1 = "Frequency"
        }
    };

    /// <summary>
    ///     The followers' grids (see the remarks): with it the model's 12AU7 followers rest at −45 V on their 47k to
    ///     −180 V.
    /// </summary>
    const double FollowerGrid = -70;

    /// <summary>
    ///     What B+ feeds besides the plates: R54's current by the drawing's voltages (660 V to 470 V across 15k), which
    ///     is the preamp, the driver and the bleeders.
    /// </summary>
    const double PreampCurrent = 12.7e-3;

    /// <summary>
    ///     The stand-in screen supply's resistor: 660 V to 380 V at <see cref="PreampCurrent" /> (at idle the model's
    ///     screens, far below the plates, draw next to nothing).
    /// </summary>
    const double ScreenDropper = (660 - 380) / PreampCurrent;

    /// <summary>From the stand-in screen node to the preamp's node: 380 V to 345 V.</summary>
    const double PreampDropper = (380 - Preamp) / PreampCurrent;

    /// <summary>
    ///     V1A: R1 22k with R3 100k across it into the grid (0 dB input), or with <see cref="SwitchRole.Switch3" /> R3
    ///     in series and R1 to ground (−15 dB input); R4 3.3M leak, R7 100k from the 340 V node, R8 1.5k with C2 22
    ///     µF. Out at its plate.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (grid, plate, cathode, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, FirstStage);
        net.SwitchedResistor(net.Input, grid, 22e3 * 100e3 / (22e3 + 100e3), 100e3, SwitchRole.Switch3);
        net.SwitchedResistor(grid, StageNetlist.Ground, Open, 22e3, SwitchRole.Switch3);
        net.Resistor(grid, StageNetlist.Ground, 3.3e6);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return new("V1A", net) { SupplyNode = 1 };
    }

    /// <summary>
    ///     C3 0.1 µF from V1A's plate to a node N with C4 56 pF to ground, then the Ultra Lo network
    ///     (<see cref="SwitchRole.Switch2" />, S1 C and D) into the 1M audio Gain pot. Out: R19 220k from N to a node
    ///     Q, wired to the pot's top (S1C shorts R21), with R20 330k from Q to ground. In: R19 and R21 220k in series
    ///     with C8 4.7 nF from Q to ground between them, and R11 100k + C7 470 pF from N to the pot's top beside them:
    ///     a notch near 500 Hz, and R20 lifted. R22 and R23 (3.3M) hold the idle caps' ends. Then C9 0.1 µF into V1B
    ///     (R27 100k, R28 1.5k over R29 1.5k, the R26 1M leak to their junction); Ultra Hi
    ///     (<see cref="SwitchRole.Switch1" />, S1B) grounds C10 33 nF from the cathode, which otherwise ends on R31
    ///     100k. Out at V1B's plate.
    /// </summary>
    static NetworkStageDesign GainStage() {
        var net = new StageNetlist();
        var wiper = net.Pot(UltraLo(net), StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        var (_, cathode) = SplitCathodeTriode(net, wiper, 1e6, 1.5e3);
        var bypass = net.Node();
        net.Capacitor(cathode, bypass, 33e-9, CapacitorRole.Bright);
        net.Resistor(bypass, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(bypass, StageNetlist.Ground, Open, Wire, SwitchRole.Switch1);
        return new("V1B", net);
    }

    /// <summary>
    ///     C3, C4 and the Ultra Lo network from <paramref name="net" />'s input (see <see cref="GainStage" />);
    ///     returns the Gain pot's top.
    /// </summary>
    static int UltraLo(StageNetlist net) {
        var (n, q, top) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, n, 0.1e-6);
        net.Capacitor(n, StageNetlist.Ground, 56e-12);
        net.Resistor(n, q, 220e3);
        net.Resistor(q, top, 220e3);
        net.SwitchedResistor(q, top, Wire, Open, SwitchRole.Switch2);
        net.SwitchedResistor(q, StageNetlist.Ground, 330e3, Open, SwitchRole.Switch2);

        var (low, series, high) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(q, low, 4.7e-9);
        net.Resistor(low, StageNetlist.Ground, 3.3e6);
        net.SwitchedResistor(low, StageNetlist.Ground, Open, Wire, SwitchRole.Switch2);
        net.Resistor(n, series, 100e3);
        net.Capacitor(series, high, 470e-12);
        net.Resistor(high, StageNetlist.Ground, 3.3e6);
        net.SwitchedResistor(high, top, Open, Wire, SwitchRole.Switch2);
        return top;
    }

    /// <summary>
    ///     A 12AX7 half as V1B and V2A are wired: 0.1 µF from <paramref name="input" /> into the grid, 100k from the
    ///     preamp's node to the plate, 1.5k from the cathode to a junction and <paramref name="foot" /> from there to
    ///     ground, the grid's <paramref name="leak" /> returning to the junction. The stage's output is the plate;
    ///     returns the plate and the cathode.
    /// </summary>
    static (int Plate, int Cathode) SplitCathodeTriode(StageNetlist net, int input, double leak, double foot) {
        var (grid, plate, cathode, junction, rail) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Capacitor(input, grid, 0.1e-6);
        net.Resistor(grid, junction, leak);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, junction, 1.5e3);
        net.Resistor(junction, StageNetlist.Ground, foot);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return (plate, cathode);
    }

    /// <summary>
    ///     The Baxandall (C11 0.1 µF, R48 220k, C23 470 pF, C20 1 nF and C21 10 nF, R37 22k, C27 4.7 nF, R30 100k; the
    ///     pots are marked 1MA), in the same stage as the triode it feeds: its output impedance is hundreds of
    ///     kilohms, and V2A's input, with the leak bootstrapped from the cathode, loads it. R32 100k and C16 0.1 µF
    ///     into V2A (R43 100k, R44 1.5k over R45 2.2k, the R40 470k leak to their junction); C5 0.1 µF and R12 100k
    ///     from its plate onto R13 8.2k and the 50k linear Master. The Midrange pot (50k linear) runs from the cathode
    ///     (C12 10 µF) to the top of R13 (R36 470 Ω), its wiper to ground through the coil and the Frequency switch's
    ///     cap (<see cref="PotRole.Control1" />): up, the resonant circuit bypasses the cathode (boost); down, it
    ///     shunts the output divider (cut). Out at the Master's wiper.
    /// </summary>
    static NetworkStageDesign MidrangeStage() {
        var net = new StageNetlist();
        var coupled = net.Node();
        net.Resistor(AmpegParts.InputBaxandall(net, 470e-12, 4.7e-9, 100e3, PotTaper.Audio), coupled, 100e3);
        var (plate, cathode) = SplitCathodeTriode(net, coupled, 470e3, 2.2e3);

        var (divider, top, boost, cut) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, divider, 0.1e-6);
        net.Resistor(divider, top, 100e3);
        net.Resistor(top, StageNetlist.Ground, 8.2e3);
        net.Capacitor(cathode, boost, 10e-6);
        net.Resistor(cut, top, 470);
        var wiper = net.Pot(boost, cut, new(50e3, PotTaper.Linear), PotRole.Middle);
        // One branch for each tap: the switch leaves the others' caps out (0), so only one carries current.
        CoilTap(net, wiper, Coil, [0.68e-6, 0.15e-6, 0, 0, 0]);
        CoilTap(net, wiper, CoilTap2, [0, 0, 0.1e-6, 0.033e-6, 0]);
        CoilTap(net, wiper, CoilTap3, [0, 0, 0, 0, 0.033e-6]);
        net.Output = net.Pot(top, StageNetlist.Ground, new(50e3, PotTaper.Linear), PotRole.Master);
        return new("V2A", net);
    }

    /// <summary>
    ///     The coil from the Midrange wiper to one tap, and that tap's caps to ground by the Frequency switch's
    ///     position (0: none on this tap).
    /// </summary>
    static void CoilTap(StageNetlist net, int wiper, double henries, double[] farads) {
        var tap = net.Node();
        net.Inductor(wiper, tap, henries);
        net.SelectedCapacitor(tap, StageNetlist.Ground, farads, PotRole.Control1);
    }

    /// <summary>
    ///     V2B, a cathode follower: C6 0.1 µF from the Master's wiper, its plate on the rail, R15 4.7k over R16 220k
    ///     with the R14 220k leak to their junction. Out at its cathode.
    /// </summary>
    static NetworkStageDesign FollowerStage() {
        var net = new StageNetlist();
        var (grid, cathode, junction, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Capacitor(net.Input, grid, 0.1e-6);
        net.Resistor(grid, junction, 220e3);
        net.Resistor(cathode, junction, 4.7e3);
        net.Resistor(junction, StageNetlist.Ground, 220e3);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc83);
        net.Output = cathode;
        return new("V2B", net);
    }
}
