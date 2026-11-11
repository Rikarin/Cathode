using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Randall RG100ES (1980s, solid state, 80 W into 8 Ω), both channels through the High input: per channel a JFET
///     gain stage (33k drain, 2.2k source: unbypassed in the green channel, 0.47 µF across it in the red), the 50k
///     audio Gain pot with 5 nF from its top to its wiper (through 22k in the red channel), 1k into a second JFET stage
///     (33k, 2.2k ‖ 10 µF, 20 pF gate to drain) direct-coupled to a JFET source follower (4.7k), 1 µF and the 10k
///     Master; the red channel adds two back-to-back 4 V zeners at the Master's top, 2 nF across its upper half and
///     the Sustain Boost (<see cref="SwitchRole.Switch2" />), two antiparallel 1N914 there. Each wiper has 10k to the
///     shared tone stack (2 nF treble, 4.7k slope, 0.22 µF bass, 0.2 µF middle; 50k linear treble, 50k audio bass, 2k
///     middle), 5 nF into a JFET follower biased by 1M / 1M (10k source), 10 µF and 1k, the Presence (a 2k rheostat and
///     0.1 µF to ground), 2.2k to the effects send, and the power amp: a differential pair, a voltage amplifier and
///     quasi-complementary 2N6254 outputs on ±40 V, its feedback taken from a 0.18 Ω resistor in the speaker's return.
///     The red channel (channel 2, the high-gain one) plays by default; <see cref="SwitchRole.Switch1" /> is the green
///     (channel 1).
/// </summary>
/// <remarks>
///     <para>
///         Source: Randall's schematic PCB 2146B (drawn 3-81, revision A 9-85; RG80-112SC, RG80PH, RG80ES, RG100ES and
///         RG100HT on one sheet) with the RG100ES / RG80ES owner's manual, el34world.com
///         Randall_rg80_100_guit_amp.pdf, read from the 345 dpi scan: every part value above, the TIS58 / 2N5484
///         JFETs, the DC voltages (first stage +15 V drain, +1 V source; second +11.5 V drain, +1.3 V source; first
///         follower +12 V; last follower +14 V gate, +15 V source) and the 1 kHz signal voltages (6 mV in, 40 mV at
///         the green Gain pot, 2.1 V after the second stage and at the Master, 400 mV at the last follower, 250 mV at
///         the send, 20 V at the speaker).
///     </para>
///     <para>
///         The preamp rail is labelled +24 V (1k from +40 V onto a 24 V zener), but the voltages printed on the stages
///         need more: +14 V at the middle of the 1M / 1M divider is 28 V, and the drain and source voltages of the two
///         gain stages agree with 28 to 30 V. The model runs the stages on 28 V. The JFET is one sample fitted inside
///         the 2N5484 datasheet's ranges to those voltages and to the printed stage gains
///         (<see cref="JfetModel.J2N5484" />).
///     </para>
///     <para>
///         The power amp is the library's solid-state stage. Into a resistor, feedback from the speaker's current is
///         the same as feedback of the fraction 0.18 Ω × 330 / (1.5k + 330) / 8 Ω = 1 / 246 of the output voltage; the
///         open-loop gain, 118, is the one that turns the printed 250 mV into the printed 20 V (80×) with that
///         fraction, and it also gives the output impedance of the manual's "damping factor 2-4 at 8 ohms" (3.8 Ω).
///         The feedback resistors here are that equivalent: R45 + R46 (44k, the DC feedback) over 179 Ω, with the
///         corner of C34 against them (1.4 Hz). What a 3.8 Ω source does with a real speaker's impedance (more level at
///         its resonance and in the highs) is not modelled: the stage's load is a resistor.
///     </para>
///     <para>
///         Not modelled: the reverb (its drive taps the treble wiper through 5 nF and 1M; its return's 47k is a load
///         on the send, at ground with the reverb off), the Low input, the footswitch's "Both" position (the idle
///         channel's Master top is grounded, as the footswitch does, and its 10k and Master still load the stack), the
///         effects loop, the RG100HT's signal output. Stand-ins: the zeners are seven silicon junctions each way (4.6 V
///         with a soft knee; the library has no zener). Assumed: Presence and Bass turn their resistance up, Middle
///         moves its wiper to the top. Guessed: the power amp's bandwidth (20 kHz open loop), the rails' source
///         resistance (1.5 Ω), the headroom (2 V), the idle current (30 mA), two junctions a side. The RG80ES is the
///         same sheet with another mains transformer and the same 80 W into 8 Ω, so it has no type of its own.
///     </para>
/// </remarks>
public static class RandallRG100ES {
    const double Rail = 28;
    const int Ground = StageNetlist.Ground;

    static readonly JfetModel Fet = JfetModel.J2N5484;

    public static AmpDesign Design { get; } = new(
        "RG100ES",
        [
            new ChannelSwitchStageDesign("Input", SwitchRole.Switch1, [InputStage(true)], [InputStage(false)]),
            new ChannelSwitchStageDesign("Gain", SwitchRole.Switch1, [GainStage(true)], [GainStage(false)]),
            new ChannelSwitchStageDesign("Tone", SwitchRole.Switch1, [ToneStage(true)], [ToneStage(false)]),
            SendStage()
        ],
        AmpDesign.SolidStateTubePlaceholder,
        -3
    ) {
        SolidState = new(
            "2N6254 class AB",
            0.22e-6,
            48e3,
            44e3,
            44e3 / 245.5,
            0.11 / (44e3 / 245.5),
            118,
            118 * 20e3,
            40,
            1.5,
            6000e-6,
            2,
            2,
            0.27,
            0.03,
            8,
            80
        ) { MidbandGain = 80 },
        Panel = AmpPanel.Standard with {
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Channel 1",
            Switch2 = "Sustain Boost",
            HasTubes = false
        }
    };

    /// <summary>
    ///     Q5 (red) or Q1 (green): the High input's 68k onto both channels' input networks (0.01 µF, 1M and 250 pF
    ///     each; the idle channel's gate draws nothing), the playing one's JFET with 33k to the rail and 2.2k on the
    ///     source, bypassed by 0.47 µF in the red channel. Out at the drain.
    /// </summary>
    static NetworkStageDesign InputStage(bool red) {
        var net = new StageNetlist();
        var (gate, idle, drain, source) = (net.Node(), net.Node(), net.Node(), net.Node());
        foreach (var node in new[] { gate, idle }) {
            net.Capacitor(net.Input, node, 0.01e-6);
            net.Resistor(node, Ground, 1e6);
            net.Capacitor(node, Ground, 250e-12);
        }

        var rail = GainFet(net, drain, gate, source);
        net.Resistor(rail, drain, 33e3);
        if (red) {
            net.Capacitor(source, Ground, 0.47e-6);
        }

        net.Output = drain;
        return new(red ? "Q5" : "Q1", net, 68e3);
    }

    /// <summary>
    ///     Q6 and Q7 (red) or Q2 and Q3 (green): 0.02 µF onto the Gain pot, 5 nF from its top to its wiper (in the red
    ///     channel through 22k), 1k into the second stage's gate, 20 pF from gate to drain, 33k and 2.2k ‖ 10 µF, and
    ///     the follower on its drain with 4.7k on the source. Out at the follower's source.
    /// </summary>
    static NetworkStageDesign GainStage(bool red) {
        var net = new StageNetlist();
        var (top, gate, drain, source, follower) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 0.02e-6);
        var wiper = net.Pot(top, Ground, new(50e3, PotTaper.Audio), PotRole.Gain);
        var bright = top;
        if (red) {
            bright = net.Node();
            net.Resistor(top, bright, 22e3);
        }

        net.Capacitor(bright, wiper, 0.005e-6);
        net.Resistor(wiper, gate, 1e3);
        net.Capacitor(gate, drain, 20e-12);
        var rail = GainFet(net, drain, gate, source);
        net.Resistor(rail, drain, 33e3);
        net.Capacitor(source, Ground, 10e-6);
        net.Jfet(rail, drain, follower, Fet);
        net.Resistor(follower, Ground, 4.7e3);
        net.Output = follower;
        // Full-scale noise at full gain takes the pair up to 45 iterations: the second stage cut off with the
        // follower's gate conducting against the rail.
        return new(red ? "Q6" : "Q2", net) { MaxIterations = 64 };
    }

    /// <summary>A JFET with 2.2k from its source to ground; returns the rail's node, for its drain load.</summary>
    static int GainFet(StageNetlist net, int drain, int gate, int source) {
        var rail = net.Node();
        net.Supply(rail, Rail);
        net.Resistor(source, Ground, 2.2e3);
        net.Jfet(drain, gate, source, Fet);
        return rail;
    }

    /// <summary>
    ///     1 µF onto the playing channel's Master (10k), in the red channel with the zeners, the Sustain Boost's diodes
    ///     and 2 nF across the Master's upper half; 10k from its wiper to the stack, and the idle channel's 10k and
    ///     Master from there to ground; the tone stack. Out at the treble wiper.
    /// </summary>
    static NetworkStageDesign ToneStage(bool red) {
        var net = new StageNetlist();
        var (top, mix, idleTop) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 1e-6);
        var wiper = net.Pot(top, Ground, new(10e3, PotTaper.Linear), PotRole.Master);
        if (red) {
            // D3 and D4, 4 V zeners back to back: one conducts forward while the other breaks down.
            net.Diodes(top, Ground, DiodeModel.Silicon, 7, 7);
            var boost = net.Node();
            net.Diodes(top, boost, DiodeModel.Silicon, 1, 1);
            net.SwitchedResistor(boost, Ground, 0, 1, SwitchRole.Switch2);
            net.Capacitor(top, wiper, 0.002e-6);
        }

        net.Resistor(wiper, mix, 10e3);
        // The idle channel: the footswitch grounds its Master's top.
        net.Resistor(idleTop, Ground, 1);
        net.Resistor(net.Pot(idleTop, Ground, new(10e3, PotTaper.Linear), PotRole.Master), mix, 10e3);

        var (trebleTop, slope, bassTop, middleTop) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(mix, trebleTop, 0.002e-6, CapacitorRole.Stack);
        net.Resistor(mix, slope, 4.7e3);
        net.Capacitor(slope, bassTop, 0.22e-6, CapacitorRole.Stack);
        net.Output = net.Pot(trebleTop, bassTop, new(50e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(50e3, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, Ground, new(2e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 0.2e-6, CapacitorRole.Stack);
        return new("Tone", net);
    }

    /// <summary>
    ///     Q4: 5 nF onto the gate at half the rail (1M / 1M), 10k on the source, 10 µF and 1k, the Presence rheostat
    ///     (2k) and 0.1 µF to ground, 2.2k to the send, and what hangs on it: the reverb return's 47k and the power
    ///     amp's 0.22 µF, 1k and 47k ‖ 500 pF. Out at the send.
    /// </summary>
    static NetworkStageDesign SendStage() {
        var net = new StageNetlist();
        var (rail, gate, source, coupled, tap, shunt) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (send, input, baseNode) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, gate, 0.005e-6);
        net.Supply(rail, Rail);
        net.Resistor(rail, gate, 1e6);
        net.Resistor(gate, Ground, 1e6);
        net.Jfet(rail, gate, source, Fet);
        net.Resistor(source, Ground, 10e3);
        net.Capacitor(source, coupled, 10e-6);
        net.Resistor(coupled, tap, 1e3);
        net.Rheostat(tap, shunt, new(2e3, PotTaper.Linear), PotRole.Presence);
        net.Capacitor(shunt, Ground, 0.1e-6);
        net.Resistor(tap, send, 2.2e3);
        net.Resistor(send, Ground, 47e3);
        net.Capacitor(send, input, 0.22e-6);
        net.Resistor(input, baseNode, 1e3);
        net.Resistor(baseNode, Ground, 47e3);
        net.Capacitor(baseNode, Ground, 500e-12);
        net.Output = send;
        return new("Q4", net);
    }
}
