using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Vibroverb AB763 (40 W, 1 × 15): the Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each V1A
///     into the fixed-mid Fender stack and the 1M volume with its 120 pF bright switch, V1B, the AB763 mixing network
///     with V4B and the reverb mix divider on the Vibrato channel, a 12AT7 long-tail pair, two 6L6GC in fixed bias at
///     465 V on a GZ34, the 125A7A transformer (4k : 8 Ω), and 820 Ω / 47 Ω feedback from the 8 Ω tap. No master,
///     presence or mid control. Sources: the Fender drawing "Vibroverb AB763"
///     (schematicheaven.net/fenderamps/vibroverb_ab763_schem.pdf: every preamp, inverter and power-section value, the
///     supply nodes 465 / 460 / 450 / 410 V, the 125A7A); Rob Robinette, "AB763 model differences" (4k : 8 Ω, the 15"
///     speaker); <see cref="Ab763" />.
/// </summary>
/// <remarks>
///     Unverified: the 6L6 idle current (35 mA; the drawing gives the −55 V bias supply, not the current), the supply's
///     source resistance, and the 15" speaker's impedance curve (a typical open-back 15: resonance near 75 Hz).
/// </remarks>
public static class Vibroverb {
    /// <summary>
    ///     An open-back 1 × 15: a lower resonance than a 12, a little more voice-coil inductance (typical values).
    /// </summary>
    static readonly SpeakerDesign OpenBack1x15 = new(6.4, 1.3e-3, 25, 75);

    public static AmpDesign Design { get; } = new(
        "Vibroverb AB763",
        [
            Ab763.FirstStage(410),
            Ab763.SecondStage(410),
            Ab763.Mixer(410) with { Tremolo = Ab763.Vibrato(465) }
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
            40,
            Ab763.PhaseInverter(450),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(1, 47),
            true,
            new(4e3),
            OpenBack1x15,
            new(100, 35e-6, 100, 20e-6, 5.7e3, 20e-6, 8.8e-3, RectifierModel.GZ34),
            InverterInput: Ab763.InverterInput
        ),
        -6
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
