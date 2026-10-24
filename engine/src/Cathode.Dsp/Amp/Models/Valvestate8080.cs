using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall Valvestate 8080 (80 W, 1 × 12) and 8100 (100 W head), both channels. The input (10 nF, 1M) feeds two TL072
///     sections. Boost: IC1a (1 + 100k ‖ 100 pF over 4k7 + 47 nF), 220 nF into IC3b (an M5201 switching op-amp,
///     inverting: 100k in, or 100k ‖ 10k in OD2, with 22k from its input to ground in OD1; the 1M log Gain pot as a
///     rheostat ‖ 47 pF as feedback), 220 nF and 2k7 onto two red LEDs to ground, 22k into the ECC83's first triode
///     (1M leak; 100k plate, 1k unbypassed) whose DC-coupled follower (100k) returns 220k + 100 nF to the grid and drives
///     a tone stack (470 pF, 33k, 22 nF, 22 nF, 5 nF from the bass pot's top to ground; 220k linear treble, 1M log bass,
///     22k linear middle with the middle cap on its wiper), 47k into a follower's 470k (IC4), then 4k7 and 220 nF into
///     the Contour network (1 nF, 33k + 47k, 47 nF and 10 nF, the 100k linear Contour pot's wiper grounded between them,
///     22 nF), the 100k log Volume and IC5b (1 + 47k / 4k7). Normal (Switch 1): IC1b (1 + 100k ‖ 47 pF over 22k + 47 nF;
///     Crunch adds 4.7 nF + 4k7 from the 47 nF's foot to ground), 47 nF and 10k into IC2, whose input arm and feedback are
///     the 220k linear Gain
///     pot's two parts with 1 nF and 100 pF (Crunch adds two LEDs each way with 1 nF), 220 Ω into a 4.7 nF / 3k3 /
///     220 nF / 220 nF stack (22k linear treble, 100k log bass, 4k7 linear middle on the wiper, 22 nF from the slope to
///     ground) and IC5a (×2). Both: 10 µF, the effects loop (21k to ground, the send tapped at 1k, the return's IC6a
///     at ×18), IC6b summing the dry and the return (22k, 22k, the mix pot's halves to ground, 22k; 220k), IC8a (220k in,
///     100k), 10 µF into the 10k linear Master, and the power amp: a TL072 driving a class-AB stage of BDV65/BDV64
///     Darlingtons (0.33 Ω emitter resistors) with 33k of voltage feedback over 1k.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawings PC0689.DGM "CCT diagram 8080/8100 Valvestate" issue 5 (preamp, 28/04/94) and
///         PC0689P.DGM issue 9 (power amp and supply, 07/03/94), vector PDFs at schematicheaven
///         (marshallamps/valvestate80_80w_8080_8100_8412.pdf); every value above is read from them.
///     </para>
///     <para>
///         Guessed: the valve's supply (250 V at the 10 µF node; the drawing gives the chain from the autotransformer's
///         H.T. tap, 1k and 33 µF, 47k and 10 µF, but no voltage), the rails (±39 V on the 2200 µF reservoirs rated
///         50 V, which 80 W into 8 Ω needs), the power amp's open-loop gain, bandwidth and idle current, the Contour's
///         direction (10 grounds the 47 nF's end of the pot), the M5201 as a JRC4558. Simplified: the power amp's
///         current feedback (0.33 Ω in the speaker's return through 10 µF and 470 Ω, which raises its output impedance)
///         is left out, so the speaker load does not move its response; its voltage feedback is the 33k over R59's 1k.
///         The loop is wired with nothing plugged in (the send normalled to the return) and its level switch at −10;
///         the mix pot sits at noon; the reverb is off; IC4 is an ideal follower. IC1, IC3b, IC5a, IC6a and IC8a
///         are linear amplifiers (<see cref="StageNetlist.Amplifier" />): IC3b's rail clipping (gain up to 110) would
///         only square a signal its LEDs clamp at 1.8 V just after it (the LEDs' current, and so their voltage, a little
///         higher); the others stay inside their rails. IC2, IC5b and IC6b clip at their rails.
///     </para>
///     <para>
///         Panel: Gain is the playing channel's gain, Bass, Middle and Treble both channels' stacks, Control 1 the
///         Contour and Control 2 the boost Volume, Master the master; Switch 1 selects the Normal channel, Switch 2 the
///         OD2 mode, Switch 3 the Normal channel's Crunch. A solid-state power amp: no tube parameters.
///     </para>
/// </remarks>
public static class Valvestate8080 {
    const double Closed = 10;
    const double Open = 1e12;
    const double Rails = 15;
    const double Valve = 250;

    static readonly OpAmpModel Tl072 = OpAmpModel.TL072;
    static readonly OpAmpModel M5201 = OpAmpModel.JRC4558;

    public static AmpDesign Design { get; } = new(
        "Valvestate 8080",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("IC1a", BoostInput()),
                    new NetworkStageDesign("IC3b", GainStage()),
                    new NetworkStageDesign("V1", Valve12AX7()) { MaxIterations = 64 },
                    new BufferStageDesign("IC4"),
                    new NetworkStageDesign("IC5b", ContourStage())
                ],
                [
                    new NetworkStageDesign("IC1b", NormalInput()),
                    new NetworkStageDesign("IC2", NormalGain()),
                    new NetworkStageDesign("IC5a", NormalStack())
                ]
            ),
            new NetworkStageDesign("IC6", EffectsLoop()),
            new NetworkStageDesign("IC8a", MasterStage())
        ],
        AmpDesign.SolidStateTubePlaceholder,
        -9
    ) {
        SolidState = new(
            "BDV65/BDV64 class AB",
            0.22e-6,
            33e3,
            33e3,
            1e3,
            100e-6,
            1e4,
            2e6,
            39,
            1,
            2200e-6,
            3,
            2,
            0.33,
            0.03,
            8,
            80
        ),
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Control1 = "Contour",
            Control2 = "Volume",
            Switch1 = "Normal",
            Switch2 = "OD2",
            Switch3 = "Crunch",
            HasTubes = false
        }
    };

    /// <summary>The input's 10 nF and 1M, and IC1a: non-inverting, 100k ‖ 100 pF over 4k7 + 47 nF.</summary>
    static StageNetlist BoostInput() {
        var net = new StageNetlist();
        var (plus, minus, leg, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, plus, 10e-9);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Resistor(output, minus, 100e3);
        net.Capacitor(output, minus, 100e-12);
        net.Resistor(minus, leg, 4.7e3);
        net.Capacitor(leg, StageNetlist.Ground, 47e-9);
        net.Amplifier(plus, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     220 nF into IC3b, inverting: 100k to its input, and in OD2 (switch 2) 10k beside it, in OD1 22k from the
    ///     cap to ground instead; the Gain pot as a rheostat ‖ 47 pF as feedback; 220 nF and 2k7 onto the two LEDs to
    ///     ground. Out at the LEDs (V1's grid draws next to nothing from them).
    /// </summary>
    static StageNetlist GainStage() {
        var net = new StageNetlist();
        var (x, minus, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, x, 220e-9);
        net.Resistor(x, minus, 100e3);
        net.SwitchedResistor(x, minus, 0, 10e3, SwitchRole.Switch2);
        net.SwitchedResistor(x, StageNetlist.Ground, 22e3, Open, SwitchRole.Switch2);
        net.Rheostat(minus, output, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(minus, output, 47e-12);
        net.Amplifier(StageNetlist.Ground, minus, output);
        var (a, clamp) = (net.Node(), net.Node());
        net.Capacitor(output, a, 220e-9);
        net.Resistor(a, clamp, 2.7e3);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Led, 1, 1);
        net.Output = clamp;
        return net;
    }

    /// <summary>
    ///     From the LEDs: 22k to V1a's grid (1M leak), 100k plate, 1k unbypassed; V1b's follower (100k)
    ///     with 220k + 100 nF back to V1a's grid, into the stack; the treble wiper's 47k into IC4's 470k. Out at IC4's
    ///     input.
    /// </summary>
    static StageNetlist Valve12AX7() {
        var net = new StageNetlist();
        var (grid, rail, plate, cathode, follower, back) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 22e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Supply(rail, Valve);
        net.Resistor(rail, plate, 100e3);
        net.Resistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Triode(rail, plate, follower, TriodeModel.Ecc83);
        net.Resistor(follower, StageNetlist.Ground, 100e3);
        net.Resistor(follower, back, 220e3);
        net.Capacitor(back, grid, 100e-9);

        var (trebleTop, slope, bassTop, middleTop, output) = (net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(follower, trebleTop, 470e-12, CapacitorRole.Stack);
        net.Resistor(follower, slope, 33e3);
        net.Capacitor(slope, bassTop, 22e-9, CapacitorRole.Stack);
        net.Capacitor(bassTop, StageNetlist.Ground, 5e-9);
        var treble = net.Pot(trebleTop, bassTop, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Resistor(treble, output, 47e3);
        net.Resistor(output, StageNetlist.Ground, 470e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     From IC4: 4k7 and 220 nF into the Contour network, the Volume pot, 100 nF into IC5b (470k to ground;
    ///     1 + 47k / 4k7).
    /// </summary>
    static StageNetlist ContourStage() {
        var net = new StageNetlist();
        var (a, y, z, k, w, j, plus, minus, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node());
        net.Resistor(net.Input, a, 4.7e3);
        net.Capacitor(a, y, 220e-9);
        net.Capacitor(y, z, 1e-9);
        net.Resistor(y, k, 33e3);
        net.Resistor(k, z, 47e3);
        net.Capacitor(y, w, 47e-9);
        net.Capacitor(w, StageNetlist.Ground, 10e-9);
        var wiper = net.Pot(w, j, new(100e3, PotTaper.Linear), PotRole.Control1);
        net.Resistor(wiper, StageNetlist.Ground, Closed);
        net.Capacitor(j, k, 22e-9);
        var volume = net.Pot(z, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Control2);
        net.Capacitor(volume, plus, 100e-9);
        net.Resistor(plus, StageNetlist.Ground, 470e3);
        net.Resistor(output, minus, 47e3);
        net.Resistor(minus, StageNetlist.Ground, 4.7e3);
        net.OpAmp(plus, minus, output, M5201, Rails);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The input's 10 nF and 1M, IC1b: non-inverting, 100k ‖ 47 pF over 22k + 47 nF; Crunch (switch 3) grounds the
    ///     4k7 that 4.7 nF hangs from the 47 nF's foot (470k to ground).
    /// </summary>
    static StageNetlist NormalInput() {
        var net = new StageNetlist();
        var (plus, minus, leg, bright, crunch, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        net.Capacitor(net.Input, plus, 10e-9);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Resistor(output, minus, 100e3);
        net.Capacitor(output, minus, 47e-12);
        net.Capacitor(minus, leg, 47e-9);
        net.Resistor(leg, StageNetlist.Ground, 22e3);
        net.Capacitor(leg, bright, 4.7e-9);
        net.Resistor(bright, StageNetlist.Ground, 470e3);
        net.Resistor(bright, crunch, 4.7e3);
        net.SwitchedResistor(crunch, StageNetlist.Ground, 0, Closed, SwitchRole.Switch3);
        net.Amplifier(plus, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     47 nF and 10k into IC2's input arm, the Gain pot's upper part with 1 nF across it, to the inverting input at its
    ///     wiper; the lower part and 100 pF to the output, and with Crunch the LED pairs and 1 nF.
    /// </summary>
    static StageNetlist NormalGain() {
        var net = new StageNetlist();
        var (a, arm, crunch, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 47e-9);
        net.Resistor(a, arm, 10e3);
        var minus = net.Pot(arm, output, new(220e3, PotTaper.Linear), PotRole.Gain);
        net.Capacitor(arm, minus, 1e-9);
        net.Capacitor(minus, output, 100e-12);
        net.Diodes(minus, crunch, DiodeModel.Led, 1, 1);
        net.Capacitor(minus, crunch, 1e-9);
        net.SwitchedResistor(crunch, output, Open, Closed, SwitchRole.Switch3);
        net.OpAmp(StageNetlist.Ground, minus, output, Tl072, Rails);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     220 Ω into the Normal channel's stack (4.7 nF, 3k3, 220 nF, 220 nF, 22 nF from the slope to ground), 220 nF
    ///     from the treble wiper into IC5a (47k to ground, 1 + 10k / 10k).
    /// </summary>
    static StageNetlist NormalStack() {
        var net = new StageNetlist();
        var (top, trebleTop, slope, bassTop, middleTop, plus, minus, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, top, 220);
        net.Capacitor(top, trebleTop, 4.7e-9, CapacitorRole.Stack);
        net.Resistor(top, slope, 3.3e3);
        net.Capacitor(slope, StageNetlist.Ground, 22e-9, CapacitorRole.Stack);
        net.Capacitor(slope, bassTop, 220e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(22e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(100e3, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(4.7e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 220e-9, CapacitorRole.Stack);
        net.Capacitor(treble, plus, 220e-9);
        net.Resistor(plus, StageNetlist.Ground, 47e3);
        net.Resistor(output, minus, 10e3);
        net.Resistor(minus, StageNetlist.Ground, 10e3);
        net.Amplifier(plus, minus, output);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     10 µF from IC5, the loop-level divider (10k, 10k, 1k) with the send at its 1k, 100 nF into IC6a (100k to
    ///     ground; 1 + 47k / 2k7); IC6b sums the dry signal (22k, half the 22k mix pot to ground, 22k) and the return
    ///     (likewise) into 220k.
    /// </summary>
    static StageNetlist EffectsLoop() {
        var net = new StageNetlist();
        var (p, tap1, tap2, ret, minusA, outA, u, v, sum, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node());
        net.Capacitor(net.Input, p, 10e-6);
        net.Resistor(p, tap1, 10e3);
        net.Resistor(tap1, tap2, 10e3);
        net.Resistor(tap2, StageNetlist.Ground, 1e3);
        net.Capacitor(tap2, ret, 100e-9);
        net.Resistor(ret, StageNetlist.Ground, 100e3);
        net.Resistor(outA, minusA, 47e3);
        net.Resistor(minusA, StageNetlist.Ground, 2.7e3);
        net.Amplifier(ret, minusA, outA);
        net.Resistor(p, u, 22e3);
        net.Resistor(u, StageNetlist.Ground, 11e3);
        net.Resistor(u, sum, 22e3);
        net.Resistor(outA, v, 22e3);
        net.Resistor(v, StageNetlist.Ground, 11e3);
        net.Resistor(v, sum, 22e3);
        net.Resistor(sum, output, 220e3);
        net.OpAmp(StageNetlist.Ground, sum, output, Tl072, Rails);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     220 nF and 220k into IC8a, inverting, 100k feedback (the reverb's 100k from its pot, at zero, to ground; 22k at
    ///     the non-inverting input), 10 µF into the Master pot. Out at its wiper.
    /// </summary>
    static StageNetlist MasterStage() {
        var net = new StageNetlist();
        var (a, minus, plus, output, coupled) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 220e-9);
        net.Resistor(a, minus, 220e3);
        net.Resistor(minus, StageNetlist.Ground, 100e3);
        net.Resistor(minus, output, 100e3);
        net.Resistor(plus, StageNetlist.Ground, 22e3);
        net.Amplifier(plus, minus, output);
        net.Capacitor(output, coupled, 10e-6);
        net.Output = net.Pot(coupled, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Master);
        return net;
    }
}
