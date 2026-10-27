using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Dumble Overdrive Special of the 1970s (the Robben Ford era), with its Rock/Jazz and Deep switches: the clean
///     preamp, or (Switch 1) the overdrive. V1: 1M across the input, 22k to the grid, 100k plate from 303 V, 1.5k with
///     5 µF; its plate drives the stack: 330 pF to the 250k audio treble pot, 100k to a node S; S through 0.1 µF to the
///     top of the 250k audio bass pot, whose wiper (Rock) joins its top and the treble pot's foot and whose foot has
///     1.8k to ground; S through 50 nF and the 100k linear middle (a rheostat) to ground. Jazz (Switch 2) lifts the
///     treble pot's foot onto 5 nF to ground and joins the bass wiper to the treble wiper through 100k. The treble
///     wiper feeds the 1M audio volume (500 pF Bright from its top to the wiper); from the bass wiper, 270k and 270k to
///     the volume's wiper, their junction taken to ground through 1M ‖ 10 nF and 10k by Deep (Switch 3); 220k into V2
///     (100k, 1.5k with 5 µF, 22M with 50 nF from plate to grid). Overdrive: 10 nF into the 100k linear trim (at half),
///     68k into V3 (100k, 1.5k with 5 µF, 500 pF plate to cathode), 20 nF and 100k into the 100k linear Overdrive pot
///     (Control 1), 180k into V4 (the same), 10 nF into the 100k linear output trim (OD Level, Control 2). The output
///     section: 10 nF into a 12AX7 long-tail pair (1M leaks, 820 Ω bias, 24k tail; 100k plates from 436 V), 0.1 µF into
///     200k leaks and 3.3k stoppers, two 6L6GC at 457 V with 500 Ω screen resistors, fixed bias; 3.9k feedback into 390
///     Ω, with the Accent switch's 2.2 µF across it; silicon rectifier, 50 µF (two 100 µF stacked), an 80 Ω choke, 20
///     µF filters and 2.2k / 22k / 1k droppers (460 / 454 / 436 / 306 / 303 V).
/// </summary>
/// <remarks>
///     <para>
///         Source: the "70's ODS" preamp, overdrive, power-supply and output-section redraws on schematicheaven
///         (https://www.schematicheaven.net/dumble.html: <c>dumbleamps/preamp.pdf</c>, <c>distorto.pdf</c>,
///         <c>power.pdf</c>, <c>output.pdf</c>), CAD drawings with node voltages but no stated provenance. Every value
///         above is printed on them; the HRM stack of later amps is not (no HRM drawing was found).
///     </para>
///     <para>
///         Read from the switch drawings, marked: Rock/Jazz is a two-pole switch whose commons are the treble pot's
///         foot and the bass wiper; in Rock both join the bass pot's top (the bass then a rheostat), in Jazz the treble
///         foot goes to the 5 nF and the bass wiper to the 100k. Deep's open position leaves the 270k pair as a fixed
///         path from the bass wiper to the volume's wiper.
///     </para>
///     <para>
///         Guesses, marked: the power tubes are not named on the drawing (6L6GC, as in Robben Ford's ODS; the 2 × 6L6
///         at 457 V gives 50 W); the output transformer (4k, a typical value for a 6L6 pair); the feedback tap (8 Ω);
///         the 10 nF drawn at V2's output and at the inverter's grid is taken as one part drawn on both sheets; both
///         trims at half (the output trim is on the panel as OD Level); the 6L6s' idle current (40 mA). The Accent
///         switch is the Presence knob (Accent): a 10k reverse-audio rheostat in series with the 2.2 µF, so 0 is nearly
///         off (10k beside the 390 Ω), 10 the switch on, and noon (1k) part way. The balance pot between the inverter's
///         plate resistors is centred (100k each).
///     </para>
/// </remarks>
public static class OverdriveSpecial70s {
    /// <summary>The 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    public static AmpDesign Design { get; } = new(
        "ODS 70s",
        [
            new NetworkStageDesign("V1", FirstStage()),
            // V2 sits in both chains (only the playing one runs), so each channel ends on its own last gain stage.
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("V2", StackStage())],
                [
                    new NetworkStageDesign("V2", StackStage()),
                    new NetworkStageDesign("V3", OverdriveFirst()),
                    new NetworkStageDesign("V4", OverdriveSecond())
                ]
            )
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            457,
            500,
            0.04,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, 436, 100e3, 100e3, 24e3, 820),
            0.1e-6,
            200e3,
            3.3e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 3.9e3, 390, new(10e3, PotTaper.ReverseAudio), 2.2e-6),
            true,
            new(4e3),
            SpeakerDesign.OpenBack2x12,
            // 100 + 100 µF stacked, the 80 Ω choke to the screens' 20 µF, then 2.2k and 22k to V3's node as one
            // dropper.
            new(25, 50e-6, 80, 20e-6, 24.2e3, 20e-6, 5e-3, RectifierModel.Silicon),
            InverterInput: new(10e-9, 1e6)
        ),
        -9
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Presence = "Accent",
            Master = null,
            Depth = null,
            Cut = null,
            Switch1 = "Overdrive",
            Switch2 = "Jazz",
            Switch3 = "Deep",
            Control1 = "Overdrive",
            Control2 = "OD Level"
        }
    };

    /// <summary>V1: 1M across the input, 22k to the grid; 100k plate from 303 V, 1.5k with 5 µF.</summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 22e3);
        net.Supply(supply, 303);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The stack on V1's plate with Rock/Jazz (Switch 2), the volume with its Bright cap, the Deep path (Switch 3)
    ///     and V2 (220k to the grid; 100k from 303 V, 1.5k with 5 µF, 22M with 50 nF from plate to grid).
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (trebleTop, trebleFoot, slope, bassTop, bassFoot, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, trebleTop, 330e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleFoot, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Resistor(net.Input, slope, 100e3);
        net.Capacitor(slope, bassTop, 0.1e-6, CapacitorRole.Stack);
        var bass = net.Pot(bassTop, bassFoot, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(bassFoot, StageNetlist.Ground, 1.8e3);
        net.Capacitor(slope, middle, 50e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(100e3, PotTaper.Linear), PotRole.Middle);

        // Rock (Switch 2 off): the treble foot and the bass wiper on the bass pot's top. Jazz: the treble foot onto 5
        // nF, the bass wiper through 100k to the treble wiper.
        net.SwitchedResistor(trebleFoot, bassTop, 1, 0, SwitchRole.Switch2);
        net.SwitchedResistor(bass, bassTop, 1, 0, SwitchRole.Switch2);
        net.SwitchedCapacitor(trebleFoot, StageNetlist.Ground, 5e-9, SwitchRole.Switch2, CapacitorRole.Stack);
        net.SwitchedResistor(bass, treble, 0, 100e3, SwitchRole.Switch2);

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, volume, 500e-12, SwitchRole.Bright, CapacitorRole.Bright);
        var deep = net.Node();
        net.Resistor(bass, deep, 270e3);
        net.Resistor(deep, volume, 270e3);
        var (deepFoot, deepTail) = (net.Node(), net.Node());
        net.SwitchedResistor(deep, deepFoot, 0, 1, SwitchRole.Switch3);
        net.Resistor(deepFoot, deepTail, 1e6);
        net.Capacitor(deepFoot, deepTail, 10e-9);
        net.Resistor(deepTail, StageNetlist.Ground, 10e3);

        var (grid, supply, plate, cathode, feedback) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(volume, grid, 220e3);
        net.Supply(supply, 303);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Capacitor(plate, feedback, 50e-9);
        net.Resistor(feedback, grid, 22e6);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     10 nF into the input trim (100k linear at half), 68k into V3 (100k from 306 V, 1.5k with 5 µF, 500 pF from
    ///     plate to cathode).
    /// </summary>
    static StageNetlist OverdriveFirst() {
        var net = new StageNetlist();
        var (top, trim, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 10e-9);
        net.Resistor(top, trim, 50e3);
        net.Resistor(trim, StageNetlist.Ground, 50e3);
        net.Resistor(trim, grid, 68e3);
        net.Supply(supply, 306);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, cathode, 500e-12);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     20 nF and 100k into the Overdrive pot (100k linear, Control 1), 180k into V4 (as V3), 10 nF into the output
    ///     trim (100k linear, Control 2); the output is its wiper.
    /// </summary>
    static StageNetlist OverdriveSecond() {
        var net = new StageNetlist();
        var (coupled, top, grid, supply, plate, cathode, level) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, top, 100e3);
        var drive = net.Pot(top, StageNetlist.Ground, new(100e3, PotTaper.Linear), PotRole.Control1);
        net.Resistor(drive, grid, 180e3);
        net.Supply(supply, 306);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 5e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, cathode, 500e-12);
        net.Capacitor(grid, plate, GridPlate);
        net.Capacitor(plate, level, 10e-9);
        net.Output = net.Pot(level, StageNetlist.Ground, new(100e3, PotTaper.Linear), PotRole.Control2);
        return net;
    }
}
