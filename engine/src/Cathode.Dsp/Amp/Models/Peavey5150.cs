using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Peavey 5150 (6505), lead channel: V1A (220k, 1.82k ‖ 1 µF, 68k stopper), 470k ‖ 2.2 nF into the lead pre gain
///     (1M audio with 1M across it, 1M ‖ 1 nF from its top to the wiper), 470k into V1B (100k, 1.82k ‖ 1 µF), a 470k/1M
///     divider into the V2A cold clipper (39k unbypassed), 330k ‖ 1 nF and 220k into V2B (220k, 1.82k ‖ 1 µF), 1M and
///     22 nF into V5B (220k, 2.2k unbypassed, 100k grid leak), 1M and 22 nF into V5A (100k, 2.2k unbypassed, 1M
///     from its coupled output back to the grid), then 470k into a pad (33k, and 10 nF + 47k, to ground) and the stack
///     (47k slope, 470 pF, 22 nF, 22 nF; 250k linear treble, 1M audio bass, 50k linear middle), the lead post gain,
///     the post stage V3A (68k, 3.3k ‖ 4.7 nF), a long-tail pair, four 6L6GC idling cold, feedback with presence and
///     resonance.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Peavey "EVH 5150" factory schematic (preamp, tone stack and phase inverter, power amp pages, as
///         distributed by thetubestore), read by eye from the scan. The 6505 is electrically the same amp.
///     </para>
///     <para>
///         Read with doubt from the scan: the pre gain pot's value (printed "1M5A3", read as 1M audio), the middle pot
///         (read as 50k linear), a 100 pF beside the 470 pF treble cap (C60, whose wiring could not be read; left out).
///         Not on the pages read: the preamp rails (taken as 340 V and 320 V, the inverter's as 430 V), B+ (490 V,
///         docs/06 gives 485-500 V), the idle current (13 mA, docs/06 gives 11-14 mA), the output transformer (1.9k
///         assumed), the resonance network (the depth control's generic shelf stands in for it), the feedback tap
///         (8 Ω assumed).
///     </para>
///     <para>
///         Simplified: the send follower V3B and the loop are a unity buffer (V3A's grid leak is 1M, so the post
///         gain's wiper sees the follower's high input resistance); the rhythm channel (its Bright and Crunch) is not
///         modelled.
///     </para>
/// </remarks>
public static class Peavey5150 {
    public static AmpDesign Design { get; } = new(
        "5150",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                340,
                220e3,
                1.82e3,
                1e-6,
                68e3,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V1B", PreGain()),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                340,
                100e3,
                39e3,
                CouplingCap: 22e-9,
                SeriesResistor: 470e3,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V2B", ThirdGain()),
            new TriodeStageDesign(
                "V5B",
                TriodeModel.Ecc83,
                320,
                220e3,
                2.2e3,
                InputResistance: 1e6,
                CouplingCap: 22e-9,
                GridLeak: 100e3
            ),
            new NetworkStageDesign("V5A", FeedbackStage()),
            new NetworkStageDesign("Stack", Stack()),
            new NetworkStageDesign("V3A", PostStage())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            490,
            100,
            0.013,
            false,
            0,
            120,
            new LongTailPairDesign(TriodeModel.Ecc83, 430, 82e3, 100e3, 10e3, 470),
            47e-9,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 39e3, 4.7e3, new(10e3, PotTaper.Audio), 0.1e-6),
            true,
            new(1.9e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(25, 100e-6, 100, 47e-6, 10e3, 22e-6, 6e-3)
        ),
        -7.5
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Pre Gain",
            Master = "Post Gain",
            Depth = "Resonance",
            Cut = null,
            Bright = null
        }
    };

    /// <summary>
    ///     The lead pre gain into V1B: 22 nF, 470k ‖ 2.2 nF, the 1M audio pot with 1M across it and 1M ‖ 1 nF from its
    ///     top to the wiper, 470k to the grid; 100k plate, 1.82k ‖ 1 µF.
    /// </summary>
    static StageNetlist PreGain() {
        var net = new StageNetlist();
        var (coupled, top, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, top, 470e3);
        net.Capacitor(coupled, top, 2.2e-9);
        net.Resistor(top, StageNetlist.Ground, 1e6);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(top, wiper, 1e6);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Supply(supply, 340);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.82e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V2B: 22 nF into 330k ‖ 1 nF to ground, 220k to the grid; 220k plate, 1.82k ‖ 1 µF.</summary>
    static StageNetlist ThirdGain() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Capacitor(coupled, StageNetlist.Ground, 1e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, 320);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.82e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V5A with shunt feedback: 1M and 22 nF in, 1M grid leak; 100k plate, 2.2k unbypassed; 22 nF out, and 1M from
    ///     the coupled output back to the grid.
    /// </summary>
    static StageNetlist FeedbackStage() {
        var net = new StageNetlist();
        var (series, grid, supply, plate, cathode, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, series, 1e6);
        net.Capacitor(series, grid, 22e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 320);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 22e-9);
        net.Resistor(output, grid, 1e6);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     470k into the pad (33k, and 10 nF + 47k, to ground), the stack (47k slope, 470 pF treble cap, 22 nF bass and
    ///     middle caps) and the post gain (1M audio) on the treble wiper.
    /// </summary>
    static StageNetlist Stack() {
        var net = new StageNetlist();
        var (pad, shelf, slope, trebleTop, trebleBottom, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, pad, 470e3);
        net.Resistor(pad, StageNetlist.Ground, 33e3);
        net.Capacitor(pad, shelf, 10e-9);
        net.Resistor(shelf, StageNetlist.Ground, 47e3);
        net.Capacitor(pad, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(pad, slope, 47e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(50e3, PotTaper.Linear), PotRole.Middle);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }

    /// <summary>
    ///     The post stage V3A from the loop return: 47 nF, 1M grid leak; 68k plate, 3.3k ‖ 4.7 nF; 22 nF out, 100k in
    ///     series with the inverter's grid and its 1M leak.
    /// </summary>
    static StageNetlist PostStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, coupled, inverter) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, grid, 47e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 320);
        net.Resistor(supply, plate, 68e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.Capacitor(cathode, StageNetlist.Ground, 4.7e-9);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, inverter, 100e3);
        net.Output = inverter;
        return net;
    }
}
