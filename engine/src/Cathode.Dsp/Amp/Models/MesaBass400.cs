using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Bass 400 (1985): two inputs into V1A (100k) and V1B (150k), each with its 1M volume and Bright switch,
///     mixed at V2A's grid; V2A (100k) with Bass Shift on its cathode, the Mark stack with Treble Shift, V2B as a cathode
///     follower; the six-band graphic equaliser (Mesa's LC network on a discrete op-amp) into the 250k master; the driver,
///     two differential stages in cascade (V3, a long-tail pair on a -68 V tail; V4, a pair on a shared 470 Ω), and six
///     6550 in fixed bias on a 540 V silicon supply, without global feedback.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawing "Mesa/Boogie Bass 400" (© 1985, two sheets: audio; supplies and the
///         graphic equaliser), https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_bass400.pdf, rendered with
///         its page transforms at the scan's 149 dpi (the strips the PDF holds are stored mirrored; an earlier reading of
///         them could not make out the volume return or V3's coupling) and read by eye at 2-4×. Read: the inputs' 47 nF
///         and 1M, V1A 100k with 1.5k ‖ 15 µF, V1B 150k with 1.2k ‖ 15 µF, 47 nF into the 1M volumes with 120 pF Bright
///         switches, 1.5M (volume 1) and 470k (volume 2) into the mix node with 470k to ground, the volumes' feet on the
///         ground bus (the return docs/06 could not read); V2A 100k with 1000 Ω ‖ 0.15 µF and 15 µF through 33k that Bass
///         Shift shorts; the stack 250 pF (+ 750 pF Treble Shift), 100k, 47 nF, 0.1 µF, 250k bass, 10k middle; 47 nF into
///         V2B with 3.3M from the 370 V node, 33k cathode, 0.1 µF into 15k; the equaliser: a PNP follower (470k / 1M
///         bias), the six 50k sliders between the op-amp's inputs, each wiper through 470 Ω (680 Ω on the top band), an
///         inductor and a cap to ground (1 H / 22 µF, 1 H / 2.2 µF, 0.35 H / 1 µF, 0.22 H / 0.22 µF, 0.068 H / 0.15 µF,
///         0.033 H / 47 nF: 34, 107, 269, 723, 1,570 and 4,040 Hz), 3.3k on the output, 0.1 µF into 3.3k; the 250k
///         master; V3: 33k plates from the 370 V node, the cathodes joined into 33k to -68 V, V3B's grid on ground; each
///         plate through 47 nF onto 62k to ground and through 470k onto V4's grid with 220k to ground (V3B's 62k sits on
///         the ground line beside its grid); V4's cathodes joined into 470 Ω; 0.1 µF into 100k feeds from the -63 V bias,
///         2.2k stoppers, 2.7k screens from the 535 V node; supplies 540 V (A), 535 V (B, after the choke), 370 V (C,
///         after 10k), -68 V and -63 V. The marked voltages give the preamp's draw (16.5 mA through the 10k).
///     </para>
///     <para>
///         Guessed (not printed): V4's plate resistors (no value on the sheet: 47k, which with 12AX7s puts the plates at
///         279 V and the cathodes at 1.8 V against the marked 276 V and 2 V; 100k gave 221 V), the 6550s' idle current
///         (45 mA, inside the tube's dissipation; the library's 6550 then wants -67 V of bias where the sheet marks -63 V,
///         and 65 mA would match the mark at 35 W a tube), the equaliser's input and feedback resistors (10k each, as in the TriAxis Dynamic
///         Voice; unlabelled), the tapers (volumes and master audio, sliders and treble linear; treble 250k), the
///         reservoir (ten 220 µF in series pairs: 550 µF), the output transformer (1.2k plate to plate, about 400 W at
///         540 V), the supply's source resistance, the choke's resistance and the rated power. Simplified: the
///         equaliser's discrete op-amp is linear (it swings about ±10 V around its -20.7 V bias; the levels here stay
///         below that except with the volumes and the sliders all up), its PNP follower an ideal buffer; the first grid
///         leaks taken to the grids (drawn on the jacks' side of the 47 nF); the effects loop and its blend a wire, the
///         equaliser's in/out switch in; the coupling caps inside the driver shorts (the driver's table). With the read
///         values V3's plates rest at 335 V, where the sheet marks 350 V.
///     </para>
///     <para>
///         Panel: Volume 1 on Gain, Volume 2 on Control 1, Input 2 (Switch 1: the guitar in input 2 instead), Bass Shift
///         (Switch 2), Treble Shift (Switch 3), Bright on both channels; the sliders on the five EQ bands (34 Hz to
///         1.6 kHz) and the 4 kHz slider on Control 2.
///     </para>
/// </remarks>
public static class MesaBass400 {
    const double Closed = 1;
    const double Open = 1e12;
    const double Rail = 370; // C

    public static AmpDesign Design { get; } = new(
        "Bass 400",
        [
            new NetworkStageDesign("V1", InputStage()),
            new NetworkStageDesign("V2", SecondStage()),
            new NetworkStageDesign("Graphic EQ", Equalizer())
        ],
        new(
            "6 x 6550",
            PentodeModel.SixtyFiveFifty,
            3,
            540,
            2.7e3,
            0.045,
            false,
            0,
            400,
            new DifferentialDriverDesign(
                TriodeModel.Ecc83,
                Rail,
                33e3,
                33e3,
                -68,
                62e3,
                470e3,
                220e3,
                TriodeModel.Ecc83,
                Rail,
                47e3,
                470
            ),
            0.1e-6,
            100e3,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 100e3, 4.7e3, new(1e3, PotTaper.Linear), 1e-15),
            false,
            new(1.2e3),
            SpeakerDesign.Closed8x10,
            new(40, 550e-6, 30, 20e-6, 10e3, 30e-6, 16.5e-3, RectifierModel.Silicon)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume 1",
            Control1 = "Volume 2",
            Presence = null,
            Depth = null,
            Cut = null,
            Switch1 = "Input 2",
            Switch2 = "Bass Shift",
            Switch3 = "Treble Shift",
            Control2 = "4 kHz",
            Equalizer = ["34 Hz", "107 Hz", "269 Hz", "723 Hz", "1.6 kHz"]
        }
    };

    /// <summary>
    ///     V1A and V1B with their volumes: the guitar in input 1 (or input 2 with Switch 1), each through 47 nF onto 1M;
    ///     47 nF into each 1M volume (120 pF Bright from top to wiper), the wipers through 1.5M and 470k into the mix node
    ///     with 470k to ground. Out at the mix node, V2A's grid.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (rail, mix) = (net.Node(), net.Node());
        net.Supply(rail, Rail);
        var (one, two) = (net.Node(), net.Node());
        net.SwitchedResistor(net.Input, one, Closed, Open, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, two, Open, Closed, SwitchRole.Switch1);
        Channel(net, rail, one, mix, 100e3, 1.5e3, PotRole.Gain, 1.5e6);
        Channel(net, rail, two, mix, 150e3, 1.2e3, PotRole.Control1, 470e3);
        net.Resistor(mix, StageNetlist.Ground, 470e3);
        net.Output = mix;
        return net;
    }

    static void Channel(
        StageNetlist net,
        int rail,
        int jack,
        int mix,
        double plateResistor,
        double cathodeResistor,
        PotRole volume,
        double mixResistor
    ) {
        var (grid, plate, cathode, top) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(jack, StageNetlist.Ground, 10e6); // an open jack's input
        net.Capacitor(jack, grid, 47e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Resistor(rail, plate, plateResistor);
        net.Resistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, top, 47e-9);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), volume);
        net.SwitchedCapacitor(top, wiper, 120e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.Resistor(wiper, mix, mixResistor);
    }

    /// <summary>
    ///     V2A and V2B in one stage (V2B's grid conducts and loads the stack, which a Thévenin cut would miss): V2A's 100k
    ///     plate, 1000 Ω ‖ 0.15 µF on its cathode and 15 µF through 33k that Bass Shift (Switch 2) shorts; the Mark stack off
    ///     the plate (250 pF, 750 pF more with Treble Shift on Switch 3, 100k, 47 nF, 0.1 µF; 250k treble, 250k bass, 10k
    ///     middle); 47 nF from the treble wiper into V2B, a follower with 3.3M from the rail to its grid (the only DC path
    ///     drawn: the grid biases itself by its grid current) and 33k on its cathode; 0.1 µF into 15k. Out at the 15k.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, shift) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Rail);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.15e-6);
        net.Capacitor(cathode, shift, 15e-6);
        net.Resistor(shift, StageNetlist.Ground, 33e3);
        net.SwitchedResistor(shift, StageNetlist.Ground, 0, Closed, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        var (wiper, _, _) = MesaParts.Stack(
            net,
            plate,
            250e-12,
            750e-12,
            SwitchRole.Switch3,
            100e3,
            47e-9,
            0.1e-6,
            new(250e3, PotTaper.Linear),
            new(250e3, PotTaper.Audio),
            new(10e3, PotTaper.Linear)
        );

        var (grid, follower, output) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(wiper, grid, 47e-9);
        net.Resistor(rail, grid, 3.3e6);
        net.Resistor(follower, StageNetlist.Ground, 33e3);
        net.Triode(rail, grid, follower, TriodeModel.Ecc83);
        net.Capacitor(follower, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 15e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The graphic equaliser and the master: the follower's bias (470k ‖ 1M) at the input, a buffer, 10k into the
    ///     op-amp's non-inverting input P, the six 50k sliders between P and the inverting input N (up is towards N:
    ///     boost), each wiper through its series R, L and C to ground, 10k of feedback; 3.3k on the output, 0.1 µF into
    ///     3.3k and the 250k master. Out at the master's wiper, V3A's grid.
    /// </summary>
    static StageNetlist Equalizer() {
        var net = new StageNetlist();
        var (buffer, p, n, output, coupled) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 470e3 * 1e6 / (470e3 + 1e6));
        net.Amplifier(net.Input, buffer, buffer);
        net.Resistor(buffer, p, 10e3);
        net.Resistor(output, n, 10e3);
        Band(net, p, n, PotRole.Equalizer1, 470, 1, 22e-6);
        Band(net, p, n, PotRole.Equalizer2, 470, 1, 2.2e-6);
        Band(net, p, n, PotRole.Equalizer3, 470, 0.35, 1e-6);
        Band(net, p, n, PotRole.Equalizer4, 470, 0.22, 0.22e-6);
        Band(net, p, n, PotRole.Equalizer5, 470, 0.068, 0.15e-6);
        Band(net, p, n, PotRole.Control2, 680, 0.033, 47e-9);
        net.Amplifier(p, n, output);
        net.Resistor(output, StageNetlist.Ground, 3.3e3);
        net.Capacitor(output, coupled, 0.1e-6);
        net.Resistor(coupled, StageNetlist.Ground, 3.3e3);
        net.Output = net.Pot(coupled, StageNetlist.Ground, new(250e3, PotTaper.Audio), PotRole.Master);
        return net;
    }

    /// <summary>A slider between P and N (up towards N), its wiper through R, L and C to ground.</summary>
    static void Band(StageNetlist net, int p, int n, PotRole role, double ohms, double henries, double farads) {
        var wiper = net.Pot(n, p, new(50e3, PotTaper.Linear), role);
        var (a, b) = (net.Node(), net.Node());
        net.Resistor(wiper, a, ohms);
        net.Inductor(a, b, henries);
        net.Capacitor(b, StageNetlist.Ground, farads);
    }
}
