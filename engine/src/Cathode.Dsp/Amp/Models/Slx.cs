using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM900 SL-X (2100SL-X 100 W, 2500SL-X 50 W): the all-valve JCM900, a single channel of five ECC83
///     stages with two master volumes. The jack (1M to ground, 68k) into V1a (220k, 2k7 ‖ 1 µF, 100 pF from the
///     plate to the cathode), 22 nF and 4k7 ‖ 47 nF onto the 1M audio Gain with 1 nF across its upper half, V2a
///     (100k with 1 nF across it, 820 Ω unbypassed), 10 nF into a 220k/220k divider, V2b (27k, 2k7), 22 nF and
///     470k ‖ 470 pF onto the 1M audio Volume (1M to ground), V3a (220k, 2k7) DC-coupled to the V3b follower
///     (100k), and the Marshall stack on its cathode (470 pF, 33k slope, 22 nF bass, 22 nF middle; 220k linear
///     treble, 220k bass, 22k middle). The treble wiper goes through 680k onto 68k and a TL072 buffer, the effects
///     loop, the return amp (inverting, 220k ‖ 10 pF) and one of the two master stages (inverting, 150k in, the 1M
///     audio master pot ‖ 10 pF as feedback), then the 12AX7 long-tail pair (82k/91k, 1k, 15k tail, 1M grid
///     resistors, 100 pF across the first plate resistor and 100 pF between the plates) and four (2100) or two
///     (2500) EL34 on 1k screens.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall's drawings for the 2100SL-X/2500SL-X, scans on schematicheaven.net
///         (<c>jcm900_slx_100w_2100.pdf</c> and <c>jcm900_slx_50w_2500.pdf</c>): the preamp sheet SLXa-61-02 issue
///         5 ("PRE AMP CCT", 05/05/93), the power amp sheet 2100-60-34 issue 10 ("EL34 VALVE TYPE") and
///         2100-60-02 issue 9 (mains, loop and the Direct/Record outputs). Every preamp and inverter value above is
///         read from them, as are the output stage's 1k5 grid stoppers, 150k grid leaks, 47 nF coupling caps, the
///         1k 5 W screen resistors the Output Power Select switch picks, the −48 V bias the sheet asks for, and the
///         16/8/4 Ω taps of the C3070 transformer.
///     </para>
///     <para>
///         Inferred, because the preamp sheet names its wires by tube pin and not by stage, so the order of the
///         five stages is not drawn: the chain above is the one that uses every drawn part exactly once and leaves
///         the two 1M audio pots as the amp's Gain and Volume — the 220k/220k divider between the stage with the
///         1 nF plate cap and the 27k stage, the 470k ‖ 470 pF ahead of the second pot, and the 220k stage
///         DC-coupled to the 100k follower that drives the stack, which is how every Marshall of this family is
///         wired. The three 2k7 cathode resistors are drawn without bypass caps and are taken as unbypassed.
///     </para>
///     <para>
///         Guessed, as neither sheet prints a voltage: the rails (300 V for the preamp, 330 V for the inverter,
///         470 V B+, as <see cref="JCM900" />), the idle current (40 mA) and the output transformers (1.7k plate to
///         plate for the 100 W, 3.2k for the 50 W, as the JCM900 Dual Reverb's). The feedback is the JCM900's too:
///         100k from the 16 Ω tap into 4k7 at the tail's foot with the 22k linear Presence and 0.1 µF across it.
///         The power sheet draws the tail's foot with a 4k7 to ground and a 100 nF coming in from a wire, and no
///         feedback resistor anywhere on the three sheets, so where Marshall put it is not readable here.
///         Simplified: the four op-amps are linear amplifiers (<see cref="StageNetlist.Amplifier" />) — the buffer
///         and the return stay inside their rails, and a master stage's rail clipping would only square what the
///         inverter has clipped already; the effects loop is a wire, with its send level pot at full and the
///         Direct and Record outputs left out; the Output Power Select switch (which halves the screen resistors)
///         is not modelled.
///     </para>
///     <para>
///         Panel: Gain and Control 1 (Volume) are the two preamp pots, Master is Master Volume A, and the
///         <b>Master B</b> switch plays the second master stage, whose own pot is Control 2 (Master B), as the
///         amp's footswitch does. The amp has no bright switch, depth or cut.
///     </para>
/// </remarks>
public static class Slx {
    const double Preamp = 300;
    const double Inverter = 330;

    /// <summary>The 2100SL-X: four EL34.</summary>
    public static AmpDesign Design { get; } = new(
        "JCM900 SL-X 2100",
        Stages(),
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.040,
            false,
            0,
            100,
            // The 15k tail stands on the 4k7 feedback shunt: 19.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, Inverter, 82e3, 91e3, 19.7e3, 1e3),
            47e-9,
            150e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 100e3, 4.7e3, new(22e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 50e-6, 22e3, 33e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) { Panel = Panel() };

    /// <summary>The 2500SL-X: two EL34 and the same preamp.</summary>
    public static AmpDesign Head50 { get; } = new(
        "JCM900 SL-X 2500",
        Stages(),
        Design.Power with {
            Name = "2 x EL34",
            TubesPerSide = 1,
            RatedPower = 50,
            Transformer = new(3.2e3),
            PowerSupply = new(40, 50e-6, 100, 50e-6, 22e3, 33e-6)
        },
        -7
    ) { Panel = Panel() };

    static AmpPanel Panel() =>
        AmpPanel.Standard with {
            Master = "Master A",
            Control1 = "Volume",
            Control2 = "Master B",
            Bright = null,
            Depth = null,
            Cut = null,
            Switch1 = "Master B",
            BehindSwitch = new Dictionary<string, string> { ["control2"] = "switch1" }
        };

    static AmpStageDesign[] Stages() => [
        new NetworkStageDesign("V1a", InputStage(), 68e3),
        new NetworkStageDesign("V2a", SecondStage()),
        new NetworkStageDesign("V2b", ThirdStage()),
        new NetworkStageDesign("V3", FollowerAndStack()),
        new NetworkStageDesign("Loop", Loop()),
        new ChannelSwitchStageDesign(
            "Master",
            SwitchRole.Switch1,
            [new NetworkStageDesign("Master A", Master(PotRole.Master))],
            [new NetworkStageDesign("Master B", Master(PotRole.Control2))]
        )
    ];

    /// <summary>
    ///     V1a behind the jack's 1M and 68k: 220k plate, 2k7 ‖ 1 µF, 100 pF from the plate to the cathode; 22 nF
    ///     and 4k7 ‖ 47 nF onto the 1M audio Gain with 1 nF across its upper half. Out at its wiper.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Capacitor(plate, cathode, 100e-12);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, top, 4.7e3);
        net.Capacitor(coupled, top, 47e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V2a (100k with 1 nF across it, 820 Ω unbypassed), 10 nF into the 220k/220k divider. Out at the divider.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 1e-9, CapacitorRole.Plate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 10e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2b (27k, 2k7 unbypassed), 22 nF and 470k ‖ 470 pF onto the 1M audio Volume, which has 1M to ground.
    ///     Out at its wiper.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, top) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 27e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, top, 470e3);
        net.Capacitor(coupled, top, 470e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(top, StageNetlist.Ground, 1e6);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V3a (220k, 2k7) DC-coupled to the V3b follower (100k), the stack on its cathode (470 pF, 33k, 22 nF,
    ///     22 nF; 220k linear treble, 220k bass, 22k middle) and 680k from the treble wiper onto the buffer's 68k.
    ///     Out there.
    /// </summary>
    static StageNetlist FollowerAndStack() {
        var net = new StageNetlist();
        var (rail, plate, cathode, follower) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Triode(rail, plate, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);

        var (trebleTop, trebleFoot, slope, middle, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(follower, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(follower, slope, 33e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, trebleFoot, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleFoot, middle, new(220e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, output, 680e3);
        net.Resistor(output, StageNetlist.Ground, 68e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The send buffer (a unity follower), the loop as a wire, and the return amp (inverting, 220k in and
    ///     220k ‖ 10 pF of feedback). Out at its output.
    /// </summary>
    static StageNetlist Loop() {
        var net = new StageNetlist();
        var (buffered, coupled, minus, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Amplifier(net.Input, buffered, buffered);
        net.Capacitor(buffered, coupled, 22e-6);
        net.Resistor(coupled, minus, 220e3);
        net.Resistor(minus, output, 220e3);
        net.Capacitor(minus, output, 10e-12);
        net.Amplifier(StageNetlist.Ground, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     A master stage: 150k into the inverting input, the 1M audio master pot as a rheostat ‖ 10 pF as feedback.
    /// </summary>
    static StageNetlist Master(PotRole role) {
        var net = new StageNetlist();
        var (minus, output) = (net.Node(), net.Node());
        net.Resistor(net.Input, minus, 150e3);
        net.Rheostat(minus, output, new(1e6, PotTaper.Audio), role);
        net.Capacitor(minus, output, 10e-12);
        net.Amplifier(StageNetlist.Ground, minus, output);
        net.Output = output;
        return net;
    }
}
