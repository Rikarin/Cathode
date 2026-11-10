using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Rect-o-Verb (first series, 50 W), lead channel: V1A (220k, 1.8k ‖ 1 µF), 20 nF and 680k ‖ 2 nF into
///     the 250k audio lead gain (1 nF from its top to the wiper), 470k with 20 pF to ground and 150 Ω into V1B (100k,
///     1.8k ‖ 1 µF and 15 µF through 22k), 47 nF with 1.5M to ground and 475k into the V2B cold clipper (1M grid leak,
///     39k unbypassed, 100k plate with 1 nF across it), 20 nF with 330k to ground and 220k into V3A (220k, 1.8k ‖ 1
///     µF), a DC-coupled cathode follower (100k) driving the stack (500 pF, 47k slope, 20 nF, 20 nF; 250k linear
///     treble, 1M audio bass, 25k linear middle), the presence shunt on the treble wiper (22k and 3 nF into a
///     rheostat), 220k into the 1M audio lead master; a long-tail pair (82k/100k, 470 Ω, 10k tail, feedback 47k into
///     4.7k from the 8 Ω tap) and two 6L6GC at 454 V with 1k screens.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawing set "Solo 50 / Rectoverb" (seven sheets; the Rectoverb's own parts
///         boxed "Rectoverb only"),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_solo50_rectoverb.pdf. Every preamp and
///         power-amp value above is read from it, with its voltages (E 394 V, D 376 V, C 415 V, A 454 V, B 451 V, bias
///         -48 V). The supply sheet is titled Solo 50 (silicon rectifier).
///     </para>
///     <para>
///         The supply's decoupling, from that sheet: the choke to B (30 µF), 5.6k to C (10 µF: V3, the inverter), and
///         15k to D (10 µF: V1B, V2B), 22k to E (10 µF: V1A) and 47k to F (10 µF: the loop's V4, not modelled; its draw
///         stays on C). The sheet draws two resistors side by side from B, only one labelled (5K6); the tree here is
///         the one the drawn voltages fit (D and E hang from C: 39 V across 15k and 21 V across 22k, which V1A's 220k
///         plate load draws). Each node's draw is what its voltage drops across its resistor (C 6.4 mA in all).
///     </para>
///     <para>
///         The stack is not wired as the Dual Rectifier's: sheet "Preamp pt. 2" draws the middle (25KL) and the bass
///         (1MA) both as two-terminal variable resistors in series to ground, with the middle's 20 nF on the junction
///         between them (the middle's top), where the Dual Rectifier's drawing puts it on the middle pot's wiper.
///     </para>
///     <para>
///         Guesses and simplifications, marked: the presence rheostat's value is not printed (250k linear here); the
///         effects-loop stage V4 and the output level control are left out (the lead master feeds the inverter, here
///         the Master knob); the reverb is at zero (its send, a 475k from the treble wiper, is left out); in the lead
///         channel the feedback shunt carries 0.1 µF, 2.7k and 5.6k in series (the rhythm channel's presence pot is
///         switched out), here the power amp's presence network at that value with its knob hidden; the 120 pF across
///         each inverter plate resistor and the 75 pF between the plates are left out. Not on the drawing: the idle
///         current (40 mA), the primary (4k, a typical value for two 6L6) and the Series II changes. The rhythm channel
///         is not modelled.
///     </para>
/// </remarks>
public static class RectoVerb {
    public static AmpDesign Design { get; } = new(
        "Rect-o-Verb",
        [
            new NetworkStageDesign("V1A", InputAndGain()) { SupplyNode = 2 },
            new NetworkStageDesign("V1B", SecondStage()) { SupplyNode = 1 },
            new NetworkStageDesign("V2B", ColdClipper()) { SupplyNode = 1 },
            // V3A (20 nF into 330k to ground, 220k to the grid; 220k plate; 1.8k ‖ 1 µF) and the follower with the
            // stack are the Dual Rectifier's, with this drawing's values: 500 pF treble cap, the middle a rheostat
            // with its 20 nF at the top, a 250k presence rheostat and 220k ahead of the lead master.
            new NetworkStageDesign("V3A", DualRectifier.FourthStage(415, 0)),
            new NetworkStageDesign(
                "V3B",
                DualRectifier.FollowerAndStack(
                    415,
                    500e-12,
                    null,
                    new(250e3, PotTaper.Linear),
                    middleOnWiper: false,
                    masterSeries: 220e3
                )
            )
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            454,
            1e3,
            0.04,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, 415, 82e3, 100e3, 10e3, 470),
            47e-9,
            220e3,
            0,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 47e3, 4.7e3, new(16.6e3, PotTaper.Linear), 0.1e-6),
            true,
            new(4e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            // The choke to B's 30 µF, 5.6k to C's 10 µF: 451 V to 415 V is 6.4 mA.
            new(30, 110e-6, 100, 30e-6, 5.6e3, 10e-6, 6.4e-3, RectifierModel.Silicon)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control2 = "Presence"
        },
        // From C: 15k to D (V1B, V2B) and 22k to E (V1A), 10 µF each, drawing what the drawn voltages drop across them.
        PreampSupply = new([new(15e3, 10e-6, (415 - 376) / 15e3), new(22e3, 10e-6, (415 - 394) / 22e3)])
    };

    /// <summary>
    ///     V1A and the lead gain: 1M grid leak; 220k plate; 1.8k ‖ 1 µF; 20 nF, 680k ‖ 2 nF into the 250k audio gain
    ///     with 1 nF across its upper half; 470k from the wiper with 20 pF to ground.
    /// </summary>
    static StageNetlist InputAndGain() {
        var net = new StageNetlist();
        var (supply, plate, cathode, coupled, top, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 394);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 20e-9);
        net.Resistor(coupled, top, 680e3);
        net.Capacitor(coupled, top, 2e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Capacitor(grid, StageNetlist.Ground, 20e-12);
        net.Output = grid;
        return net;
    }

    /// <summary>V1B: 150 Ω grid stopper; 100k plate; 1.8k ‖ 1 µF and 15 µF through 22k.</summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode, partial) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 150);
        net.Supply(supply, 376);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Capacitor(cathode, partial, 15e-6);
        net.Resistor(partial, StageNetlist.Ground, 22e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The V2B cold clipper: 47 nF with 1.5M to ground, 475k into the grid with 1M to ground; 100k plate with 1 nF
    ///     across it; 39k unbypassed.
    /// </summary>
    static StageNetlist ColdClipper() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1.5e6);
        net.Resistor(coupled, grid, 475e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(supply, 376);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, 1e-9);
        net.CathodeResistor(cathode, StageNetlist.Ground, 39e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }
}
