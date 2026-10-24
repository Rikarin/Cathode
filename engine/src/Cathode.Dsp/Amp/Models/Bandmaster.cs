using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Bandmaster AB763 (40 W head, usually into a closed 2 × 12): the Normal or Vibrato channel
///     (<see cref="AmpPanel.Switch1" />), each V1A into the fixed-mid Fender stack and the 1M volume with its 120 pF bright
///     switch, V1B, then no reverb: the Normal channel's 0.047 µF and the Vibrato channel's 0.1 µF into their 220k mixing
///     resistors, a 12AT7 long-tail pair with a 500 pF input cap, two 6L6GC in fixed bias at 440 V on a silicon rectifier,
///     the 125A6A transformer (4k : 4 Ω), and 820 Ω / 100 Ω feedback from the 4 Ω tap. Sources: the Fender drawing
///     "Bandmaster AB763" (schematicheaven.net/fenderamps/bandmaster_ab763_schem.pdf: every value, the six-diode
///     rectifier, the supply nodes 440 / 440 / 430 / 375 V); Rob Robinette, "AB763 model differences" (the 125A6A's
///     4k : 4 Ω, the 2 × 12 cabinet); <see cref="Ab763" />.
/// </summary>
/// <remarks>
///     Unverified: the 6L6 idle current (35 mA; the drawing gives a −48 V bias supply), the supply's source resistance,
///     and the cabinet's impedance curve (the closed-back preset). The vibrato (intensity at 0) is not modelled.
/// </remarks>
public static class Bandmaster {
    public static AmpDesign Design { get; } = new(
        "Bandmaster AB763",
        [
            Ab763.FirstStage(375),
            Ab763.SecondStage(375),
            Ab763.VibratoMixer()
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 440,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 40,
            PhaseInverter: Ab763.PhaseInverter(430),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: Ab763.Feedback(1 / Math.Sqrt(2), 100),
            HasFeedback: true,
            Transformer: new(4e3),
            Speaker: SpeakerDesign.Closed4x12,
            PowerSupply: new(40, 35e-6, 100, 20e-6, 5.7e3, 20e-6, 11e-3, RectifierModel.Silicon),
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
            Switch1 = "Vibrato Ch"
        }
    };
}
