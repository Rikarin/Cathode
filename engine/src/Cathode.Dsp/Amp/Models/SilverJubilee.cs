using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall Silver Jubilee 2555, 100 W (factory drawing "2555 STD" issue 3, 6-6-88, file 2555.DGM, and "2555 PSU"
///     issue 2: https://el34world.com/charts/Schematics/files/Marshall/Marshall_25aniv_silverjubilee_50_100w_2555.pdf):
///     V1A (0.047 µF in, 1M, 68k; 100k, 2k7 with 0.68 µF, 47 pF plate to cathode), 0.022 µF into the 1M log Input Gain
///     with its 1 nF bright cap; 100k to V1B (100k with 470 pF from the rail, 1k5 with 10 µF), 0.022 µF and 47k onto
///     the LED clipping network (1 nF and the 220k Lead Master across it), the relay's shunt feedback, 47k from the
///     Lead Master's wiper to V2A (100k, 1k5 unbypassed, 1M from its coupled plate to its grid) with the 1N4007 pair
///     and 2.2 nF on its grid; the 100k/10k send divider and 0.1 µF into V2B (220k, 470 unbypassed) on the Jubilee's
///     own tone network and the 1M log Output Master; a 12AX7 long-tail pair (0.022 µF in, 1M, 470, 10k, 82k/100k, 22
///     nF out to 220k), four EL34 (5k6 stoppers, 1k5 screens) with the High/Low (pentode/triode) switch, 100k feedback
///     with 4k7 and the 22k presence with 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         The relay RL1 (its coil shorted by the Output Master's pull switch, "Pull Boost", or the footswitch) moves
///         one contact, L. At rest (Boost, the drawn position) L is grounded: V1B's 220k grid leak and its 220k plate
///         load go to ground and V1B runs at full gain into the clipping network: the lead sound. Energised (Boost off)
///         L joins the clipping network's top, T: the 220k from V1B's grid to T is shunt feedback around V1B (its gain
///         falls to about 220k over the 100k and the Gain pot's source), the 220k from V1B's coupling cap parallels the
///         47k, and 10k + 100k from L carry T to V2A's grid beside the Lead Master: the rhythm sound. "Pull Rhythm
///         Clip" (on the Input Gain) joins L to the 1N4007 pair, which sits on V2A's grid through 47k and 2.2 nF and
///         then clamps T directly. The LED strings (an LED and two 1N4007 one way, an LED and one 1N4007 the other) are
///         three and two fitted junctions here (1.0 V at 1 mA each, n = 1.9, the strings' slope): 3.0 V and 2.0 V
///         against about 3.0 and 2.4 V for the parts (LED colour not stated). Five and four silicon junctions fit both
///         thresholds but left Newton unconverged on a few dozen samples at full drive in the rhythm setting. C21 reads
///         "1n" at the scan's resolution (1 µF would short the signal).
///     </para>
///     <para>
///         The tone network, from V2B's plate: 0.1 µF to the back gang of the tandem 1M log Bass pot (100k to ground at
///         its foot), whose wiper is node P, also fed by 4.7 nF; P through 220 pF to the 220k linear Treble's top and
///         on through 470 pF to the 100k log Middle's top; P through 100k to a node feeding the Middle's wiper through
///         10 nF and, through 0.1 µF and 10k, the Treble's foot S; S has 2.2 nF and 100k plus the Bass's front gang (a
///         rheostat) to ground. The Treble's wiper feeds the Output Master. The direction of the tandem gangs is
///         assumed (the back gang's wiper toward 0.1 µF and the front gang's resistance both rise with Bass).
///     </para>
///     <para>
///         V1B with the clipping network and both clippers is one circuit (a triode and two clippers), cut from V1A at
///         its plate (a resistive source) and from V2A at its grid: V2A's 1M feedback loads that node through the cut,
///         not the network behind it (-14 dB against the uncut chain at 2 mV; with V2A inside the same circuit it was
///         -27 dB, at half again the cost). V2A ends at the send divider's tap, V2B starts with the return's 0.1 µF.
///         CPU: the V1B circuit's Newton takes about 6.5 iterations a sample on loud noise in the rhythm setting (the
///         shunt feedback around the clipping network), which makes this the dearest Marshall model.
///     </para>
///     <para>
///         Not on the drawing (guesses): B+ 470 V, the preamp nodes (300 V, inverter 330 V), the EL34s' 35 mA, the
///         output transformer's 1.7k (as the JCM800 2203), the feedback tap (4 Ω, as the JCM800), the choke's
///         resistance. The send/return jacks are taken as linked (the divider stays); the 10 Ω send resistor, the 47 pF
///         across the inverter's output and the DI output are left out.
///     </para>
/// </remarks>
public static class SilverJubilee {
    const double Closed = 10;
    const double Open = 1e12;
    const double PreampSupply = 300;

    /// <summary>One third of an LED and two 1N4007: 1.0 V at 1 mA, n = 1.9 (see the remarks).</summary>
    static readonly DiodeModel LedString = new(1e-3 * Math.Exp(-1.0 / (1.9 * 0.02585)), 1.9);

    public static AmpDesign Design { get; } = new(
        "Silver Jubilee",
        [V1A(), V1B(), V2A(), V2B()],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1.5e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback,
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 50e-6, 10e3, 50e-6),
            TriodeSwitch: SwitchRole.Switch3,
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Input Gain",
            Master = "Output Master",
            Control1 = "Lead Master",
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Boost",
            Switch2 = "Rhythm Clip",
            Switch3 = "Low Power"
        }
    };

    /// <summary>
    ///     V1A: 0.047 µF in, 1M, 68k; 100k, 2k7 with 0.68 µF, 47 pF plate to cathode. The output is the plate, whose
    ///     source resistance (100k in parallel with r_p) is resistive, so the cut costs little.
    /// </summary>
    static NetworkStageDesign V1A() {
        var net = new StageNetlist();
        var (jack, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, jack, 47e-9);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 68e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.68e-6);
        net.Capacitor(plate, cathode, 47e-12);
        net.Output = plate;
        return new("V1A", net);
    }

    /// <summary>
    ///     From V1A's plate to V2A's grid (the output): 0.022 µF into the 1M log Input Gain with 1 nF across its upper
    ///     half, 100k to V1B, the clipping network, the relay contact L, the Lead Master and the rhythm clip, as one
    ///     circuit (see the remarks).
    /// </summary>
    static NetworkStageDesign V1B() {
        var net = new StageNetlist();
        var (top, grid, rail, plate, cathode, coupled) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        var (clip, relay, rhythm, v2Grid, clamp) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 22e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 1e-9, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 100e3);
        net.Resistor(grid, relay, 220e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 470e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 10e-6);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, relay, 220e3);
        net.Resistor(coupled, clip, 47e3);

        // The relay: L to ground with Boost (at rest), to the clipping network's top without.
        net.SwitchedResistor(relay, StageNetlist.Ground, Open, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(relay, clip, Closed, Open, SwitchRole.Switch1);

        net.Diodes(clip, StageNetlist.Ground, LedString, 3, 2);
        net.Capacitor(clip, StageNetlist.Ground, 1e-9);
        var lead = net.Pot(clip, StageNetlist.Ground, new(220e3, PotTaper.Linear), PotRole.Control1);
        net.Resistor(lead, v2Grid, 47e3);
        net.Resistor(relay, rhythm, 10e3);
        net.Resistor(rhythm, v2Grid, 100e3);
        net.Resistor(v2Grid, clamp, 47e3);
        net.Capacitor(clamp, StageNetlist.Ground, 2.2e-9);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Rectifier, 1, 1);
        net.SwitchedResistor(clamp, relay, 0, Closed, SwitchRole.Switch2);
        net.Output = v2Grid;
        return new("V1B", net);
    }

    /// <summary>
    ///     V2A from its grid: 100k, 1k5 unbypassed; 0.1 µF out, with 1M from there back to the grid (AC shunt
    ///     feedback), and the send divider (100k over 10k), whose tap (a resistive source) is the output.
    /// </summary>
    static NetworkStageDesign V2A() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, send) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(plate, coupled, 0.1e-6);
        net.Resistor(coupled, net.Input, 1e6);
        net.Resistor(coupled, send, 100e3);
        net.Resistor(send, StageNetlist.Ground, 10e3);
        net.Output = send;
        return new("V2A", net);
    }

    /// <summary>
    ///     0.1 µF from the return to V2B's 1M leak, V2B (220k, 470 unbypassed) on the Jubilee's tone network (see the
    ///     remarks) and the 1M log Output Master.
    /// </summary>
    static NetworkStageDesign V2B() {
        var net = new StageNetlist();
        var (rail, grid) = (net.Node(), net.Node());
        net.Supply(rail, PreampSupply);
        net.Capacitor(net.Input, grid, 0.1e-6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        var (plate, cathode, bassTop, bassFoot) = (net.Node(), net.Node(), net.Node(), net.Node());
        var (u, q, qr, s, frontTop) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var midTop = net.Node();
        net.Resistor(rail, plate, 220e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 470);

        var bass = new Pot(1e6, PotTaper.Audio);
        net.Capacitor(plate, bassTop, 0.1e-6, CapacitorRole.Stack);
        var p = net.Pot(bassTop, bassFoot, bass, PotRole.Bass);
        net.Resistor(bassFoot, StageNetlist.Ground, 100e3);
        net.Capacitor(plate, p, 4.7e-9, CapacitorRole.Stack);
        net.Capacitor(p, u, 220e-12, CapacitorRole.Stack);
        net.Capacitor(u, midTop, 470e-12, CapacitorRole.Stack);
        var middle = net.Pot(midTop, StageNetlist.Ground, new(100e3, PotTaper.Audio), PotRole.Middle);
        net.Resistor(p, q, 100e3);
        net.Capacitor(q, middle, 10e-9, CapacitorRole.Stack);
        net.Capacitor(q, qr, 0.1e-6, CapacitorRole.Stack);
        net.Resistor(qr, s, 10e3);
        var treble = net.Pot(u, s, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(s, StageNetlist.Ground, 2.2e-9, CapacitorRole.Stack);
        net.Resistor(s, frontTop, 100e3);
        net.Rheostat(frontTop, StageNetlist.Ground, bass, PotRole.Bass);
        var master = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Output = master;
        return new("V2B", net);
    }
}
