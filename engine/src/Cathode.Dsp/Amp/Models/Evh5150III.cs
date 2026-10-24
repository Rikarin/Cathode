using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     EVH 5150III (100 W), red channel: V1A (220k, 1.8k ‖ 1 µF, 10k + 39 pF and 68k at the input, a 68k + 830 pF
///     treble shunt on the plate), 470 pF into the gain pot (1M audio, 1 nF bright cap), 470k and 10k into V1B (100k,
///     1.8k ‖ 1 µF), 1 nF and a 470k/820k divider into the V2A cold clipper (5.1k unbypassed, 100k plate with 1 nF
///     across it), 330k and 220k into V2B (220k, 1.8k ‖ 1 µF), 1M into V3A (220k, 2.2k unbypassed, 100k leak), 1M
///     into V3B (220k, 2.2k unbypassed, 22 nF + 1M from plate to grid), 560k into a cathode follower (its grid held
///     by 220k and 330k ‖ 220 nF), 43k into a 33k + 10 nF shunt and the stack (47k slope, 470 pF, 47 nF bass, 22 nF
///     middle; 250k linear treble, 1M audio bass, 25k linear middle), the channel volume; a long-tail pair, four 6L6GC.
/// </summary>
/// <remarks>
///     <para>
///         Source: the MARKTHERENCE LiveSPICE redraws "EVH 5150 III 100S Stealth - Channel 3" and its power amp, which
///         are hobbyist redraws of unclear provenance (docs/06 §3.4: "redraw only"). Every value here is unverified
///         against a factory drawing.
///     </para>
///     <para>
///         Further guesses: the redraw draws the presence cap as two 68 nF in parallel on a 10k pot; one 68 nF is used
///         (docs/06 quotes 6.8 nF). Its resonance network (120k ‖ 6.8 nF with a 1M rheostat in the feedback) is the
///         depth control's generic shelf here; the feedback tap is taken as 8 Ω, the output transformer as 1.9k, the
///         idle current as 35 mA; the stack's middle pot is the usual rheostat (the redraw has the cap on its wiper).
///     </para>
/// </remarks>
public static class Evh5150III {
    public static AmpDesign Design { get; } = new(
        "5150III",
        [
            new NetworkStageDesign("V1A", InputStage(), 10e3),
            new NetworkStageDesign("V1B", GainStage()),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                297,
                100e3,
                5.1e3,
                CouplingCap: 1e-9,
                SeriesResistor: 470e3,
                GridLeak: 820e3,
                PlateBypass: 1e-9
            ),
            new NetworkStageDesign("V2B", FourthStage()),
            new TriodeStageDesign(
                "V3A",
                TriodeModel.Ecc83,
                341,
                220e3,
                2.2e3,
                CouplingCap: 22e-9,
                SeriesResistor: 1e6,
                GridLeak: 100e3
            ),
            new NetworkStageDesign("V3B", FeedbackStage()),
            new NetworkStageDesign("V4A", FollowerAndStack())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            485,
            100,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 402, 82e3, 100e3, 10e3, 470),
            47e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 39e3, 4.7e3, new(10e3, PotTaper.Audio), 68e-9),
            true,
            new(1.9e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            new(25, 100e-6, 100, 47e-6, 10e3, 22e-6, 6e-3)
        ),
        -7
    ) { Panel = AmpPanel.Standard with { Master = "Volume", Depth = "Resonance", Cut = null, Bright = null } };

    /// <summary>
    ///     V1A: 39 pF and 1M at the jack, 68k stopper; 220k plate, 1.8k ‖ 1 µF; 68k + 830 pF from the plate to B+.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, shunt) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, StageNetlist.Ground, 39e-12);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 68e3);
        net.Supply(supply, 297);
        net.Resistor(supply, plate, 220e3);
        net.Resistor(plate, shunt, 68e3);
        net.Capacitor(shunt, supply, 830e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The gain pot into V1B: 470 pF, 1M to ground, the 1M audio gain with 1 nF across its upper half and 1M from
    ///     its wiper to ground, 470k and 10k to the grid; 100k plate, 1.8k ‖ 1 µF.
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (top, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 470e-12);
        net.Resistor(top, StageNetlist.Ground, 1e6);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        net.Resistor(wiper, grid, 480e3);
        net.Supply(supply, 297);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V2B: 22 nF into 330k to ground, 220k to the grid; 220k plate, 1.8k ‖ 1 µF.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, 341);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     V3B: 22 nF and 1M in, 1M grid leak; 220k plate, 2.2k unbypassed; 22 nF + 1M from the plate to the grid.
    /// </summary>
    static StageNetlist FeedbackStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, feedback) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, grid, 1e6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 341);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, feedback, 22e-9);
        net.Resistor(feedback, grid, 1e6);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The follower and the stack: 560k from V3B's plate to the grid, 220k from the grid to 330k ‖ 220 nF; 100k
    ///     cathode load; 43k into 33k + 10 nF to ground; the stack and the 1M audio volume on the treble wiper.
    /// </summary>
    static StageNetlist FollowerAndStack() {
        var net = new StageNetlist();
        var (grid, hold, supply, follower, drive, shelf, slope, trebleTop, trebleBottom, middle) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Resistor(net.Input, grid, 560e3);
        net.Resistor(grid, hold, 220e3);
        net.Resistor(hold, StageNetlist.Ground, 330e3);
        net.Capacitor(hold, StageNetlist.Ground, 220e-9);
        net.Supply(supply, 341);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Triode(supply, grid, follower, TriodeModel.Ecc83);
        net.Resistor(follower, drive, 43e3);
        net.Resistor(drive, shelf, 33e3);
        net.Capacitor(shelf, StageNetlist.Ground, 10e-9);
        net.Capacitor(drive, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(drive, slope, 47e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 47e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
