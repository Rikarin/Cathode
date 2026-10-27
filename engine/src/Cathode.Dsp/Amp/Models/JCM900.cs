using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM900 Dual Reverb (4100 100 W, 4500 50 W head, 4502 2 × 12 combo), both channels. The input (10k, 100
///     pF, 0.1 µF, 1M) feeds IC1, an M5201 switching dual op-amp whose two sections share one output, the channel
///     selecting which drives it: channel A non-inverting 1 + 100k ‖ 22 pF over 15k + 0.1 µF, channel B 1 + 100k ‖ 470
///     pF over 4k7 + 47 nF. Channel A: 47 nF, 22k to ground, 12k into IC4a, inverting, whose input arm is the upper
///     part of the 220k linear Preamp pot with 1 nF across it and whose feedback is the lower part with 47 pF and two
///     red LEDs each way. Channel B: 0.22 µF and 10k into IC6b, inverting, the 1M audio Gain pot as a rheostat with 220
///     pF as its feedback; 0.22 µF and 4k7 onto a W005F bridge with a 1N4007 across its DC terminals (a symmetric clamp
///     of three junctions each way to ground); the "Boost Pre-EQ" ladder (4.7 nF and 4k7 from the clamp, 10k, 10k,
///     100k, 4k7 with 0.22 µF and 0.22 µF from the 4k7) into IC4b (1 + 10k ‖ 100 pF over 22k). Both: 2k2 and 68k into
///     V1a (150k plate, 1k5 unbypassed, 22 pF grid to cathode) with 220k of shunt feedback from the tone stack's input,
///     the V1b follower (100k) through 0.1 µF into a 470 pF / 33k / 22 nF / 22 nF stack (220k linear treble, 1M audio
///     bass, 22k linear middle with the middle cap on its wiper), 470k into the 470k-loaded send buffer (IC5b); the
///     loop's return op-amp (IC5a, inverting, 100k ‖ 47 pF) into V2a (100k, 2k2 with 470 Ω + 0.22 µF and the 10k linear
///     Presence pot across it); 47 nF and 2M2 into V2b (100k, 1k2 unbypassed; 220k grid leak) with 470k of shunt
///     feedback from its coupled plate; the master, an M5201 inverting stage (220k in, the 1M audio Master pot as a
///     rheostat ‖ 22 pF as feedback), into the inverter.
/// </summary>
/// <remarks>
///     <para>
///         Sources: the preamp is Marshall drawing JMP52-02 "Pre-amp cct 4100/4500" issue 11 (23/09/97), a vector PDF
///         on el34world (Marshall_jcm900_dual_reverb_jmp52_02_preamp_iss11.pdf), every value above read from it; the
///         power amps are drawing 4100-60-02 issue 11 "Power amp cct" (100 W, el34world
///         Marshall_jcm900_dualrev_100w_4100.pdf, sheet 3) and the Marshall "JCM900 Series 50w Power Amp Circuit
///         Diagram" CDD112 issue 10 for models 2500, 2501, 2502, 4500, 4501 and 4502 (el34world
///         Marshall_jcm900_dualrev_50w_4500.pdf, sheet 3), both scans: a long-tail pair with 82k ‖ 100 pF and 100k
///         plates, 100 pF between them, 1k bias and 15k tail, 1M grid resistors, 150k grid leaks and 1k5 stoppers; 47
///         nF out on the 100 W, 22 nF on the 50 W; the 50 W's screens 2k2 5 W (the 100 W taken with 1k 5 W, Marshall's
///         100 W EL34 value: 2203, 1959SLP, DSL, JVM); feedback "100k – DRs" with 100 pF across it into 4k7 at the
///         tail, the presence cap there "not fitted" on the Dual Reverbs (their Presence is V2a's cathode network, a
///         preamp control).
///     </para>
///     <para>
///         Guessed, as no drawing gives them: the rails (300 V for the preamp, 330 V for the inverter, 460 V B+; the
///         sheets have no DC voltages), the idle current (40 mA, the bias set to −40 to −42 V as the sheet says), the
///         output transformers (3.2k as AmpBooks' 2204 for the 50 W, 1.7k for the 100 W), the feedback tap (4 Ω, as the
///         JCM800's 100k; the 50 W sheet takes it from a wire whose tap is not legible), the effects return's input
///         resistor (100k, for unity gain: it is on the jack board, which is not drawn), and IC6b's type (the M5201 as
///         a JRC4558). Simplified: every op-amp but IC6b is a linear amplifier (<see cref="StageNetlist.Amplifier" />):
///         IC4b and the return stay inside their rails, IC1 too but for channel B above about 0.6 V in (where the boost
///         and the clamp follow anyway); IC4a's LEDs hold its output to about 3.5 V; the master's rail clipping (above
///         about 13 V, at high Master settings) would only square a signal the inverter has clipped already. Channel
///         B's IC6b clips at its rails as it does (gain up to 100), into the bridge clamp. The send buffer (IC5b, a
///         unity follower of a few volts at most) is an ideal buffer, the loop is a wire, the reverb is off (its
///         drive's 470k + 220k loads V2a's coupled plate and its return's 680k + 220k V2b's grid, as resistors to
///         ground), the deselected master's 220k and pot hang from the master's input node to the output (the M5201's
///         idle inputs taken as open), the 100 pF across the feedback resistor (a 16 kHz corner) is left out. Not
///         modelled: the Output Power Select switch (screens on a lower supply) and the 100 W's US version (4 × 5881
///         with 56k ‖ 10 nF of feedback).
///     </para>
///     <para>
///         Panel: Switch 1 selects channel B (the footswitch's "channel"); Gain is the playing channel's gain pot
///         (channel A's Preamp, channel B's Gain, marked 0–20 on the amp: its 10 here is the amp's 20), Master both
///         channels' masters, Control 1 the Presence (a preamp control here, so the power amp's presence is hidden).
///     </para>
/// </remarks>
public static class JCM900 {
    const double Preamp = 300;
    const double Rails = 15;

    static readonly OpAmpModel M5201 = OpAmpModel.JRC4558;

    /// <summary>The 4502 combo and 4500 head: two EL34.</summary>
    public static AmpDesign Design { get; } = new(
        "JCM900 4502",
        Stages(),
        new(
            "2 x EL34",
            PentodeModel.EL34,
            1,
            460,
            2.2e3,
            0.040,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 15e3, 1e3),
            22e-9,
            150e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(0.5), 100e3, 4.7e3, new(22e3, PotTaper.Linear), 1e-12),
            true,
            new(3.2e3),
            SpeakerDesign.Closed4x12,
            new(40, 50e-6, 100, 50e-6, 22e3, 50e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) { Panel = Panel() };

    /// <summary>The 4100 head: four EL34 (the export version).</summary>
    public static AmpDesign Head100 { get; } = new(
        "JCM900 4100",
        Stages(),
        Design.Power with {
            Name = "4 x EL34",
            TubesPerSide = 2,
            RatedPower = 100,
            CouplingCap = 47e-9,
            ScreenResistor = 1e3, // the 50 W's 2k2 is the 50 W's alone
            Transformer = new(1.7e3),
            PowerSupply = new(25, 100e-6, 100, 50e-6, 22e3, 50e-6)
        },
        -7
    ) { Panel = Panel() };

    static AmpPanel Panel() =>
        AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "Presence",
            Switch1 = "Channel B"
        };

    static AmpStageDesign[] Stages() => [
        new ChannelSwitchStageDesign(
            "Channel",
            SwitchRole.Switch1,
            [
                new NetworkStageDesign("IC1a", InputStage(100e3, 22e-12, 15e3, 0.1e-6), 10e3),
                new NetworkStageDesign("IC4a", NormalStage())
            ],
            [
                new NetworkStageDesign("IC1b", InputStage(100e3, 470e-12, 4.7e3, 47e-9), 10e3),
                new NetworkStageDesign("IC6b", BoostStage()),
                new NetworkStageDesign("IC4b", PreEqStage())
            ]
        ),
        new NetworkStageDesign("V1", FirstTube()) { MaxIterations = 64 },
        new BufferStageDesign("IC5b"),
        new NetworkStageDesign("IC5a", ReturnStage()),
        new NetworkStageDesign("V2a", PresenceStage()),
        new NetworkStageDesign("V2b", MasterStage()) { MaxIterations = 64 }
    ];

    /// <summary>
    ///     After the jack's 10k: 100 pF to ground, 0.1 µF, 1M to ground, IC1 (non-inverting) with the playing channel's
    ///     feedback (<paramref name="feedback" /> ‖ <paramref name="feedbackCap" />) over <paramref name="shunt" /> and
    ///     <paramref name="shuntCap" />.
    /// </summary>
    static StageNetlist InputStage(double feedback, double feedbackCap, double shunt, double shuntCap) {
        var net = new StageNetlist();
        var (plus, minus, leg, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, StageNetlist.Ground, 100e-12);
        net.Capacitor(net.Input, plus, 0.1e-6);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Resistor(output, minus, feedback);
        net.Capacitor(output, minus, feedbackCap);
        net.Resistor(minus, leg, shunt);
        net.Capacitor(leg, StageNetlist.Ground, shuntCap);
        net.Amplifier(plus, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     Channel A: 47 nF, 22k to ground, 12k into IC4a's input arm, the Preamp pot's upper part with 1 nF across it,
    ///     to the inverting input at its wiper; the lower part, 47 pF and the LED pairs from there to the output, which
    ///     carries 100k to ground.
    /// </summary>
    static StageNetlist NormalStage() {
        var net = new StageNetlist();
        var (a, arm, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 47e-9);
        net.Resistor(a, StageNetlist.Ground, 22e3);
        net.Resistor(a, arm, 12e3);
        var minus = net.Pot(arm, output, new(220e3, PotTaper.Linear), PotRole.Gain);
        net.Capacitor(arm, minus, 1e-9);
        net.Capacitor(minus, output, 47e-12);
        net.Diodes(minus, output, DiodeModel.Led, 2, 2);
        net.Resistor(output, StageNetlist.Ground, 100e3);
        net.Amplifier(StageNetlist.Ground, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     Channel B's boost: 0.22 µF and 10k into IC6b, inverting, the Gain pot as a rheostat ‖ 220 pF as feedback.
    /// </summary>
    static StageNetlist BoostStage() {
        var net = new StageNetlist();
        var (a, minus, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 0.22e-6);
        net.Resistor(a, minus, 10e3);
        net.Rheostat(minus, output, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(minus, output, 220e-12);
        net.OpAmp(StageNetlist.Ground, minus, output, M5201, Rails);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     Channel B after the boost: 0.22 µF and 4k7 onto the bridge clamp, the pre-EQ ladder, IC4b (non-inverting,
    ///     1 + 10k ‖ 100 pF over 22k), 100k on its output.
    /// </summary>
    static StageNetlist PreEqStage() {
        var net = new StageNetlist();
        var (a, clamp, top, plus, middle, lower, low, minus, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node());
        net.Capacitor(net.Input, a, 0.22e-6);
        net.Resistor(a, clamp, 4.7e3);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Rectifier, 3, 3);
        net.Capacitor(clamp, top, 4.7e-9);
        net.Resistor(clamp, low, 4.7e3);
        net.Resistor(top, plus, 10e3);
        net.Resistor(plus, StageNetlist.Ground, 100e3);
        net.Resistor(plus, middle, 10e3);
        net.Capacitor(low, middle, 0.22e-6);
        net.Resistor(middle, lower, 100e3);
        net.Capacitor(low, lower, 0.22e-6);
        net.Resistor(lower, StageNetlist.Ground, 4.7e3);
        net.Resistor(output, minus, 10e3);
        net.Capacitor(output, minus, 100e-12);
        net.Resistor(minus, StageNetlist.Ground, 22e3);
        net.Resistor(output, StageNetlist.Ground, 100e3);
        net.Amplifier(plus, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V1, from IC4's output: 2k2 and 68k to V1a's grid (22 pF to its cathode), 150k plate, 1k5 unbypassed; the V1b
    ///     follower (100k) DC-coupled; 0.1 µF into the stack, whose input returns 220k to V1a's grid; the treble wiper,
    ///     470k into the send buffer's 470k. Out at the buffer's input.
    /// </summary>
    static StageNetlist FirstTube() {
        var net = new StageNetlist();
        var (f, grid, rail, plate, cathode, follower, stack) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, f, 2.2e3);
        net.Resistor(f, grid, 68e3);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 150e3);
        net.Resistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(grid, cathode, 22e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Triode(rail, plate, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Capacitor(follower, stack, 0.1e-6);
        net.Resistor(stack, grid, 220e3);

        var (trebleTop, slope, bassTop, middleTop, send) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(stack, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(stack, slope, 33e3);
        net.Capacitor(slope, bassTop, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Resistor(treble, send, 470e3);
        net.Resistor(send, StageNetlist.Ground, 470e3);
        net.Output = send;
        return net;
    }

    /// <summary>
    ///     The send's 10 µF and the return op-amp IC5a, inverting: the jack board's input resistor (100k, a guess) and
    ///     100k ‖ 47 pF.
    /// </summary>
    static StageNetlist ReturnStage() {
        var net = new StageNetlist();
        var (a, minus, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 10e-6);
        net.Resistor(a, minus, 100e3);
        net.Resistor(minus, output, 100e3);
        net.Capacitor(minus, output, 47e-12);
        net.Amplifier(StageNetlist.Ground, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     V2a: 1M grid leak, 100k plate, 2k2 with 470 Ω + 0.22 µF and the Presence pot (a rheostat, none of it in
    ///     circuit at 10) across it. Out at the plate.
    /// </summary>
    static StageNetlist PresenceStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, a, b) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Resistor(cathode, a, 470);
        net.Capacitor(a, b, 0.22e-6);
        net.Rheostat(b, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Control1, true);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     47 nF from V2a's plate (the reverb drive's 470k + 220k to ground), 2M2 into V2b's grid (220k leak, the
    ///     reverb return's 680k + 220k), 100k plate, 1k2 unbypassed, 0.1 µF out with 470k back to the grid; the master
    ///     stage, 220k into IC8's inverting input with the Master pot ‖ 22 pF as feedback, and the idle section's 220k
    ///     and pot from the same input node to the output.
    /// </summary>
    static StageNetlist MasterStage() {
        var net = new StageNetlist();
        var (a, grid, rail, plate, cathode, m, minus, idle, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node());
        net.Capacitor(net.Input, a, 47e-9);
        net.Resistor(a, StageNetlist.Ground, 690e3);
        net.Resistor(a, grid, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 220e3);
        net.Resistor(grid, StageNetlist.Ground, 900e3);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, m, 0.1e-6);
        net.Resistor(m, grid, 470e3);
        net.Resistor(m, minus, 220e3);
        net.Rheostat(minus, output, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Capacitor(minus, output, 22e-12);
        net.Resistor(m, idle, 220e3);
        net.Rheostat(idle, output, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Capacitor(idle, output, 22e-12);
        net.Amplifier(StageNetlist.Ground, minus, output);
        net.Output = output;
        return net;
    }
}
