using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Rect-o-Verb (first series, 50 W), both channels (Switch 1 Rhythm). The lead channel: V1A (220k,
///     1.8k ‖ 1 µF), 20 nF and 680k ‖ 2 nF into
///     the 250k audio lead gain (1 nF from its top to the wiper), 470k with 20 pF to ground and 150 Ω into V1B (100k,
///     1.8k ‖ 1 µF and 15 µF through 22k), 47 nF with 1.5M to ground and 475k into the V2B cold clipper (1M grid leak,
///     39k unbypassed, 100k plate with 1 nF across it), 20 nF with 330k to ground and 220k into V3A (220k, 1.8k ‖ 1
///     µF), a DC-coupled cathode follower (100k) driving the stack (500 pF, 47k slope, 20 nF, 20 nF; 250k linear
///     treble, 1M audio bass, 25k linear middle), the presence shunt on the treble wiper (22k and 3 nF into a
///     rheostat), 220k into the 1M audio lead master. The rhythm channel: V1A's plate, with 1 nF to ground, into a
///     stack of its own (250 pF, 150k slope, 0.1 µF, 47 nF; 250k audio treble and bass, 10k audio middle) and the 1M
///     audio rhythm gain (120 pF from its top to the wiper, 10 pF from the wiper to ground), V1B with its 15 µF
///     straight to ground, 47 nF with 1.5M to ground and 3.3M ‖ 39 pF into V2A (330k and 47 pF from grid to ground,
///     100k ‖ 500 pF plate, 1.5k unbypassed), 47 nF and 330k into the 250k linear rhythm master; Pushed (Switch 2) puts
///     2 nF beside the 250 pF treble cap and 2 nF across the 3.3M. A long-tail pair (82k/100k, 470 Ω, 10k tail,
///     feedback 47k into 4.7k from the 8 Ω tap; across the 4.7k, 0.1 µF and 2.7k into the rhythm channel's 25k linear
///     presence pot, or into 5.6k on the lead channel) and two 6L6GC at 454 V with 1k screens.
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
///         Guesses and simplifications, marked: the presence rheostat's value is not printed (250k linear here); the
///         effects-loop stage V4 and the output level control are left out (the lead master feeds the inverter, here
///         the Master knob, as the rhythm master does on its channel); the reverb (two NE5532 stages around the spring
///         tank, sent from the lead treble wiper through 475k or from V1B's plate on the rhythm channel) is left out
///         with its send and its return (a 330k from each master's top to the 100k reverb pot's wiper, which at zero is
///         a 330k to ground there); the 120 pF across each inverter plate resistor and the 75 pF between the plates are
///         left out, and the 10M beside each of RY5's 2 nF caps (it keeps them charged). Not on the drawing: the idle
///         current (40 mA), the primary (4k, a typical value for two 6L6) and the Series II changes. The Solo 50's
///         Modern / Vintage switch (the feedback's LDR) is not on the panel here: the feedback stays in.
///     </para>
///     <para>
///         One set of knobs plays the channel that is on, as on the other two-channel models: Gain, Bass, Middle,
///         Treble and Master are that channel's pots, and Presence (Control 2) is the lead channel's preamp shunt or
///         the rhythm channel's pot in the feedback (RY4a: on the lead channel 5.6k stands in its place). Pushed
///         (RY5) acts on the rhythm channel only. Where the grid line crosses RY5a's arm on preamp sheet 2 no dot is
///         printed; it is read as joined (the 47 pF from grid to ground, the 2 nF across the 3.3M), as the same
///         circuit is drawn on the three-channel Dual Rectifier's channel 1.
///     </para>
/// </remarks>
public static class RectoVerb {
    public static AmpDesign Design { get; } = new(
        "Rect-o-Verb",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("V1A", InputAndGain()) { SupplyNode = 2 },
                    new NetworkStageDesign("V1B", SecondStage(22e3)) { SupplyNode = 1 },
                    new NetworkStageDesign("V2B", ColdClipper()) { SupplyNode = 1 },
                    new NetworkStageDesign("V3A", FourthStage()),
                    new NetworkStageDesign("V3B", FollowerAndStack())
                ],
                [
                    new NetworkStageDesign("Rhythm V1A", RhythmInputAndStack()) { SupplyNode = 2 },
                    new NetworkStageDesign("Rhythm V1B", SecondStage(0)) { SupplyNode = 1 },
                    new NetworkStageDesign("Rhythm V2A", RhythmOutput()) { SupplyNode = 1 }
                ]
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
            new(1, 47e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6) { PresenceSeries = 2.7e3 },
            true,
            new(4e3),
            new(Inductance: 1.4e-3, EddyResistance: 22, Resonance: 95, PeakImpedance: 30, Q: 2.2),
            // The choke to B's 30 µF, 5.6k to C's 10 µF: 451 V to 415 V is 6.4 mA.
            new(30, 110e-6, 100, 30e-6, 5.6e3, 10e-6, 6.4e-3, RectifierModel.Silicon),
            PresencePot: PotRole.Control2,
            // RY4a: off the rhythm channel, 5.6k in the pot's place.
            PresenceOut: new(SwitchRole.Switch1, false, 1 - 5.6e3 / 25e3)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control2 = "Presence",
            Switch1 = "Rhythm",
            Switch2 = "Pushed"
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

    /// <summary>
    ///     V1B: 150 Ω grid stopper; 100k plate; 1.8k ‖ 1 µF and 15 µF through <paramref name="partial" /> (22k on the
    ///     lead channel; 0 on the rhythm channel, where RY2a grounds the cap).
    /// </summary>
    static StageNetlist SecondStage(double partial) {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 150);
        net.Supply(supply, 376);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        if (partial > 0) {
            var foot = net.Node();
            net.Capacitor(cathode, foot, 15e-6);
            net.Resistor(foot, StageNetlist.Ground, partial);
        } else {
            net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        }

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

    /// <summary>V3A: 20 nF into 330k to ground, 220k to the grid; 220k plate; 1.8k ‖ 1 µF.</summary>
    static StageNetlist FourthStage() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, 415);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cathode follower (100k), DC-coupled to V3A's plate, driving the stack, the presence shunt on the treble
    ///     wiper (22k, 3 nF and a 250k linear rheostat) and 220k into the 1M audio lead master.
    /// </summary>
    static StageNetlist FollowerAndStack() {
        var net = new StageNetlist();
        var (supply, follower, slope, trebleTop, trebleBottom, middle, shunt, presence, top) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Supply(supply, 415);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Triode(supply, net.Input, follower, TriodeModel.Ecc83);
        net.Capacitor(follower, trebleTop, 500e-12, CapacitorRole.Stack);
        net.Resistor(follower, slope, 47e3);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 20e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 20e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, shunt, 22e3);
        net.Capacitor(shunt, presence, 3e-9);
        net.Rheostat(presence, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        net.Resistor(treble, top, 220e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }

    /// <summary>
    ///     The rhythm channel's V1A, stack and gain: 1M grid leak; 220k plate; 1.8k ‖ 1 µF; on the plate 1 nF to
    ///     ground and the stack (250 pF, and 2 nF beside it when Pushed, to the 250k audio treble; 150k slope; 0.1 µF
    ///     to the 250k audio bass; 47 nF to the 10k audio middle); the treble wiper into the 1M audio gain with 120 pF
    ///     from its top to the wiper and 10 pF from the wiper to ground.
    /// </summary>
    static StageNetlist RhythmInputAndStack() {
        var net = new StageNetlist();
        var (supply, plate, cathode, slope, trebleTop, trebleFoot, middleTop) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(supply, 394);
        net.Resistor(supply, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, StageNetlist.Ground, 1e-9);
        net.Capacitor(plate, trebleTop, 250e-12, CapacitorRole.Stack);
        net.SwitchedCapacitor(plate, trebleTop, 2e-9, SwitchRole.Switch2, CapacitorRole.Stack);
        net.Resistor(plate, slope, 150e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Capacitor(slope, trebleFoot, 0.1e-6, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middleTop, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middleTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middleTop, StageNetlist.Ground, new(10e3, PotTaper.Audio), PotRole.Middle);
        var gain = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, gain, 120e-12, CapacitorRole.Bright);
        net.Capacitor(gain, StageNetlist.Ground, 10e-12);
        net.Output = gain;
        return net;
    }

    /// <summary>
    ///     The rhythm channel's V2A and master: 47 nF with 1.5M to ground, 3.3M ‖ 39 pF (and 2 nF across them when
    ///     Pushed) into the grid with 330k and 47 pF to ground; 100k ‖ 500 pF plate; 1.5k unbypassed; 47 nF and 330k
    ///     into the 250k linear master.
    /// </summary>
    static StageNetlist RhythmOutput() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode, blocked, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1.5e6);
        net.Resistor(coupled, grid, 3.3e6);
        net.Capacitor(coupled, grid, 39e-12);
        net.SwitchedCapacitor(coupled, grid, 2e-9, SwitchRole.Switch2);
        net.Resistor(grid, StageNetlist.Ground, 330e3);
        net.Capacitor(grid, StageNetlist.Ground, 47e-12);
        net.Supply(supply, 376);
        net.Resistor(supply, plate, 100e3);
        net.Capacitor(supply, plate, 500e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, blocked, 47e-9);
        net.Resistor(blocked, top, 330e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
