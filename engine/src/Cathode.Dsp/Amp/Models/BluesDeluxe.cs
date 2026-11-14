using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Blues Deluxe (40 W, 1 × 12) and Blues DeVille (60 W, 4 × 10 / 2 × 12), both channels. The input jacks
///     (68k each, 1M to ground) into V1A (100k, 1.5k ‖ 22 µF), 0.01 µF and 100k with the Bright switch's 750 pF
///     across it onto the line the 250k 15 % audio Volume shunts to ground, 750 pF into V1B's grid (510k leak); on
///     the Normal channel a relay grounds the Volume's foot and shorts the 750 pF, on Drive it opens both, so the
///     Volume does nothing there and the stage is fed through the small cap alone. V1B (100k, 1.5k unbypassed) into
///     the Fender stack with the middle cap on the middle pot's wiper (250 pF, 100k slope, 0.1 µF bass, 0.022 µF
///     middle; treble 250k 30 % audio, bass 1M audio, middle 25k linear), its treble wiper through 220k to V2A's
///     grid, where the 250k 15 % audio Drive pot shunts to ground on the Drive channel and a fixed 6.8k pad does on
///     the Normal one. V2A (100k, 1.5k ‖ 22 µF), 0.022 µF and 330k to the master node, which the 1M audio Master
///     shunts to ground on the Drive channel (a relay grounds its foot), and where the preamp-out buffer's 1M + 47k
///     and the reverb amp's 220k hang; from there into the 12AX7 long-tail pair (91k/100k, 470 Ω, 6.8k tail) and two
///     (Deluxe) or four (DeVille) 6L6GC in fixed bias, feedback from the 8 Ω tap through 68k into 4.7k with the 25k
///     reverse-audio Presence and 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         Source: Fender's factory drawings "Blues Deluxe schematic diagram" 047019 rev D (part 004702000, plotted
///         09 Feb 1994) and "Blues DeVille and Blues DeVille 212" 047003 rev E (part 004700400), scans on
///         schematicheaven.net. Every component value above and both supplies are read from them: the Deluxe's B+
///         427 V on two 100 µF in series, a choke to the screens at 425 V, 6.8k 1 W to the inverter at 370 V and
///         10k 1 W to the preamp at 321 V, bias −46.7 V, 470 Ω 1 W screen resistors, output transformer 047269 with
///         8 Ω, 4 Ω and (DeVille) 2 Ω taps, "38 watts RMS at 5 % THD" (the DeVille "50 watts"); the DeVille's
///         471 V B+, 468 V screens, 413 V inverter and 364 V preamp through the same two resistors. The two
///         drawings' preamps are the same circuit with the same values.
///     </para>
///     <para>
///         The Deluxe's printed test points check the chain (1 kHz, Drive selected, controls at half rotation): 4 mV
///         in, 222 mV at V1A's plate, 155 mV at V1B's grid (this model's Drive feed through the 750 pF and the 510k
///         gives 166 mV), 3.01 V at V1B's plate (a gain of 19 from an unbypassed 100k stage), 43.3 mV at V2A's grid
///         (the stack at noon into the 220k and the Drive pot's 37.5k at half rotation) and 2.48 V at its plate
///         (a gain of 57). The master node's 15 mV at the preamp-out buffer's 1M/47k divider puts it at about 340 mV,
///         which is the loads above on the 330k; the inverter's printed 280 mV follows. The one printed voltage this
///         model does not reach is the 50 mV marked at the master node itself, which no reading of the drawing
///         squares with the 15 mV below it.
///     </para>
///     <para>
///         Inferred, because the scan does not separate the two wires that run from the channel relay's contacts
///         down to V1A's plate network: which contact is the Normal channel's. It is taken from what each position
///         has to do — the Normal channel needs the Volume's foot grounded and the 750 pF shorted, the Drive channel
///         needs neither, and the Hot Rod Deluxe that followed this amp feeds its Drive channel through 560 pF in
///         the same place (<see cref="HotRodDeluxe" />). The relay coils' names on the drawing (OFF-DRIVE for the
///         input pair, ON-DRIVE for the Drive pot's and the Master's) fix which way each contact goes.
///     </para>
///     <para>
///         Guessed, as neither drawing prints them: the idle current (30 mA a tube, as the Hot Rod Deluxe's drawing
///         gives for the same bottles at a similar B+) and the output transformer's primary (4.25k plate to plate
///         for the Deluxe, as the Hot Rod Deluxe's Fender 005-0438; half that for the DeVille's four tubes).
///         Simplified: the three pots the drawing wires as rheostats (Volume, Drive, Master: each has its wiper tied
///         to one end and shunts its node to ground) are rheostats here too, with the taper rising as the knob does;
///         the 0.1 µF across the inverter's 6.8k tail is left out (a long-tail pair's tail has no cap in
///         <see cref="LongTailPairDesign" />, and at audio it only removes what little tail degeneration remains);
///         the 9.1k the drawing puts between V3A's plate and its coupling cap, which taps that side 9 % down, is
///         left out and its 91k alone taken as the plate resistor; the reverb is at zero and its amp's 220k input
///         resistor is kept as a load on the master node, as are the preamp-out buffer's 1M + 47k; the effects
///         loop (the Power Amp In jack, normalled) is a wire; and the two 15 V zeners that clamp the master node at
///         about ±16 V are left out, as on the Hot Rod Deluxe.
///     </para>
///     <para>
///         Panel: Gain is the Drive channel's Drive, Control 1 the Normal channel's Volume, Master the Drive
///         channel's (it does nothing on Normal, as on the amp), and <b>Normal Ch</b> selects the Normal channel —
///         the Drive channel is the default, so that Gain and Master act on it, as on
///         <see cref="HotRodDeluxe" />. Bright is the Normal channel's bright switch across the 100k; it is wired
///         on both channels here, as the drawing wires it.
///     </para>
/// </remarks>
public static class BluesDeluxe {
    /// <summary>A switched-out element (an open relay contact).</summary>
    const double Open = 1e9;

    /// <summary>A closed relay contact.</summary>
    const double Wire = 1;

    public static AmpDesign Design { get; } = Build(
        "Blues Deluxe",
        321,
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            427,
            470,
            0.030,
            false,
            0,
            40,
            new LongTailPairDesign(TriodeModel.Ecc83, 370, 91e3, 100e3, 6.8e3, 470),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 68e3, 4.7e3, new(25e3, PotTaper.ReverseAudio, 0.3), 0.1e-6),
            true,
            new(4.25e3),
            SpeakerDesign.OpenBack1x12,
            // The screens' 425 V through 6.8k + 10k to the preamp's 321 V is 6.2 mA.
            new(40, 50e-6, 100, 50e-6, 16.8e3, 22e-6, 6.2e-3, RectifierModel.Silicon),
            InverterInput: new(22e-9, 1e6)
        ),
        -6.5
    );

    /// <summary>The Blues DeVille: the same preamp on higher rails, four 6L6GC into a 4 × 10.</summary>
    public static AmpDesign Deville { get; } = Build(
        "Blues DeVille",
        364,
        Design.Power with {
            Name = "4 x 6L6GC",
            TubesPerSide = 2,
            Supply = 471,
            RatedPower = 60,
            PhaseInverter = new LongTailPairDesign(TriodeModel.Ecc83, 413, 91e3, 100e3, 6.8e3, 470),
            Transformer = new(2.1e3),
            Speaker = SpeakerDesign.OpenBack4x10,
            // The screens' 468 V through 6.8k + 10k to the preamp's 364 V is 6.2 mA.
            PowerSupply = new(30, 50e-6, 100, 50e-6, 16.8e3, 22e-6, 6.2e-3, RectifierModel.Silicon)
        },
        -8
    );

    static AmpDesign Build(string name, double preamp, PowerAmpDesign power, double trim) =>
        new(
            name,
            [
                new NetworkStageDesign("V1A", InputStage(preamp), 34e3),
                new NetworkStageDesign("V1B", StackStage(preamp)),
                new TriodeStageDesign("V2A", TriodeModel.Ecc83, preamp, 100e3, 1.5e3, 22e-6),
                new NetworkStageDesign("Master", Output())
            ],
            power,
            trim
        ) {
            Panel = AmpPanel.Standard with {
                Gain = "Drive",
                Control1 = "Volume",
                Depth = null,
                Cut = null,
                Switch1 = "Normal Ch"
            }
        };

    /// <summary>
    ///     V1A (100k, 1.5k ‖ 22 µF) with the channel network on its plate: 0.01 µF, 100k with the Bright switch's
    ///     750 pF across it, the Volume as a shunt to ground whose foot the relay grounds on the Normal channel
    ///     only, and 750 pF into V1B's grid (510k leak) which the same relay shorts on Normal. Out at that grid.
    /// </summary>
    static StageNetlist InputStage(double preamp) {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);

        var (coupled, line, foot, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, coupled, 0.01e-6);
        net.Resistor(coupled, line, 100e3);
        net.SwitchedCapacitor(coupled, line, 750e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Rheostat(line, foot, new(250e3, PotTaper.Audio, 0.15), PotRole.Control1);
        net.SwitchedResistor(foot, StageNetlist.Ground, Open, Wire, SwitchRole.Switch1);
        net.Capacitor(line, grid, 750e-12);
        net.SwitchedResistor(line, grid, Open, Wire, SwitchRole.Switch1);
        net.Resistor(grid, StageNetlist.Ground, 510e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V1B (100k, 1.5k unbypassed), the stack on its plate, 220k to V2A's grid, and there the Drive pot as a
    ///     shunt to ground (the relay grounds its foot on the Drive channel) with the 6.8k pad under it, which is
    ///     the only shunt on the Normal channel, where the relay shorts the pot instead. Out at V2A's grid.
    /// </summary>
    static StageNetlist StackStage(double preamp) {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (wiper, _) = FenderParts.MidWiperStack(
            net,
            plate,
            250e-12,
            100e3,
            0.1e-6,
            0.022e-6,
            new(250e3, PotTaper.Audio, 0.3),
            new(1e6, PotTaper.Audio),
            new(25e3, PotTaper.Linear)
        );

        var (grid, foot) = (net.Node(), net.Node());
        net.Resistor(wiper, grid, 220e3);
        net.Rheostat(grid, foot, new(250e3, PotTaper.Audio, 0.15), PotRole.Gain);
        net.SwitchedResistor(foot, StageNetlist.Ground, Wire, Open, SwitchRole.Switch1);
        net.SwitchedResistor(foot, grid, Open, Wire, SwitchRole.Switch1);
        net.Resistor(foot, StageNetlist.Ground, 6.8e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2A's plate through 0.022 µF and 330k to the master node: the Master as a shunt to ground whose foot the
    ///     relay grounds on the Drive channel only, the preamp-out buffer's 1M + 47k and the reverb amp's 220k. Out
    ///     at the master node, which drives the inverter's coupling cap.
    /// </summary>
    static StageNetlist Output() {
        var net = new StageNetlist();
        var (coupled, master, foot) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.022e-6);
        net.Resistor(coupled, master, 330e3);
        net.Rheostat(master, foot, new(1e6, PotTaper.Audio), PotRole.Master);
        net.SwitchedResistor(foot, StageNetlist.Ground, Wire, Open, SwitchRole.Switch1);
        net.Resistor(master, StageNetlist.Ground, 1.047e6);
        net.Resistor(master, StageNetlist.Ground, 220e3);
        net.Output = master;
        return net;
    }
}
