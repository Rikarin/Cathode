using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JMP-1 MIDI valve preamp, its Clean I/II and OD I/II channels with Bass Shift, into a generic 100 W EL34
///     power amp. The input (10k, 100 pF, 100 nF, 1M) feeds IC34, an M5201 switching op-amp whose sections share one
///     output: OD, non-inverting 1 + 82k ‖ 470 pF over 15k + 22 nF (Bass Shift: 68 nF + 220k in parallel); Clean, 1 +
///     56k ‖ 100 pF over 27k + 33 nF (Clean II: 15k in parallel with the 27k). 1 µF into the Gain, two cascaded sections
///     of a TC9176 digital attenuator with a TL072 follower between them (220 nF, 220k; 10 µF). Clean: 220 nF, 1k into
///     VB1a (100k, 1k5 + 1k5 ‖ 2.2 µF, 680k grid leak), whose plate drives a voicing network (100k to a node that 47 nF
///     and 100 nF split: 47 nF onto 6k8 to ground, 100 nF through 82k to that 6k8's top and through 82k on toward the
///     grid; 220 pF + 220k ‖ 330k from the plate beside it; Bass Shift 22k to ground at the 100 nF), 680k ‖ 100 pF into
///     VB1b (1M leak; 100k, 2k7 ‖ 2.2 µF), 47 nF, 680k over 82k. OD: a TL072 follower, 47 nF (68k), 5k1 into an inverting
///     TL072 with 1M ‖ 22 pF, 5k6 and 220 nF onto a W005F bridge with a 1N4007 across it (three junctions each way to
///     ground), 1M into VB2a (220k, 2k2 unbypassed) DC-coupled to the VB2b follower (100k), 47 nF, 330k over 680k, then
///     OD I's 680k over 82k or OD II's voicing network (470 pF + 100k beside 33k, 22 nF, 22 nF, 100k, 36k, 3k9 ‖ 2k2, into
///     220k over 330k), chosen by 4053 analogue switches. Both through IC18 (M5201 followers) into the active tone
///     circuit: an inverting input stage (10k in, 10k ‖ 47 pF) and an inverting output stage (10k, 10k), each band filter
///     driven from the input stage and returned through 2k7 or 6k8 to a TC9170 pot's wiper, whose ends go to the two
///     stages' summing nodes (Bass: a 156 Hz Sallen-Key low-pass; Middle: a Sallen-Key band-pass of gain 2; Presence:
///     4.7 nF networks round a difference stage and an inverter; Treble: a Sallen-Key high-pass); then 100 nF (100k),
///     IC17 (OD: 1 + 47k ‖ 1 nF over 10k + 2.2 µF; Clean: 1 + 33k ‖ 100 pF over 15k + 10 µF), the Volume (a second
///     TC9176, cascaded as the Gain), 10 µF, the output section's inverting mixer (two 33k + 33k paths into 47k, the
///     effects-mix TC9170's halves shunting their midpoints), 10 µF, the 10k Output level pot, 10k into IC2a (1 + 10k ‖
///     470 pF over 4k7), 10 µF and 470 Ω.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawing JMP1-61-04 issue 10 (2001/02), six vector sheets on DrTube
///         (drtube.com/schematics/marshall/jmp1-61-04.pdf): channel circuit (sheet 1), power supply (2), active tone (3),
///         output (4), digital (5), front panel (6); every value above is read from them. The switching (which M5201
///         section and 4053 channel each mode selects, and the JFETs' states) follows the drive lines' names on sheet 1
///         (CL/OD, ODII, CLII, B_SHIFT, the BC184 inverters that pull the J174 gates low to switch them on); IC34a, IC18a
///         and IC17a are taken as the OD sections, as IC18b is wired to the clean valve's output.
///     </para>
///     <para>
///         Guessed: the digital pots' resistances (TC9176 and TC9170 taken as 10k: the drawing names the parts but not
///         their values; each TC9176 section as a linear pot at the knob's position, so that the cascaded pair follows a
///         square law, about −12 dB at the centre (two audio-taper sections would put the centre near −40 dB); the
///         TC9170 bands as linear pots, centre flat), the HT (230 V after the 47k dropper; the transformer's HT winding voltage is not given), the effects mix
///         (its pot at the centre), the output level switch at +4 (the 10k / 2k7 pad out). Simplified: every op-amp but
///         the OD channel's inverting stage (which swings to its ±15 V rails into the clamp) as a linear amplifier; the
///         effects loop and speaker emulator outputs left out; the 4053s and JFETs as ideal switches.
///     </para>
///     <para>
///         Power amp: the JMP-1 has none. This model feeds its line output into the JCM900 4100's documented 100 W
///         section (Marshall 4100-60-02 issue 11: four EL34, 82k / 100k long-tail pair, 100k of feedback; see
///         <see cref="JCM900" />), as a Marshall 9100 / 9200-style EL34 power amp would be.
///     </para>
///     <para>
///         Panel: Switch 1 selects the OD channels, Switch 2 Clean II or OD II, Switch 3 Bass Shift; Gain, Bass, Middle
///         and Treble are the preamp's; Control 2 the preamp's Presence band; Master the channel Volume; Control 1 the
///         Output level. The power amp's presence and depth are hidden.
///     </para>
/// </remarks>
public static class Jmp1 {
    const double Closed = 10;
    const double Ht = 230;
    const double DigitalPot = 10e3;
    const double Rails = 15;

    public static AmpDesign Design { get; } = new(
        "JMP-1",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [
                    new NetworkStageDesign("Clean input", CleanInput()),
                    new NetworkStageDesign("VB1a", CleanFirst()),
                    new NetworkStageDesign("VB1b", CleanSecond())
                ],
                [
                    new NetworkStageDesign("OD input", OdInput()),
                    new NetworkStageDesign("VB2a", OdTriode()),
                    new NetworkStageDesign("VB2b", OdFollower())
                ]
            ),
            new NetworkStageDesign("Active tone", ActiveTone()),
            new NetworkStageDesign("Output", Output())
        ],
        JCM900.Head100.Power with { Name = "4 x EL34 (JCM900 4100 power amp)" },
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Master = "Volume",
            Control1 = "Output",
            Control2 = "Presence",
            Switch1 = "OD",
            Switch2 = "Clean II / OD II",
            Switch3 = "Bass Shift"
        }
    };

    /// <summary>
    ///     The input (10k, 100 pF, 100 nF, 1M) to the playing IC34 section's non-inverting input; returns that node.
    /// </summary>
    static int InputNetwork(StageNetlist net) {
        var (a, plus) = (net.Node(), net.Node());
        net.Resistor(net.Input, a, 10e3);
        net.Capacitor(a, StageNetlist.Ground, 100e-12);
        net.Capacitor(a, plus, 100e-9);
        net.Resistor(plus, StageNetlist.Ground, 1e6);
        return plus;
    }

    /// <summary>
    ///     From IC34's output: 1 µF into the Gain's first section, 220 nF (220k) into the TL072 follower, 10 µF into the
    ///     second section; returns its wiper (CL_DRIVE).
    /// </summary>
    static int GainControl(StageNetlist net, int from) {
        var (first, buffered, second) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(from, first, 1e-6);
        var wiper1 = net.Pot(first, StageNetlist.Ground, new(DigitalPot, PotTaper.Linear), PotRole.Gain);
        var plus = net.Node();
        net.Capacitor(wiper1, plus, 220e-9);
        net.Resistor(plus, StageNetlist.Ground, 220e3);
        net.Amplifier(plus, buffered, buffered);
        net.Capacitor(buffered, second, 10e-6);
        return net.Pot(second, StageNetlist.Ground, new(DigitalPot, PotTaper.Linear), PotRole.Gain);
    }

    /// <summary>
    ///     IC34b (1 + 56k ‖ 100 pF over 27k + 33 nF, with 15k beside the 27k in Clean II), the Gain, 220 nF and 1k into
    ///     VB1a's grid (680k leak). Out at that grid.
    /// </summary>
    static StageNetlist CleanInput() {
        var net = new StageNetlist();
        var plus = InputNetwork(net);
        var (output, minus, foot, two, coupled, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Amplifier(plus, minus, output);
        net.Resistor(output, minus, 56e3);
        net.Capacitor(output, minus, 100e-12);
        net.Resistor(minus, foot, 27e3);
        net.Resistor(minus, two, 15e3);
        net.SwitchedResistor(two, foot, 0, Closed, SwitchRole.Switch2);
        net.Capacitor(foot, StageNetlist.Ground, 33e-9);
        var drive = GainControl(net, output);
        net.Capacitor(drive, coupled, 220e-9);
        net.Resistor(coupled, grid, 1e3);
        net.Resistor(grid, StageNetlist.Ground, 680e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     VB1a from its grid: 100k, 1k5 + 1k5 ‖ 2.2 µF; the voicing network into VB1b's grid (1M leak). Out at that grid.
    /// </summary>
    static StageNetlist CleanFirst() {
        var net = new StageNetlist();
        var (rail, plate, cathode, lower) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, lower, 1.5e3);
        net.Resistor(lower, StageNetlist.Ground, 1.5e3);
        net.Capacitor(lower, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (m, k, n, o, treble, shift, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(plate, m, 100e3);
        net.Capacitor(m, k, 47e-9);
        net.Capacitor(m, n, 100e-9);
        net.Resistor(n, k, 82e3);
        net.Resistor(k, StageNetlist.Ground, 6.8e3);
        net.Resistor(n, o, 82e3);
        net.Resistor(n, shift, 22e3);
        net.SwitchedResistor(shift, StageNetlist.Ground, 0, Closed, SwitchRole.Switch3);
        net.Capacitor(plate, treble, 220e-12);
        net.Resistor(treble, o, 220e3 * 330e3 / (220e3 + 330e3));
        net.Resistor(o, grid, 680e3);
        net.Capacitor(o, grid, 100e-12);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Output = grid;
        return net;
    }

    /// <summary>VB1b from its grid: 100k, 2k7 ‖ 2.2 µF; 47 nF, 680k over 82k. Out at the divider (IC18b's input).</summary>
    static StageNetlist CleanSecond() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 47e-9);
        net.Resistor(coupled, output, 680e3);
        net.Resistor(output, StageNetlist.Ground, 82e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     IC34a (1 + 82k ‖ 470 pF over 15k + 22 nF; Bass Shift adds 68 nF + 220k beside the 22 nF), the Gain, the IC19a
    ///     follower, 47 nF (68k), 5k1 into IC19b (inverting, 1M ‖ 22 pF, ±15 V rails), 5k6 and 220 nF onto the bridge
    ///     clamp, 1M to ground. Out at VB2a's grid.
    /// </summary>
    static StageNetlist OdInput() {
        var net = new StageNetlist();
        var plus = InputNetwork(net);
        var (output, minus, foot, shift) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Amplifier(plus, minus, output);
        net.Resistor(output, minus, 82e3);
        net.Capacitor(output, minus, 470e-12);
        net.Resistor(minus, foot, 15e3);
        net.Capacitor(foot, StageNetlist.Ground, 22e-9);
        net.Capacitor(foot, shift, 68e-9);
        net.Resistor(shift, StageNetlist.Ground, 220e3);
        net.SwitchedResistor(shift, StageNetlist.Ground, 0, Closed, SwitchRole.Switch3);
        var drive = GainControl(net, output);

        var (buffered, coupled, minus2, output2, a, clamp) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Amplifier(drive, buffered, buffered);
        net.Capacitor(buffered, coupled, 47e-9);
        net.Resistor(coupled, StageNetlist.Ground, 68e3);
        net.Resistor(coupled, minus2, 5.1e3);
        net.OpAmp(StageNetlist.Ground, minus2, output2, OpAmpModel.TL072, Rails);
        net.Resistor(output2, minus2, 1e6);
        net.Capacitor(output2, minus2, 22e-12);
        net.Resistor(output2, a, 5.6e3);
        net.Capacitor(a, clamp, 220e-9);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Rectifier, 3, 3);
        net.Resistor(clamp, StageNetlist.Ground, 1e6);
        net.Output = clamp;
        return net;
    }

    /// <summary>VB2a from its grid: 220k, 2k2 unbypassed. Out at the plate, which drives VB2b's grid directly.</summary>
    static StageNetlist OdTriode() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The VB2b follower (100k), 47 nF, 330k over 680k, then OD I's 680k over 82k or (switch 2) OD II's voicing
    ///     network. Out at the selected one's output (IC18a's input).
    /// </summary>
    static StageNetlist OdFollower() {
        var net = new StageNetlist();
        var (rail, cathode, coupled, divided, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Ht);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Capacitor(cathode, coupled, 47e-9);
        net.Resistor(coupled, divided, 330e3);
        net.Resistor(divided, StageNetlist.Ground, 680e3);

        // OD I: 680k over 82k.
        var (odOne, odOneOut) = (net.Node(), net.Node());
        net.SwitchedResistor(divided, odOne, Closed, 0, SwitchRole.Switch2);
        net.Resistor(odOne, odOneOut, 680e3);
        net.Resistor(odOneOut, StageNetlist.Ground, 82e3);
        net.SwitchedResistor(odOneOut, output, Closed, 0, SwitchRole.Switch2);

        // OD II: 470 pF + 100k and 33k / 22 nF / 22 nF / 100k / 36k / 3k9 ‖ 2k2, into 220k over 330k.
        var (odTwo, treble, sum, mid, branch, foot, odTwoOut) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.SwitchedResistor(divided, odTwo, 0, Closed, SwitchRole.Switch2);
        net.Capacitor(odTwo, treble, 470e-12);
        net.Resistor(treble, sum, 100e3);
        net.Resistor(odTwo, mid, 33e3);
        net.Capacitor(mid, branch, 22e-9);
        net.Resistor(branch, sum, 100e3);
        net.Resistor(branch, foot, 36e3);
        net.Capacitor(mid, foot, 22e-9);
        net.Resistor(foot, StageNetlist.Ground, 3.9e3 * 2.2e3 / (3.9e3 + 2.2e3));
        net.Resistor(sum, odTwoOut, 220e3);
        net.Resistor(odTwoOut, StageNetlist.Ground, 330e3);
        net.SwitchedResistor(odTwoOut, output, 0, Closed, SwitchRole.Switch2);

        net.Resistor(output, StageNetlist.Ground, 100e6);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     IC18 (a follower), 22 µF and 10k into the active tone circuit's input stage (IC22a, 10k ‖ 47 pF), its four band
    ///     filters and TC9170 pots, the output stage (IC22b, 10k in, 10k feedback), 470 Ω. Out after the 470 Ω (ATO).
    /// </summary>
    static StageNetlist ActiveTone() {
        var net = new StageNetlist();
        var (ati, coupled, cut, a, boost, b, ato) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Amplifier(net.Input, ati, ati);
        net.Capacitor(ati, coupled, 22e-6);
        net.Resistor(coupled, cut, 10e3);
        net.Amplifier(StageNetlist.Ground, cut, a);
        net.Resistor(a, cut, 10e3);
        net.Capacitor(a, cut, 47e-12);
        net.Resistor(a, boost, 10e3);
        net.Amplifier(StageNetlist.Ground, boost, b);
        net.Resistor(b, boost, 10e3);
        net.Resistor(b, ato, 470);

        // Bass: 220 nF (47k), a Sallen-Key low-pass (4k7, 10k, 220 nF, 100 nF), 2.2 µF and 2k7 to the wiper.
        var (b1, b2, b3, bassOut, bassArm, bassWiper) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(a, b1, 220e-9);
        net.Resistor(b1, StageNetlist.Ground, 47e3);
        net.Resistor(b1, b2, 4.7e3);
        net.Resistor(b2, b3, 10e3);
        net.Capacitor(b2, bassOut, 220e-9);
        net.Capacitor(b3, StageNetlist.Ground, 100e-9);
        net.Amplifier(b3, bassOut, bassOut);
        net.Capacitor(bassOut, bassArm, 2.2e-6);
        net.Resistor(bassArm, bassWiper, 2.7e3);
        Band(net, bassWiper, boost, cut, PotRole.Bass);

        // Middle: 10k (47 nF to ground, 6k8 from the output), 10 nF to the + input (22k), gain 2; 6k8 to the wiper.
        var (m1, m2, midOut, midMinus, midWiper) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(a, m1, 10e3);
        net.Capacitor(m1, StageNetlist.Ground, 47e-9);
        net.Resistor(midOut, m1, 6.8e3);
        net.Capacitor(m1, m2, 10e-9);
        net.Resistor(m2, StageNetlist.Ground, 22e3);
        net.Amplifier(m2, midMinus, midOut);
        net.Resistor(midOut, midMinus, 10e3);
        net.Resistor(midMinus, StageNetlist.Ground, 10e3);
        net.Resistor(midOut, midWiper, 6.8e3);
        Band(net, midWiper, boost, cut, PotRole.Middle);

        // Presence: 4.7 nF to s1 (10k into IC23a's - input, 10k feedback), 4.7 nF to s2 (10k), 4.7 nF to s3 (10k),
        // 10k into its + input (10k to ground); IC23b inverts (10k, 10k); 1 µF and 6k8 to the wiper.
        var (s1, s2, s3, p, pMinus, pOut, qMinus, qOut, pArm, presenceWiper) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node(), net.Node());
        net.Capacitor(a, s1, 4.7e-9);
        net.Resistor(s1, pMinus, 10e3);
        net.Capacitor(s1, s2, 4.7e-9);
        net.Resistor(s2, StageNetlist.Ground, 10e3);
        net.Capacitor(s2, s3, 4.7e-9);
        net.Resistor(s3, StageNetlist.Ground, 10e3);
        net.Resistor(s3, p, 10e3);
        net.Resistor(p, StageNetlist.Ground, 10e3);
        net.Amplifier(p, pMinus, pOut);
        net.Resistor(pOut, pMinus, 10e3);
        net.Resistor(pOut, qMinus, 10e3);
        net.Amplifier(StageNetlist.Ground, qMinus, qOut);
        net.Resistor(qOut, qMinus, 10e3);
        net.Capacitor(qOut, pArm, 1e-6);
        net.Resistor(pArm, presenceWiper, 6.8e3);
        Band(net, presenceWiper, boost, cut, PotRole.Control2);

        // Treble: a Sallen-Key high-pass (10 nF, 4.7 nF, 10k to ground, 10k from the output), 220 nF, 6k8 to the wiper.
        var (t1, t2, trebleOut, tArm, trebleWiper) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(a, t1, 10e-9);
        net.Capacitor(t1, t2, 4.7e-9);
        net.Resistor(t2, StageNetlist.Ground, 10e3);
        net.Amplifier(t2, trebleOut, trebleOut);
        net.Resistor(trebleOut, t1, 10e3);
        net.Capacitor(trebleOut, tArm, 220e-9);
        net.Resistor(tArm, trebleWiper, 6.8e3);
        Band(net, trebleWiper, boost, cut, PotRole.Treble);

        net.Resistor(ato, StageNetlist.Ground, 1e6);
        net.Output = ato;
        return net;
    }

    /// <summary>
    ///     A band's TC9170 pot (linear), driven at its <paramref name="wiper" />: its boost end to the output stage's
    ///     summing node, its cut end to the input stage's.
    /// </summary>
    static void Band(StageNetlist net, int wiper, int boost, int cut, PotRole role) {
        net.Rheostat(boost, wiper, new(DigitalPot, PotTaper.Linear), role, true);
        net.Rheostat(wiper, cut, new(DigitalPot, PotTaper.Linear), role);
    }

    /// <summary>
    ///     From ATO: 100 nF (100k) into IC17 (OD: 1 + 47k ‖ 1 nF over 10k + 2.2 µF; Clean: 1 + 33k ‖ 100 pF over 15k + 10
    ///     µF), the Volume (two TC9176 sections with a TL072 follower between), 10 µF (40.9k to ground), the output mixer
    ///     (inverting, 33k + 33k twice into 47k, the effects-mix pot's halves shunting the midpoints), 10 µF, the 10k Output
    ///     level pot, 10k into IC2a (1 + 10k ‖ 470 pF over 4k7), 10 µF, 470 Ω, 1M. Out there.
    /// </summary>
    static StageNetlist Output() {
        var net = new StageNetlist();
        var (plus, od, odMinus, odFoot, clean, cleanMinus, cleanFoot, ic17) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, plus, 100e-9);
        net.Resistor(plus, StageNetlist.Ground, 100e3);
        net.Amplifier(plus, odMinus, od);
        net.Resistor(od, odMinus, 47e3);
        net.Capacitor(od, odMinus, 1e-9);
        net.Resistor(odMinus, odFoot, 10e3);
        net.Capacitor(odFoot, StageNetlist.Ground, 2.2e-6);
        net.Amplifier(plus, cleanMinus, clean);
        net.Resistor(clean, cleanMinus, 33e3);
        net.Capacitor(clean, cleanMinus, 100e-12);
        net.Resistor(cleanMinus, cleanFoot, 15e3);
        net.Capacitor(cleanFoot, StageNetlist.Ground, 10e-6);
        net.SwitchedResistor(od, ic17, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(clean, ic17, Closed, 0, SwitchRole.Switch1);

        var (mid, buffered, second, dry) = (net.Node(), net.Node(), net.Node(), net.Node());
        var wiper1 = net.Pot(ic17, StageNetlist.Ground, new(DigitalPot, PotTaper.Linear), PotRole.Master);
        net.Capacitor(wiper1, mid, 220e-9);
        net.Resistor(mid, StageNetlist.Ground, 220e3);
        net.Amplifier(mid, buffered, buffered);
        net.Capacitor(buffered, second, 10e-6);
        var wiper2 = net.Pot(second, StageNetlist.Ground, new(DigitalPot, PotTaper.Linear), PotRole.Master);
        net.Capacitor(wiper2, dry, 10e-6);
        net.Resistor(dry, StageNetlist.Ground, 40.9e3);

        // The output mixer, its effects-mix pot (wiper grounded) at the centre: 5k from each path's midpoint to ground.
        var (x1, x2, minus, mixed, coupled, level, minus2, driven, arm, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
                net.Node(), net.Node());
        net.Resistor(dry, x1, 33e3);
        net.Resistor(x1, StageNetlist.Ground, DigitalPot / 2);
        net.Resistor(x1, minus, 33e3);
        net.Resistor(dry, x2, 33e3);
        net.Resistor(x2, StageNetlist.Ground, DigitalPot / 2);
        net.Resistor(x2, minus, 33e3);
        net.Amplifier(StageNetlist.Ground, minus, mixed);
        net.Resistor(mixed, minus, 47e3);
        net.Capacitor(mixed, coupled, 10e-6);
        var wiper = net.Pot(coupled, StageNetlist.Ground, new(10e3, PotTaper.Linear), PotRole.Control1);
        net.Resistor(wiper, level, 10e3);
        net.Amplifier(level, minus2, driven);
        net.Resistor(driven, minus2, 10e3);
        net.Capacitor(driven, minus2, 470e-12);
        net.Resistor(minus2, StageNetlist.Ground, 4.7e3);
        net.Capacitor(driven, arm, 10e-6);
        net.Resistor(arm, output, 470);
        net.Resistor(output, StageNetlist.Ground, 1e6);
        net.Output = output;
        return net;
    }
}
