using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Champ AA764 (blackface and silverface, about 5 W, 1 × 8): one 12AX7 and a single-ended 6V6GT. V1A (100k,
///     1.5k ‖ 25 µF, 34k at the jack) with the Fender stack on its plate (no coupling cap; a fixed 15k where the
///     bigger amps have 6.8k or a Middle pot) and the 1M volume on the treble wiper; V1B (100k, 1.5k ‖ 25 µF over the
///     47 Ω the feedback returns to), 0.02 µF into the 6V6's 220k grid leak; the 6V6 cathode-biased on 470 Ω ‖ 25 µF
///     with no screen resistor, at 360 V on a 5Y3GT, 2.7k feedback from the speaker. Panel: Volume, Treble, Bass.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Fender drawing "Champ Amp AA764" with its layout
///         (schematicheaven.net/fenderamps/champ_aa764_schem.pdf, code I-FD): every value above, the stack (250 pF,
///         100k, 0.1 µF, 0.047 µF, the bass pot's foot through 15k to ground; treble and bass 250k audio, volume 1M
///         audio), the supply (360 V on 20 µF, 1k to 350 V for the screen, 10k to 330 V for the 12AX7, 20 µF each)
///         and the voltages (V1A's plate 200 V and cathode 1.8 V, V1B's 205 V and 1.7 V, the 6V6's plate and screen
///         350 V, its cathode 19 V).
///     </para>
///     <para>
///         The feedback returns to V1B's cathode, so its gain from there to its plate is folded into the feedback's
///         tap gain, as on the Princetons (<see cref="PrincetonReverb" />): about 46, the 55 measured on their
///         identical stage scaled by this one's plate load (100k ‖ 220k instead of 100k). The 6V6 stage is the 5F1's
///         (<see cref="Champ.Power" />). Unverified: the output transformer (the drawing names it, 125A35A, without an
///         impedance: taken as the 5F1 Champ's 8k : 4 Ω), the supply's source resistance, and the 8" speaker's
///         impedance curve.
///     </para>
///     <para>
///         The Vibro Champ AA764 (schematicheaven.net/fenderamps/champ_vibro_aa764_schem.pdf, 045401) is this amp on a
///         slightly lower supply (355 / 340 / 320 V) with a second 12AX7 as its tremolo: a phase-shift oscillator
///         whose plate drives a cathode follower (68k) into the 25k reverse-audio Intensity pot, the wiper on V1B's
///         cathode. A tremolo that swings a preamp cathode is not in the stage library, so that type is not built.
///     </para>
/// </remarks>
public static class ChampAA764 {
    /// <summary>
    ///     V1B's gain from its cathode to its plate, which the feedback's tap gain carries (see the remarks).
    /// </summary>
    const double CathodeGain = 46;

    /// <summary>
    ///     The drawing's stack: the AB763 fixed-mid stack with 15k under the bass pot (here a 30k pot left at noon).
    /// </summary>
    static ToneStackDesign Stack { get; } =
        Ab763.FixedMidStack with { Name = "Fender Champ AA764", Middle = new(30e3, PotTaper.Linear) };

    public static AmpDesign Design { get; } = new(
        "Champ AA764",
        [
            PrincetonReverb.FirstStage(330) with { Stack = Stack },
            SecondStage()
        ],
        Champ.Power(
            360,
            Champ.Feedback(CathodeGain, 2.7e3, 47),
            new(150, 20e-6, 1e3, 20e-6, 10e3, 20e-6, 2e-3, RectifierModel.FiveY3),
            SpeakerDesign.OpenBack1x8
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

    /// <summary>
    ///     V1B (<see cref="PrincetonAA964.FeedbackTriode" />) on the 330 V node; the output is the plate, into the
    ///     power amp's 0.02 µF and the 6V6's 220k grid leak.
    /// </summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        net.Output = PrincetonAA964.FeedbackTriode(net, 330);
        return new("V1B", net);
    }
}
