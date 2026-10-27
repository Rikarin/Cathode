using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Engl Savage 120 (E610), lead channel. V1B (100k, 1.5k ‖ 22 µF), 10 nF and 100k into the 1M audio gain; V2A
///     (100k, 1k ‖ 22 µF); 47 nF with 2.2M to ground, 100k into the 250k audio Lead pot (2.2M to ground before it), its
///     wiper on V2B's grid (1 nF to ground with Lead Boost); V2B (330k, 3.3k, bypassed by 10 µF with Lead Boost); 47 nF
///     into V3B's 1M grid leaks; V3B (100k, 1.5k ‖ 22 µF); 47 nF and 2.2M into the stack (470 pF, 220 pF more with
///     Contour; 47k slope; 47 nF, 22 nF; 250k linear treble, 1M audio bass, 20k linear middle); 100k, 100k into V3A's
///     idle grid network (470k) and the 250k audio lead volume; the loop's send follower V5A (1k cathode) and return
///     stage V5B (100k, 1k ‖ 22 µF), 100 nF and 47k into the 1M linear master; a long-tail pair (82k/100k, 470 Ω, 22k
///     tail, 100 nF to the second grid) and two 6550 (2.2k stoppers, 1k screens, 150k bias feeds).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Engl factory drawing "Savage 120 type E610", drawing 610-2 (ENGL Gerätebau GmbH, two sheets: the
///         whole amp, then the relay logic),
///         https://el34world.com/charts/Schematics/files/Engl/Engl_savage_120_sch.pdf. Every value above is read from
///         it. An earlier hand-drawn factory sheet
///         (https://el34world.com/charts/Schematics/files/Engl/Engl_savage_120.pdf) was not compared value by value.
///     </para>
///     <para>
///         The supply's decoupling as drawn: R108 470 Ω 5 W to the screens' CE20 100 µF, R106 and R105 (2.2k each) to
///         CE21 22 µF (the inverter), R107 1k to V5 (no cap of its own: here on CE21), R56 2.2k 1 W to CE4 10 µF (V3,
///         V4), R55 2.2k to CE9 10 µF (V2), R54 2.2k to CE6 10 µF (V1).
///     </para>
///     <para>
///         Guesses and simplifications, marked: the drawing prints no rail voltages (B+ 560 V, idle 50 mA a tube and a
///         3.4k primary are typical values for two 6550 at 120 W; the preamp's rails, 530 V at CE21 down to 511 V at
///         CE6, are the drawn chain's drops at the assumed draws); Rough/Smooth is at Smooth, which
///         bypasses V3A (Rough puts V3A and its 100k/4.7k pad in the path), and the high-balance network is off; the
///         crunch-mode pad at V3B's grid (33k via relay RE4B) is out; the loop is empty, its follower driving V5B
///         through the 10k balance resistor. The feedback network is simplified: the presence pot sits in series with
///         the feedback on the real amp (turning it down lowers the feedback); here 1.5k plus half the 50k presence
///         from the 8 Ω tap into a 4.8k shunt (1.5k and 3.3k), the presence's 10 nF across it, and the Depth Boost
///         switch (10 nF across the feedback path) as the depth control.
///     </para>
/// </remarks>
public static class Savage120 {
    // The rails at rest, from the drawn chain at the assumed draws (no voltages are printed): CE21, the inverter and
    // V5, 556.9 V at CE20 less 4.4k × 6 mA; CE4 (V3) less 3.2k × 4 mA; CE9 (V2) less 2.2k × 2 mA; CE6 (V1) less 2.2k ×
    // 1 mA. They were once taken as 380 V for every stage and 400 V for the inverter, which the drawn droppers cannot
    // give from a 560 V B+ at any plausible draw (380 V would need about 30k at 6 mA).
    const double InverterRail = 530;
    const double V3Rail = 518;
    const double V2Rail = 513;
    const double V1Rail = 511;

    public static AmpDesign Design { get; } = new(
        "Savage 120",
        [
            new NetworkStageDesign("V1B", InputAndGain()) { SupplyNode = 3 },
            new NetworkStageDesign("V2A", SecondStage()) { SupplyNode = 2 },
            new NetworkStageDesign("V2B", ThirdStage()) { SupplyNode = 2 },
            new TriodeStageDesign("V3B", TriodeModel.Ecc83, V3Rail, 100e3, 1.5e3, 22e-6) { SupplyNode = 1 },
            new NetworkStageDesign("Stack", LeadStack()) { SupplyNode = 1 },
            new NetworkStageDesign("V5A", Follower()),
            new NetworkStageDesign("V5B", ReturnAndMaster())
        ],
        new(
            "2 x 6550",
            PentodeModel.SixtyFiveFifty,
            1,
            560,
            1e3,
            0.05,
            false,
            0,
            120,
            new LongTailPairDesign(TriodeModel.Ecc83, InverterRail, 82e3, 100e3, 22e3, 470),
            47e-9,
            150e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 26.5e3, 4.8e3, new(50e3, PotTaper.Linear), 10e-9),
            true,
            new(3.4e3),
            SpeakerDesign.Closed4x12,
            // R108 470 Ω 5 W to the screens' CE20 100 µF, R106 + R105 (2.2k each) to CE21 22 µF.
            new(35, 220e-6, 470, 100e-6, 4.4e3, 22e-6, 6e-3, RectifierModel.Silicon),
            InverterInput: new(100e-9, 100e3)
        ),
        -7.5
    ) {
        Panel = AmpPanel.Standard with {
            Depth = "Depth Boost",
            Cut = null,
            Bright = null,
            Control1 = "Lead",
            Control2 = "Lead Volume",
            Switch1 = "Contour",
            Switch2 = "Lead Boost"
        },
        PreampSupply = Decoupling
    };

    /// <summary>
    ///     The drawing's chain below CE21 (the inverter, and V5 behind R107 1k with no cap of its own, here on CE21):
    ///     R107 + R56 (3.2k) to CE4 10 µF (V3, V4), R55 2.2k to CE9 10 µF (V2), R54 2.2k to CE6 10 µF (V1). The drawing
    ///     prints no voltages, so each node draws an equal share of the preamp's 6 mA per triode on it (twelve in all,
    ///     the inverter's two included): 0.5 mA each.
    /// </summary>
    static PreampSupplyDesign Decoupling =>
        new([new(3.2e3, 10e-6, 2e-3), new(2.2e3, 10e-6, 1e-3, 1), new(2.2e3, 10e-6, 1e-3, 2)]);

    /// <summary>V1B and the gain: 1M grid leak; 100k plate; 1.5k ‖ 22 µF; 10 nF, 100k into the 1M audio gain.</summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, V1Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 10e-9);
        net.Resistor(coupled, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return net;
    }

    /// <summary>
    ///     V2A and the Lead pot: 100k plate; 1k ‖ 22 µF; 47 nF, 2.2M to ground, 100k into the 250k audio Lead with 2.2M
    ///     to ground at its top.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, V2Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Resistor(coupled, top, 100e3);
        net.Resistor(top, StageNetlist.Ground, 2.2e6);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);
        return net;
    }

    /// <summary>
    ///     V2B: 1 nF from the grid to ground with Lead Boost; 330k plate; 3.3k, bypassed by 10 µF with Lead Boost; 47
    ///     nF into V3B's grid leaks (1M and 1M).
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (supply, plate, cathode, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedCapacitor(net.Input, StageNetlist.Ground, 1e-9, SwitchRole.Switch2);
        net.Supply(supply, V2Rail);
        net.Resistor(supply, plate, 330e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 3.3e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 10e-6, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, 47e-9);
        net.Resistor(output, StageNetlist.Ground, 500e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The stack: 47 nF and 2.2M to ground; 470 pF (and 220 pF more with Contour) to the 250k linear treble's top;
    ///     47k slope; 47 nF to the 1M audio bass rheostat, 22 nF to the 20k linear middle; the treble wiper into 100k,
    ///     loaded there by V3A's idle grid network (100k into 470k), and 100k on into the 250k audio lead volume.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var (coupled, slope, trebleTop, bassTop, middle, junction, grid, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);
        net.Capacitor(coupled, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(coupled, trebleTop, 220e-12, SwitchRole.Switch1, CapacitorRole.Stack);
        net.Resistor(coupled, slope, 47e3);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, bassTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, junction, 100e3);
        net.Resistor(junction, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Resistor(junction, top, 100e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control2);
        return net;
    }

    /// <summary>
    ///     V5A, the loop's send follower: 100k grid leak; 100k ‖ 47 nF to the rail; 1k cathode; 1 µF and the balance's
    ///     10k into V5B's 100k grid leak.
    /// </summary>
    static StageNetlist Follower() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, send, ret) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 1);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, 47e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, send, 1e-6);
        net.Resistor(send, ret, 10e3);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Output = ret;
        return net;
    }

    /// <summary>V5B, the return stage (100k, 1k ‖ 22 µF), and 100 nF, 47k into the 1M linear master.</summary>
    static StageNetlist ReturnAndMaster() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(supply, InverterRail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, top, 47e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
