using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Deluxe Reverb AB763 (22 W): the Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each V1A
///     (100k, 1.5k ‖ 25 µF, 34k stopper) into the Fender stack with its fixed 6.8k mid, the 1M volume (the Vibrato
///     channel's with its fixed 47 pF bright cap), V1B, then the AB763 mixing network (V4B and the reverb mix divider on
///     the Vibrato channel) into a 12AT7 long-tail pair, two 6V6GT in fixed bias at 415 V on a GZ34, the 125A1A
///     transformer (6.6k : 8 Ω), and 820 Ω / 47 Ω feedback from the 8 Ω tap. No master, presence, bright switch or
///     middle control. Sources: see <see cref="Ab763" />; docs/06 §3.1.
/// </summary>
/// <remarks>
///     Supply nodes (Robinette): 420 V at the rectifier, 415 V after the choke, 325 V at the inverter, 280 V at the
///     preamp. Pot values (treble and bass 250k audio, volume 1M audio) are the common AB763 values and agree with the
///     LiveSPICE redraw, but no factory parts list was read. The 6V6 idle current (24 mA) is a typical bias, not a
///     factory figure; the supply's source resistance and the preamp node's dropping resistor are estimates.
/// </remarks>
public static class DeluxeReverb {
    /// <summary>The AB763 Deluxe's stack: the mid pot replaced by a fixed 6.8k (here a 13.6k pot left at noon).</summary>
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
        "Deluxe Reverb",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                280,
                1.5e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                34e3,
                BrightCap: 47e-12,
                BrightSwitch: SwitchRole.Switch1
            ),
            Ab763.SecondStage(280),
            Ab763.Mixer(280) with { Tremolo = Ab763.Vibrato(415) }
        ],
        new(
            "2 x 6V6GT",
            PentodeModel.SixV6GT,
            1,
            415,
            470,
            0.024,
            false,
            0,
            22,
            Ab763.PhaseInverter(325),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(1, 47),
            true,
            new(6.6e3),
            SpeakerDesign.OpenBack1x12,
            new(100, 32e-6, 100, 16e-6, 20e3, 16e-6, 6e-3, RectifierModel.GZ34),
            InverterInput: Ab763.InverterInput
        ),
        -7
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Vibrato Ch",
            TremoloRate = "Speed",
            TremoloDepth = "Intensity"
        }
    };
}
