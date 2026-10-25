using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Dual Rectifier, two-channel (1993, "Preamp RF-1F"), red channel: V1A (220k, 1.8k ‖ 1 µF), 20 nF into
///     the voicing network (2.2M to ground; 2.2M ‖ 82 pF beside 680k ‖ 2 nF) and the 1M audio red gain (1 nF from its
///     top to the wiper), 470k with 20 pF to ground into V2A (100k, 1.8k ‖ 1 µF), 20 nF and 470k into the V2B cold
///     clipper (1M grid leak, 39k unbypassed, 100k plate with 1 nF across it), 20 nF with 330k to ground and 220k into
///     V3A (220k, 1.8k ‖ 1 µF), a DC-coupled cathode follower (100k) driving the red stack (680 pF to the 250k linear
///     treble, 47k slope, 20 nF to the 1M audio bass, 20 nF from the slope to the wiper of the 25k linear middle), the
///     preamp presence on the treble wiper (22k and 3 nF into a 25k rheostat) and the 1M audio master; 20 nF into a
///     long-tail pair (82k / 91k, 470 Ω, 10k + 4.7k tail), feedback 47k with 0.1 µF into the 4.7k (the orange channel's
///     25k presence pot with 0.1 µF across it) in Vintage only; four 6L6GC at 460 V with 1k screens, the rectifier
///     switch (silicon or two 5U4) and Bold/Spongy.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "Dual Rectifier" (preamp RF-1F, Geo. M., 6-93; power amp, effects
///         loop, power supplies, the LDR switching matrix with its table of LDR states per mode),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_dualrectifier.pdf, read by eye. Node
///         voltages: A 460, B 454, C 422, D 406, E 402 V; bias -51 V.
///     </para>
///     <para>
///         The supply's decoupling, from the power supplies sheet: the choke to B (30 µF), R602 2.7k to C (30 µF: V3,
///         the loop, the inverter), and from C both R261 15k to D (30 µF: V2) and R252 22k to E (30 µF: V1A); each
///         node's draw is what its drawn voltage drops across its resistor (C 11.9 mA in all, D 1.1 mA, E 0.9 mA).
///     </para>
///     <para>
///         The drawing corrects the earlier model, which followed the Yahiake LiveSPICE redraw: in both red modes the
///         LDR table has V1A's, V2A's and V3A's cathodes fully bypassed (LDR3, LDR10 on) and the 680k ‖ 2 nF branch in
///         (LDR4 on); Modern and Vintage differ only in LDR19, the global feedback (on in Vintage, off in Modern), and
///         LDR7, a treble roll-off on the idle orange channel's presence. The treble cap is 680 pF in both; the cold
///         clipper has 1 nF from plate to supply; the middle pot takes its 20 nF on the wiper; the red presence is a
///         preamp shunt.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the inverter's second plate resistor is printed "90K" (91k, as on the
///         three-channel drawing); the 120 pF across each plate resistor and the 75 pF between the plates are left out;
///         the effects loop (V4) is bypassed (loop off: the master's wiper drives the inverter's 20 nF); the orange
///         stack, which hangs on the red one through 1M, is left out; the feedback tap is taken as 8 Ω; the orange
///         presence pot in the feedback has no knob here (fixed at noon), the red one is Control 2. Not printed: the
///         output transformer (1.9k), the idle current (40 mA), the 5U4 pair's sag (two 5U4GB in parallel), Spongy as
///         90 % mains (Mesa describes it only as a lower plate voltage).
///     </para>
/// </remarks>
public static class DualRectifier {
    /// <summary>Two 5U4GB in parallel: twice one tube's perveance.</summary>
    public static RectifierModel FiveU4Pair { get; } = new("2 x 5U4", 2 * RectifierModel.FiveU4.Perveance);

    public static AmpDesign Design { get; } = new(
        "Dual Rectifier",
        [
            new NetworkStageDesign("V1A", InputStage(402, 0)) { SupplyNode = 2 },
            new NetworkStageDesign("V2A", GainStage(new(1e6, PotTaper.Audio), 470e3, 406, 0, null)) { SupplyNode = 1 },
            new NetworkStageDesign("V2B", ColdClipper(470e3, 406)) { SupplyNode = 1 },
            new NetworkStageDesign("V3A", FourthStage(422, 0)),
            new NetworkStageDesign("V3B", FollowerAndStack(422, 680e-12, null, new(25e3, PotTaper.Linear)))
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            460,
            1e3,
            0.04,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 422, 82e3, 91e3, 14.7e3, 470),
            47e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 47e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.9e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            // 220 + 220 µF stacked, the choke to B's 30 µF, R602 2.7k to C's 30 µF: 454 V to 422 V is 11.9 mA.
            new(30, 110e-6, 100, 30e-6, 2.7e3, 30e-6, 11.9e-3, RectifierModel.Silicon),
            InverterInput: new(20e-9, 1e6),
            SupplySwitches: [
                new(SwitchRole.Switch2, FiveU4Pair),
                new(SwitchRole.Switch3, Variac: 0.9)
            ],
            FeedbackOffSwitch: SwitchRole.Switch1
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control2 = "Presence",
            Switch1 = "Modern",
            Switch2 = "Tube Rectifier",
            Switch3 = "Spongy"
        },
        PreampSupply = Decoupling(406, 402, 30e-6)
    };

    /// <summary>
    ///     The supply sheet's nodes below C (the preamp node, V3 and the inverter): R261 15k to D (V2) and R252 22k to
    ///     E (V1A), each on its own filter cap, drawing what the drawn voltages drop across them from C's 422 V.
    /// </summary>
    internal static PreampSupplyDesign Decoupling(double d, double e, double cap, double c = 422) =>
        new([new(15e3, cap, (c - d) / 15e3), new(22e3, cap, (c - e) / 22e3)]);

    /// <summary>
    ///     A cathode: 1.8k to ground with 1 µF across it, through <paramref name="series" /> ohms (the three-channel's
    ///     100 Ω; 0 for none).
    /// </summary>
    static void Cathode(StageNetlist net, int cathode, double series) {
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        if (series > 0) {
            var bypass = net.Node();
            net.Capacitor(cathode, bypass, 1e-6);
            net.Resistor(bypass, StageNetlist.Ground, series);
        } else {
            net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        }
    }

    /// <summary>V1A: 1M grid leak; 220k plate from <paramref name="supply" />; 1.8k with 1 µF.</summary>
    internal static StageNetlist InputStage(double supply, double bypassSeries) {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 220e3);
        Cathode(net, cathode, bypassSeries);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The voicing network and the gain into V2A: 20 nF, 2.2M to ground, 2.2M ‖ 82 pF and 680k ‖ 2 nF (taken out
    ///     while <paramref name="raw" /> is on) to the gain pot's top, 1 nF across its upper part, the wiper through
    ///     <paramref name="stopper" /> to the grid with 20 pF to ground; 100k plate, 1.8k with 1 µF.
    /// </summary>
    internal static StageNetlist GainStage(
        Pot gain,
        double stopper,
        double supply,
        double bypassSeries,
        SwitchRole? raw
    ) {
        var net = new StageNetlist();
        var (coupled, top, grid, rail, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, top, 2.2e6);
        net.Capacitor(coupled, top, 82e-12);
        if (raw is { } role) {
            net.SwitchedResistor(coupled, top, 680e3, 0, role);
            net.SwitchedCapacitor(coupled, top, 0, role, off: 2e-9);
        } else {
            net.Resistor(coupled, top, 680e3);
            net.Capacitor(coupled, top, 2e-9);
        }

        var wiper = net.Pot(top, StageNetlist.Ground, gain, PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, stopper);
        net.Capacitor(grid, StageNetlist.Ground, 20e-12);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        Cathode(net, cathode, bypassSeries);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cold clipper V2B: 20 nF, <paramref name="series" /> into the grid with 1M to ground; 100k plate with 1 nF
    ///     from plate to supply; 39k unbypassed.
    /// </summary>
    internal static StageNetlist ColdClipper(double series, double supply) {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, grid, series);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 1e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 39e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V3A: 20 nF into 330k to ground, 220k to the grid; 220k plate, 1.8k with 1 µF.</summary>
    internal static StageNetlist FourthStage(double supply, double bypassSeries) {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 220e3);
        Cathode(net, cathode, bypassSeries);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cathode follower V3B (DC-coupled to V3A's plate, 100k to ground) driving the stack: <paramref name="treble" />
    ///     to the 250k linear treble pot (<paramref name="modern" /> more while Switch 1 is on), 47k slope, 20 nF to the 1M
    ///     audio bass rheostat, 20 nF from the slope to the 25k linear middle pot's wiper; on the treble wiper the presence
    ///     shunt (22k, 3 nF and <paramref name="presence" /> as a rheostat, Control 2) and the 1M audio master.
    /// </summary>
    internal static StageNetlist FollowerAndStack(double supply, double treble, double? modern, Pot presence) {
        var net = new StageNetlist();
        var (rail, follower, slope, trebleTop, trebleFoot, middleTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Triode(rail, net.Input, follower, TriodeModel.Ecc83);
        net.Capacitor(follower, trebleTop, treble, CapacitorRole.Stack);
        if (modern is { } extra) {
            net.SwitchedCapacitor(follower, trebleTop, extra, SwitchRole.Switch1, CapacitorRole.Stack);
        }

        net.Resistor(follower, slope, 47e3);
        var wiper = net.Pot(trebleTop, trebleFoot, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleFoot, 20e-9, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 20e-9, CapacitorRole.Stack);

        var (shunt, shuntCap) = (net.Node(), net.Node());
        net.Resistor(wiper, shunt, 22e3);
        net.Capacitor(shunt, shuntCap, 3e-9);
        net.Rheostat(shuntCap, StageNetlist.Ground, presence, PotRole.Control2);
        net.Output = net.Pot(wiper, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }
}
