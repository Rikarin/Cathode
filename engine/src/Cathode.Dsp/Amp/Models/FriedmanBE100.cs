using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Friedman BE-100, the BE channel (<see cref="Design" />) and the HBE channel (<see cref="Hbe" />), from the
///     "BE-100 v2" trace (layout by Equinox, drawing by Patrice Frey, rev 1.5, 3 May 2014; a clean CAD drawing posted on
///     music-electronics-forum.com, https://music-electronics-forum.com/filedata/fetch?id=853106&amp;d=1591761457):
///     V2A (33k stopper, 10M leak; 220k plus 100k with 500 pF across the 100k, 2k7 with 0.68 µF) into 2.2 nF (plus
///     22 nF with Fat) and the C45 switch's network into the V2B stopper (33k); V2B (100k, 2k7 with 0.68 µF), 2.2 nF
///     into the 1M Gain pot, 470k with 500 pF and 470k to V3A (100k with 500 pF from the rail, 820 with 0.68 µF, and
///     470 µF with Voice); the V3B cathode follower (100k) on a Marshall stack (500 pF, 33k, 22 nF, 22 nF;
///     250k-B/1M-A/25k-B) with the Sat clamp at its input; 220k into 1M and the 1M master; a 12AX7 long-tail pair
///     (22 nF in, 1M, 470, 10k, 82k/100k, 22 nF out to 220k), four EL34 (1k5 stoppers, 1k screens), 47k and
///     220k with 4.7 nF of feedback into the 5k presence pot with 0.1 µF on its wiper.
/// </summary>
/// <remarks>
///     <para>
///         The channel switch (CLN-BE-HBE) drives two relays: one grounds the Gain pot's top on the clean channel, the
///         other feeds V2A's 33k either from the input jack (BE) or from V1A (HBE: 33k stopper; 220k plus 100k with
///         500 pF, 2k7 with 0.68 µF) through 2.2 nF into a 1M/68k divider. The clean channel (V1A, a Fender-style stack,
///         V1B) is not modelled.
///     </para>
///     <para>
///         Read from the trace: the "C45" switch (C45A/C45B) puts either 68k in series with 68k to ground (off here), or
///         560k with 680 pF across it in series with 220k to ground (on) between V2A's coupling cap and V2B's stopper.
///         Voice (SW3) grounds either a 470 µF across V3A's 820 (on) or a 500 pF at the stack's input (off). Sat (SW7, on
///         the panel's Bright) hangs 100 nF and 10k from the follower's cathode onto 1M and two MPSA06 drawn as "Z1/Z2":
///         their base-emitter junctions used as zeners. Here they are a symmetric clamp of four LED-model junctions each
///         way (about 6.8 V at 1 mA, a forward drop plus an emitter-base breakdown); the trace's wiring of the pair would
///         clamp one polarity only, which is not settled (guess). Through 10k against the follower's few hundred ohms the
///         clamp compresses the stack's drive only a little, as the switch is said to.
///     </para>
///     <para>
///         The effects loop (two LND150 followers, a 50k send level, 2M2 and 33k) is taken as transparent: the master's
///         wiper drives the inverter's 22 nF. The feedback's 220k with 4.7 nF in series with 47k is the feedback
///         design's fixed low shelf (47k in the mid band, 267k below 154 Hz). Not on the trace (guesses): B+ 470 V, the
///         preamp nodes (V1/V2 330 V, V3 370 V, inverter 425 V, from the 4k7 + 10k, 10k, 10k droppers), the EL34s'
///         35 mA, the output transformer's 1.7k plate to plate, the feedback tap (4 Ω assumed; the trace takes it from a
///         separate secondary pin), the reservoir (two 100 µF in series) and the choke's resistance.
///     </para>
/// </remarks>
public static class FriedmanBE100 {
    const double Closed = 10;
    const double Open = 1e12;
    const double V1Supply = 330;
    const double V2Supply = 330;
    const double V3Supply = 370;

    /// <summary>
    ///     The panel: the Sat switch on Bright, Fat, C45 and Voice on the model's switches; no Cut or Depth. (Declared
    ///     before the designs, which read it as they are initialised.)
    /// </summary>
    public static AmpPanel Panel { get; } = AmpPanel.Standard with {
        Cut = null,
        Depth = null,
        Bright = "Sat",
        Switch1 = "Fat",
        Switch2 = "C45",
        Switch3 = "Voice"
    };

    /// <summary>The BE channel: the guitar straight into V2A's stopper.</summary>
    public static AmpDesign Design { get; } = Build("BE-100", [V2A(true)]);

    /// <summary>The HBE channel: V1A and its 1M/68k divider ahead of V2A.</summary>
    public static AmpDesign Hbe { get; } = Build("BE-100 HBE", [V1A(), V2A(false)]);

    static AmpDesign Build(string name, AmpStageDesign[] front) =>
        new(
            name,
            [
                ..front,
                new TriodeStageDesign("V2B", TriodeModel.Ecc83, V2Supply, 100e3, 2.7e3, 0.68e-6, 33e3),
                V3A(),
                Follower()
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
                new LongTailPairDesign(TriodeModel.Ecc83, 425, 82e3, 100e3, 10e3, 470),
                22e-9,
                220e3,
                1.5e3,
                0,
                new(1e6, PotTaper.Linear),
                new(Math.Sqrt(0.5), 47e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6, 805, 52.0 / 272),
                true,
                new(1.7e3),
                SpeakerDesign.Closed4x12,
                new(25, 50e-6, 100, 16.5e-6, 14.7e3, 22e-6),
                InverterInput: new(22e-9, 1e6)
            ),
            -9
        ) { Panel = Panel };

    /// <summary>
    ///     A BE-100 gain stage's plate load: 220k from the rail to a junction, then 100k to the plate with 500 pF across
    ///     it (V1A, V2A); returns the plate.
    /// </summary>
    static int SplitPlate(StageNetlist net, double supply) {
        var (rail, junction, plate) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, junction, 220e3);
        net.Resistor(junction, plate, 100e3);
        net.Capacitor(junction, plate, 500e-12);
        return plate;
    }

    /// <summary>A grid, plate and 2k7 cathode with 0.68 µF.</summary>
    static void Triode(StageNetlist net, int plate, int grid) {
        var cathode = net.Node();
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.68e-6);
    }

    /// <summary>HBE: the input jack's 1M, the 33k stopper, V1A, and its 2.2 nF into the 1M/68k divider (the output).</summary>
    static NetworkStageDesign V1A() {
        var net = new StageNetlist();
        var (grid, divider, top) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Resistor(net.Input, grid, 33e3);
        var plate = SplitPlate(net, V1Supply);
        Triode(net, plate, grid);
        net.Capacitor(plate, top, 2.2e-9);
        net.Resistor(top, divider, 1e6);
        net.Resistor(divider, StageNetlist.Ground, 68e3);
        net.Output = divider;
        return new("V1A", net);
    }

    /// <summary>
    ///     V2A from its 33k stopper (with the jack's 1M when the guitar plays into it) and 10M leak, to the C45 switch's
    ///     output at V2B's stopper: 2.2 nF out, plus 22 nF with Fat; then 68k in series and 68k to ground, or (C45)
    ///     560k with 680 pF in series and 220k to ground.
    /// </summary>
    static NetworkStageDesign V2A(bool jack) {
        var net = new StageNetlist();
        var (grid, coupled, plain, voiced, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        if (jack) {
            net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        }

        net.Resistor(net.Input, grid, 33e3);
        net.Resistor(grid, StageNetlist.Ground, 10e6);
        var plate = SplitPlate(net, V2Supply);
        Triode(net, plate, grid);
        net.Capacitor(plate, coupled, 2.2e-9);
        net.SwitchedCapacitor(plate, coupled, 22e-9, SwitchRole.Switch1);
        net.Resistor(coupled, plain, 68e3);
        net.Resistor(coupled, voiced, 560e3);
        net.Capacitor(coupled, voiced, 680e-12);
        net.SwitchedResistor(plain, output, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(voiced, output, 0, Closed, SwitchRole.Switch2);
        net.SwitchedResistor(output, StageNetlist.Ground, 68e3, 220e3, SwitchRole.Switch2);
        net.Output = output;
        return new("V2A", net);
    }

    /// <summary>
    ///     V3A: 2.2 nF from V2B's plate into the 1M Gain pot, 470k with 500 pF to the grid, 470k leak; 100k with 500 pF
    ///     from the rail, 820 with 0.68 µF and (Voice) 470 µF; with its grid-plate capacitance behind the 470k.
    /// </summary>
    static NetworkStageDesign V3A() {
        var net = new StageNetlist();
        var (top, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 2.2e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(wiper, grid, 470e3);
        net.Capacitor(wiper, grid, 500e-12);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Supply(rail, V3Supply);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(rail, plate, 500e-12);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 820);
        net.Capacitor(cathode, StageNetlist.Ground, 0.68e-6);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 470e-6, SwitchRole.Switch3);
        net.Output = plate;
        return new("V3A", net);
    }

    /// <summary>
    ///     The V3B cathode follower, DC-coupled to V3A's plate, on the stack (500 pF, 33k, 22 nF, 22 nF; 250k-B treble,
    ///     1M-A bass, 25k-B middle); the treble wiper through 220k onto 1M and the 1M-A master, whose wiper is the
    ///     output. Sat: 100 nF and 10k from the cathode to the clamp and its 1M. Voice off: 500 pF at the cathode.
    /// </summary>
    static NetworkStageDesign Follower() {
        var net = new StageNetlist();
        var (rail, cathode, top, slope, bottom, middle) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(),
            net.Node());
        var (sat, satResistor, clamp, load) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V3Supply);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.SwitchedCapacitor(cathode, StageNetlist.Ground, 0, SwitchRole.Switch3, off: 500e-12);
        net.Capacitor(cathode, sat, 100e-9);
        net.Resistor(sat, satResistor, 10e3);
        net.SwitchedResistor(satResistor, clamp, 0, Closed, SwitchRole.Bright);
        net.Resistor(clamp, StageNetlist.Ground, 1e6);
        net.Diodes(clamp, StageNetlist.Ground, DiodeModel.Led, 4, 4);
        net.Capacitor(cathode, top, 500e-12, CapacitorRole.Stack);
        net.Resistor(cathode, slope, 33e3);
        net.Capacitor(slope, bottom, 22e-9, CapacitorRole.Stack);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bottom, middle, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, load, 220e3);
        net.Resistor(load, StageNetlist.Ground, 1e6);
        var master = net.Pot(load, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.Output = master;
        return new("V3B", net);
    }
}
