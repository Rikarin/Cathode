using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Peavey 5150 II (sold from 2004 as the 6505+), 120 W: the lead channel (<see cref="Design" />) and the rhythm
///     channel with its Crunch and Bright switches (<see cref="Rhythm" />). Both start at V1A (R2 210k, R12 1.82k ‖ C3
///     1 µF; R27 10k and R22 68k in series with the grid) and its coupling cap C6 22 nF with R176 1M to ground.
///     <para>
///         Lead: C17 470 pF onto the Lead Pre (VR6 1M audio, R41 1M across the track, R40 1M ‖ C19 1 nF from its top
///         to the wiper), R82 470k into V1B (R3 100k, R17 1.82k ‖ C4 1 µF); C2 1 nF, R6 470k over R9 1M into the V2A
///         cold clipper (R1 100k, R15 39k unbypassed); C1 22 nF, R11 330k ‖ C48 1 nF, R7 220k into V2B (R4 220k, R13
///         1.82k ‖ C5 1 µF); C57 22 nF, R32 1M over R101 100k into V5B (R96 220k, R102 2.2k unbypassed); R87 1M and
///         C56 22 nF into V5A (R86 100k, R93 2.2k unbypassed, R91 1M grid leak, R88 1M from the coupled output back
///         to the grid); R89 470k into a pad (R24 47k, and C59 10 nF + R94 33k, to ground) and the stack (R28 47k
///         slope, C14 470 pF, C18 and C21 22 nF; High 250k linear, Low 1M audio, Mid 50k linear with R95 47k across
///         it and the mid cap on its wiper) into the Lead Post (VR10 1M audio, R162 1M across it).
///     </para>
///     <para>
///         Rhythm: through R19 470k ‖ C51 470 pF onto the Clean Pre (VR1 1M audio, C27 6.8 nF from its top to the
///         wiper) with R174 22k and R23 6.8k + C63 68 nF to ground at its top, or, with Crunch in, through C13 2.2 nF
///         alone; R152 470k ‖ C151 470 pF over R154 470k into V6A (R150 220k, R158 2.2k ‖ C157 0.33 µF); C165 22 nF,
///         R164 470k over R172 220k into V6B (R170 220k, R155 2.2k ‖ C166 2.2 µF); R171 100k, C153 0.33 µF, then R75
///         22k + C10 3.3 nF to ground (through R159 1M with Bright in) and R153 18k onto R156 10k to ground (Crunch
///         adds R163 33k and R157 82k + C155 33 nF beside it) and the stack (R151 33k slope, C150 100 pF ‖ C152 470
///         pF, C154 and C156 22 nF; High 250k linear, Low 1M audio, Mid 25k audio with the cap on its wiper) into the
///         Clean Post (VR5 1M audio).
///     </para>
///     <para>
///         Both: the send follower V3B (R81 2.2M and C52 22 nF at its grid, R79 1M bootstrapped from the junction of
///         R80 1k and R16 100k), the loop, the post stage V3A (C11 47 nF, R26 1M; R5 68k, R14 3.3k ‖ C158 4.7 nF), C49
///         22 nF and R48 100k into a 12AX7 long-tail pair (R46 82k and R58 100k, R52 470 Ω, R51 10k standing on R57
///         4.7k), C28 and C29 47 nF, R54 and R55 220k, 2.2k stoppers, four 6L6GC with 100 Ω screen resistors; feedback
///         through R56 39k and the Resonance (1M audio rheostat ‖ 6.8 nF) onto the 4.7k, with the Presence (10k audio,
///         its track in series with 0.1 µF across the 4.7k and 33 nF from its wiper to ground).
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         Source: Peavey's factory set "5150II.pdf" as el34world distributes it
///         (el34world.com/charts/Schematics/files/Peavey/5150_2.pdf): the preamp drawing "VH 5150II PREAMP", P9700041,
///         16 February 2001 (the whole amp but the output stage: both channels, the relays and their logic, the
///         inverter, the feedback network, the supply), its bill of materials (board 5150_2, 25 January 2001), and the
///         power board "VHALEN 120 POWER BOARD", P9000115, 1 June 2001. A clean vector-quality scan: every value above
///         is read from the drawing and checked against the bill of materials (which settles R2 as 210k 1 %, C15 as
///         0.1 µF and the pot tapers: "AUD" for the pre gains, Low, post gains, presence and resonance, "LIN" for High
///         and the lead Mid).
///     </para>
///     <para>
///         Relays, drawn at rest: K1 and K2 pull in together for the lead channel (their coils are in series with the
///         Lead LED), K4 with the Crunch switch on either channel. On the lead channel K2A grounds the foot of R175
///         68k + C167 820 pF, which hangs from V1A's coupling cap through K4A at rest: a treble shunt on V1A that the
///         lead model has, with the rhythm channel's network beside it (R19 ‖ C51 into R174 and R23 + C63; the Clean
///         Pre's own 1M track across the 22k is left out). Crunch pressed while on the lead channel would lift that
///         shunt; the lead model keeps Crunch out. On the rhythm channel K2A grounds the Lead Pre's wiper instead, so
///         C17 sees R41, R40, C19 and the track above the wiper to ground: taken with the Lead Pre at noon (321k ‖ 1
///         nF). K1 puts each channel's own presence and resonance pots in the feedback; the two networks have the same
///         values, so one power amp serves both.
///     </para>
///     <para>
///         Not on the drawing, so assumed: it prints no voltages. B+ is taken as 490 V and the idle current as 13 mA a
///         tube, as the 5150 model's (docs/06); the output transformer (70500207) as 1.9k plate to plate; the feedback
///         from its white lead as the 8 Ω winding (the power board labels that lead's jack 8 Ω; the preamp page adds a
///         16/8/4 Ω selector whose wiring to the transformer is not drawn). The supply's own resistance is a guess. The
///         preamp rails follow from the drawing's chain with the model's tubes: the screen node behind R210 400 Ω (two
///         100 µF in series), R59 4k to V3 (the inverter, 47 µF), R60 ‖ R173 (two 22k: 11k) to V2 (V2B, V3, V5; 22
///         µF) and R61 10k to V1 (V1, V2A, V6; 22 µF), at the draws of all twelve triodes whichever channel plays.
///     </para>
///     <para>
///         Simplified: the follower V3B is an ideal unity buffer (it swings a few volts on a 100k cathode load), with
///         its real input network; the loop's coupling caps (0.33 µF into 100k, twice) and R85 220k at the return are
///         left out, as are C20 39 pF and C15 0.1 µF with R25 1M at the input. V3A's 1M grid leak R50 returns to the
///         inverter's bias junction on the drawing and to ground here, as in the 5150 model, whose post stage this is.
///         The channel-switch mute (Q7, Q8) is not modelled.
///     </para>
/// </remarks>
public static class Peavey6505Plus {
    /// <summary>
    ///     V3, the inverter's rail: the screen node (490 V less R210 400 Ω × 11 mA) less R59 4k × the preamp's 11 mA
    ///     (the inverter's 3.2 mA, V2's 4.5 mA, V1's 3.3 mA, each found with the model's tubes at these rails).
    /// </summary>
    const double V3Rail = 442;

    /// <summary>V2 (V2B, V3, V5): V3 less 11k × 7.75 mA (its own 4.48 mA and V1's).</summary>
    const double V2Rail = 356;

    /// <summary>V1 (V1, V2A, V6): V2 less R61 10k × 3.27 mA.</summary>
    const double V1Rail = 324;

    static readonly AmpPanel Panel = AmpPanel.Standard with {
        Gain = "Pre Gain",
        Bass = "Low",
        Middle = "Mid",
        Treble = "High",
        Master = "Post Gain",
        Depth = "Resonance",
        Cut = null,
        Bright = null
    };

    static readonly PowerAmpDesign Power = new(
        "4 x 6L6GC",
        PentodeModel.SixL6GC,
        2,
        490,
        100,
        0.013,
        false,
        0,
        120,
        // R51 10k stands on R57 4.7k: 14.7k from the bias junction to ground.
        new LongTailPairDesign(TriodeModel.Ecc83, V3Rail, 82e3, 100e3, 14.7e3, 470),
        47e-9,
        220e3,
        2.2e3,
        0,
        new(1e6, PotTaper.Linear),
        new(1, 39e3, 4.7e3, new(10e3, PotTaper.Audio), 0.1e-6) {
            PresenceTapCap = 33e-9, Resonance = new(new(1e6, PotTaper.Audio), 6.8e-9)
        },
        true,
        new(1.9e3),
        SpeakerDesign.Closed4x12,
        new(25, 165e-6, 400, 50e-6, 4e3, 47e-6, 11e-3, RectifierModel.Silicon)
    );

    /// <summary>The drawing's chain below V3: 11k into 22 µF (V2), then 10k into 22 µF (V1).</summary>
    static readonly PreampSupplyDesign Supply = new([new(11e3, 22e-6, 4.48e-3), new(10e3, 22e-6, 3.27e-3, 1)]);

    /// <summary>R27 10k and R22 68k between the jack and V1A's grid.</summary>
    const double InputResistance = 78e3;

    static readonly AmpStageDesign Post = PeaveyParts.Post(V2Rail);

    /// <summary>The lead channel.</summary>
    public static AmpDesign Design { get; } = new(
        "6505+",
        [
            new NetworkStageDesign("V1", LeadPre(), InputResistance) { SupplyNode = 2 },
            ..PeaveyParts.ColdClipperToDriver(V1Rail, V2Rail, 1e-9),
            new NetworkStageDesign("Stack", LeadStack()) { SupplyNode = 1 },
            Post
        ],
        Power,
        -6
    ) { Panel = Panel, PreampSupply = Supply };

    /// <summary>The rhythm channel: Switch 1 is Crunch, and the Bright switch is the panel's.</summary>
    public static AmpDesign Rhythm { get; } = new(
        "6505+ Rhythm",
        [
            new NetworkStageDesign("V1A V6A", CleanPre(), InputResistance) { SupplyNode = 2 },
            new TriodeStageDesign(
                "V6B",
                TriodeModel.Ecc83,
                V1Rail,
                220e3,
                2.2e3,
                2.2e-6,
                CouplingCap: 22e-9,
                SeriesResistor: 470e3,
                GridLeak: 220e3
            ) { SupplyNode = 2 },
            new NetworkStageDesign("Stack", CleanStack()) { SupplyNode = 1 },
            Post
        ],
        Power,
        -4
    ) { Panel = Panel with { Bright = "Bright", Switch1 = "Crunch" }, PreampSupply = Supply };

    /// <summary>
    ///     V1A on the stage's input (210k plate, 1.82k ‖ 1 µF), then C6 22 nF from its plate with R176 1M to ground;
    ///     returns the coupled node. V1A shares a circuit with the stage it drives: the networks on its coupling cap
    ///     load the plate heavily, and cut there (one output resistance for the plate) the lead chain agreed with the
    ///     whole circuit to no better than -23 dB.
    /// </summary>
    static int Coupled(StageNetlist net) {
        var plate = TriodeParts.CommonCathode(net, net.Input, V1Rail, 210e3, 1.82e3, 1e-6);
        var coupled = net.Node();
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        return coupled;
    }

    /// <summary>
    ///     V1A and the Lead Pre into V1B. Beside it on V1A's coupling cap, as the relays stand on the lead channel with
    ///     Crunch out: R175 68k + C167 820 pF to ground, and R19 470k ‖ C51 470 pF into R174 22k and R23 6.8k + C63 68
    ///     nF to ground.
    /// </summary>
    static StageNetlist LeadPre() {
        var net = new StageNetlist();
        var coupled = Coupled(net);
        var (shunt, clean, shelf, top, grid) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(coupled, shunt, 68e3);
        net.Capacitor(shunt, StageNetlist.Ground, 820e-12);
        net.Resistor(coupled, clean, 470e3);
        net.Capacitor(coupled, clean, 470e-12);
        net.Resistor(clean, StageNetlist.Ground, 22e3);
        net.Resistor(clean, shelf, 6.8e3);
        net.Capacitor(shelf, StageNetlist.Ground, 68e-9);
        net.Capacitor(coupled, top, 470e-12);
        net.Resistor(top, StageNetlist.Ground, 1e6);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(top, wiper, 1e6);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Output = TriodeParts.CommonCathode(net, grid, V1Rail, 100e3, 1.82e3, 1e-6);
        return net;
    }

    /// <summary>
    ///     V1A and the Clean Pre into V6A, Crunch on Switch 1: out, R19 470k ‖ C51 470 pF onto the pot with R174 22k
    ///     and R23 6.8k + C63 68 nF to ground; in, C13 2.2 nF alone. Beside it the Lead Pre with its wiper grounded
    ///     (C17 470 pF into 321k ‖ 1 nF, the Lead Pre at noon).
    /// </summary>
    static StageNetlist CleanPre() {
        var net = new StageNetlist();
        var coupled = Coupled(net);
        var (lead, top, shelf, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(coupled, lead, 470e-12);
        net.Resistor(lead, StageNetlist.Ground, 321e3);
        net.Capacitor(lead, StageNetlist.Ground, 1e-9);
        net.SwitchedCapacitor(coupled, top, 2.2e-9, SwitchRole.Switch1, off: 470e-12);
        net.SwitchedResistor(coupled, top, 470e3, 0, SwitchRole.Switch1);
        net.SwitchedResistor(top, StageNetlist.Ground, 22e3, 0, SwitchRole.Switch1);
        net.SwitchedResistor(top, shelf, 6.8e3, 0, SwitchRole.Switch1);
        net.Capacitor(shelf, StageNetlist.Ground, 68e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 6.8e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Capacitor(wiper, grid, 470e-12);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = TriodeParts.CommonCathode(net, grid, V1Rail, 220e3, 2.2e3, 0.33e-6);
        return net;
    }

    /// <summary>
    ///     Either channel's stack from <paramref name="drive" />: the treble cap onto the High pot (250k linear), the
    ///     slope resistor, 22 nF to the foot of the High pot and the Low rheostat (1M audio) under it, and 22 nF to the
    ///     wiper of the Mid pot, which runs from the Low rheostat's foot to ground. Returns the High pot's wiper and
    ///     the Mid pot's top.
    /// </summary>
    static (int Wiper, int MiddleTop) Stack(
        StageNetlist net,
        int drive,
        double slopeResistor,
        double trebleCap,
        Pot mid
    ) {
        var (slope, trebleTop, trebleBottom, middleTop) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(drive, trebleTop, trebleCap, CapacitorRole.Stack);
        net.Resistor(drive, slope, slopeResistor);
        var treble = net.Pot(trebleTop, trebleBottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleBottom, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, mid, PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        return (treble, middleTop);
    }

    /// <summary>
    ///     The post gain (1M audio) on <paramref name="wiper" /> and the send follower V3B as an ideal buffer behind
    ///     its own input network: R81 2.2M to ground, C52 22 nF to the grid, R79 1M from the grid to the junction of
    ///     the cathode's R80 1k and R16 100k. The stage's output is the follower's.
    /// </summary>
    static void PostAndFollower(StageNetlist net, int wiper) {
        var post = net.Pot(wiper, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        var (grid, follower, foot) = (net.Node(), net.Node(), net.Node());
        net.Resistor(post, StageNetlist.Ground, 2.2e6);
        net.Capacitor(post, grid, 22e-9);
        net.Amplifier(grid, follower, follower);
        net.Resistor(follower, foot, 1e3);
        net.Resistor(foot, StageNetlist.Ground, 100e3);
        net.Resistor(grid, foot, 1e6);
        net.Output = follower;
    }

    /// <summary>
    ///     The lead channel's pad, stack, post gain and follower: R89 470k into R24 47k and C59 10 nF + R94 33k to
    ///     ground; R95 47k across the Mid pot, R162 1M across the Lead Post.
    /// </summary>
    static StageNetlist LeadStack() {
        var net = new StageNetlist();
        var (pad, shelf) = (net.Node(), net.Node());
        net.Resistor(net.Input, pad, 470e3);
        net.Resistor(pad, StageNetlist.Ground, 47e3);
        net.Capacitor(pad, shelf, 10e-9);
        net.Resistor(shelf, StageNetlist.Ground, 33e3);
        var (wiper, middleTop) = Stack(net, pad, 47e3, 470e-12, new(50e3, PotTaper.Linear));
        net.Resistor(middleTop, StageNetlist.Ground, 47e3);
        net.Resistor(wiper, StageNetlist.Ground, 1e6);
        PostAndFollower(net, wiper);
        return net;
    }

    /// <summary>
    ///     The rhythm channel from V6B's plate: R171 100k and C153 0.33 µF; R75 22k + C10 3.3 nF to ground, through
    ///     R159 1M with Bright in (the switch shorts it at rest); R153 18k onto R156 10k to ground, where Crunch
    ///     (Switch 1) grounds the far end of R163 33k and R157 82k + C155 33 nF; the stack, the Clean Post and the
    ///     follower.
    /// </summary>
    static StageNetlist CleanStack() {
        var net = new StageNetlist();
        var (series, drive, top) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, series, 100e3);
        net.Capacitor(series, drive, 0.33e-6);
        net.Resistor(drive, top, 18e3);
        net.Resistor(top, StageNetlist.Ground, 10e3);
        var (bright, foot) = (net.Node(), net.Node());
        net.Resistor(drive, bright, 22e3);
        net.Capacitor(bright, foot, 3.3e-9);
        net.SwitchedResistor(foot, StageNetlist.Ground, 1, 1e6, SwitchRole.Bright);
        var (crunch, shelf) = (net.Node(), net.Node());
        net.Resistor(top, crunch, 33e3);
        net.Resistor(top, shelf, 82e3);
        net.Capacitor(shelf, crunch, 33e-9);
        net.SwitchedResistor(crunch, StageNetlist.Ground, 0, 1, SwitchRole.Switch1);
        var (wiper, _) = Stack(net, top, 33e3, 570e-12, new(25e3, PotTaper.Audio));
        PostAndFollower(net, wiper);
        return net;
    }
}
