using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Roland JC-120 Jazz Chorus (1984 board, 2 × 60 W, solid state), the amp path of its effect channel with the
///     effects off: the High input's 33k into IC2b (non-inverting, 100k / 39k, 3.6×), IC2a (47k over 1k + 10 µF + the
///     Distortion pot's 50k, 1.9× with the distortion off), with the distortion on (<see cref="SwitchRole.Switch1" />)
///     the Distortion pot (<see cref="PotRole.Control2" />, to 48×) and the antiparallel diodes after the two 3.9k, a
///     Fender-type tone stack (150 pF, 100k slope, 82 nF bass, 47 nF mid; treble 250k linear, bass 250k audio, middle
///     10k linear, with a 1k between the bass and middle pots), 100k into the 1M linear volume with the Hi-Treble pot
///     (1M linear in series with 470 pF from the treble wiper to the volume wiper), IC3a (270k / 22k, 13×), the 33k /
///     270k pad and the IC3b follower (an ideal buffer here), the IC1a mixer (47k against the silent reverb return's
///     100k, then 100k / 33k), and one channel of the discrete power amp: a differential pair and voltage amplifier
///     with 27k / 1k feedback (28×, 1k through 10 µF), Darlington outputs with 0.33 Ω emitter resistors on ±40 V rails.
/// </summary>
/// <remarks>
///     <para>
///         Source: Roland's JC-120 / JC-160 circuit diagram dated December 1984 (preamp board PM 70126811, power amp
///         70126840 / 850), schematicheaven.net/newamps/roland_jazz_chorus.pdf, read at full resolution: every value
///         above, the M5218 op-amps (4558 type) on ±15 V, the rails (+40.0 V / −40.0 V idle, 33.5 V loaded), the
///         3300 µF reservoirs, and its signal-voltage annotations (21 mV in, 72 mV after IC2b, 130 mV after IC2a, 710
///         mV after IC3a with every tone control full up, 290 mV after IC3b, 800 mV into the power amp, 22 V out),
///         which the stage gains reproduce.
///     </para>
///     <para>
///         The distortion: off (the default, and how the factory voltages are measured), the pot's whole 50k sits in
///         IC2a's ground leg and the diodes are out of circuit, so the Distortion knob does nothing; on, the knob turns
///         the 50k down (a linear track assumed: the drawing gives only its value) and a silicon pair (4148 type)
///         clamps the node after the two 3.9k to about ±0.6 V before the stack. The switch stands for the footswitch's
///         FET switching, as two channels of the same stages.
///     </para>
///     <para>
///         Not modelled: the chorus and vibrato (the Chorus block), the reverb (its return is silent), the input clamp
///         diodes, the mute FET, the second power amp (with the chorus off both carry the same signal), the
///         normal channel (Bright switch, no Hi-Treble). Guessed: the power amp's open-loop gain (80 dB) and
///         gain-bandwidth (2 MHz), the rails' source resistance (4 Ω, from the idle and loaded rail voltages at about
///         1.2 A of mean draw), the idle current (30 mA) and the output stage's two junctions a side.
///     </para>
///     <para>
///         The panel is honest about what a solid-state amp lacks: no presence, depth, cut or master, and none of the
///         tube expert parameters (<see cref="AmpPanel.HasTubes" />); Sag, Variac and B+ Time Const act on its rails.
///     </para>
/// </remarks>
public static class JazzChorus120 {
    const double Rails = 15;

    static readonly OpAmpModel M5218 = OpAmpModel.JRC4558;

    public static AmpDesign Design { get; } = new(
        "JC-120",
        [
            InputStage(),
            new ChannelSwitchStageDesign("IC2a", SwitchRole.Switch1, [DriveStage(false)], [DriveStage(true)]),
            new ChannelSwitchStageDesign("Tone", SwitchRole.Switch1, [ToneStage(false)], [ToneStage(true)]),
            GainStage(),
            PadStage(),
            new BufferStageDesign("IC3b"),
            MixStage()
        ],
        AmpDesign.SolidStateTubePlaceholder,
        -3
    ) {
        SolidState = new(
            "2 x 60 W class AB",
            0.082e-6,
            56e3,
            27e3,
            1e3,
            10e-6,
            1e4,
            2e6,
            40,
            4,
            3300e-6,
            2.5,
            2,
            0.33,
            0.03,
            8,
            60
        ),
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Control1 = "Hi Treble",
            Control2 = "Distortion",
            Switch1 = "Distortion",
            HasTubes = false
        }
    };

    /// <summary>
    ///     The High input's 33k, 0.082 µF and 1k into IC2b's non-inverting input (1M to ground, 100 pF), its 100k ‖ 47
    ///     pF feedback over 39k.
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (coupled, plus, minus, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.082e-6);
        net.Resistor(coupled, plus, 1e3);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Capacitor(plus, StageNetlist.Ground, 100e-12);
        net.Resistor(output, minus, 100e3);
        net.Capacitor(output, minus, 47e-12);
        net.Resistor(minus, StageNetlist.Ground, 39e3);
        net.OpAmp(plus, minus, output, M5218, Rails);
        net.Output = output;
        return new("IC2b", net, 33e3);
    }

    /// <summary>
    ///     0.082 µF and 1M into IC2a, its 47k ‖ 100 pF feedback over 1k, 10 µF and the distortion pot's 50k: all of it
    ///     in circuit with the distortion off (1.9×), and with it on (<paramref name="distortion" />) a rheostat on
    ///     <see cref="PotRole.Control2" /> that turning up takes out, to 48× at 10.
    /// </summary>
    static NetworkStageDesign DriveStage(bool distortion) {
        var net = new StageNetlist();
        var (plus, minus, leg, pot, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, plus, 0.082e-6);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Resistor(output, minus, 47e3);
        net.Capacitor(output, minus, 100e-12);
        net.Resistor(minus, leg, 1e3);
        net.Capacitor(leg, pot, 10e-6);
        if (distortion) {
            net.Rheostat(pot, StageNetlist.Ground, new(50e3, PotTaper.Linear), PotRole.Control2, falling: true);
        } else {
            net.Resistor(pot, StageNetlist.Ground, 50e3);
        }

        net.OpAmp(plus, minus, output, M5218, Rails);
        net.Output = output;
        return new("IC2a", net);
    }

    /// <summary>
    ///     0.082 µF out of IC2a (270k to ground), the distortion's two 3.9k with, when it is on
    ///     (<paramref name="distortion" />), its antiparallel silicon diodes to ground after them, 0.01 µF + 1.5M to
    ///     ground, the tone stack, 100k into the volume, the Hi-Treble path, and 0.082 µF into IC3a's input with its 1M
    ///     to ground. Out at IC3a's non-inverting input.
    /// </summary>
    static NetworkStageDesign ToneStage(bool distortion) {
        var net = new StageNetlist();
        var (a, b, top, shunt, k) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, a, 0.082e-6);
        net.Resistor(a, StageNetlist.Ground, 270e3);
        net.Resistor(a, b, 3.9e3);
        net.Resistor(b, top, 3.9e3);
        if (distortion) {
            net.Diodes(top, StageNetlist.Ground, DiodeModel.Silicon, 1, 1);
        }

        net.Capacitor(top, shunt, 0.01e-6);
        net.Resistor(shunt, StageNetlist.Ground, 1.5e6);

        var (trebleTop, bassTop, bassBottom, middleTop) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(top, trebleTop, 150e-12, CapacitorRole.Stack);
        net.Resistor(top, k, 100e3);
        net.Capacitor(k, bassTop, 82e-9, CapacitorRole.Stack);
        net.Capacitor(k, middleTop, 47e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, bassBottom, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(bassBottom, middleTop, 1e3);
        net.Rheostat(middleTop, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Middle);

        var (volumeTop, bright, dead, input) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(treble, volumeTop, 100e3);
        var volume = net.Pot(volumeTop, StageNetlist.Ground, new(1e6, PotTaper.Linear), PotRole.Gain);
        // Hi-Treble: a 1M pot as a variable resistor, none of it in circuit at 10.
        net.Capacitor(treble, bright, 470e-12);
        var hiTreble = net.Pot(bright, dead, new(1e6, PotTaper.Linear), PotRole.Control1);
        net.Resistor(hiTreble, volume, 1);
        net.Capacitor(volume, input, 0.082e-6);
        net.Resistor(input, StageNetlist.Ground, 1e6);
        net.Output = input;
        return new("Tone", net);
    }

    /// <summary>IC3a: non-inverting, 270k ‖ 100 pF over 22k.</summary>
    static NetworkStageDesign GainStage() {
        var net = new StageNetlist();
        var (minus, output) = (net.Node(), net.Node());
        net.Resistor(output, minus, 270e3);
        net.Capacitor(output, minus, 100e-12);
        net.Resistor(minus, StageNetlist.Ground, 22e3);
        net.OpAmp(net.Input, minus, output, M5218, Rails);
        net.Output = output;
        return new("IC3a", net);
    }

    /// <summary>
    ///     1 µF and 270k, the 33k / 270k pad, 1 µF and 27k into IC3b. Out at IC3b's input, which draws nothing: IC3b is
    ///     a unity follower (22k ‖ 47 pF in its feedback) that its input, a few volts at most, cannot clip, so it runs
    ///     as an ideal buffer (<see cref="BufferStageDesign" />).
    /// </summary>
    static NetworkStageDesign PadStage() {
        var net = new StageNetlist();
        var (c, d, e) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, c, 1e-6);
        net.Resistor(c, StageNetlist.Ground, 270e3);
        net.Resistor(c, d, 33e3);
        net.Resistor(d, StageNetlist.Ground, 270e3);
        net.Capacitor(d, e, 1e-6);
        net.Resistor(e, StageNetlist.Ground, 27e3);
        net.Output = e;
        return new("Pad", net);
    }

    /// <summary>
    ///     1 µF and 47k from IC3b into IC1a's non-inverting input (the reverb return's 100k to ground beside it), its
    ///     100k ‖ 47 pF feedback over 33k.
    /// </summary>
    static NetworkStageDesign MixStage() {
        var net = new StageNetlist();
        var (f, plus, minus, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, f, 1e-6);
        net.Resistor(f, plus, 47e3);
        net.Resistor(plus, StageNetlist.Ground, 100e3);
        net.Resistor(output, minus, 100e3);
        net.Capacitor(output, minus, 47e-12);
        net.Resistor(minus, StageNetlist.Ground, 33e3);
        net.OpAmp(plus, minus, output, M5218, Rails);
        net.Output = output;
        return new("IC1a", net);
    }
}
