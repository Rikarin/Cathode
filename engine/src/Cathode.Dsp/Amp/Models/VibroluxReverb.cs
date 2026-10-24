using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Vibrolux Reverb AA964 (35 W, 2 × 10; the first blackface Vibrolux Reverb: there is no AB763 one): the
///     Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each V1A into the fixed-mid Fender stack and the 1M
///     volume with its 120 pF bright switch, V1B, the AB763 mixing network with V4B and the reverb mix divider on the
///     Vibrato channel, a 12AT7 long-tail pair, two 6L6GC in fixed bias at 410 V on a GZ34, the 125A6A transformer
///     (4k : 4 Ω), and 820 Ω / 100 Ω feedback from the 4 Ω tap. Sources: the Fender drawing "Vibrolux Reverb AA964"
///     (schematicheaven.net/fenderamps/vibroluxreverb_aa964_schem.pdf: the preamp, reverb mixer and inverter values, the
///     820 Ω / 100 Ω feedback, the supply nodes 410 / 408 / 310 / 260 V, the 125A6A); Rob Robinette, "AB763 model
///     differences" (the 125A6A's 4k : 4 Ω); <see cref="Ab763" />.
/// </summary>
/// <remarks>
///     Unverified: the 6L6 idle current (35 mA; the drawing gives a −42 V bias supply), the supply's source resistance,
///     and the 2 × 10's impedance curve (the 4 × 10 preset).
/// </remarks>
public static class VibroluxReverb {
    public static AmpDesign Design { get; } = new(
        "Vibrolux Reverb AA964",
        [
            Ab763.FirstStage(260),
            Ab763.SecondStage(260),
            Ab763.Mixer(260) with { Tremolo = Ab763.Vibrato(410) }
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            410,
            470,
            0.035,
            false,
            0,
            35,
            Ab763.PhaseInverter(310),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(1 / Math.Sqrt(2), 100),
            true,
            new(4e3),
            SpeakerDesign.OpenBack4x10,
            new(100, 32e-6, 100, 16e-6, 20e3, 16e-6, 7.4e-3, RectifierModel.GZ34),
            InverterInput: Ab763.InverterInput
        ),
        -5
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Switch1 = "Vibrato Ch",
            TremoloRate = "Speed",
            TremoloDepth = "Intensity"
        }
    };
}
