using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Mark V (90 W), channel 3 with its three modes (Mark IIC+, Mark IV, Extreme). V1A (150k, 1.5k ‖
///     0.47 µF) drives the channel's stack (1000 pF treble cap, 100k slope, 0.1 µF, 47 nF; 200k audio treble, 250k audio
///     bass, 10k audio middle) and a fixed divider on the treble wiper (330k and 56k into 470k + 100k, 180 pF from the
///     treble pot's top); V1B (100k, 1.5k ‖ 0.47 µF); 47 nF and 100k to ground, 680k into the 1M audio gain, its wiper
///     on V5A's grid with 475k to ground and 120 pF to the cathode (82k, 1.5k ‖ 2.2 µF); 20 nF and 270k into 68k ‖ 1 nF
///     at V4B (120 pF grid to cathode, 270k, 3.3k with 0.22 µF by the Bright switch); 47 nF, 220k ‖ 250 pF into 680k ‖
///     100k ‖ 547 pF at V3A (100k, 1.5k with the mode's bypass); 47 nF, 47k into 47k, 150k and 3.3k into V6A's grid with
///     47k to ground (4.7k more in Mark IV mode) and 120 pF to the cathode (120k, 1k with 4.4 µF in IIC+ mode, else
///     15 µF); the channel master (100k audio, a shunt); the five-band graphic EQ; the output control; a long-tail pair
///     and Simul-Class power (two 6L6GC in pentode beside two triode-strapped 6L6GC).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "Mark V" (John M, Mar 10 2009, board rev MARKV-1, eight sheets:
///         supply, preamp parts 1–3, reverb, EQ, loop/driver, power amp),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Mesaboogiemark_v.pdf. The supply sheet names the
///         relays: M7 is channel 3's IIC+ mode, M8 Mark IV, M9 Extreme, each on in its mode. Every preamp value is read
///         from it; channel-3 voltages: C 410 V, E 405 V, A 448 V.
///     </para>
///     <para>
///         As drawn, marked: the stack's 750 pF and 250 pF treble caps are both connected (1000 pF). The relays read
///         from the drawing: Mark IV mode (RYM8) adds the 4.7k at V6A's grid; IIC+ mode (RYM7) puts 4.4 µF (two
///         2.2 µF) on V6A's cathode in place of 15 µF.
///     </para>
///     <para>
///         Guesses, marked: the drawing does not say which mode drives the JFET switches. Here V1A's 15 µF bypass
///         (J175A) is off in channel 3; V3A's 2.2 µF (J175B) is on in Mark IV mode and its 100 µF (J175C) in Extreme
///         mode, IIC+ mode leaving it unbypassed (as the IIC+'s equivalent stage is; the Mark IV's has a partial
///         bypass). The 3.3M ‖ 20 pF feed from V1B into V3A's grid, the reverb (level zero; its send and return loads
///         are the 47k and 150k above), the effects loop and its recovery stage V6B, and the zener clamps are left out.
///         The inverter and feedback are simplified: 82k/91k plates, 470 Ω, a 4.8k tail (1.5k and 3.3k, whose 47 nF is
///         left out), feedback 4.7k + 3.3k + 56k from the 8 Ω tap into the 1.5k, the presence pots (values not
///         printed) as 25k with 0.1 µF. The primary (2.2k) is a typical value; 45 W and 10 W modes are not modelled.
///     </para>
/// </remarks>
public static class MarkV {
    public static AmpDesign Design { get; } = new(
        "Mark V",
        [
            new NetworkStageDesign("V1A", InputAndStack()),
            new TriodeStageDesign("V1B", TriodeModel.Ecc83, 405, 100e3, 1.5e3, 0.47e-6),
            new NetworkStageDesign("V5A", GainStage()),
            new NetworkStageDesign("V4B", LeadStage()),
            new NetworkStageDesign("V3A", LeadReturn()),
            new NetworkStageDesign("V6A", Recovery()),
            new EqualizerStageDesign("GEQ", EqualizerStageDesign.MarkBands),
            new CouplingStageDesign("Output", 0.68e-6, new(1e6, PotTaper.Audio), PotRole.Master, 220e3)
        ],
        new(
            "4 x 6L6GC (Simul-Class)",
            PentodeModel.SixL6GC,
            1,
            448,
            470,
            0.04,
            false,
            0,
            90,
            new LongTailPairDesign(TriodeModel.Ecc83, 410, 82e3, 91e3, 4.8e3, 470),
            0.1e-6,
            150e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 64e3, 1.5e3, new(25e3, PotTaper.Linear), 0.1e-6),
            true,
            new(2.2e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(30, 110e-6, 100, 30e-6, 5.6e3, 33e-6, 6e-3, RectifierModel.Silicon),
            Strapped: PentodeModel.SixL6GC,
            StrappedPerSide: 1
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Master = "Output",
            Control1 = "Master",
            Switch1 = "IIC+ Mode",
            Switch2 = "Mark IV Mode",
            Switch3 = "Bright",
            Equalizer = ["80", "240", "750", "2200", "6600"]
        }
    };

    /// <summary>
    ///     V1A with channel 3's stack on its plate: 1M grid leak; 150k plate; 1.5k ‖ 0.47 µF; 1000 pF, 100k slope,
    ///     0.1 µF, 47 nF; the treble wiper into 330k + 56k and 470k + 100k to ground, 180 pF from the treble pot's top.
    /// </summary>
    static StageNetlist InputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, slope, trebleTop, trebleBottom, middle, divider, output) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 405);
        net.Resistor(supply, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        net.Capacitor(plate, trebleTop, 1000e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(200e3, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(10e3, PotTaper.Audio), PotRole.Middle);

        net.Resistor(treble, divider, 330e3);
        net.Resistor(divider, output, 56e3);
        net.Resistor(output, StageNetlist.Ground, 570e3);
        net.Capacitor(trebleTop, output, 180e-12, CapacitorRole.Bright);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The gain into V5A: 47 nF and 100k to ground, 680k into the 1M audio gain, its wiper on the grid with 475k to
    ///     ground and 120 pF to the cathode; 82k plate, 1.5k ‖ 2.2 µF.
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (coupled, top, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 100e3);
        net.Resistor(coupled, top, 680e3);
        var grid = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(grid, StageNetlist.Ground, 475e3);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 82e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V4B: 20 nF, 270k into 68k ‖ 1 nF at the grid, 120 pF to the cathode; 270k plate; 3.3k with 0.22 µF while the
    ///     Bright switch is on.
    /// </summary>
    static StageNetlist LeadStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, grid, 270e3);
        net.Resistor(grid, StageNetlist.Ground, 68e3);
        net.Capacitor(grid, StageNetlist.Ground, 1e-9);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 270e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 0.22e-6, SwitchRole.Switch3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V3A: 47 nF, 220k ‖ 250 pF into 680k ‖ 100k ‖ 547 pF; 100k plate; 1.5k with 2.2 µF in Mark IV mode and
    ///     100 µF in Extreme mode (neither IIC+ nor Mark IV), both guesses.
    /// </summary>
    static StageNetlist LeadReturn() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, extreme, notIv) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Capacitor(coupled, grid, 250e-12);
        net.Resistor(grid, StageNetlist.Ground, 680e3 * 100e3 / (680e3 + 100e3));
        net.Capacitor(grid, StageNetlist.Ground, 547e-12);
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 2.2e-6, SwitchRole.Switch2);
        net.Capacitor(cathode, extreme, 100e-6);
        net.SwitchedResistor(extreme, notIv, 1, 0, SwitchRole.Switch1);
        net.SwitchedResistor(notIv, StageNetlist.Ground, 1, 0, SwitchRole.Switch2);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The pad and V6A: 47 nF, 47k into 47k to ground, 150k and 3.3k into the grid with 47k to ground (and 4.7k in
    ///     Mark IV mode), 120 pF to the cathode; 120k plate; 1k with 4.4 µF in IIC+ mode, else 15 µF; 47 nF into the
    ///     channel master, a 100k audio rheostat to ground.
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (coupled, send, ret, grid, supply, plate, cathode, master) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, send, 47e3);
        net.Resistor(send, StageNetlist.Ground, 47e3);
        net.Resistor(send, ret, 150e3);
        net.Resistor(ret, grid, 3.3e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.SwitchedResistor(grid, StageNetlist.Ground, 0, 4.7e3, SwitchRole.Switch2);
        net.Capacitor(grid, cathode, 120e-12);
        net.Supply(supply, 410);
        net.Resistor(supply, plate, 120e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 4.4e-6, SwitchRole.Switch1, off: 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, master, 47e-9);
        net.Rheostat(master, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Control1);
        net.Output = master;
        return net;
    }
}
