using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark IIC+, lead channel: V1A (150k, 1.5k ‖ 0.47 µF, with a 15 µF bypass through 15k that the Shift
///     pull shorts) driving the Fender-topology stack directly (100k slope, 250 pF treble cap, 750 pF more with Shift,
///     0.1 µF, 47 nF; 250k treble and bass, 10k middle) and Volume 1 (1M audio, 180 pF pull-bright); V1B; the lead
///     drive (680k into 1M audio) into the V3A cascade (82k, 120 pF grid-to-cathode), a 270k/68k divider into the
///     V3B lead stage (270k plate with 1 nF across it, 3.3k with 0.22 µF through 15k, shorted by the lead-bright pull),
///     a 220k ‖ 250 pF / 100k ‖ 500 pF divider into V2B (1.5k unbypassed), the lead master shunting the signal into
///     a 150k/4.3k pad, the V4A recovery stage (120k, 1k ‖ 0.47 µF, 15 µF through 15k, shorted by the deep pull), the
///     master, the five-band graphic EQ, a long-tail pair and Simul-Class power (two 6L6GC in pentode beside two
///     triode-strapped EL34).
/// </summary>
/// <remarks>
///     <para>
///         Sources: the Yahiake LiveSPICE redraw "Mark IIC+(++) Preamp_GEQ" (every preamp value and the wiring, in its
///         stock, non-"++" positions), the annotated redraw on boogieforum (docs/06 §3.4), and the Music Electronics
///         Forum thread on the 120 pF grid-to-cathode cap. Values from a redraw, not the factory scan.
///     </para>
///     <para>
///         Guesses, marked: the redraw draws V3A's 2.2 µF cathode cap unconnected; it is connected here (a cascade stage
///         this hot is bypassed on every Mark lead circuit described in text). The rhythm path's 3.3M feed into V2B's
///         grid, live in the lead mode too, is left out (about -33 dB under the lead signal). The graphic EQ is the
///         behavioural constant-Q slot (<see cref="EqualizerStageDesign" />), not the LC network, whose topology the
///         redraws disagree on. The power amp is not on the redraw: inverter 82.5k/90.9k (docs/06), 475 Ω and 10k
///         tail, a 4.7k feedback shunt, 100k feedback from the 8 Ω tap, a 25k presence pot with 0.1 µF (the Mesa
///         redraws' values), B+ 465 V, 40 mA idle, a 2.5k primary: typical values, not readings.
///     </para>
/// </remarks>
public static class MarkIIC {
    public static AmpDesign Design { get; } = new(
        "Mark IIC+",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 408, 100e3, 1.5e3, 15e-6),
            new NetworkStageDesign("V3A", LeadDrive()),
            new NetworkStageDesign("V3B", LeadStage()),
            new NetworkStageDesign("V2B", LeadReturn()),
            new NetworkStageDesign("V4A", Recovery()),
            new CouplingStageDesign("Master", 47e-9, new(1e6, PotTaper.Audio), PotRole.Master, 15e3),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands)
        ],
        new(
            "2 x 6L6GC + 2 x EL34 (Simul-Class)",
            PentodeModel.SixL6GC,
            1,
            465,
            1e3,
            0.04,
            false,
            0,
            75,
            new LongTailPairDesign(TriodeModel.Ecc83, 427, 82.5e3, 90.9e3, 10e3, 475),
            47e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 100e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6, 90),
            true,
            new(2.5e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(30, 120e-6, 100, 47e-6, 10e3, 22e-6, 6e-3),
            Strapped: PentodeModel.EL34,
            StrappedPerSide: 1
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Depth = null,
            Cut = null,
            Control1 = "Lead Drive",
            Control2 = "Lead Master",
            Switch1 = "Shift",
            Switch2 = "Deep",
            Switch3 = "Lead Bright",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     V1A with the stack on its plate and Volume 1 on the treble wiper: 1M grid leak; 150k plate; 1.5k ‖ 0.47 µF,
    ///     and 15 µF through 15k (shorted by Shift); the stack; Volume 1 with 180 pF across its upper half (Bright).
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, shift, slope, trebleTop, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 408);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, shift, 15e-6);
        net.SwitchedResistor(shift, StageNetlist.Ground, 15e3, 1, SwitchRole.Switch1);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        net.Capacitor(plate, trebleTop, 250e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(plate, trebleTop, 750e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        var trebleBottom = net.Node();
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Middle);

        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, volume, 180e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Output = volume;
        return net;
    }

    /// <summary>
    ///     The lead drive into V3A: 47 nF from V1B and 100k to ground (the rhythm channel's load), 20 nF and 680k into
    ///     the 1M audio lead drive, its wiper 470k to ground and 10k to the grid with 120 pF to the cathode; 82k plate,
    ///     1.5k ‖ 2.2 µF.
    /// </summary>
    static StageNetlist LeadDrive() {
        var net = new StageNetlist();
        var (coupled, lead, top, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Capacitor(coupled, lead, 20e-9);
        net.Resistor(lead, top, 680e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(wiper, StageNetlist.Ground, 470e3);
        net.Resistor(wiper, grid, 10e3);
        net.Capacitor(wiper, cathode, 120e-12);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 82e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V3B: 22 nF, 270k into 68k ‖ 1 nF, 10k to the grid; 270k plate with 1 nF across it; 3.3k with 0.22 µF through
    ///     15k (shorted by the lead-bright pull).
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        var (coupled, divider, grid, supply, plate, cathode, bright) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, divider, 270e3);
        net.Resistor(divider, StageNetlist.Ground, 68e3);
        net.Capacitor(divider, StageNetlist.Ground, 1e-9);
        net.Resistor(divider, grid, 10e3);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 270e3);
        net.Capacitor(supply, plate, 1e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, bright, 0.22e-6);
        net.SwitchedResistor(bright, StageNetlist.Ground, 15e3, 1, SwitchRole.Switch3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead signal back into V2B: 47 nF, 220k ‖ 250 pF into 100k ‖ 500 pF, with V2B's own 680k ‖ 47 pF grid
    ///     network; 100k plate, 1.5k unbypassed.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Capacitor(coupled, grid, 250e-12);
        net.Resistor(grid, StageNetlist.Ground, 100e3 * 680e3 / (100e3 + 680e3));
        net.Capacitor(grid, StageNetlist.Ground, 547e-12);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The lead master and the recovery stage V4A: 47k and 47 nF from V2B, the 250k audio lead master as a shunt to
    ///     ground, 150k into 4.7k ‖ 47k, 120 pF grid-to-cathode; 120k plate, 1k ‖ 0.47 µF and 15 µF through 15k
    ///     (shorted by the deep pull).
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (series, master, grid, supply, plate, cathode, deep) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, series, 47e3);
        net.Capacitor(series, master, 47e-9);
        net.Rheostat(master, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        net.Resistor(master, grid, 150e3);
        net.Resistor(grid, StageNetlist.Ground, 4.7e3 * 47e3 / (4.7e3 + 47e3));
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 425);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Capacitor(cathode, deep, 15e-6);
        net.SwitchedResistor(deep, StageNetlist.Ground, 15e3, 1, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
