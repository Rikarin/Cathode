using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Dual Rectifier, red channel: V1A (220k, 1.8k with 1 µF through 47k, shorted in Modern), the voicing
///     network (2.2M to ground, 2.2M ‖ 82 pF, and in Modern 680k ‖ 2 nF beside it) into the gain pot (1M audio, 1 nF
///     bright cap), 470k and 20 pF into V1B (100k, the same switched bypass), a 470k/1M divider into the V2A cold
///     clipper (39k unbypassed, 100k plate), 330k and 220k into V2B (220k, switched bypass), a DC-coupled cathode
///     follower driving the stack (47k slope, 500 pF, 680 pF in Modern, 20 nF, 20 nF; 220k linear treble, 1M audio
///     bass, 25k linear middle) and the master; a long-tail pair, four 6L6GC, the rectifier switch (silicon or two 5U4)
///     and Bold/Spongy.
/// </summary>
/// <remarks>
///     <para>
///         Sources: the Yahiake LiveSPICE redraw "Dual Rectifier Preamp" (every preamp value; its Orange/Red relay
///         group is taken as Vintage/Modern), the warpedmusician "Mesa Rectifier design concepts" series and the
///         pedalpcb thread on the stack (docs/06 §3.4). A redraw, not the factory scan.
///     </para>
///     <para>
///         Guesses and conflicts, marked: the cold clipper's plate is 100k (redraw; the analysis says 330k); the
///         redraw's V2B bypass switch is drawn unconnected, here it follows the other two; the redraw wires the middle
///         pot with its cap on the wiper and a preamp "presence" (22k + 3 nF into a 25k rheostat) on the treble wiper,
///         here the middle is the usual rheostat and the presence is the power amp's (in the feedback). Not on the
///         redraw: the power amp (inverter 82k/100k, 10k tail, 47k feedback from the 4 Ω tap into 4.7k, a 25k presence
///         with 0.1 µF, B+ 470 V, 40 mA idle, a 1.9k primary, all typical values), the 5U4 pair's sag (two 5U4GB in
///         parallel), and Spongy as 90 % mains (Mesa describes it only as a lower plate voltage).
///     </para>
/// </remarks>
public static class DualRectifier {
    /// <summary>Two 5U4GB in parallel: twice one tube's perveance.</summary>
    public static RectifierModel FiveU4Pair { get; } = new("2 x 5U4", 2 * RectifierModel.FiveU4.Perveance);

    public static AmpDesign Design { get; } = new(
        "Dual Rectifier",
        [
            new NetworkStageDesign("V1A", InputStage()),
            new NetworkStageDesign("V1B", GainStage()),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                394,
                100e3,
                39e3,
                CouplingCap: 20e-9,
                SeriesResistor: 470e3,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V2B", FourthStage()),
            new NetworkStageDesign("V3A", FollowerAndStack())
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 2,
            Supply: 470,
            ScreenResistor: 1e3,
            IdleCurrent: 0.04,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 100,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 415, 82e3, 100e3, 10e3, 470),
            CouplingCap: 47e-9,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1 / Math.Sqrt(2), 47e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: true,
            Transformer: new(1.9e3),
            Speaker: new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            PowerSupply: new(30, 110e-6, 100, 47e-6, 10e3, 22e-6, 5e-3, RectifierModel.Silicon),
            SupplySwitches: [
                new(SwitchRole.Switch2, FiveU4Pair),
                new(SwitchRole.Switch3, Variac: 0.9)
            ]
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Modern",
            Switch2 = "Tube Rectifier",
            Switch3 = "Spongy"
        }
    };

    /// <summary>A cathode: <paramref name="ohms" /> to ground, 1 µF through 47k, the 47k shorted in Modern.</summary>
    static void SwitchedCathode(StageNetlist net, int cathode, double ohms) {
        var bypass = net.Node();
        net.CathodeResistor(cathode, StageNetlist.Ground, ohms);
        net.Capacitor(cathode, bypass, 1e-6);
        net.SwitchedResistor(bypass, StageNetlist.Ground, 47e3, 1, SwitchRole.Switch1);
    }

    /// <summary>V1A: 1M grid leak; 220k plate; 1.8k with the switched bypass.</summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 413);
        net.Resistor(supply, plate, 220e3);
        SwitchedCathode(net, cathode, 1.8e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The voicing network and the gain pot into V1B: 20 nF, 2.2M to ground, 2.2M ‖ 82 pF and (Modern) 680k ‖ 2 nF
    ///     to the pot's top, the 1M audio gain with 1 nF across its upper half, 470k and 20 pF at the grid; 100k plate,
    ///     1.8k with the switched bypass.
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (coupled, top, grid, supply, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, top, 2.2e6);
        net.Capacitor(coupled, top, 82e-12);
        net.SwitchedResistor(coupled, top, 0, 680e3, SwitchRole.Switch1);
        net.SwitchedCapacitor(coupled, top, 2e-9, SwitchRole.Switch1);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Capacitor(grid, StageNetlist.Ground, 20e-12);
        net.Supply(supply, 394);
        net.Resistor(supply, plate, 100e3);
        SwitchedCathode(net, cathode, 1.8e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V2B: 20 nF into 330k to ground, 220k to the grid; 220k plate, 1.8k with the switched bypass.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, 415);
        net.Resistor(supply, plate, 220e3);
        SwitchedCathode(net, cathode, 1.8e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cathode follower, DC-coupled to V2B's plate, 100k to ground, driving the stack (500 pF, and 180 pF more
    ///     in Modern; 47k slope; 20 nF bass and middle caps) and the 1M audio master on the treble wiper.
    /// </summary>
    static StageNetlist FollowerAndStack() {
        var net = new StageNetlist();
        var (supply, follower, slope, trebleTop, trebleBottom, middle) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, 415);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Triode(supply, net.Input, follower, TriodeModel.Ecc83);
        net.Capacitor(follower, trebleTop, 500e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(follower, trebleTop, 180e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(follower, slope, 47e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 20e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 20e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
