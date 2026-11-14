using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Soldano Hot Rod 50 and 100 (the single-channel SLO), the Normal and Overdrive preamps. The jack (1M, 68k)
///     into V1A (220k, 1k8 ‖ 1 µF), 0.02 µF to both gain networks: Overdrive, 0.001 µF and 470 pF around the 1M
///     audio Overdrive Preamp pot, its wiper through 470k; Normal, the SSB switch's 1M in series with 250 pF to
///     ground onto the 1M audio Normal Preamp pot. Then V2A (100k, 1k8 ‖ 1 µF), 0.02 µF and 470k into the V2B cold
///     clipper (100k with 0.001 µF across it, 39k unbypassed, 1M leak), 0.02 µF through the channel optocoupler
///     onto 330k and 220k into V3A (220k, 1k8 ‖ 1 µF), the V3B follower whose 100k + 2k2 carries the effects send,
///     the return stage V4A (100k, 2k2) DC-coupled to the V4B follower, which drives the Soldano stack (470 pF,
///     47k slope, 0.02 µF, 0.02 µF; 250k treble, 1M audio bass, 25k middle) and the 1M audio Overdrive Master;
///     then the 12AX7 long-tail pair (82k/100k, 470 Ω, 10k tail on the 4k7 feedback shunt) and two (50 W) or four
///     (100 W) 6L6GC on 470 Ω screens, with 39k of feedback and the 25k Presence with 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         Sources: the three Soldano sheets on schematicheaven.net (<c>soldano_50-100_hotrod.pdf</c>): "Soldano
///         Hot Rod 50/100 Plus preamp &amp; driver circuit" (the preamp, every value above), "Soldano
///         Reverb-O-Sonic combo &amp; Hot Rod 50, 50+ tone controls &amp; power amp" of 5 July 1996 (the stack,
///         the long-tail pair, the output stage, the feedback and presence network, "V1-5 12AX7, V6&amp;7 5881 or
///         6L6GC") and "Soldano Hot Rod 100+ power supply &amp; switching circuits" of the same date (the bridge,
///         the 220 µF pairs, the standby, the choke, the 10k 2 W to the preamp node, the bias adjust and the four
///         optocouplers the footswitch drives).
///     </para>
///     <para>
///         Inferred: what the SSB switch does. It sits in the 0.02 µF from V1A's plate and picks either the
///         Overdrive network straight or a 1M in series with 250 pF to ground ahead of the Normal pot, so it is the
///         amp's channel switch and the Normal channel is the darker, quieter path; Switch 1 selects it here. Both
///         pots' wipers meet at the 470k into V2A, so the idle channel's pot loads the playing one; that load is
///         kept.
///     </para>
///     <para>
///         Guessed, as none of the three sheets prints a voltage: the rails. B+ is 490 V, the screens sit just
///         under it, and the 10k 2 W drops to 380 V at the inverter and the last two stages, with the preamp
///         sheet's two 15k droppers below that (360 V for V2, 350 V for V1 and the cold clipper), the voltages
///         Soldano's 1996 SLO-100 drawing prints for the same supply (<see cref="Slo100" />). Also guessed: the
///         idle current (40 mA), the output transformers (2.2k plate to plate for the 100 W as on the SLO, 4.4k
///         for the 50 W) and the pot tapers the sheets do not mark ("log" is marked on the gain pots, the bass and
///         the master). Simplified: the channel optocoupler on V2B's output is a closed contact and the one across
///         the cold clipper's cathode is dark; the effects loop's jacks are joined; the Hot Rod 50+ and 100+'s
///         Clean and Crunch volumes with their own two optocouplers (the power-amp sheet notes they are "omitted on
///         Hot Rod 50") are left out, so the Overdrive Master is the amp's one master.
///     </para>
///     <para>
///         Panel: Gain is the playing channel's preamp pot, Master the Overdrive Master, and <b>Normal Ch</b> the
///         SSB switch (the Overdrive channel is the default, as on <see cref="Slo100" />). The amp has no bright
///         switch, depth or cut.
///     </para>
/// </remarks>
public static class SoldanoHotRod {
    /// <summary>The preamp sheet's lowest node, V1 and the cold clipper's.</summary>
    const double RailOne = 350;

    /// <summary>The node above it, V2's.</summary>
    const double RailTwo = 360;

    /// <summary>The 10k 2 W node: V3, V4 and the inverter.</summary>
    const double RailThree = 380;

    public static ToneStackDesign Stack { get; } = new(
        "Hot Rod",
        470e-12,
        47e3,
        0.02e-6,
        0.02e-6,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Audio),
        new(25e3, PotTaper.Linear)
    );

    /// <summary>The Hot Rod 100: four 6L6GC.</summary>
    public static AmpDesign Design { get; } = Build(
        "Hot Rod 100",
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            490,
            470,
            0.04,
            false,
            0,
            100,
            // The 10k tail stands on the 4k7 feedback shunt: 14.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, RailThree, 82e3, 100e3, 14.7e3, 470),
            0.02e-6,
            220e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 39e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6),
            true,
            new(2.2e3),
            SpeakerDesign.Closed4x12,
            // The screens through the sheet's 10k 2 W to the 380 V node: 11 mA, as the SLO's drawing gives.
            new(30, 110e-6, 100, 110e-6, 10e3, 47e-6, 11e-3),
            InverterInput: null
        ),
        -7.5
    );

    /// <summary>The Hot Rod 50: two 6L6GC.</summary>
    public static AmpDesign Head50 { get; } = Build(
        "Hot Rod 50",
        Design.Power with {
            Name = "2 x 6L6GC",
            TubesPerSide = 1,
            RatedPower = 50,
            Transformer = new(4.4e3),
            PowerSupply = new(40, 110e-6, 100, 110e-6, 10e3, 47e-6, 8e-3)
        },
        -6.5
    );

    static AmpDesign Build(string name, PowerAmpDesign power, double trim) =>
        new(
            name,
            [
                new TriodeStageDesign(
                    "V1A",
                    TriodeModel.Ecc83,
                    RailOne,
                    220e3,
                    1.8e3,
                    1e-6,
                    68e3,
                    GridLeak: 1e6
                ) { SupplyNode = 2 },
                new ChannelSwitchStageDesign(
                    "Channel",
                    SwitchRole.Switch1,
                    [new NetworkStageDesign("V2A", Gain(false)) { SupplyNode = 1 }],
                    [new NetworkStageDesign("V2A Normal", Gain(true)) { SupplyNode = 1 }]
                ),
                new TriodeStageDesign(
                    "V2B",
                    TriodeModel.Ecc83,
                    RailOne,
                    100e3,
                    39e3,
                    CouplingCap: 0.02e-6,
                    SeriesResistor: 470e3,
                    GridLeak: 1e6,
                    PlateBypass: 1e-9
                ) { SupplyNode = 2 },
                new NetworkStageDesign("V3A", ThirdStage()),
                new NetworkStageDesign("V3B", SendFollower()),
                new TriodeStageDesign(
                    "V4A",
                    TriodeModel.Ecc83,
                    RailThree,
                    100e3,
                    2.2e3,
                    CouplingCap: 1e-6,
                    GridLeak: 1e6
                ),
                new ToneStackStageDesign(
                    "V4B",
                    TriodeModel.Ecc83,
                    StackDriver.CathodeFollower,
                    RailThree,
                    100e3,
                    Stack,
                    new(1e6, PotTaper.Audio),
                    PotRole.Master
                )
            ],
            power,
            trim
        ) {
            Panel = AmpPanel.Standard with { Bright = null, Depth = null, Cut = null, Switch1 = "Normal Ch" },
            // The preamp sheet's two 15k droppers below the 10k 2 W node, 22 µF each: node 1 for V2 and node 2
            // for V1 and the cold clipper, each drawing what the SLO's drawn voltages drop across them.
            PreampSupply = new(
                [
                    new(15e3, 22e-6, (RailThree - RailTwo) / 15e3 - (RailTwo - RailOne) / 15e3),
                    new(15e3, 22e-6, (RailTwo - RailOne) / 15e3, 1)
                ]
            )
        };

    /// <summary>
    ///     V1A's plate through 0.02 µF into both gain networks and V2A (100k, 1k8 ‖ 1 µF). Overdrive: 0.001 µF
    ///     across the feed and 470 pF from the pot's top to its wiper. Normal (<paramref name="normal" />): 1M in
    ///     series with 250 pF to ground. Either wiper goes through 470k to the grid; the idle channel's pot stays
    ///     on the line. Out at the plate.
    /// </summary>
    static StageNetlist Gain(bool normal) {
        var net = new StageNetlist();
        var (coupled, overdrive, normalTop, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.02e-6);

        // The overdrive network: 0.001 µF from the line to the pot's top, which also carries the 1M leak.
        net.Capacitor(coupled, overdrive, 0.001e-6);
        net.Resistor(overdrive, StageNetlist.Ground, 1e6);
        var drive = net.Pot(overdrive, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(overdrive, drive, 470e-12, CapacitorRole.Bright);

        // The normal network: 1M in series, 250 pF to ground, onto its own pot.
        net.Resistor(coupled, normalTop, 1e6);
        net.Capacitor(normalTop, StageNetlist.Ground, 250e-12);
        var volume = net.Pot(normalTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);

        net.Resistor(normal ? volume : drive, grid, 470e3);
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailTwo);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The cold clipper's plate through 0.02 µF and the channel optocoupler (closed) onto 330k to ground, then
    ///     220k into V3A (220k, 1k8 ‖ 1 µF). Out at its plate.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.02e-6);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Resistor(coupled, grid, 220e3);
        net.Supply(rail, RailThree);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The V3B send follower, DC-coupled to V3A's plate: 100k and 2k2 to ground, the send taken across the 2k2
    ///     (its jack joined to the return). Out at the follower's cathode, which the return stage's 1 µF takes.
    /// </summary>
    static StageNetlist SendFollower() {
        var net = new StageNetlist();
        var (rail, follower, send) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, RailThree);
        net.Resistor(follower, send, 100e3);
        net.Resistor(send, StageNetlist.Ground, 2.2e3);
        net.Triode(rail, net.Input, follower, TriodeModel.Ecc83);
        net.Output = send;
        return net;
    }
}
