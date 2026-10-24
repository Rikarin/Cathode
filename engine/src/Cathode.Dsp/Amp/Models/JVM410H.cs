using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JVM410H, the OD1 and OD2 channels in their highest-gain mode (every triode in the cascade). The input
///     (1k, 47 nF, 100 pF and 1M to ground, 1k grid stopper) into V6b (150k, 1k5 ‖ 1 µF, 220 pF plate to cathode), 100 nF
///     and 2M2 into the channel's gain network on the front panel (OD1: 3.3 nF into 470k over the 1M audio Gain pot, 1 nF
///     across the 470k and the pot's upper part; OD2: 1 nF, 470k, 1.5 nF), 220k into V6a (4M7 to ground before it; 220k,
///     2k7 ‖ 680 nF), 22 nF, 220k and 10M into V7b (470k leak; 82k + 22k plate load with 1 nF from the plate to the
///     rail, 10k unbypassed), 22 nF, 100k and 10M into V7a (470k leak; 220k, 820 Ω unbypassed), DC-coupled to the V8b
///     follower (47k), 330 nF and 470k, both OD tone stacks in parallel (470 pF, 33k or 47k slope, 22 nF, 22 nF; 200k
///     linear treble, 1M audio bass, 20k linear middle), each treble wiper's 39k into its 1M linear Volume, 330k from each
///     wiper onto a bus that the idle channels' volumes (their wipers grounded by relays) load with 330k each; 22 nF into
///     the V8a follower (1k5 + 100k, its 1M grid leak bootstrapped), 330 nF, the send divider (15k + 4k7), 22k into the
///     loop's mix node (the bypassed loop's 200k mix pot and 22k + 100k return to ground), 1M into V9b's shunt-feedback
///     mixer (22 nF to the grid, 1M leak; 150k, 2k2 ‖ 22 µF; 4M7 from the plate back to the 1M's far end; the reverb
///     driver's 470k + 82k on the plate), 100k over 120k and 22 nF into the V9a follower (as V8a), 330 nF into the 200k
///     linear Master, 4M7 and 10k into the inverter: 22 nF, 1M, 470 Ω and 10k + 4k7 tail, 82k / 100k with 47 pF across,
///     47 nF out, 220k leaks, 5k6 stoppers, four EL34 with 1k screens, 82k ‖ 120 pF of feedback into the 4k7.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawings JVM410-60-02 issue 2 (main board, "Pre amp" and "Power amp / DI out", 2006) and
///         JVM410-61-02 issue 1 (front panels 1 and 2: the gain networks, tone stacks, volumes, masters, presence and
///         resonance), vector PDF on el34world (Marshall_jvm410_sch.pdf); every value above is read from them.
///     </para>
///     <para>
///         Guessed: the rails (HT1 to HT5 are named on the drawings but their voltages are not: 420 V for the inverter,
///         330 / 310 / 290 / 270 V down the preamp's 10k / 22 µF chain, 470 V B+), the idle current (35 mA a tube), the
///         output transformer (1.7k), the feedback tap (16 Ω), which state of the channel and mode relays each mode
///         selects (here: V7b's 82k / 22k junction left on its 1 µF and 1M, as the relay rests; V6a, V7b and V7a all in
///         the
///         path, the Red mode's cascade). Simplified: the loop bypassed with nothing plugged in, the reverb at zero (its
///         return's injection not modelled), the presence and resonance networks as the library's presence pot (10k with
///         100 nF) and depth, 120 pF across the feedback resistor and from the power grids to ground left out, the idle
///         gain networks taken as disconnected by their relays.
///     </para>
///     <para>
///         Where the stages are cut: at the next grid wherever a coupling network follows a plate (V6a's, V7b's and
///         V7a's grids, which draw nothing until they conduct; cut at the gain pot's reactive wiper instead, the chain
///         was 21 dB from the reference), at V7a's plate (the DC-coupled follower's grid, as on the JCM800), at the bus
///         (V8a's 22 nF and bootstrapped leak in its own netlist, so that its grid rests where the leak puts it) and at
///         V8a's cathode. Eight one-triode stages cost about half of what the same circuit cost as five stages with two
///         triode pairs.
///     </para>
///     <para>
///         Panel: Switch 1 selects OD2; Gain, Bass, Middle and Treble are the playing channel's (the idle channel's stack
///         follows the same knobs and loads the follower as it does in the amp); Control 1 its Volume; Master Master 1.
///     </para>
/// </remarks>
public static class JVM410H {
    const double Closed = 10;
    const double Open = 1e12;
    const double Ht2 = 330;
    const double Ht3 = 310;
    const double Ht4 = 290;
    const double Ht5 = 270;

    public static AmpDesign Design { get; } = new(
        "JVM410H OD",
        [
            new NetworkStageDesign("V6b", FirstStage(), 1e3),
            new NetworkStageDesign("V6a", SecondStage()),
            new NetworkStageDesign("V7b", ColdClipper()),
            new NetworkStageDesign("V7a", ThirdStage()),
            new NetworkStageDesign("V8b", Stacks()),
            new NetworkStageDesign("V8a", LoopFollower()),
            new NetworkStageDesign("V9b", LoopMixer()),
            new NetworkStageDesign("V9a", MasterStage())
        ],
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
            new(Math.Sqrt(2), 82e3, 4.7e3, new(10e3, PotTaper.Linear), 0.1e-6, 90),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 470, 50e-6, 10e3, 22e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Presence = "Presence",
            Depth = "Resonance",
            Cut = null,
            Bright = null,
            Control1 = "Volume",
            Switch1 = "OD2"
        }
    };

    /// <summary>
    ///     After the jack's 1k: 47 nF, 100 pF and 1M to ground, 1k into V6b (150k, 1k5 ‖ 1 µF, 220 pF plate to cathode),
    ///     100 nF and 2M2 to ground, the playing channel's gain network (OD1, or OD2 with switch 1), 4M7 to ground and 220k.
    ///     Out at V6a's grid, which draws nothing until it conducts: the cut there is exact in the linear range, where
    ///     one at the reactive wiper was 21 dB off.
    /// </summary>
    static StageNetlist FirstStage() {
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

        var (output, grid6a) = (net.Node(), net.Node());
        GainNetwork(net, coupled, output, 3.3e-9, 1e-9, false);
        GainNetwork(net, coupled, output, 1e-9, 1.5e-9, true);
        net.Resistor(output, StageNetlist.Ground, 4.7e6);
        net.Resistor(output, grid6a, 220e3);
        net.Output = grid6a;
        return net;
    }

    /// <summary>
    ///     One channel's gain network, from <paramref name="from" />: <paramref name="input" /> into 470k over the 1M audio
    ///     Gain pot, with <paramref name="across" /> from the 470k's top to the wiper, which drives
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
    ///     V7b, from its grid: 22k + 82k from the rail, the junction on 1 µF and 1M to ground, 1 nF from the plate to the
    ///     rail, 10k unbypassed; 22 nF, 100k and 10M to ground. Out at V7a's grid (470k leak).
    /// </summary>
    static StageNetlist ColdClipper() {
        var net = new StageNetlist();
        var (rail, junction, held, plate, cathode, c, next) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var b = net.Input;
        net.Supply(rail, Ht4);
        net.Resistor(rail, junction, 82e3);
        net.Resistor(junction, plate, 22e3);
        net.Capacitor(junction, held, 1e-6);
        net.Resistor(held, StageNetlist.Ground, 1e6);
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
    ///     The V8b follower (47k), DC-coupled to V7a's plate, 330 nF and 470k, both OD stacks with their volumes and 330k
    ///     each onto the bus, which the idle channels' grounded volumes load with 330k each (clean and crunch, and the
    ///     idle OD channel's own). Out at the bus.
    /// </summary>
    static StageNetlist Stacks() {
        var net = new StageNetlist();
        var (rail3, follower, line, bus) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail3, Ht3);
        net.Triode(rail3, net.Input, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 47e3);
        net.Capacitor(follower, line, 330e-9);
        net.Resistor(line, StageNetlist.Ground, 470e3);
        net.Resistor(line, StageNetlist.Ground, 4.7e6);
        Stack(net, line, bus, 33e3, false);
        Stack(net, line, bus, 47e3, true);
        net.Resistor(bus, StageNetlist.Ground, 330e3 / 2);
        net.Output = bus;
        return net;
    }

    /// <summary>
    ///     One OD channel's stack (470 pF, <paramref name="slope" />, 22 nF, 22 nF; 200k linear treble, 1M audio bass, 20k
    ///     linear middle), 39k from the treble wiper into the 1M linear Volume, 330k from its wiper to the bus; the wiper
    ///     grounded while the channel is idle (switch 1 not <paramref name="od2" />).
    /// </summary>
    static void Stack(StageNetlist net, int line, int bus, double slope, bool od2) {
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
        net.SwitchedResistor(volume, StageNetlist.Ground, od2 ? Closed : Open, od2 ? Open : Closed, SwitchRole.Switch1);
        net.Resistor(volume, bus, 330e3);
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
}
