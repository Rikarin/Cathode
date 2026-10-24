using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Princeton AA964 (blackface, no reverb, about 12 W, 1 × 10): V1A (100k, 1.5k ‖ 25 µF, 34k at the jack) with
///     the fixed-mid Fender stack on its plate (no coupling cap) and the 1M volume on the treble wiper, V1B (100k,
///     1.5k ‖ 25 µF over the 47 Ω the feedback returns to), 0.022 µF into a cathodyne on half a 12AX7 (56k plate, 56k
///     cathode, 1k bias, its 1M grid leak to the bias junction), two 6V6GT in fixed bias with no grid stoppers or screen
///     resistors, at 420 V on a GZ34, 2.7k feedback from the speaker. Panel: Volume, Treble, Bass. The tremolo
///     (intensity at 0) is not modelled.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Fender drawing "Princeton Amp AA964" (schematicheaven.net/fenderamps/princeton_aa964_schem.pdf, code
///         L-FD, drawing 045419): every value above, the stack (250 pF, 100k, 0.1 µF, 0.047 µF, the bass pot's bottom
///         through 6.8k to ground; treble and bass 250k audio, volume 1M audio), the supply (420 V on 20 µF, 1k to 415 V
///         for the screens, 18k and 18k to 290 V for the preamp and the cathodyne), the voltages (V1 plates 190 V,
///         cathodes 1.5 V, the cathodyne's cathode 65 V), the −34 V bias.
///     </para>
///     <para>
///         The feedback returns to V1B's cathode, so its gain from cathode to plate (about 55 over the 47 Ω, as measured on
///         the Princeton Reverb's identical V3B) is folded into the feedback's tap gain (see <see cref="PrincetonReverb" />).
///         Unverified: the output transformer (125A10B, taken as the Deluxe's 6.6k : 8 Ω as for the Princeton Reverb), the
///         6V6 idle current (22 mA), the supply's source resistance; the 10" speaker is the 1 × 12 preset, as on the
///         Princeton Reverb.
///     </para>
/// </remarks>
public static class PrincetonAA964 {
    /// <summary>V1B's gain from its cathode to its plate, which the feedback's tap gain carries.</summary>
    const double CathodeGain = 55;

    public static AmpDesign Design { get; } = new(
        "Princeton AA964",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                Supply: 290,
                CathodeResistor: 1.5e3,
                Stack: Ab763.FixedMidStack,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Gain,
                PlateResistor: 100e3,
                CathodeBypass: 25e-6,
                InputResistance: 34e3
            ),
            SecondStage()
        ],
        new(
            "2 x 6V6GT",
            PentodeModel.SixV6GT,
            TubesPerSide: 1,
            Supply: 420,
            ScreenResistor: 1,
            IdleCurrent: 0.022,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 12,
            PhaseInverter: new CathodyneDesign(TriodeModel.Ecc83, 290, 56e3, 56e3, 1e3),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(CathodeGain, 2.7e3, 47, new(1e3, PotTaper.Linear), 1e-12),
            HasFeedback: true,
            Transformer: new(6.6e3),
            Speaker: SpeakerDesign.OpenBack1x12,
            PowerSupply: new(150, 20e-6, 1e3, 20e-6, 36e3, 20e-6, 3.5e-3, RectifierModel.GZ34)
        ),
        0
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
    ///     V1B: 100k, 1.5k ‖ 25 µF above the 47 Ω feedback resistor, with its grid-plate capacitance; 0.022 µF into the
    ///     cathodyne's 1M grid leak, which returns to its bias junction (following the grid at about 0.93: about 14M; 10M
    ///     here).
    /// </summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, feedback, rail) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, 290);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, feedback, 1.5e3);
        net.Capacitor(cathode, feedback, 25e-6);
        net.Resistor(feedback, StageNetlist.Ground, 47);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        var output = net.Node();
        net.Capacitor(plate, output, 22e-9);
        net.Resistor(output, StageNetlist.Ground, 10e6);
        net.Output = output;
        return new("V1B", net);
    }
}
