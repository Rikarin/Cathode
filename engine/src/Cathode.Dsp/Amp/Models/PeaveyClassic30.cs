using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Peavey Classic 30 (30 W, 1 × 12, three 12AX7 and four EL84), from Peavey's drawing 81501580 "Classic 30"
///     (assembly 98871950, 24 March 1994). V1A (47k and 39 pF at the jack, 0.1 µF into a 470k grid leak; 150k, 1.5k ‖
///     22 µF) and 0.047 µF feed both channels: the Normal channel's 680k ‖ 0.001 µF into the 250k Volume, and the Lead
///     channel's 470 pF into the 1M Pre pot, whose wiper (470 pF and 2.2M to ground) is V2A's grid. V2A (150k, 1.5k ‖
///     0.47 µF), 0.001 µF, 470k ‖ 470 pF into V2B's 470k grid leak; V2B (100k, 1k ‖ 22 µF), 1M and 0.022 µF into the
///     10k Post pot with 10k + 0.015 µF beside it. A relay takes the Volume's or the Post's wiper through 150k to V1B's
///     grid (220k to ground through the reverb pot; 150k, 1.5k ‖ 22 µF), which has 2.2M of feedback from behind its
///     0.047 µF plate cap, 0.0015 µF from there to ground, and the tone stack: 270 pF, a 68k slope, 0.022 µF to the
///     bass pot and 0.022 µF to the middle pot's wiper (treble 250k linear, bass 1M audio, middle 20k). The treble
///     wiper goes through 0.1 µF and 1M to V3A (100k, 1.5k unbypassed, where the feedback returns), 0.1 µF into V3B, a
///     cathodyne (68k plate, 1.5k and 68k cathode, a 1M grid leak to their junction), 0.022 µF and 220k to the −14 V
///     bias, 47k stoppers, four EL84 at 332 V. The feedback is from the 8 Ω tap: 220k ‖ 0.0047 µF and 100k into a node
///     that 22 µF ties to V3A's cathode, with 0.2 µF (two 0.1 µF) and 10k from it to ground.
/// </summary>
/// <remarks>
///     <para>
///         Panel: Pre (the Lead channel's gain, on the Gain knob) and Post (its volume, on Master), Volume (the Normal
///         channel's, Control 1), Bass, Middle, Treble; <b>Normal Ch</b> selects the Normal channel (the Lead channel
///         is the default, so that Gain and Master act on it; on the amp the relay rests on the Normal channel);
///         <b>Boost</b> is the amp's Boost switch, which puts 0.047 µF across the stack's 270 pF, so the treble pot
///         passes the whole band (a mid and bass lift). The amp has no presence control and no bright switch.
///     </para>
///     <para>
///         Source: schematicheaven.net/newamps/peavey_classic30.pdf, the factory sheet, read at 2 ×: every value above,
///         the supply (332 V at the plates on two 47 µF, 400 Ω to 328 V at the screens on 22 µF, 10k to 275 V "B++"
///         for V2B, V1B and V3, 10k to 260 V "B+" for V1A and V2A, 22 µF each), the plate voltages (V1A 160 V, V2A 150
///         V, V2B 175 V, V1B 165 V, V3A 195 V, V3B 225 V), the −14 V bias (47k and 33k from −36 V), the 100 Ω 5 W
///         screen resistors, the relay's contacts and the pots' values. The pots' tapers are from Peavey's codes on the
///         sheet where they are plain (Volume "250KANS", Pre and Bass "1MANSP5" and Post "10KA": audio; Treble "250KL":
///         linear); the Middle's "20KSMS" is taken as linear.
///     </para>
///     <para>
///         The feedback loop includes V3A, which is the power amp's loop stage (<see cref="PowerAmpDesign.Loop" />):
///         its 1.5k is the feedback's shunt, the 10k + 0.2 µF a branch across it (<see cref="FeedbackDesign.Branch" />)
///         and the 0.0047 µF across the 220k a low shelf (<see cref="FeedbackDesign.LowFeedback" />: 100k above about
///         490 Hz, 320k below about 150 Hz). V3A's cathode sees 1.5k ‖ 10k ‖ 100k for the signal (1287 Ω) and 1.5k at
///         DC, so the loop stage holds the difference as a bypassed part. The 22 µF that couples the node to the
///         cathode is a short.
///     </para>
///     <para>
///         Simplified: the reverb (at 0: its pot's wiper grounds V1B's 220k mixing resistor, which is kept, and the
///         drive's 220k, 0.001 µF and 1M load on the channel relay is kept too); the effects loop's emitter follower
///         (it only drives the send jack: the return jack's switch takes the treble wiper straight to V3A's 0.1 µF,
///         and the follower's 0.1 µF and 1M bias load is kept); the two 9.1 V zeners on the reverb drive; the relay's
///         second contact, which grounds V2A's output on the Normal channel; every EL84 has a 100 Ω screen resistor
///         (the sheet draws one each for V4 and V7 and ties V5's and V6's screens to the node); the cathodyne's grid
///         leak follows its grid at 0.95, so the signal sees 20M.
///     </para>
///     <para>
///         Unverified (not on the sheet): the output transformer's primary, 4k plate to plate (twice the EL84 data
///         sheet's 8k class AB pair), the EL84s' idle current (24 mA each: what −14 V gives on this engine's EL84 at
///         332 V and 328 V screens), the supply's source resistance. The Classic 50 (a long-tail pair, presence and
///         master) and the Delta Blues (this circuit with a tremolo and a 15" speaker) are not built.
///     </para>
/// </remarks>
public static class PeaveyClassic30 {
    /// <summary>A switched-out element, and a closed relay contact.</summary>
    const double Open = 1e9;

    const double Wire = 1;

    /// <summary>"B+": V1A and V2A.</summary>
    const double FirstRail = 260;

    /// <summary>"B++": V2B, V1B, V3A and the cathodyne.</summary>
    const double SecondRail = 275;

    /// <summary>V3A's cathode resistor, the feedback's shunt.</summary>
    const double Shunt = 1.5e3;

    /// <summary>The feedback's series resistor above the 220k ‖ 0.0047 µF.</summary>
    const double FeedbackResistor = 100e3;

    /// <summary>The 10k + 0.2 µF from the feedback node to ground.</summary>
    static readonly ShuntBranch FeedbackBranch = new(10e3, 0.2e-6);

    /// <summary>
    ///     What V3A's cathode sees for the signal: the shunt, the branch's 10k and the feedback resistor in parallel.
    /// </summary>
    static double CathodeLoad => 1 / (1 / Shunt + 1 / FeedbackBranch.Resistor + 1 / FeedbackResistor);

    public static AmpDesign Design { get; } = new(
        "Classic 30",
        [
            InputStage(),
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [LeadFirstStage(), LeadSecondStage(), StackStage()],
                [StackStage()]
            )
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            2,
            332,
            100,
            0.024,
            false,
            0,
            30,
            new CathodyneDesign(TriodeModel.Ecc83, SecondRail, 68e3, 68e3, 1.5e3),
            22e-9,
            220e3,
            47e3,
            0,
            new(1e6, PotTaper.Linear),
            // The 0.0047 µF across the 220k: all 320k below 154 Hz, the 100k alone above 493 Hz.
            new(1, FeedbackResistor, Shunt, new(1e3, PotTaper.Linear), 1e-12, 493, 100.0 / 320) {
                Branch = FeedbackBranch
            },
            true,
            new(4e3),
            SpeakerDesign.OpenBack1x12,
            // The 10k to B++ carries (328 - 275) / 10k = 5.3 mA.
            new(40, 94e-6, 400, 22e-6, 10e3, 22e-6, 5.3e-3, RectifierModel.Silicon),
            Loop: new(TriodeModel.Ecc83, SecondRail, 100e3, Shunt - CathodeLoad, CathodeLoad, InverterCoupling(), 40)
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Pre",
            Master = "Post",
            Control1 = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Normal Ch",
            Switch2 = "Boost"
        },
        // B+ hangs from B++ through 10k and 22 µF: (275 - 260) / 10k = 1.5 mA for V1A and V2A.
        PreampSupply = new([new(10e3, 22e-6, 1.5e-3)])
    };

    /// <summary>
    ///     A common-cathode 12AX7 on <paramref name="rail" /> volts: <paramref name="plateResistor" />,
    ///     <paramref name="cathodeResistor" /> with <paramref name="bypass" /> across it. Returns the plate.
    /// </summary>
    static int Triode(
        StageNetlist net,
        int grid,
        double rail,
        double plateResistor,
        double cathodeResistor,
        double bypass
    ) {
        var (plate, cathode, supply) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, rail);
        net.Resistor(supply, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, bypass);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>
    ///     V1A behind the jack's 47k (39 pF to ground, 0.1 µF, 470k), with both channels' networks on its 0.047 µF:
    ///     680k ‖ 0.001 µF into the Volume, and 470 pF into the Pre pot, whose wiper carries V2A's grid (470 pF and
    ///     2.2M) on either channel. The relay (<see cref="SwitchRole.Switch1" />: on is the Normal channel) takes one
    ///     wiper to the output.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Capacitor(net.Input, StageNetlist.Ground, 39e-12);
        net.Capacitor(net.Input, grid, 0.1e-6);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        var plate = Triode(net, grid, FirstRail, 150e3, 1.5e3, 22e-6);

        var (split, normalTop, leadTop) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, split, 47e-9);
        net.Resistor(split, normalTop, 680e3);
        net.Capacitor(split, normalTop, 1e-9, CapacitorRole.Bright);
        var volume = net.Pot(normalTop, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Control1);

        net.Capacitor(split, leadTop, 470e-12);
        var pre = net.Pot(leadTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(pre, StageNetlist.Ground, 2.2e6);
        net.Capacitor(pre, StageNetlist.Ground, 470e-12);

        var output = net.Node();
        net.SwitchedResistor(volume, output, Open, Wire, SwitchRole.Switch1);
        net.SwitchedResistor(pre, output, Wire, Open, SwitchRole.Switch1);
        net.Output = output;
        return new("V1A", net, 47e3) { SupplyNode = 1 };
    }

    /// <summary>
    ///     The Lead channel's V2A (150k, 1.5k ‖ 0.47 µF): 0.001 µF, then 470k ‖ 470 pF into V2B's 470k grid leak, the
    ///     output.
    /// </summary>
    static NetworkStageDesign LeadFirstStage() {
        var net = new StageNetlist();
        var plate = Triode(net, net.Input, FirstRail, 150e3, 1.5e3, 0.47e-6);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (coupled, output) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 1e-9);
        net.Resistor(coupled, output, 470e3);
        net.Capacitor(coupled, output, 470e-12);
        net.Resistor(output, StageNetlist.Ground, 470e3);
        net.Output = output;
        return new("V2A", net) { SupplyNode = 1 };
    }

    /// <summary>
    ///     The Lead channel's V2B (100k, 1k ‖ 22 µF): 1M and 0.022 µF into the 10k Post pot, with 10k + 0.015 µF
    ///     beside it; the output is the wiper.
    /// </summary>
    static NetworkStageDesign LeadSecondStage() {
        var net = new StageNetlist();
        var plate = Triode(net, net.Input, SecondRail, 100e3, 1e3, 22e-6);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (series, top, shelf) = (net.Node(), net.Node(), net.Node());
        net.Resistor(plate, series, 1e6);
        net.Capacitor(series, top, 22e-9);
        net.Resistor(top, shelf, 10e3);
        net.Capacitor(shelf, StageNetlist.Ground, 15e-9);
        net.Output = net.Pot(top, StageNetlist.Ground, new(10e3, PotTaper.Audio), PotRole.Master);
        return new("V2B", net) { SupplyNode = 0 };
    }

    /// <summary>
    ///     From the relay: the reverb drive's 220k, 0.001 µF and 1M, and 150k into V1B's grid (220k to ground; 150k,
    ///     1.5k ‖ 22 µF). Behind the plate's 0.047 µF: 2.2M back to the grid, 0.0015 µF to ground, the Boost's 0.047 µF
    ///     (220k to ground; <see cref="SwitchRole.Switch2" /> ties it to the treble pot's top) and the stack. The
    ///     treble wiper has the loop follower's 0.1 µF and 1M, and 0.1 µF into V3A's 1M grid leak, the output.
    /// </summary>
    static NetworkStageDesign StackStage() {
        var net = new StageNetlist();
        var (grid, drive, driveInput) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, drive, 220e3);
        net.Capacitor(drive, driveInput, 1e-9);
        net.Resistor(driveInput, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 150e3);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        var plate = Triode(net, grid, SecondRail, 150e3, 1.5e3, 22e-6);
        net.Capacitor(grid, plate, Ab763.GridPlate);

        var (feed, boost) = (net.Node(), net.Node());
        net.Capacitor(plate, feed, 47e-9);
        net.Resistor(feed, grid, 2.2e6);
        net.Capacitor(feed, StageNetlist.Ground, 1.5e-9);
        net.Capacitor(feed, boost, 47e-9);
        net.Resistor(boost, StageNetlist.Ground, 220e3);

        var (treble, top) = FenderParts.MidWiperStack(
            net,
            feed,
            270e-12,
            68e3,
            22e-9,
            22e-9,
            new(250e3, PotTaper.Linear),
            new(1e6, PotTaper.Audio),
            new(20e3, PotTaper.Linear)
        );
        net.SwitchedResistor(boost, top, 0, Wire, SwitchRole.Switch2);

        var (follower, output) = (net.Node(), net.Node());
        net.Capacitor(treble, follower, 0.1e-6);
        net.Resistor(follower, StageNetlist.Ground, 1e6);
        net.Capacitor(treble, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 1e6);
        net.Output = output;
        return new("V1B", net) { SupplyNode = 0 };
    }

    /// <summary>
    ///     From V3A's plate (inside the feedback loop) to the cathodyne's grid: 0.1 µF and the 1M grid leak, which
    ///     returns to the cathodyne's bias junction (20M for the signal: see the type's remarks).
    /// </summary>
    static StageNetlist InverterCoupling() {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Capacitor(net.Input, grid, 0.1e-6);
        net.Resistor(grid, StageNetlist.Ground, 20e6);
        net.Output = grid;
        return net;
    }
}
