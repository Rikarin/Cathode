using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Tremolux AA763 (35 W head, usually into a closed 2 × 10): the Normal or Vibrato channel
///     (<see cref="AmpPanel.Switch1" />), each V1A into the fixed-mid Fender stack and the 1M volume with its 120 pF
///     bright
///     switch, V1B, the Normal channel's 0.047 µF and the Vibrato channel's 0.1 µF into their 220k mixing resistors, a
///     12AT7 long-tail pair with equal 100k plates, a 27k tail and a 500 pF input cap, two 6L6GC in fixed bias at 415 V on
///     a GZ34, the 125A6A transformer (4k : 4 Ω), and 820 Ω / 100 Ω feedback from the 4 Ω tap. Sources: the Fender
///     drawing "Tremolux AA763" (schematicheaven.net/fenderamps/tremolux_aa763_schem.pdf: every value, the inverter's
///     100k / 100k and 27k, the supply nodes 415 / 410 / 340 / 300 V, the 125A6A, with the layout agreeing on the
///     inverter); Rob Robinette, "AB763 model differences" (the 125A6A's 4k : 4 Ω); <see cref="Ab763" />.
/// </summary>
/// <remarks>
///     The drawing shows no 1.5k grid stoppers on the 6L6s; this model has none (1 Ω). Unverified: the 6L6 idle current
///     (35 mA; the drawing gives −45 V at the grids), the supply's source resistance, the cabinet's impedance curve (the
///     4 × 10 preset stands in for the closed 2 × 10). The tremolo (intensity at 0) is not modelled.
/// </remarks>
public static class Tremolux {
    public static AmpDesign Design { get; } = new(
        "Tremolux AA763",
        [
            Ab763.FirstStage(300),
            Ab763.SecondStage(300),
            Ab763.VibratoMixer() with { Tremolo = Ab763.Vibrato(415, 56e3) }
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            415,
            470,
            0.035,
            false,
            0,
            35,
            new LongTailPairDesign(TriodeModel.Ecc81, 340, 100e3, 100e3, 27e3, 470),
            0.1e-6,
            220e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(1 / Math.Sqrt(2), 100),
            true,
            new(4e3),
            SpeakerDesign.OpenBack4x10,
            new(100, 32e-6, 100, 16e-6, 20e3, 16e-6, 5.5e-3, RectifierModel.GZ34),
            InverterInput: new(500e-12, 1e6)
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
