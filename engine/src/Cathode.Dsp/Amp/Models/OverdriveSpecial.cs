using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Dumble Overdrive Special, a late-1997 ODS 100 as traced by its owner's friend: the CAD redraw "1997 Humble
///     Overdraft Specimen, Revision 1.2" (schematicheaven <c>dumbleamps/97fullschem.pdf</c>, which marks each value
///     confirmed or probable and gives the measured DC voltages) and the notes of the visit
///     (<c>dumbleamps/dumblenotes.pdf</c>: transformer part numbers, pots, voltages). The clean channel, or (Switch 1)
///     the overdrive. V1B: 1M, 22k, 220k plate, 3.3k with 5 µF (209.6 V, 1.716 V); the plate drives the "skyline"
///     stack directly: 330 pF to the 250k linear treble pot, 150k to a node S, S through 0.1 µF to the treble pot's far
///     end, the 500k log bass pot (1 nF across it) and 9.8k to ground, S through 10 nF and the 250k log middle pot to
///     ground; the treble wiper into the 1M log volume, its wiper through 220k ‖ 390 pF to V1A (150k plate, 3.3k with
///     5 µF; 206 V, 1.788 V), 47 nF on. Clean: the 1M log master (47 pF from its top to the wiper, per the notes) to
///     the output section. Overdrive: from the same 47 nF, 100k into the 100k linear OD trim, 68k into V2A (180k, 2.7k
///     with 5 µF; 227.5 V, 2.03 V), 2.2 nF and 100k into the 250k log Drive pot, 180k into V2B (120k, 1.8k with 1 µF;
///     229 V, 2.03 V), its plate into a fixed Marshall-topology stack (330 pF, 33k, 22 nF, 22 nF; 250k treble, 1M bass
///     and 20k middle trimmers) and the 1M log OD Level, whose wiper replaces the master's on the relay. The output
///     section: 10 nF into a 12AX7 long-tail pair (1M leaks, 820 Ω bias, 24k tail; 91k / 110k plates from 433 V; 303 /
///     317 V, cathodes 60.9 V), 0.1 µF into 200k leaks and 5.1k stoppers, four EL34 (the notes' V4-V7; the drawing
///     shows one pair) at 465 V with 458 V screens through 1k, fixed bias at -40.7 V; feedback through 4.7k into the
///     tail's foot, which has 390 Ω to ground and the 2k linear presence pot with 1 µF across it; silicon rectifier
///     from 345-0-345 V, 200 + 200 µF stacked, a 3 H choke, and 3k / 12k / 10k droppers to the 433 / 361 / 323 V nodes.
/// </summary>
/// <remarks>
///     <para>
///         The notes give the output transformer as a TF-170, a Marshall 100 W replacement listed with a 1.73k primary,
///         used here. Assumed, not drawn or measured: the EL34s' idle current (45 mA each, which puts the bias near the
///         measured -40.7 V), the feedback tap (8 Ω), the overdrive's internal trimmers at half rotation (OD trim, and
///         the OD stack's treble, bass and middle), and the
///         master's 47 pF bright cap (in the notes, not on the drawing; Fractal also notes a bright cap on Dumble
///         masters). The drawing takes the overdrive's input from V1A's 47 nF ahead of the master and returns the OD
///         Level's wiper in the master's place, so in the overdrive the Master knob does nothing (later amps put the
///         overdrive's return ahead of the master; this one is drawn otherwise). V1A's plate resistor is drawn 150k
///         (marked confirmed), but its measured 206 V plate and 1.788 V cathode fit 220k from 323 V; the drawn value
///         is kept, so the model's V1A sits at 232 V.
///     </para>
///     <para>
///         Not modelled: the Bright, Deep/Mid and Rock/Jazz switches and the Pre-Amp Boost (none is on the 1997
///         drawing; the 1970s ODS drawings on the same site have Rock/Jazz and Deep), the effects loop, the HRM
///         stack of other ODS amps. The idle overdrive's master still hangs on the 47 nF (1M); in the overdrive, the
///         master pot's 1M stays on V1A's output.
///     </para>
/// </remarks>
public static class OverdriveSpecial {
    /// <summary>The 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    public static AmpDesign Design { get; } = new(
        "ODS 97",
        [
            new NetworkStageDesign("V1B", FirstStage()),
            // V1A sits in both chains (only the playing one runs), so each channel ends on its own last gain stage.
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("V1A", SkylineStage()), new NetworkStageDesign("Master", CleanMaster())],
                [
                    new NetworkStageDesign("V1A", SkylineStage()),
                    new NetworkStageDesign("V2A", OverdriveFirst()),
                    new NetworkStageDesign("V2B", OverdriveSecond())
                ]
            )
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            465,
            1e3,
            0.045, // fitted to the measured -40.7 V grid bias
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 433, 91e3, 110e3, 24e3, 820),
            0.1e-6,
            200e3,
            5.1e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 4.7e3, 390, new(2e3, PotTaper.Linear), 1e-6),
            true,
            new(1.73e3),
            SpeakerDesign.OpenBack2x12,
            // 200 + 200 µF stacked, the 3 H choke (90 Ω) to the screens' 47 + 47 µF stacked, then 3k and 12k to the
            // 361 V node as one dropper (the 433 V and 323 V nodes hang on the same chain).
            new(25, 100e-6, 90, 23.5e-6, 15e3, 23.5e-6, 6.5e-3, RectifierModel.Silicon),
            InverterInput: new(10e-9, 1e6)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Overdrive",
            Control1 = "Drive",
            Control2 = "OD Level"
        }
    };

    /// <summary>V1B: 1M across the input, 22k to the grid; 220k plate from 323 V, 3.3k with 5 µF.</summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 22e3);
        net.Supply(supply, 323);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The skyline stack on V1B's plate, the volume (1M log) and 220k ‖ 390 pF into V1A (150k from 323 V, 3.3k with
    ///     5 µF, its grid-plate capacitance).
    /// </summary>
    static StageNetlist SkylineStage() {
        var net = new StageNetlist();
        var (trebleTop, trebleBottom, slope, bassFoot, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, trebleTop, 330e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(net.Input, slope, 150e3);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, bassFoot, new(500e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(trebleBottom, bassFoot, 1e-9, CapacitorRole.Stack);
        net.Resistor(bassFoot, StageNetlist.Ground, 9.8e3);
        net.Capacitor(slope, middle, 10e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Middle);
        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(volume, grid, 220e3);
        net.Capacitor(volume, grid, 390e-12);
        net.Supply(supply, 323);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>The clean channel's master: 47 nF into a 1M log pot with 47 pF across its upper part.</summary>
    static StageNetlist CleanMaster() {
        var net = new StageNetlist();
        var top = net.Node();
        net.Capacitor(net.Input, top, 47e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Capacitor(top, wiper, 47e-12, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     The overdrive's input: 47 nF (with the idle master's 1M to ground), 100k into the OD trim (100k linear at
    ///     half), 68k into V2A (180k from 361 V, 2.7k with 5 µF).
    /// </summary>
    static StageNetlist OverdriveFirst() {
        var net = new StageNetlist();
        var (coupled, trimTop, trim, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, trimTop, 100e3);
        net.Resistor(trimTop, trim, 50e3);
        net.Resistor(trim, StageNetlist.Ground, 50e3);
        net.Resistor(trim, grid, 68e3);
        net.Supply(supply, 361);
        net.Resistor(supply, plate, 180e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     2.2 nF and 100k into the Drive pot (250k log, Control 1), 180k into V2B (120k from 361 V, 1.8k with 1 µF),
    ///     the fixed OD stack on its plate (trimmers at half) and the OD Level (1M log, Control 2); the output is its
    ///     wiper.
    /// </summary>
    static StageNetlist OverdriveSecond() {
        var net = new StageNetlist();
        var (coupled, top, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 2.2e-9);
        net.Resistor(coupled, top, 100e3);
        var drive = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
        net.Resistor(drive, grid, 180e3);
        net.Supply(supply, 361);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        var (trebleTop, treble, trebleBottom, slope, middleTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, trebleTop, 330e-12);
        net.Resistor(trebleTop, treble, 125e3);
        net.Resistor(treble, trebleBottom, 125e3);
        net.Resistor(plate, slope, 33e3);
        net.Capacitor(slope, trebleBottom, 22e-9);
        net.Resistor(trebleBottom, middleTop, 500e3);
        net.Capacitor(slope, middleTop, 22e-9);
        net.Resistor(middleTop, StageNetlist.Ground, 10e3);
        var level = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control2);
        net.Output = level;
        return net;
    }
}
