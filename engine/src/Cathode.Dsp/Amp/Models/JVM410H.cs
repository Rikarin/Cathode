using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JVM410H, all four channels in their three modes, as two types: <see cref="Design" /> (OD1 and OD2) and
///     <see cref="CleanCrunch" />. The input (1k, 47 nF, 100 pF and 1M to ground, 1k grid stopper) into V6b (150k,
///     1k5 ‖ 1 µF, 220 pF plate to cathode), 100 nF and 2M2 to ground, then the playing channel's network on the front
///     panel (OD1: 3.3 nF into 470k over the 1M audio Gain pot, 1 nF from the 470k's top to the wiper; OD2: 1 nF, 470k,
///     1.5 nF; Crunch: 10 nF, 470k ‖ 470 pF, the 1M audio Gain with 1 nF from its top to its wiper; Clean: a pre-gain
///     stack, 220 pF / 56k / 100 nF / 47 nF with 200k linear treble, 200k audio bass and 20k linear middle, the treble
///     wiper on the 1M audio Clean Gain with 220 pF from its top to its wiper). The network's output (connector pin 3)
///     always drives V6a's grid through 220k (4M7 to ground before it) and, by the mode relays, either V7a's grid (RL4
///     on), V7b's grid (RL3 and RL4 off) or nothing more (RL3 on: V6a's output then drives V7b). V6a: 220k, 2k7 ‖ 680 nF;
///     22 nF, 220k and 10M into V7b (470k leak; 82k + 22k plate load, the junction on 1 µF and 1M to ground, or on 1 µF
///     to ground with RL2 on, 1 nF from the plate to the rail, 10k unbypassed); 22 nF, 100k and 10M into V7a (470k leak;
///     220k, 820 Ω unbypassed), DC-coupled to the V8b follower (47k), 330 nF and 470k, then the channels' stacks: the OD
///     channels' (470 pF, 33k or 47k slope, 22 nF, 22 nF; 200k linear treble, 1M audio bass, 20k linear middle on its
///     wiper; 39k into the 1M linear Volume) with RL204 on, else the Crunch stack (as OD1's) and the Clean volume network
///     (10k, then 4.7 nF and 220k onto the 1M linear Clean Volume, or 47k past it in the Green mode); each volume's 330k
///     onto a bus that the idle channels' volumes, grounded by their relays, load with 330k each. 22 nF into the V8a
///     follower (1k5 + 100k, its 1M grid leak bootstrapped), 330 nF, the send divider (15k + 4k7), 22k into the loop's
///     mix node (the bypassed loop's 200k mix pot and 22k + 100k return to ground), 1M into V9b's shunt-feedback mixer
///     (22 nF to the grid, 1M leak; 150k, 2k2 ‖ 22 µF; 4M7 from the plate back to the 1M's far end; the reverb driver's
///     470k + 82k on the plate), 100k over 120k and 22 nF into the V9a follower (as V8a), 330 nF into the 200k linear
///     Master, 4M7 and 10k into the inverter: 22 nF, 1M, 470 Ω and 10k + 4k7 tail, 82k / 100k with 47 pF across, 47 nF
///     out, 220k leaks, 5k6 stoppers, four EL34 with 1k screens, 82k ‖ 120 pF of feedback into the 4k7.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawings JVM410-60-02 issue 2 (main board, "Pre amp" and "Power amp / DI out", 2006),
///         JVM410-61-02 issue 1 (front panels 1 and 2: the channel networks, tone stacks, volumes, masters, presence and
///         resonance, the relay latches) and JVM410-67-02 issue 2 (the footswitch), vector PDFs on DrTube
///         (drtube.com/schematics/marshall/jvm410-60-02-1-iss2.pdf and so on; el34world's Marshall_jvm410_sch.pdf
///         holds the first two); every value above is read from them. The relays' rest and energised contacts are the
///         drawings' ("+5V CONTACT" marks the energised one): RL3 at rest joins V7b's grid to the network's output, and
///         energised to V6a's output; RL4 at rest joins V7a's grid to V7b's output, and energised to the network's
///         output; RL2 energised grounds V7b's 1 µF; RL203 picks OD2, RL204 the OD stacks, RL208 the OD networks,
///         RL209 the Crunch network over the Clean one, RL205 the Clean Volume's 47k bypass; RL201, RL202, RL206 and
///         RL207 at rest ground the idle volumes.
///     </para>
///     <para>
///         Which relays each mode energises is set by the ATmega8's firmware (main-board relays RL2, RL3 and RL4 on its
///         latch's A1, A2 and A3), which no drawing gives. It is deduced from the manual's description of each mode's
///         gain structure (JVM410H/C handbook, "Each of its 4 channels has 3 modes": Crunch Green "gain + gain + tone",
///         Orange "gain + gain + gain + tone", Red "the topology of the Orange mode but with more gain"; OD1 Green "very
///         similar to ... Crunch Red", Orange "adds another gain stage", Red "adds more gain"; Clean Green the Volume
///         out of circuit and each further mode another gain stage after the tone stack) and the three paths the relays
///         allow: two triodes (V6b, V7a: RL4 on), three (V6b, V7b, V7a: RL3 and RL4 off) or four (RL3 on), with V7b's
///         gain cut by RL2 (about 2 against 9). The table: Clean 2 / 3 / 4 low, Crunch 2 / 3 low / 3, OD 3 / 4 low / 4
///         (triodes / RL2 on). Inferred: "more gain" in the same topology is RL2 off; the Clean modes' RL2 (guesses:
///         Orange off, as with it on the mode is no louder than Green at full Volume; Red on, as with it off it would be
///         the OD Red's cascade); RL3 off in the two-stage modes (V7b's grid then also hangs on
///         the network's output); RL205 off outside the Clean Green mode. No forum trace or measurement of the relay
///         states was found (searched: Marshall Forum, Fractal Audio forum, the handbooks).
///     </para>
///     <para>
///         Guessed: the rails (HT1 to HT5 are named on the drawings but their voltages are not: 420 V for the inverter,
///         330 / 310 / 290 / 270 V down the preamp's 10k / 22 µF chain, 470 V B+), the idle current (35 mA a tube), the
///         output transformer (1.7k), the feedback tap (16 Ω). Simplified: V6a's grid, when V6a is not in the path, as
///         drawing nothing (its 220k then loads the network not at all), the loop bypassed with nothing plugged in, the
///         reverb at zero (its return's injection not modelled), the presence and resonance networks as the library's
///         presence pot (10k with 100 nF) and depth, 120 pF across the feedback resistor and from the power grids to
///         ground left out.
///     </para>
///     <para>
///         Where the stages are cut: at the next grid wherever a coupling network or the channel network feeds a grid
///         (V6a's, V7b's and V7a's grids, which draw nothing until they conduct, with their leaks in the stage before),
///         at V7a's plate (the DC-coupled follower's grid, as on the JCM800), at the bus (V8a's 22 nF and bootstrapped
///         leak in its own netlist, so that its grid rests where the leak puts it) and at V8a's cathode. Each mode is
///         its own chain of stages (<see cref="ChannelSwitchStageDesign" />), so only the playing mode's triodes run.
///     </para>
///     <para>
///         Panel: Switch 2 selects the Orange mode and Switch 3 the Red one (Red wins; both off is Green); on
///         <see cref="Design" /> Switch 1 selects OD2, on <see cref="CleanCrunch" /> the Crunch channel. Gain, Bass,
///         Middle and Treble are the playing channel's (the idle stacks that load the line follow the same knobs);
///         Control 1 its Volume (out of circuit in the Clean Green mode); Master Master 1.
///     </para>
/// </remarks>
public static class JVM410H {
    const double Closed = 10;
    const double Open = 1e12;
    const double Ht2 = 330;
    const double Ht3 = 310;
    const double Ht4 = 290;
    const double Ht5 = 270;

    /// <summary>The Clean channel's modes (Green, Orange, Red).</summary>
    static readonly Mode[] CleanModes = [new(2, false, true), new(3, false, false), new(4, true, false)];

    /// <summary>The Crunch channel's modes.</summary>
    static readonly Mode[] CrunchModes = [new(2, false, false), new(3, true, false), new(3, false, false)];

    /// <summary>The OD channels' modes.</summary>
    static readonly Mode[] OdModes = [new(3, false, false), new(4, true, false), new(4, false, false)];

    /// <summary>OD1, or OD2 on Switch 1.</summary>
    public static AmpDesign Design { get; } = new(
        "JVM410H OD",
        [
            Modes(OdModes, OdNetworks, ChannelSet.Od),
            ..SharedStages()
        ],
        Power(),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Presence = "Presence",
            Depth = "Resonance",
            Cut = null,
            Bright = null,
            Control1 = "Volume",
            Switch1 = "OD2",
            Switch2 = "Orange",
            Switch3 = "Red"
        }
    };

    /// <summary>The Clean channel, or the Crunch channel on Switch 1.</summary>
    public static AmpDesign CleanCrunch { get; } = new(
        "JVM410H Clean/Crunch",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [Modes(CleanModes, CleanNetwork, ChannelSet.Clean)],
                [Modes(CrunchModes, CrunchNetwork, ChannelSet.Crunch)]
            ),
            ..SharedStages()
        ],
        Power(),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Presence = "Presence",
            Depth = "Resonance",
            Cut = null,
            Bright = null,
            Control1 = "Volume",
            Switch1 = "Crunch",
            Switch2 = "Orange",
            Switch3 = "Red"
        }
    };

    static PowerAmpDesign Power() =>
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 420, 82e3, 100e3, 10e3, 470),
            47e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 82e3, 4.7e3, new(10e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 470, 50e-6, 10e3, 22e-6),
            InverterInput: new(22e-9, 1e6)
        );

    /// <summary>The loop follower, the mixer and the master stage, which every channel and mode shares.</summary>
    static AmpStageDesign[] SharedStages() =>
        [
            new NetworkStageDesign("V8a", LoopFollower()),
            new NetworkStageDesign("V9b", LoopMixer()),
            new NetworkStageDesign("V9a", MasterStage())
        ];

    /// <summary>
    ///     A channel's three modes as chains of stages from V6b to the bus: Switch 3 selects the Red mode, else Switch 2
    ///     the Orange one, else the Green.
    /// </summary>
    static ChannelSwitchStageDesign Modes(Mode[] modes, Action<StageNetlist, int, int> network, ChannelSet set) =>
        new(
            "Red",
            SwitchRole.Switch3,
            [
                new ChannelSwitchStageDesign(
                    "Orange",
                    SwitchRole.Switch2,
                    Chain(modes[0], network, set),
                    Chain(modes[1], network, set)
                )
            ],
            Chain(modes[2], network, set)
        );

    /// <summary>One mode's stages, from V6b through the triodes it puts in the path to the stacks' bus.</summary>
    static AmpStageDesign[] Chain(Mode mode, Action<StageNetlist, int, int> network, ChannelSet set) {
        List<AmpStageDesign> stages = [new NetworkStageDesign("V6b", FirstStage(mode, network), 1e3)];
        if (mode.Triodes == 4) {
            stages.Add(new NetworkStageDesign("V6a", SecondStage()));
        }

        if (mode.Triodes >= 3) {
            stages.Add(new NetworkStageDesign("V7b", ColdClipper(mode.LowGain)));
        }

        stages.Add(new NetworkStageDesign("V7a", ThirdStage()));
        stages.Add(new NetworkStageDesign("V8b", Stacks(set, mode.VolumeBypassed)));
        return [..stages];
    }

    /// <summary>
    ///     After the jack's 1k: 47 nF, 100 pF and 1M to ground, 1k into V6b (150k, 1k5 ‖ 1 µF, 220 pF plate to cathode),
    ///     100 nF and 2M2 to ground, the channel's <paramref name="network" /> onto the connector's pin 3, 4M7 to ground
    ///     there and the grids <paramref name="mode" /> joins to it. Out at the first grid of the mode's cascade: V6a's
    ///     (through 220k), V7b's or V7a's (directly), which draws nothing until it conducts, so the cut is exact in the
    ///     linear range (one at the reactive wiper was 21 dB off).
    /// </summary>
    static StageNetlist FirstStage(Mode mode, Action<StageNetlist, int, int> network) {
        var net = new StageNetlist();
        var (b, grid, rail, plate, cathode, coupled) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(net.Input, b, 47e-9);
        net.Capacitor(b, StageNetlist.Ground, 100e-12);
        net.Resistor(b, StageNetlist.Ground, 1e6);
        net.Resistor(b, grid, 1e3);
        net.Supply(rail, Ht5);
        net.Resistor(rail, plate, 150e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Capacitor(plate, cathode, 220e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 100e-9);
        net.Resistor(coupled, StageNetlist.Ground, 2.2e6);

        var pin3 = net.Node();
        network(net, coupled, pin3);
        net.Resistor(pin3, StageNetlist.Ground, 4.7e6);
        if (mode.Triodes == 4) {
            var grid6a = net.Node();
            net.Resistor(pin3, grid6a, 220e3);
            net.Output = grid6a;
        } else {
            // V7b's 470k leak: on the network's output in the three-stage modes, and (RL3 at rest) in the two-stage
            // ones, where V7a's 470k joins it.
            net.Resistor(pin3, StageNetlist.Ground, 470e3);
            if (mode.Triodes == 2) {
                net.Resistor(pin3, StageNetlist.Ground, 470e3);
            }

            net.Output = pin3;
        }

        return net;
    }

    /// <summary>OD1's gain network, or OD2's with switch 1 (RL203), between V6b's coupled plate and pin 3.</summary>
    static void OdNetworks(StageNetlist net, int from, int output) {
        GainNetwork(net, from, output, 3.3e-9, 1e-9, false);
        GainNetwork(net, from, output, 1e-9, 1.5e-9, true);
    }

    /// <summary>
    ///     One OD channel's gain network, from <paramref name="from" />: <paramref name="input" /> into 470k over the 1M
    ///     audio Gain pot, with <paramref name="across" /> from the 470k's top to the wiper, which drives
    ///     <paramref name="output" />; in circuit while switch 1 is <paramref name="od2" />.
    /// </summary>
    static void GainNetwork(StageNetlist net, int from, int output, double input, double across, bool od2) {
        var (relay, top, mid) = (net.Node(), net.Node(), net.Node());
        net.SwitchedResistor(from, relay, od2 ? Open : Closed, od2 ? Closed : Open, SwitchRole.Switch1);
        net.Capacitor(relay, top, input);
        net.Resistor(top, mid, 470e3);
        var wiper = net.Pot(mid, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, across);
        net.SwitchedResistor(wiper, output, od2 ? Open : Closed, od2 ? Closed : Open, SwitchRole.Switch1);
    }

    /// <summary>The Crunch channel's network: 10 nF, 470k ‖ 470 pF onto the 1M audio Gain, 1 nF from its top to its wiper.</summary>
    static void CrunchNetwork(StageNetlist net, int from, int output) {
        var (a, top) = (net.Node(), net.Node());
        net.Capacitor(from, a, 10e-9);
        net.Resistor(a, top, 470e3);
        net.Capacitor(a, top, 470e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9);
        net.Resistor(wiper, output, Closed);
    }

    /// <summary>
    ///     The Clean channel's pre-gain stack: 220 pF to the 200k linear treble pot, 56k slope, 100 nF to the bass top,
    ///     47 nF to the middle's top; the 200k audio bass and 20k linear middle as variable resistors; the treble wiper on
    ///     the 1M audio Clean Gain, 220 pF from its top to its wiper.
    /// </summary>
    static void CleanNetwork(StageNetlist net, int from, int output) {
        var (trebleTop, slope, bassTop, middleTop) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(from, trebleTop, 220e-12, CapacitorRole.Stack);
        net.Resistor(from, slope, 56e3);
        net.Capacitor(slope, bassTop, 100e-9, CapacitorRole.Stack);
        net.Capacitor(slope, middleTop, 47e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(200e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(200e3, PotTaper.Audio), PotRole.Bass);
        net.Rheostat(middleTop, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        var wiper = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, wiper, 220e-12, CapacitorRole.Bright);
        net.Resistor(wiper, output, Closed);
    }

    /// <summary>
    ///     V6a, from its grid: 220k plate, 2k7 ‖ 680 nF; 22 nF, 220k and 10M to ground. Out at V7b's grid (470k leak).
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, a, next) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var grid = net.Input;
        net.Supply(rail, Ht5);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 680e-9);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, a, 22e-9);
        net.Resistor(a, next, 220e3);
        net.Resistor(next, StageNetlist.Ground, 10e6);
        net.Resistor(next, StageNetlist.Ground, 470e3);
        net.Output = next;
        return net;
    }

    /// <summary>
    ///     V7b, from its grid: 22k + 82k from the rail, the junction on 1 µF and 1M to ground (or, with RL2 on,
    ///     <paramref name="lowGain" />, on 1 µF to ground), 1 nF from the plate to the rail, 10k unbypassed; 22 nF, 100k
    ///     and 10M to ground. Out at V7a's grid (470k leak).
    /// </summary>
    static StageNetlist ColdClipper(bool lowGain) {
        var net = new StageNetlist();
        var (rail, junction, held, plate, cathode, c, next) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var b = net.Input;
        net.Supply(rail, Ht4);
        net.Resistor(rail, junction, 82e3);
        net.Resistor(junction, plate, 22e3);
        net.Capacitor(junction, held, 1e-6);
        net.Resistor(held, StageNetlist.Ground, lowGain ? Closed : 1e6);
        net.Capacitor(plate, rail, 1e-9);
        net.Resistor(cathode, StageNetlist.Ground, 10e3);
        net.Triode(plate, b, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, c, 22e-9);
        net.Resistor(c, next, 100e3);
        net.Resistor(next, StageNetlist.Ground, 10e6);
        net.Resistor(next, StageNetlist.Ground, 470e3);
        net.Output = next;
        return net;
    }

    /// <summary>
    ///     V7a, from its grid: 220k plate, 820 Ω unbypassed. Out at the plate, which drives V8b's grid directly.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht4);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The V8b follower (47k), DC-coupled to V7a's plate, 330 nF and 470k, then the stacks RL204 puts on the line (4M7
    ///     to ground on either side): for the OD channels both OD stacks, the idle one's volume grounded, and the idle
    ///     Clean and Crunch volumes' 330k from the bus to ground; for the Clean and Crunch channels the Crunch stack and
    ///     the Clean volume network, the idle one's output grounded, and the OD volumes' 330k from the bus to ground. Out
    ///     at the bus.
    /// </summary>
    static StageNetlist Stacks(ChannelSet set, bool volumeBypassed) {
        var net = new StageNetlist();
        var (rail3, follower, line, bus) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail3, Ht3);
        net.Triode(rail3, net.Input, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 47e3);
        net.Capacitor(follower, line, 330e-9);
        net.Resistor(line, StageNetlist.Ground, 470e3);
        net.Resistor(line, StageNetlist.Ground, 4.7e6);
        if (set == ChannelSet.Od) {
            var od1 = OdStack(net, line, bus, 33e3);
            net.SwitchedResistor(od1, StageNetlist.Ground, Open, Closed, SwitchRole.Switch1);
            var od2 = OdStack(net, line, bus, 47e3);
            net.SwitchedResistor(od2, StageNetlist.Ground, Closed, Open, SwitchRole.Switch1);
        } else {
            var crunch = OdStack(net, line, bus, 33e3);
            if (set == ChannelSet.Clean) {
                net.Resistor(crunch, StageNetlist.Ground, Closed);
            }

            CleanVolume(net, line, bus, set == ChannelSet.Crunch, volumeBypassed);
        }

        net.Resistor(bus, StageNetlist.Ground, 330e3 / 2);
        net.Output = bus;
        return net;
    }

    /// <summary>
    ///     One OD-style stack (470 pF, <paramref name="slope" />, 22 nF, 22 nF; 200k linear treble, 1M audio bass, 20k
    ///     linear middle with the middle cap on its wiper), 39k from the treble wiper into the 1M linear Volume, 330k from
    ///     its wiper to the bus. Returns the volume's wiper, which a relay grounds while the channel is idle.
    /// </summary>
    static int OdStack(StageNetlist net, int line, int bus, double slope) {
        var (trebleTop, slopeNode, bassTop, middleTop, volumeTop) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(line, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(line, slopeNode, slope);
        net.Capacitor(slopeNode, bassTop, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(200e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(20e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slopeNode, middle, 22e-9, CapacitorRole.Stack);
        net.Resistor(treble, volumeTop, 39e3);
        var volume = net.Pot(volumeTop, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Control1);
        net.Resistor(volume, bus, 330e3);
        return volume;
    }

    /// <summary>
    ///     The Clean channel's volume network from the line: 10k, then 4.7 nF and 220k onto the 1M linear Clean Volume and
    ///     47k beside it; RL205 takes the volume's wiper, or in the Green mode (<paramref name="bypassed" />) the 47k, to
    ///     the 330k into the bus; that node grounded by RL206 while the channel is <paramref name="idle" />.
    /// </summary>
    static void CleanVolume(StageNetlist net, int line, int bus, bool idle, bool bypassed) {
        var (junction, a, top, common) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(line, junction, 10e3);
        net.Capacitor(junction, a, 4.7e-9);
        net.Resistor(a, top, 220e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Control1);
        if (bypassed) {
            net.Resistor(junction, common, 47e3);
        } else {
            net.Resistor(wiper, common, Closed);
        }

        if (idle) {
            net.Resistor(common, StageNetlist.Ground, Closed);
        }

        net.Resistor(common, bus, 330e3);
    }

    /// <summary>
    ///     22 nF into the V8a follower (1k5 + 100k to ground, its 1M grid leak returning between them). Out at its
    ///     cathode, a source of a few hundred ohms.
    /// </summary>
    static StageNetlist LoopFollower() {
        var net = new StageNetlist();
        var (grid, rail, cathode, tap) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, grid, 22e-9);
        net.Supply(rail, Ht3);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, tap, 1.5e3);
        net.Resistor(tap, StageNetlist.Ground, 100e3);
        net.Resistor(grid, tap, 1e6);
        net.Output = cathode;
        return net;
    }

    /// <summary>
    ///     From V8a's cathode: 330 nF, the send divider (15k + 4k7), 22k into the mix node (322k to ground through the
    ///     bypassed loop's mix pot and return), 1M to V9b's input node, which 22 nF couples to its grid (1M leak) and 4M7
    ///     ties to its plate (150k, 2k2 ‖ 22 µF; the reverb driver's 552k to ground). Out at V9b's plate.
    /// </summary>
    static StageNetlist LoopMixer() {
        var net = new StageNetlist();
        var (rail2, coupled, mix, n, grid9, plate, cathode9) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail2, Ht2);
        net.Capacitor(net.Input, coupled, 330e-9);
        net.Resistor(coupled, StageNetlist.Ground, 19.7e3);
        net.Resistor(coupled, mix, 22e3);
        net.Resistor(mix, StageNetlist.Ground, 322e3);
        net.Resistor(mix, n, 1e6);
        net.Capacitor(n, grid9, 22e-9);
        net.Resistor(grid9, StageNetlist.Ground, 1e6);
        net.Resistor(rail2, plate, 150e3);
        net.CathodeResistor(cathode9, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode9, StageNetlist.Ground, 22e-6);
        net.Triode(plate, grid9, cathode9, TriodeModel.Ecc83);
        net.Resistor(plate, n, 4.7e6);
        net.Resistor(plate, StageNetlist.Ground, 552e3);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     100k over 120k, 22 nF into the V9a follower (as V8a), 330 nF into the Master pot (200k linear), 4M7 from its
    ///     wiper to ground and 10k toward the inverter. Out after the 10k.
    /// </summary>
    static StageNetlist MasterStage() {
        var net = new StageNetlist();
        var (a, grid, rail, cathode, tap, coupled, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, a, 100e3);
        net.Resistor(a, StageNetlist.Ground, 120e3);
        net.Capacitor(a, grid, 22e-9);
        net.Supply(rail, Ht2);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, tap, 1.5e3);
        net.Resistor(tap, StageNetlist.Ground, 100e3);
        net.Resistor(grid, tap, 1e6);
        net.Capacitor(cathode, coupled, 330e-9);
        var wiper = net.Pot(coupled, StageNetlist.Ground, new(200e3, PotTaper.Linear), PotRole.Master);
        net.Resistor(wiper, StageNetlist.Ground, 4.7e6);
        net.Resistor(wiper, output, 10e3);
        net.Output = output;
        return net;
    }

    /// <summary>Which channels' networks a chain carries.</summary>
    enum ChannelSet {
        Clean,
        Crunch,
        Od
    }

    /// <summary>
    ///     A mode's relay states: the <paramref name="Triodes" /> in the cascade (2: V6b and V7a, RL4 on; 3: V6b, V7b and
    ///     V7a; 4: V6b, V6a, V7b and V7a, RL3 on), V7b's gain cut (<paramref name="LowGain" />, RL2 on) and the Clean
    ///     Volume bypassed (<paramref name="VolumeBypassed" />, RL205 on).
    /// </summary>
    readonly record struct Mode(int Triodes, bool LowGain, bool VolumeBypassed);
}
