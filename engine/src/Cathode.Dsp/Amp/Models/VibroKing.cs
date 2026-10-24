using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Vibro-King (Custom Shop, 1993; 60 W, 3 × 10), revision F: a cathode follower on the input (V1A: 0.047 µF in,
///     its grid held at half its plate supply by 2.2M / 2.2M, 100k cathode), 0.1 µF and 220k into V4A (1M grid leak, 100k,
///     1.5k with 22 µF over 22k that the <b>Fat</b> switch grounds), 0.22 µF, 470k, 0.0068 µF into the 500k volume (470k
///     beside it), V4B (100k, 1.5k ‖ 0.47 µF) with the Fender stack on its plate (150 pF, 100k slope, 0.047 µF bass,
///     0.022 µF mid; 250k treble and bass, 25k mid), 100k and 0.01 µF into a 12AX7 long-tail pair (91k/100k, 820 Ω, 39k
///     tail), two 6L6GC in fixed bias at 465 V on a silicon bridge, and no global feedback. Panel: Volume, Treble, Bass,
///     Middle, Fat. Reverb (Mix at its dry end) and vibrato (intensity at 0) are not modelled.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Fender drawing 041253
///         (thetubestore.com/lib/thetubestore/schematics/Fender/Fender-Vibro-King-Schematic.pdf),
///         revision F (EC1907, 17 May 1997), with revision A (1993) agreeing on the stack, V4B, the inverter and the power
///         stage: every value above, the supply (465 V on 2 × 220 µF in series, a choke to 464 V for the screens, 1.2k and
///         10k to 392 V for the preamp and the inverter), the screens' 470 Ω, 1.5k grid stoppers, 220k grid leaks,
///         0.047 µF coupling caps, the −46 V bias, the tail returning to ground (no feedback, no presence). Revision A's
///         Fat
///         switch has 100k where revision F has 22k (both leave the cathode effectively unbypassed with Fat off).
///     </para>
///     <para>
///         With the Mix control at its dry end the follower drives V4A directly; the reverb's wet network on the pot's
///         other
///         end and the vibrato's LDR (about 5M with the intensity at 0) on the stack's 100k are left out. Unverified: the
///         pot
///         tapers (not on the drawing: volume, treble and bass audio, mid linear, as on the AB763s), the output
///         transformer
///         (Fender 036485; Hammond's replacement 1750KA lists 4.15k : 2 Ω), the 6L6 idle current (35 mA), the supply's
///         source
///         resistance; the 3 × 10 is the 4 × 10 preset.
///     </para>
/// </remarks>
public static class VibroKing {
    const double Preamp = 392;

    public static AmpDesign Design { get; } = new(
        "Vibro-King",
        [
            Follower(),
            FirstStage(),
            StackStage()
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            465,
            470,
            0.035,
            false,
            0,
            60,
            new LongTailPairDesign(TriodeModel.Ecc83, Preamp, 91e3, 100e3, 39e3, 820),
            47e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 820, 100, new(1e3, PotTaper.Linear), 1e-12),
            false,
            new(4.15e3),
            SpeakerDesign.OpenBack4x10,
            new(40, 110e-6, 100, 47e-6, 11.2e3, 47e-6, 6.4e-3, RectifierModel.Silicon),
            InverterInput: new(10e-9, 1e6)
        ),
        -6
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Fat"
        }
    };

    /// <summary>
    ///     V1A, the input cathode follower: 0.047 µF into its grid, held at half the plate supply by 2.2M to its plate node
    ///     and 2.2M to ground, the plate on the preamp node (its 10k decoupler left out), 100k from the cathode; 0.1 µF into
    ///     220k (the Mix pot's dry end) and V4A's 1M grid leak, the output.
    /// </summary>
    static NetworkStageDesign Follower() {
        var net = new StageNetlist();
        var (grid, cathode, rail, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Capacitor(net.Input, grid, 47e-9);
        net.Resistor(grid, rail, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 2.2e6);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Triode(rail, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(cathode, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 220e3);
        net.Resistor(output, StageNetlist.Ground, 1e6);
        net.Output = output;
        return new("V1A", net, 10e3);
    }

    /// <summary>
    ///     V4A: 100k, 1.5k, its 22 µF over 22k to ground, which <see cref="SwitchRole.Switch1" /> (Fat) grounds; 0.22 µF,
    ///     470k, 0.0068 µF into the volume beside 470k; the output is the volume's wiper (V4B's grid).
    /// </summary>
    static NetworkStageDesign FirstStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail, fat) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, fat, 22e-6);
        net.Resistor(fat, StageNetlist.Ground, 22e3);
        net.SwitchedResistor(fat, StageNetlist.Ground, 0, 1, SwitchRole.Switch1);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (coupled, top) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 0.22e-6);
        net.Resistor(coupled, StageNetlist.Ground, 470e3);
        net.Capacitor(coupled, top, 6.8e-9);
        net.Resistor(top, StageNetlist.Ground, 470e3);
        net.Output = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        return new("V4A", net);
    }

    /// <summary>V4B (100k, 1.5k ‖ 0.47 µF), the stack on its plate, and the treble wiper's 100k: the output.</summary>
    static NetworkStageDesign StackStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.47e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var (top, bottom, slope, middle, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(plate, top, 150e-12, CapacitorRole.Stack);
        net.Resistor(plate, slope, 100e3);
        net.Capacitor(slope, bottom, 47e-9, CapacitorRole.Stack);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(top, bottom, new(250e3, PotTaper.Audio), PotRole.Treble);
        net.Rheostat(bottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(treble, output, 100e3);
        net.Output = output;
        return new("V4B", net);
    }
}
