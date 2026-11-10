using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Peavey 5150 (6505), lead and rhythm channels. Lead: V1A (220k, 1.82k ‖ 1 µF, 68k stopper), 470k ‖ 2.2 nF into
///     the lead pre gain (1M audio with 1M across it, 1M ‖ 1 nF from its top to the wiper), 470k into V1B (100k, 1.82k
///     ‖ 1 µF), a 470k/1M divider into the V2A cold clipper (39k unbypassed), 330k ‖ 1 nF and 220k into V2B (220k,
///     1.82k ‖ 1 µF), 1M and 22 nF into V5B (220k, 2.2k unbypassed, 100k grid leak), 1M and 22 nF into V5A (100k, 2.2k
///     unbypassed, 1M from its coupled output back to the grid), then 470k into a pad (33k, and 10 nF + 47k, to ground)
///     and the stack (47k slope, 470 pF, 22 nF, 22 nF; 250k linear treble, 1M audio bass, 50k linear middle), the post
///     gain, the post stage V3A (68k, 3.3k ‖ 4.7 nF), a long-tail pair, four 6L6GC idling cold, feedback with presence
///     and resonance. Rhythm (Switch 1): the rhythm pre gain's network with Crunch (Switch 2) and Bright into the same
///     V1B, which gets 22 µF more across its cathode, and 220k more to ground at V2A's grid.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Peavey "EVH 5150" factory schematic (preamp, tone stack and phase inverter, power amp pages, as
///         distributed by thetubestore), read by eye from the scan. The 6505 is electrically the same amp.
///     </para>
///     <para>
///         Read with doubt from the scan: the pre gain pot's value (printed "1M5A3", read as 1M audio), the middle pot
///         (read as 50k linear), a 100 pF beside the 470 pF treble cap (C60, whose wiring could not be read; left out).
///         The pages name three preamp rails: V3 (the inverter), V2 (V2B, V5, V3A) and V1 (V1A, V1B, V2A). Not on the
///         pages read: the supply page, so the rails' dropping resistors and caps (the generic chain's here, V3 to V2
///         to V1), the preamp rails' voltages (the inverter's taken as 430 V; V2 and V1 are the chain's own drops below
///         it at its assumed draws, 381 V and 363 V), B+ (490 V, docs/06 gives 485-500 V), the idle current (13 mA,
///         docs/06 gives 11-14 mA), the output transformer (1.9k assumed),
///         the resonance network (the depth control's generic shelf stands in for it), the feedback tap (8 Ω assumed).
///     </para>
///     <para>
///         The rhythm channel is on the same scan's "Initial Preamp Stage" and "Intermediate Preamp Stages" pages. From
///         V1A's 22 nF (C6): K4A picks 470k ‖ 270 pF (R19, C51) or, with Crunch, 2.2M ‖ 470 pF (R43, C13); then 1M
///         (R21) to the top of the rhythm pre gain (VR1, 1M audio, 180 pF from its top to its wiper), where K4B puts
///         39k + 0.1 µF (R23, C63) to ground unless Crunch is on; the Bright switch (S1C) closes 470 pF (C10) across
///         the 1M (open, the cap hangs across it through 2.2M, R75). K2A takes V1B's 470k from the rhythm wiper in
///         place of the lead one, and K1 grounds C26 (22 µF from V1B's cathode, otherwise standing on 47k) and R20
///         (220k from V2A's grid). The lead network stays on V1A's coupling cap as a load (470k ‖ 2.2 nF into the pot's
///         1M ‖ 1M).
///     </para>
///     <para>
///         Which channel closes which contacts is not printed on those pages. It is read from three things that agree:
///         every relay on the scan is drawn with its arm on pins 3 and 6, and for K2 those are the Clean Pre and Clean
///         Post wipers, so the drawn state is the rhythm channel with Crunch off, and in it K1 grounds C26 and R20;
///         Peavey's own sheet for the 5150 combo (drawing 99057001, 1995, attached to music-electronics-forum thread
///         909929) draws the relay that selects the pre gain and the one that grounds the 220k at V2A's grid with
///         their coils in series with the lead LED, so both are at rest on the rhythm channel; and a forum note
///         (music-electronics-forum thread 24157) that R20 limits the level in the clean and crunch modes and C26
///         gives them weight. So the lead channel has neither, as this model always assumed.
///     </para>
///     <para>
///         Read with doubt on the rhythm pages: R23 (39k) and C63 (0.1 µF), whose print is smeared, and the 180 pF
///         across the rhythm pot (C27; the combo has 39 pF there). The rhythm pre gain's taper is printed like the lead
///         one's.
///     </para>
///     <para>
///         Simplified: the send follower V3B and the loop are a unity buffer (V3A's grid leak is 1M, so the post
///         gain's wiper sees the follower's high input resistance); one Post Gain serves both channels (the amp has a
///         rhythm and a lead one side by side on the treble wiper, K2B picking the wiper); on the lead channel the
///         rhythm network's load on V1A (over 1.4M) and C26 on its 47k are left out; on the rhythm channel the lead
///         pot's 1M ‖ 1 nF to its open wiper is left out.
///     </para>
/// </remarks>
public static class Peavey5150 {
    /// <summary>
    ///     The V2 rail (V2B, V5, V3A): the chain's first node at rest, 430 V less 10k × 4.8 mA (its own 3 mA and V1's
    ///     1.8 mA). The rails were once taken as 340 V (V1) and 320 V (V2), which put the node two droppers down above
    ///     the one it hangs from.
    /// </summary>
    const double V2Rail = 381;

    /// <summary>The V1 rail (V1A, V1B, V2A): the chain's second node, V2 less 10k × 1.8 mA.</summary>
    const double V1Rail = 363;

    public static AmpDesign Design { get; } = new(
        "5150",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                V1Rail,
                220e3,
                1.82e3,
                1e-6,
                68e3,
                GridLeak: 1e6
            ) { SupplyNode = 2 },
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("V1B", PreGain()) { SupplyNode = 2 }, ColdClipper("V2A", 1e6)],
                [
                    new NetworkStageDesign("Rhythm V1B", RhythmPreGain()) { SupplyNode = 2 },
                    // K1B: R20 220k beside the 1M at the grid.
                    ColdClipper("Rhythm V2A", 1 / (1 / 1e6 + 1 / 220e3))
                ]
            ) { SupplyNode = 2 },
            new NetworkStageDesign("V2B", ThirdGain()) { SupplyNode = 1 },
            new TriodeStageDesign(
                "V5B",
                TriodeModel.Ecc83,
                V2Rail,
                220e3,
                2.2e3,
                InputResistance: 1e6,
                CouplingCap: 22e-9,
                GridLeak: 100e3
            ) { SupplyNode = 1 },
            new NetworkStageDesign("V5A", FeedbackStage()) { SupplyNode = 1 },
            new NetworkStageDesign("Stack", Stack()) { SupplyNode = 1 },
            new NetworkStageDesign("V3A", PostStage()) { SupplyNode = 1 }
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
            // The 10k tail stands on the 4.7k feedback shunt: 14.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, 430, 82e3, 100e3, 14.7e3, 470),
            47e-9,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 39e3, 4.7e3, new(10e3, PotTaper.Audio), 0.1e-6),
            true,
            new(1.9e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 47e-6, 10e3, 22e-6, 6e-3)
        ),
        -7.5
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Pre Gain",
            Master = "Post Gain",
            Depth = "Resonance",
            Cut = null,
            Switch1 = "Rhythm Ch",
            Switch2 = "Crunch"
        },
        // The drawing's rails: V3 (the inverter) is the preamp node, V2 feeds V2B, V5 and V3A, V1 feeds V1 and V2A.
        // The supply page is not among those read, so the droppers are the generic chain's (V3 to V2 to V1, 10k into
        // 22 µF each) and each node draws an equal share of the preamp's 6 mA per triode on it (ten in all): 0.6 mA.
        PreampSupply = new(
            [
                new(AmpChannel.GenericResistance, AmpChannel.GenericCap, 3e-3),
                new(AmpChannel.GenericResistance, AmpChannel.GenericCap, 1.8e-3, 1)
            ]
        )
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
        net.Supply(supply, V1Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.82e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The rhythm pre gain into V1B: 22 nF, with the lead network on it as a load (470k ‖ 2.2 nF into the lead
    ///     pot's 1M ‖ 1M); 470k ‖ 270 pF, or with Crunch 2.2M ‖ 470 pF; 1M, with the Bright switch's 470 pF across it
    ///     (open: across it through 2.2M); the 1M audio pot with 180 pF from its top to the wiper and, unless Crunch is
    ///     on, 39k + 0.1 µF from its top to ground; 470k to the grid; 100k plate, 1.82k ‖ 1 µF ‖ 22 µF.
    /// </summary>
    static StageNetlist RhythmPreGain() {
        var net = new StageNetlist();
        var (coupled, lead, feed, bright, top, shunt, grid, supply, plate, cathode) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node()
        );
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, lead, 470e3);
        net.Capacitor(coupled, lead, 2.2e-9);
        net.Resistor(lead, StageNetlist.Ground, 500e3);
        net.SwitchedResistor(coupled, feed, 470e3, 2.2e6, SwitchRole.Switch2);
        net.SwitchedCapacitor(coupled, feed, 470e-12, SwitchRole.Switch2, off: 270e-12);
        net.Resistor(feed, top, 1e6);
        net.Capacitor(feed, bright, 470e-12, CapacitorRole.Bright);
        net.Resistor(bright, top, 2.2e6);
        net.SwitchedResistor(bright, top, 0, 1, SwitchRole.Bright);
        net.Capacitor(top, shunt, 0.1e-6);
        net.SwitchedResistor(shunt, StageNetlist.Ground, 39e3, 1e12, SwitchRole.Switch2); // open with Crunch on
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 180e-12, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Supply(supply, V1Rail);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.82e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6); // C26, grounded by K1A
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The V2A cold clipper: 22 nF and 470k from V1B into <paramref name="gridLeak" /> at the grid (1M on the lead
    ///     channel); 100k plate, 39k unbypassed.
    /// </summary>
    static TriodeStageDesign ColdClipper(string name, double gridLeak) =>
        new(
            name,
            TriodeModel.Ecc83,
            V1Rail,
            100e3,
            39e3,
            CouplingCap: 22e-9,
            SeriesResistor: 470e3,
            GridLeak: gridLeak
        ) { SupplyNode = 2 };

    /// <summary>V2B: 22 nF into 330k ‖ 1 nF to ground, 220k to the grid; 220k plate, 1.82k ‖ 1 µF.</summary>
    static StageNetlist ThirdGain() {
        var net = new StageNetlist();
        var (coupled, grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Capacitor(coupled, StageNetlist.Ground, 1e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(supply, V2Rail);
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
        net.Supply(supply, V2Rail);
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
        net.Supply(supply, V2Rail);
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
