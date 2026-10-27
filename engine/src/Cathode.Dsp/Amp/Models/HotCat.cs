using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Bad Cat Hot Cat 30, from the CAD redraw with measured DC voltages "Bad Cat Hot Cat 30" (02/11/06, "DC voltage
///     readings measured with tube rectifier"; schematicheaven <c>newamps/badcat_hotcat_30.pdf</c>, vector, values read
///     as text): the Gain input, or (Switch 1) the Clean input. Gain channel: 5.1M across the jack, 100 nF and 68k into
///     V1a, a 12AX7 half with its cathode grounded (zero bias; 220k plate, 99 V), 1.5 nF into the 250k log gain pot
///     with 470k across it, 1.5 nF from the wiper to ground and 470k to V2b (220k, 1.5k with 100 nF: a treble boost;
///     145 V); 22 nF to the Edge control (10 nF and a 1M log rheostat to ground) and through 470k to V2a's grid (150k
///     and 360 pF to ground; 100k, 1.5k with 1 µF; 225 V); 10 nF into the Level pot's wiper (1M linear, its top through
///     470k to V3b's grid with 270k to ground; 100k, 1k unbypassed; 193 V), DC-coupled to the cathode follower V3a
///     (56k, 194 V) driving the AC30 top-boost network (50 pF, 1M log treble, 100k, 22 nF, 22 nF, the 1M5 log bass with
///     10k from its wiper), the treble wiper on the 100k linear master, its wiper through 220k to the inverter (the
///     master's wiring is a reading: see the remarks). Clean channel: 68k, 1M, V1b (220k, 1.5k with 22 µF; 150 V),
///     1.5 nF into a 1M log volume with 180 pF across its upper part, the wiper to the inverter's other grid. A 12AX7
///     long-tail pair (10 nF in, 1M leaks, 1.2k bias, 47k tail, 100k plates from 341 V; 264 / 269 V), 33 nF into 220k
///     leaks and 10k stoppers, the Brilliance (4.7 nF and a 1M log pot across the grids), two EL34 each on its own
///     270 Ω with 220 µF (24 V), 1k screen resistors from the rectifier's node, 376 V plates, no feedback; GZ34 or (a
///     panel switch) silicon, 382 V, a 36 H / 450 Ω choke to 373 V and a 22k / 22k / 10k / 22k dropper to each preamp
///     node (317 / 345 / 325 / 341 V).
/// </summary>
/// <remarks>
///     A redraw by a third party, not a factory drawing; the model's plates sit within 3-8 % of its DC voltages, except
///     the zero-bias V1a (88 V against 99 V: it rests on the triode's contact potential). Assumed, not drawn: the
///     output transformer's primary (4k plate to plate; the drawing names "TDS 1163" only), the Edge rheostat's
///     direction (more resistance, less treble bled away, as the knob turns up), the preamp nodes taken as fixed rails
///     behind one 22k dropper for the sag. Not modelled: the Mid-range switch on the panel (not on the drawing). The
///     Brilliance (the panel's name for it) is a Vox cut wired the other way (Fractal notes it too: clockwise is
///     brighter): the 1M log pot's resistance in series with the 4.7 nF grows by its audio law as the knob turns up
///     (<see cref="PowerAmpDesign.Brilliance" />, on the block's Presence knob), 0 the cap straight across the grids.
///     Up to 1.0.0-rc.3 it was the block's Cut, running the other way (Cut 0 was Brilliance 10); a schema-1 preset is
///     carried over (Brilliance = 10 - Cut, <c>PresetMigrations</c>). In the Clean channel the Gain knob turns the
///     clean Volume, and the Gain channel's Edge, Level, tone and master are out of the path.
///     <para>
///         The master is a reading, not the drawing (issue #94). The drawing joins the treble wiper to point F ("to
///         Phase-Inverter") and through 220k to the 100k linear pot's wiper, one end of the pot to ground and the other
///         to nothing: a 220k-320k load on the wiper, which moves the level by 1-2 dB and cannot turn it down, though
///         the amp has a working Master (the panel's eighth knob; Vintage Guitar's review turned it up for a very loud
///         30 W). A builder of this drawing on the Music Electronics Forum (thread "Bad Cat Hot Cat build") met the
///         same knob: a technician there found the drawn wiring unclear, unable to shut the signal off and worth about
///         2 dB, posted a rewiring (an attachment no longer readable) and spoke of "the 220K at point F", i.e. the
///         220k at the inverter's input. Taken here as the drafter joining the 220k to the wrong lug: the treble wiper
///         on the pot's free end (its top), the wiper through the 220k to the inverter (F). Master 0 is silence and 10
///         the treble wiper behind the 220k, the 100k loading the treble pot; the values are the drawing's. No factory
///         drawing or photo of the board was found to confirm it. Up to 1.0.0-rc.3 the master was the drawing's
///         rheostat, so a stored Hot Cat below Master 10 now drives its power amp less than it did.
///     </para>
/// </remarks>
public static class HotCat {
    /// <summary>The 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    public static AmpDesign Design { get; } = new(
        "Hot Cat 30",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1a", FirstStage()),
                    new NetworkStageDesign("V2b", GainStage()),
                    new NetworkStageDesign("V2a", SecondStage()),
                    new NetworkStageDesign("V3b", LevelStage()),
                    new NetworkStageDesign("V3a", TopBoost())
                ],
                [new NetworkStageDesign("V1b", CleanStage(), 68e3)]
            )
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            1,
            382,
            1e3,
            0.08,
            true,
            220e-6,
            30,
            new LongTailPairDesign(TriodeModel.Ecc83, 341, 100e3, 100e3, 47e3, 1.2e3),
            33e-9,
            220e3,
            10e3,
            4.7e-9,
            new(1e6, PotTaper.Audio),
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            false,
            new(4e3),
            SpeakerDesign.OpenBack2x12,
            // The screens hang on the rectifier's 33 µF node; the choke (450 Ω) and 33 µF, then a 22k dropper per node.
            new(60, 33e-6, 10, 33e-6, 22e3 + 450, 22e-6, 2.5e-3, RectifierModel.GZ34),
            SeparateCathodes: true,
            InverterInput: new(10e-9, 1e6),
            CathodeResistor: 135, // 270 Ω per tube: half of each side's, as the power amp counts it
            SupplySwitches: [new(SwitchRole.Switch2, RectifierModel.Silicon)],
            Brilliance: true
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Middle = null,
            Presence = "Brilliance",
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Clean Input",
            Switch2 = "Solid State",
            Control1 = "Edge",
            Control2 = "Level"
        }
    };

    /// <summary>
    ///     V1a: 5.1M across the jack, 100 nF and 68k to the grid with 1M to ground; 220k plate from 317 V, the cathode
    ///     grounded.
    /// </summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 5.1e6);
        net.Capacitor(net.Input, coupled, 100e-9);
        net.Resistor(coupled, grid, 68e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 317);
        net.Resistor(supply, plate, 220e3);
        net.Triode(plate, grid, StageNetlist.Ground, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The gain pot (1.5 nF in, 250k log with 470k across it, 1.5 nF from the wiper to ground, 470k on), V2b (220k
    ///     from 317 V, 1.5k with 100 nF), and on its plate 22 nF to the Edge control (10 nF and a 1M log rheostat to
    ///     ground, Control 1) and 470k to V2a's grid (150k and 360 pF to ground), which is the output. The cut is at
    ///     V2a's grid, not V2b's plate: V2b's partly bypassed cathode doubles its output impedance below 1 kHz and the
    ///     Edge network loads it hard, so a cut at the plate (at the 1 kHz impedance) was 9 dB from the reference; the
    ///     grid's source is nearly resistive (150k against 470k).
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (top, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 1.5e-9);
        net.Resistor(top, StageNetlist.Ground, 470e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(wiper, StageNetlist.Ground, 1.5e-9);
        net.Resistor(wiper, grid, 470e3);
        net.Supply(supply, 317);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 100e-9);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        var (coupled, edge, second) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Capacitor(coupled, edge, 10e-9);
        net.Rheostat(edge, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(coupled, second, 470e3);
        net.Resistor(second, StageNetlist.Ground, 150e3);
        net.Capacitor(second, StageNetlist.Ground, 360e-12);
        net.Output = second;
        return net;
    }

    /// <summary>V2a: 100k from 345 V, 1.5k with 1 µF, its grid-plate capacitance; its grid is the input.</summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, 345);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     10 nF into the Level pot's wiper (1M linear, Control 2: its bottom to ground, its top through 470k to V3b's
    ///     grid with 270k to ground); V3b: 100k from 325 V, 1k unbypassed.
    /// </summary>
    static StageNetlist LevelStage() {
        var net = new StageNetlist();
        var (top, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Control2);
        net.Capacitor(net.Input, wiper, 10e-9);
        net.Resistor(top, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 270e3);
        net.Supply(supply, 325);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cathode follower V3a (56k, its grid on V3b's plate) and the top-boost network: 50 pF to the 1M log
    ///     treble pot, 100k to a node M, the treble pot's far end through 22 nF to M, M through 22 nF to the 1M5 log
    ///     bass pot's wiper with 10k to ground, the bass pot from the treble pot's far end to ground; the treble wiper
    ///     on the 100k linear master to ground, whose wiper feeds the inverter through 220k (the output, after it).
    /// </summary>
    static StageNetlist TopBoost() {
        var net = new StageNetlist();
        var (supply, cathode, trebleTop, trebleBottom, slope, inverter) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, 325);
        net.Resistor(cathode, StageNetlist.Ground, 56e3);
        net.Triode(supply, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, trebleTop, 50e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(cathode, slope, 100e3);
        net.Capacitor(trebleBottom, slope, 22e-9, CapacitorRole.Stack);
        var bassWiper = net.Pot(StageNetlist.Ground, trebleBottom, new(1.5e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, bassWiper, 22e-9, CapacitorRole.Stack);
        net.Resistor(bassWiper, StageNetlist.Ground, 10e3);
        var master = net.Pot(treble, StageNetlist.Ground, new(100e3, PotTaper.Linear), PotRole.Master);
        net.Resistor(master, inverter, 220e3);
        net.Output = inverter;
        return net;
    }

    /// <summary>
    ///     The Clean input: 1M leak, V1b (220k from 317 V, 1.5k with 22 µF), 1.5 nF into the 1M log volume (the Gain
    ///     knob) with 180 pF across its upper part; the output is the wiper.
    /// </summary>
    static StageNetlist CleanStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, top) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 317);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, top, 1.5e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 180e-12, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }
}
