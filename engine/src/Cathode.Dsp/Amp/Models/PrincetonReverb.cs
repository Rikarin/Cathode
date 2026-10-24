using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Princeton Reverb AA1164 (blackface, about 15 W): one channel. V1A (100k, 1.5k ‖ 25 µF, 34k stopper) into the
///     Fender stack with its fixed 6.8k mid and the 1M volume, V1B, the 3.3M ‖ 10 pF reverb mix resistor into V3B's 470k
///     grid leak (an 88 % loss the third stage makes up), V3B (100k, 1.5k ‖ 25 µF over the 47 Ω the feedback returns to),
///     0.1 µF into a cathodyne phase inverter on half a 12AX7 (56k plate, 56k cathode, 1k bias), two 6V6GT in fixed bias
///     with no grid stoppers or screen resistors, on a 5U4GB and a 20 µF can with no choke, 2.7k feedback from the
///     speaker.
///     No bright, middle, master or presence; reverb and the bias tremolo are not modelled. Sources: Rob Robinette,
///     "AA1164 Princeton Reverb" (robrobinette.com): three fully bypassed stages, the cathodyne's 56k/56k/1k, 0.1 µF in,
///     the 3.3M/470k divider, 2.7k feedback, the 20/20/20/20 µF can; music-electronics-forum (2.7k into 47 Ω under the
///     stage before the inverter); docs/06 §3.1.
/// </summary>
/// <remarks>
///     <para>
///         The feedback returns to V3B's cathode, so the loop includes V3B. The power amp takes its feedback at the
///         inverter's input (inside its one Newton solve), so V3B's gain from cathode to plate (about 55 with its 47 Ω
///         undecoupled, measured on this stage) is folded into the feedback's tap gain: 2.7k into 47 Ω, times 55, from the
///         8 Ω tap. V3B's own 47 Ω degeneration stays in its stage.
///     </para>
///     <para>
///         Unverified: the output transformer (taken as the Deluxe's 6.6k : 8 Ω; Robinette names the Deluxe Reverb's as a
///         drop-in), B+ (410 V), the supply nodes (320 V at the inverter, 250 V at the preamp, from Robinette's Stokes-mod
///         page), the 6V6 idle current (22 mA), the resistor standing in for the missing choke (1k), the coupling cap
///         after V1B (0.02 µF) and the reverb driver's grid leak (1M).
///     </para>
/// </remarks>
public static class PrincetonReverb {
    /// <summary>V3B's gain from its cathode to its plate, which the feedback's tap gain carries (see the remarks).</summary>
    const double MixerGain = 55;

    static readonly ToneStackDesign Stack = new(
        "Fender AB763 fixed mid",
        250e-12,
        100e3,
        100e-9,
        47e-9,
        new(250e3, PotTaper.Audio),
        new(250e3, PotTaper.Audio),
        new(13.6e3, PotTaper.Linear)
    );

    public static AmpDesign Design { get; } = new(
        "Princeton Reverb",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                250,
                1.5e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                34e3
            ),
            SecondStage(),
            Mixer()
        ],
        new(
            "2 x 6V6GT",
            PentodeModel.SixV6GT,
            1,
            410,
            1,
            0.022,
            false,
            0,
            15,
            new CathodyneDesign(TriodeModel.Ecc83, 320, 56e3, 56e3, 1e3),
            0.1e-6,
            220e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            new(MixerGain, 2.7e3, 47, new(1e3, PotTaper.Linear), 1e-12),
            true,
            new(6.6e3),
            SpeakerDesign.OpenBack1x12,
            new(150, 20e-6, 1e3, 20e-6, 10e3, 20e-6, 5e-3, RectifierModel.FiveU4)
        ),
        -9
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null
        }
    };

    /// <summary>V1B: 100k, 1.5k ‖ 25 µF, with its grid-plate capacitance (see <see cref="Ab763.GridPlate" />).</summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = plate;
        return new("V1B", net);
    }

    /// <summary>
    ///     From V1B's plate: 0.02 µF, the reverb driver's grid leak, the 3.3M ‖ 10 pF mix resistor into V3B's 470k grid
    ///     leak; V3B (100k plate, 1.5k ‖ 25 µF above the 47 Ω feedback resistor), and its 0.1 µF into the cathodyne's
    ///     1M grid leak, which returns to the cathodyne's bias junction: that follows the grid at about 0.93, so the
    ///     signal sees about 14M (10M here).
    /// </summary>
    static NetworkStageDesign Mixer() {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 20e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, grid, 3.3e6);
        net.Capacitor(coupled, grid, 10e-12);
        net.Resistor(grid, StageNetlist.Ground, 470e3);

        var (plate, cathode, feedback, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, feedback, 1.5e3);
        net.Capacitor(cathode, feedback, 25e-6);
        net.Resistor(feedback, StageNetlist.Ground, 47);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);

        var output = net.Node();
        net.Capacitor(plate, output, 0.1e-6);
        net.Resistor(output, StageNetlist.Ground, 10e6);
        net.Output = output;
        return new("V3B", net);
    }
}
