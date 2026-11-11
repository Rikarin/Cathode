using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Gallien-Krueger 800RB (1982 boards, 300 W + 100 W bass head, solid state), in full-range mode into its 300 W
///     amp. The input's 12k and 0.1 µF into U1a (non-inverting, 33k ‖ 560 pF over 3.3k: 11×; the −10 dB switch puts
///     4.7k across the 33k, Lo Cut 0.22 µF in series with the 3.3k); the 25k audio Volume with the Hi Boost's 22 nF +
///     3.3k from U1a to its wiper; 3.3 µF into U2a (33k to ground; 22k ‖ 560 pF over 4.7k: 5.7×), straight or through
///     the Mid Contour filter (U1b: 12k, 120k and a 4.7 nF / 4.7 nF / 33k bridged T against 220k ‖ 560 pF); the Hi-Mid
///     (U2b, 4.7 nF) and Lo-Mid (U3a, 22 nF) stages, each an inverting stage of 220k / 220k with a 25k pot between 4.7k
///     + 22k from its input and 4.7k from its output, a cap across and a cap from the wiper to the inverting input;
///     Bass and Treble around U3b (bass: 3.3k + 12k, 25k pot, 3.3k, 0.22 µF across, 22k from the wiper; treble: 3.3k,
///     25k pot, 3.3k, 4.7 nF from the wiper); 0.22 µF and 4.7k to the effects send, 0.22 µF and 12k from the return
///     into the 25k Boost pot over 3.3k; the J113 boost stage (4.7k drain load from +15 V through 1k and 10 µF, 12k
///     from the source to −15 V, bypassed by 10 µF + 100 Ω); 0.22 µF onto the two 25k masters; and the 300 W power amp:
///     an LF353 error amplifier into a common-base level shifter, a PNP voltage amplifier with a bootstrapped load and,
///     behind a driver each side, three pairs of MJ15022 / MJ15023 with 0.33 Ω emitter resistors on ±85 V, its feedback
///     56k over 12k ‖ (1k + 10 µF): 62×.
/// </summary>
/// <remarks>
///     <para>
///         Source: Gallien-Krueger's 800RB service manual (el34world.com/charts/Schematics/files/Gallien_krueger/
///         Gallien_krueger_800rb_sm1.pdf, 21 pages, scanned at 635 dpi and read at full resolution): the hand-drawn
///         preamp 60045 A (Bob Gallien, 10-18-82), power amps 60044 A (11-5-82) and supply 60048 A (11-9-82). Every
///         value above is read from them, with the preamp's signal voltages for 2 mV at 200 Hz, every control at 10
///         and the filters out (19 mV after U1a, 13 mV after the contour filter, 107 mV after U2a, 115 mV after each
///         mid stage, 150 mV after U3b, 93 mV on the Boost pot and 10 mV at its foot, 0.67 V into the power amps), the
///         FET's voltages (drain +6.4 V, source +1.5 V, +13 V after the 1k), the power amp's (0.65 V at its input,
///         41 V at the speaker jack unloaded) and the 3 × 1000 µF on each ±85 V rail. The manual's own pages give the
///         bands (treble 4 kHz, high mid 1 kHz, low mid 250 Hz, bass 60 Hz), the Mid Contour ("a notch at about 500
///         Hz"), the Boost (15 dB) and the ratings (300 W into 4 Ω, 200 W into 8 Ω). The later preamps in the same
///         manual (406-0045-C of 1991, -D/E of 1995) have the same stages with other values; they are not modelled.
///     </para>
///     <para>
///         The mid controls, as drawn: the 22k sits in series with the pot on the input's side only, so 10 is within
///         1 dB of flat and the rest of the travel cuts (about −6 dB at 5, −16 dB at 0). The factory voltages agree
///         (115 mV before and after the Lo-Mid stage with it at 10), though the manual calls 5 flat. The bass pot has
///         its 12k the same way (about +5 dB at 10, −5 dB at 5 below 30 Hz). The tone, Boost and master pots are
///         taken as linear: the drawing marks only the Volume "A", and the 1995 drawing marks the others "B".
///     </para>
///     <para>
///         The J113 (<see cref="Fet" />) is fitted to the drawing: 1.4 mA at −1.5 V gate to source (the source's
///         +1.5 V over 12k to −15 V) and the gain that takes the Boost pot's 95 mV to 0.67 V on the loaded masters.
///         Its drain then rests at 7.2 V where the drawing has 6.4 V; the drawing's own figures do not agree better
///         than that (13 V after 1k from 15 V is 2 mA, the 4.7k from there to 6.4 V carries 1.4 mA).
///         The LF353s run as the library's TL072 (the same class of JFET-input dual).
///     </para>
///     <para>
///         Panel: Volume is the gain, Treble, Hi Mid (Middle), Lo Mid (Control 1) and Bass the four bands, Boost
///         Control 2, Master the 300 W amp's master (Lo Master on the amp); Bright is Hi Boost, Switch 1 the Mid
///         Contour, Switch 2 Lo Cut, Switch 3 the −10 dB input attenuator. A solid-state amp: none of the tube
///         parameters; Sag, Variac and B+ Time Const act on the ±85 V rails.
///     </para>
///     <para>
///         Not modelled: the bi-amp crossover (two LM3080 integrators around U6 and U9) and the 100 W amp, which in
///         full-range mode carries the same signal into a second cabinet: its master sits at 10 as a fixed load on
///         the FET; the crossover's 120k input is left off. The Boost footswitch (a TL604 analogue switch between the
///         pot's wiper and its foot) is a wire to the wiper. The 1N759 clamps at the input and on the Boost pot
///         (12 V zeners back to back, beyond what the stages before them swing), the direct output, the power amp's
///         current limiters and its output Zobel. Simplified: U1b (the contour filter), U2b and U3a (the mid
///         stages) are linear amplifiers (<see cref="StageNetlist.Amplifier" />). The mid stages have at most 1 dB
///         of gain, so they reach their rails only on a signal U2a has already clipped; the filter's 1.7× goes on
///         into U2a's 5.7×, which clips first. U1a, U2a and U3b clip at their rails. The power amp's feedback shunt
///         is 923 Ω (12k ‖ 1k) through the 10 µF, so its gain falls to 1 at DC where the amp's falls to 5.7 (−1 dB
///         at 31 Hz either way).
///         Guessed: the power amp's open-loop gain (80 dB), gain-bandwidth (2 MHz) and headroom (4 V), and the
///         rails' source resistance (8.2 Ω, which puts the onset of clipping at the manual's 300 W into 4 Ω). The idle
///         current is the drawing's 10 mV across two 0.33 Ω, three pairs: 45 mA.
///     </para>
/// </remarks>
public static class Gk800Rb {
    const double Open = 1e12;
    const double Wire = 1;
    const double Rails = 15;

    static readonly OpAmpModel Lf353 = OpAmpModel.TL072;

    /// <summary>The J113 as the drawing's voltages have it: V_TO −2.4 V, β 1.7 mA/V² (see the remarks).</summary>
    static readonly JfetModel Fet = JfetModel.FromBeta(-2.4, 1.7e-3);

    static readonly Pot Tone = new(25e3, PotTaper.Linear);

    public static AmpDesign Design { get; } = new(
        "GK 800RB",
        [InputStage(), VoicingStage(), MidStage(), ToneStage(), BoostStage()],
        AmpDesign.SolidStateTubePlaceholder,
        -3
    ) {
        SolidState = new(
            "300 W class AB",
            0.22e-6,
            100e3,
            56e3,
            923,
            10e-6,
            1e4,
            2e6,
            85,
            8.2,
            3000e-6,
            4,
            2,
            0.11,
            0.045,
            4,
            300
        ),
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Middle = "Hi Mid",
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = "Hi Boost",
            Control1 = "Lo Mid",
            Control2 = "Boost",
            Switch1 = "Contour",
            Switch2 = "Lo Cut",
            Switch3 = "-10 dB",
            HasTubes = false
        }
    };

    /// <summary>
    ///     The input's 12k (the source resistance), 0.1 µF, 1M and 100 pF into U1a: 33k ‖ 560 pF over 10 µF and 3.3k,
    ///     with 4.7k across the 33k for −10 dB (<see cref="SwitchRole.Switch3" />) and 0.22 µF in series with the
    ///     3.3k for Lo Cut (<see cref="SwitchRole.Switch2" />, which otherwise shorts it).
    /// </summary>
    static NetworkStageDesign InputStage() {
        var net = new StageNetlist();
        var (plus, minus, leg, foot, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, plus, 0.1e-6);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        net.Capacitor(plus, StageNetlist.Ground, 100e-12);
        net.Resistor(output, minus, 33e3);
        net.Capacitor(output, minus, 560e-12);
        net.SwitchedResistor(output, minus, Open, 4.7e3, SwitchRole.Switch3);
        net.Capacitor(minus, leg, 10e-6);
        net.Capacitor(leg, foot, 0.22e-6);
        net.SwitchedResistor(leg, foot, Wire, Open, SwitchRole.Switch2);
        net.Resistor(foot, StageNetlist.Ground, 3.3e3);
        net.OpAmp(plus, minus, output, Lf353, Rails);
        net.Output = output;
        return new("U1a", net, 12e3);
    }

    /// <summary>
    ///     The Volume (25k audio) with the Hi Boost's 22 nF and 3.3k from its top to its wiper
    ///     (<see cref="SwitchRole.Bright" />; 220k across the switch), 3.3 µF, and U2a (33k to ground, 22k ‖ 560 pF
    ///     over 4.7k), fed straight or, with the Mid Contour in (<see cref="SwitchRole.Switch1" />), from the contour
    ///     filter (<see cref="ContourFilter" />), which hangs on the 3.3 µF either way.
    /// </summary>
    static NetworkStageDesign VoicingStage() {
        var net = new StageNetlist();
        var (boost, switched, coupled) = (net.Node(), net.Node(), net.Node());
        var volume = net.Pot(net.Input, StageNetlist.Ground, new(25e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(net.Input, boost, 22e-9);
        net.Resistor(boost, switched, 220e3);
        net.SwitchedResistor(boost, switched, Open, Wire, SwitchRole.Bright);
        net.Resistor(switched, volume, 3.3e3);
        net.Capacitor(volume, coupled, 3.3e-6);
        var contour = ContourFilter(net, coupled);

        var (plus, minus, output) = (net.Node(), net.Node(), net.Node());
        net.SwitchedResistor(coupled, plus, Wire, Open, SwitchRole.Switch1);
        net.SwitchedResistor(contour, plus, Open, Wire, SwitchRole.Switch1);
        net.Resistor(plus, StageNetlist.Ground, 33e3);
        net.Resistor(output, minus, 22e3);
        net.Capacitor(output, minus, 560e-12);
        net.Resistor(minus, StageNetlist.Ground, 4.7e3);
        net.OpAmp(plus, minus, output, Lf353, Rails);
        net.Output = output;
        return new("U2a", net);
    }

    /// <summary>
    ///     The Mid Contour filter from <paramref name="input" />: 12k, then 120k to U1b's inverting input beside
    ///     4.7 nF and 4.7 nF, 33k from between the caps to its output, 220k ‖ 560 pF around it; 0.22 µF and 220k to
    ///     ground after it. Returns the node after the 0.22 µF. U1b is a linear amplifier (see the remarks).
    /// </summary>
    static int ContourFilter(StageNetlist net, int input) {
        var a = net.Node();
        net.Resistor(input, a, 12e3);
        var (b, inverting) = (net.Node(), net.Node());
        net.Capacitor(a, b, 4.7e-9);
        net.Capacitor(b, inverting, 4.7e-9);
        net.Resistor(a, inverting, 120e3);
        var filtered = net.Node();
        net.Resistor(b, filtered, 33e3);
        net.Resistor(inverting, filtered, 220e3);
        net.Capacitor(inverting, filtered, 560e-12);
        net.Amplifier(StageNetlist.Ground, inverting, filtered);
        var contour = net.Node();
        net.Capacitor(filtered, contour, 0.22e-6);
        net.Resistor(contour, StageNetlist.Ground, 220e3);
        return contour;
    }

    /// <summary>Hi-Mid (U2b, 4.7 nF, <see cref="PotRole.Middle" />), 0.22 µF, Lo-Mid (U3a, 22 nF, Control 1).</summary>
    static NetworkStageDesign MidStage() {
        var net = new StageNetlist();
        var coupled = net.Node();
        var high = MidBand(net, net.Input, 4.7e-9, PotRole.Middle);
        net.Capacitor(high, coupled, 0.22e-6);
        net.Output = MidBand(net, coupled, 22e-9, PotRole.Control1);
        return new("Mids", net);
    }

    /// <summary>
    ///     One mid stage from <paramref name="input" />: 220k to the inverting input and 220k back from the output;
    ///     4.7k and 22k from the input to the pot's top, 4.7k from its bottom to the output, <paramref name="cap" />
    ///     across the 22k and the pot, and <paramref name="cap" /> from the wiper to the inverting input. Returns the
    ///     output. The op-amp is a linear amplifier (see the remarks).
    /// </summary>
    static int MidBand(StageNetlist net, int input, double cap, PotRole role) {
        var (left, top, right, inverting, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(input, left, 4.7e3);
        net.Resistor(left, top, 22e3);
        var wiper = net.Pot(top, right, Tone, role);
        net.Resistor(right, output, 4.7e3);
        net.Capacitor(left, right, cap);
        net.Capacitor(wiper, inverting, cap);
        net.Resistor(input, inverting, 220e3);
        net.Resistor(inverting, output, 220e3);
        net.Amplifier(StageNetlist.Ground, inverting, output);
        return output;
    }

    /// <summary>
    ///     Bass and Treble around U3b. Bass: 3.3k and 12k from the input to the pot's top, 3.3k from its bottom to the
    ///     output, 0.22 µF across the 12k and the pot, 22k from the wiper to the inverting input. Treble: 3.3k to
    ///     each end of its pot, 4.7 nF from the wiper to the inverting input.
    /// </summary>
    static NetworkStageDesign ToneStage() {
        var net = new StageNetlist();
        var (left, top, right, inverting, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, left, 3.3e3);
        net.Resistor(left, top, 12e3);
        var bass = net.Pot(top, right, Tone, PotRole.Bass);
        net.Resistor(right, output, 3.3e3);
        net.Capacitor(left, right, 0.22e-6);
        net.Resistor(bass, inverting, 22e3);

        var (trebleTop, trebleBottom) = (net.Node(), net.Node());
        net.Resistor(net.Input, trebleTop, 3.3e3);
        var treble = net.Pot(trebleTop, trebleBottom, Tone, PotRole.Treble);
        net.Resistor(trebleBottom, output, 3.3e3);
        net.Capacitor(treble, inverting, 4.7e-9);
        net.OpAmp(StageNetlist.Ground, inverting, output, Lf353, Rails);
        net.Output = output;
        return new("U3b", net);
    }

    /// <summary>
    ///     The Boost pot's wiper (<see cref="BoostPot" />) on the J113's gate (220k to ground); the FET between 4.7k
    ///     from the decoupled +15 V (1k, 10 µF) and 12k to −15 V, its source bypassed by 10 µF + 100 Ω; 0.22 µF from
    ///     the drain onto the masters: the 100 W amp's at 10 (25k, with that amp's 0.22 µF and 100k), and the 300 W
    ///     amp's (<see cref="PotRole.Master" />), its wiper loaded by that amp's 100k. Out at the wiper; the 0.22 µF
    ///     after it is the power amp's input cap.
    /// </summary>
    static NetworkStageDesign BoostStage() {
        var net = new StageNetlist();
        var gate = BoostPot(net);
        net.Resistor(gate, StageNetlist.Ground, 220e3);

        var (positive, decoupled, drain) = (net.Node(), net.Node(), net.Node());
        net.Supply(positive, Rails);
        net.Resistor(positive, decoupled, 1e3);
        net.Capacitor(decoupled, StageNetlist.Ground, 10e-6);
        net.Resistor(decoupled, drain, 4.7e3);
        var (negative, source, bypass) = (net.Node(), net.Node(), net.Node());
        net.Supply(negative, -Rails);
        net.Resistor(negative, source, 12e3);
        net.Capacitor(source, bypass, 10e-6);
        net.Resistor(bypass, StageNetlist.Ground, 100);
        net.Jfet(drain, gate, source, Fet);

        var (line, high) = (net.Node(), net.Node());
        net.Capacitor(drain, line, 0.22e-6);
        net.Resistor(line, StageNetlist.Ground, 25e3);
        net.Capacitor(line, high, 0.22e-6);
        net.Resistor(high, StageNetlist.Ground, 100e3);
        var master = net.Pot(line, StageNetlist.Ground, Tone, PotRole.Master);
        net.Resistor(master, StageNetlist.Ground, 100e3);
        net.Output = master;
        return new("Q1", net);
    }

    /// <summary>
    ///     From the stage's input: 0.22 µF and 4.7k to the send, 0.22 µF and 12k from the return (normalled to it),
    ///     and the Boost pot (25k, <see cref="PotRole.Control2" />) over 3.3k. Returns the wiper.
    /// </summary>
    static int BoostPot(StageNetlist net) {
        var send = net.Node();
        net.Capacitor(net.Input, send, 0.22e-6);
        var (loop, back) = (net.Node(), net.Node());
        net.Resistor(send, loop, 4.7e3);
        net.Capacitor(loop, back, 0.22e-6);
        var (top, foot) = (net.Node(), net.Node());
        net.Resistor(back, top, 12e3);
        net.Resistor(foot, StageNetlist.Ground, 3.3e3);
        return net.Pot(top, foot, Tone, PotRole.Control2);
    }
}
