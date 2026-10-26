using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall Major 1967 (200 W, four KT88): V1 (ECC83) as two channels, the Treble channel (2.2 nF out, 2.7k ‖ 0.68 µF)
///     and the Normal channel (22 nF out, 820 ‖ 250 µF), mixed at V2A's grid; V2A (ECC83, 100k) with the global feedback
///     at its cathode, the tone stack straight off its plate, V2B (ECC83) as a cathodyne (100k / 2.7k / 100k), an ECC82
///     push-pull voltage amplifier (47k plates, a shared 1.5k cathode, 270k grid leaks) and the four KT88 in
///     ultralinear fixed bias. The tone stack is inside the feedback loop, as in the amp.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Unicord drawing 70-02-12 "Marshall 1967", July 1970,
///         http://www.drtube.com/schematics/marshall/1967u.gif
///         (2098 × 1354, read by eye at 2-4×; the el34world JPG is a photo of the same sheet). Read from it: the inputs
///         (68k, 68k, 1M per channel), V1's 100k plates, 820 ‖ 250 µF and 2.7k ‖ 0.68 µF, the 22 nF and 2.2 nF coupling
///         caps, the two 1M volumes, 470k from the Normal volume's wiper and 470k ‖ 500 pF from the Treble volume's
///         wiper into the mix node (the 2.2 nF line to the Treble volume's top meets the mix line at a dot; read as a
///         crossing, since joined there the Treble volume would only load the mix node, 2 dB over its whole travel);
///         V2A's 100k plate, 470 ‖ 25 µF above the feedback node, 470 Ω from it to ground, 47k from the 16 Ω
///         terminal, the 5k presence pot with 0.68 µF; the stack (500 pF, 33k slope, 22 nF to the bass, 22 nF to the
///         middle pot's wiper, 250k treble, a 1M bass rheostat, a 25k middle pot); 22 nF into V2B, its 1M leak to the
///         junction, 100k plate, 2.7k and 100k; 47 nF into each ECC82 grid, the 270 Ω printed at the leaks (read as 270k:
///         270 Ω would short the drive), the shared 1.5k, 47k plates; 47 nF into 68k bias feeds and 5.6k stoppers; the
///         KT88 screens on the transformer's taps through 250 Ω (ultralinear); a silicon bridge into 2 × 375 µF in
///         series, 1.8k into 2 × 200 µF, 25k to V2 and 10k to V1. The middle cap enters the middle pot's wiper, not its
///         top as in the Plexi's stack.
///     </para>
///     <para>
///         Guessed (the sheet prints no voltages, tapers or transformer data): B+ 600 V, the V2 and V2B node 350 V, the
///         ECC82's node 550 V, 40 mA a KT88 at idle, the ultralinear tap at 43 %, a 2.5k plate-to-plate primary (200 W at
///         600 V), the volumes' audio taper and the treble pot's linear one, and the 250 Ω screen resistors left out.
///         Simplified: the coupling caps between V2B and the ECC82 are shorts (the driver's table); V2A's 25 µF bypass is
///         a short for the signal; the presence branch acts on the feedback, not on V2A's own degeneration; V2A draws
///         no grid current; one high input per channel (34k), as in the Plexi model.
///     </para>
/// </remarks>
public static class MarshallMajor {
    const double Closed = 10; // a closed jack contact
    const double Open = 1e12;
    const double FirstSupply = 330; // V1, after the 10k (guessed)
    const double SecondSupply = 350; // V2A and V2B, after the 25k (guessed)

    public static AmpDesign Design { get; } = new(
        "Major 1967",
        [new NetworkStageDesign("V1", InputStage())],
        new(
            "4 x KT88",
            PentodeModel.KT88,
            2,
            600,
            1,
            0.040,
            false,
            0,
            200,
            new CathodyneDriverDesign(
                TriodeModel.Ecc83,
                SecondSupply,
                100e3,
                100e3,
                2.7e3,
                TriodeModel.Ecc82,
                550,
                47e3,
                1.5e3,
                270e3
            ),
            47e-9,
            68e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 47e3, 470, new(5e3, PotTaper.Linear), 0.68e-6, 80),
            true,
            new(2.5e3),
            SpeakerDesign.Closed4x12,
            new(30, 187.5e-6, 1.8e3, 100e-6, 25e3, 50e-6, 6e-3, RectifierModel.Silicon),
            UltralinearTap: 0.43,
            Loop: new(TriodeModel.Ecc83, SecondSupply, 100e3, 470, 470 * 47e3 / (470 + 47e3), Stack(), 40)
        ),
        -5
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Treble Vol",
            Control1 = "Normal Vol",
            Master = null,
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Jumper",
            Switch2 = "Normal Input"
        }
    };

    /// <summary>
    ///     V1 and the volumes: the guitar in the Treble channel's high input (34k, 1M), or the Normal channel's (Switch 2),
    ///     or both (Switch 1, jumpered); out at the mix node, V2A's grid.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (rail, mix) = (net.Node(), net.Node());
        net.Supply(rail, FirstSupply);
        var trebleJack = net.Node();
        net.SwitchedResistor(net.Input, trebleJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, trebleJack, 0, Closed, SwitchRole.Switch1);
        var normalJack = net.Node();
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch2);

        var (trebleOut, normalOut) = (net.Node(), net.Node());
        Channel(net, rail, trebleJack, 2.7e3, 0.68e-6, 2.2e-9, trebleOut);
        Channel(net, rail, normalJack, 820, 250e-6, 22e-9, normalOut);

        // The Treble volume: 470k ‖ 500 pF from its wiper to the mix node.
        var trebleWiper = net.Pot(trebleOut, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Resistor(mix, trebleWiper, 470e3);
        net.Capacitor(mix, trebleWiper, 500e-12, CapacitorRole.Bright);

        // The Normal volume: a plain 1M with 470k from its wiper to the mix node.
        var normalWiper = net.Pot(normalOut, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.Resistor(normalWiper, mix, 470e3);
        net.Output = mix;
        return net;
    }

    /// <summary>
    ///     One V1 triode from its jack (34k, 1M), 100k plate, its cathode, through its coupling cap to
    ///     <paramref name="output" />.
    /// </summary>
    static void Channel(
        StageNetlist net,
        int rail,
        int jack,
        double cathodeResistor,
        double bypass,
        double coupling,
        int output
    ) {
        var (grid, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 34e3);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, bypass);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, output, coupling);
    }

    /// <summary>
    ///     The tone stack off V2A's plate (inside the feedback loop): 500 pF to the 250k treble pot, 33k to a node S, S
    ///     through 22 nF to the treble pot's foot and the 1M bass rheostat, the 25k middle pot below it to ground, S through
    ///     22 nF to the middle pot's wiper. Out at the treble wiper (V2B's 22 nF and bootstrapped 1M leak: an open load).
    /// </summary>
    static StageNetlist Stack() {
        var net = new StageNetlist();
        var (top, slope, foot, bass) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 500e-12, CapacitorRole.Stack);
        net.Resistor(net.Input, slope, 33e3);
        var wiper = net.Pot(top, foot, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, foot, 22e-9, CapacitorRole.Stack);
        net.Rheostat(foot, bass, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(bass, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Output = wiper;
        return net;
    }
}
