using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark IV, lead channel (channel 3): the IIC+ front end with its own values. V1A (150k, 1.5k ‖ 0.47 µF
///     and 22 µF through 22k) driving the Fender-topology stack (250 pF, 750 pF more with Fat) and the lead gain (1M
///     audio), V1B (100k, 1.5k ‖ 15 µF), the lead drive (680k into 1M audio, 470k at the wiper, 120 pF to the cathode)
///     into V2A (82k, 1.5k ‖ 2.2 µF), a 270k/68k ‖ 1 nF divider into V2B (270k ‖ 1 nF, 3.3k with 0.22 µF through 22k,
///     shorted by Bright), 220k ‖ 250 pF into 100k ‖ 500 pF and V3A (100k, 1.5k with 15 µF through 22k, and 2.2 µF
///     straight across with Mid Boost), the lead master (100k audio shunt) into a 150k/4.7k pad and 3.3k to V4A (120k,
///     1k ‖ 15 µF), the master (250k audio), the five-band EQ, and Simul-Class power with four 6L6GC (a pentode pair
///     beside a triode-strapped pair).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Yahiake LiveSPICE redraw "Mark IV Lead Channel Preamp_GEQ" (every preamp value and the wiring),
///         docs/06 §3.4 for the power section. A redraw, not the factory scan.
///     </para>
///     <para>
///         Guesses, marked: the power amp is the IIC+ model's (82.5k/90.9k inverter, 100k feedback into 4.7k, 25k
///         presence with 0.1 µF, 465 V, 40 mA) with 6L6GC in both pairs and a 2.2k primary; the rhythm path's 3.3M feed
///         into V3A is left out; the EQ is the behavioural slot (<see cref="EqualizerStageDesign" />).
///     </para>
/// </remarks>
public static class MarkIV {
    public static AmpDesign Design { get; } = new(
        "Mark IV",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 425, 100e3, 1.5e3, CathodeBypass: 15e-6),
            new NetworkStageDesign("V2A", LeadDrive()),
            new NetworkStageDesign("V2B", LeadStage()),
            new NetworkStageDesign("V3A", LeadReturn()),
            new NetworkStageDesign("V4A", Recovery()),
            new CouplingStageDesign("Master", 47e-9, new(250e3, PotTaper.Audio), PotRole.Master, 22e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        MarkIIC.Design.Power with {
            Name = "4 x 6L6GC (Simul-Class)",
            RatedPower = 85,
            Transformer = new(2.2e3),
            Strapped = PentodeModel.SixL6GC
        },
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Lead Gain",
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "Lead Drive",
            Control2 = "Lead Master",
            Switch1 = "Fat",
            Switch2 = "Bright",
            Switch3 = "Mid Boost",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     V1A with the stack on its plate and the lead gain on the treble wiper: 1M grid leak; 150k plate; 1.5k ‖
    ///     0.47 µF and 22 µF through 22k; 250 pF treble cap (750 pF more with Fat), 100k slope, 0.1 µF, 47 nF.
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, bass, slope, trebleTop, trebleBottom, middle) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, bass, 22e-6);
        net.Resistor(bass, StageNetlist.Ground, 22e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        net.Capacitor(plate, trebleTop, 250e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(plate, trebleTop, 750e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Middle);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     The lead drive into V2A: 47 nF and 100k to ground, 20 nF and 680k into the 1M audio drive, its wiper on the
    ///     grid with 470k to ground and 120 pF to the cathode; 82k plate, 1.5k ‖ 2.2 µF.
    /// </summary>
    static StageNetlist LeadDrive() {
        var net = new StageNetlist();
        var (coupled, lead, top, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Capacitor(coupled, lead, 20e-9);
        net.Resistor(lead, top, 680e3);
        var grid = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 82e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V2B: 22 nF, 270k into 68k ‖ 1 nF at the grid; 270k plate with 1 nF across it; 3.3k with 0.22 µF through 22k
    ///     (shorted by Bright).
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, bright) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, grid, 270e3);
        net.Resistor(grid, StageNetlist.Ground, 68e3);
        net.Capacitor(grid, StageNetlist.Ground, 1e-9);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 270e3);
        net.Capacitor(supply, plate, 1e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, bright, 0.22e-6);
        net.SwitchedResistor(bright, StageNetlist.Ground, 22e3, 1, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     Back into V3A: 47 nF, 220k ‖ 250 pF into 100k ‖ 500 pF (with V3A's own 47 pF); 100k plate, 1.5k with 15 µF
    ///     through 22k and, with Mid Boost, 2.2 µF straight across.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, partial, boost) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Capacitor(coupled, grid, 250e-12);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Capacitor(grid, StageNetlist.Ground, 547e-12);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, partial, 15e-6);
        net.Resistor(partial, StageNetlist.Ground, 22e3);
        net.Capacitor(cathode, boost, 2.2e-6);
        net.SwitchedResistor(boost, StageNetlist.Ground, 0, 1, SwitchRole.Switch3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead master and V4A: 47 nF and 47k from V3A, the 100k audio lead master as a shunt, 150k into 4.7k and
    ///     3.3k to the grid with 47k to ground; 120k plate, 1k ‖ 15 µF.
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (coupled, master, pad, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, master, 47e3);
        net.Rheostat(master, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Control2);
        net.Resistor(master, pad, 150e3);
        net.Resistor(pad, StageNetlist.Ground, 4.7e3);
        net.Resistor(pad, grid, 3.3e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Supply(supply, 433);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
