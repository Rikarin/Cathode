using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Champ 5F1 (tweed, about 5 W, 1 × 8) and its sibling, the Princeton 5F2-A (<see cref="Princeton5F2" />): one
///     12AX7 and a single-ended 6V6GT. V1A (100k, 1.5k unbypassed, 34k at the jack), 0.02 µF into the 1M volume, whose
///     wiper is V1B's grid; V1B (100k, 1.5k unbypassed, the feedback's return), 0.02 µF into the 6V6's 220k grid leak; the
///     6V6 cathode-biased on 470 Ω ‖ 25 µF with no screen resistor, at 340 V on a 5Y3, an 8k : 4 Ω transformer, 22k
///     feedback from the speaker into V1B's cathode. Panel: Volume only.
/// </summary>
/// <remarks>
///     <para>
///         Sources: the Fender drawing "Champ Amp 5F1" with its layout
///         (schematicheaven.net/fenderamps/champ_5f1_schem.pdf,
///         code K-EE; the same file on el34world): every value, the supply chain (16 µF, 10k to the screen node, 22k to
///         the
///         preamp, 8 µF each) and the layout's voltages (340 / 295 / 250 V, V1A's plate 160 V, cathodes 1.5 V, the 6V6's
///         cathode 18 V); Rob Robinette, "How amps work" (the 5F1 walk-through: 68k / 1M input, 470 Ω, 10k).
///     </para>
///     <para>
///         The feedback returns to V1B's cathode, so the loop includes V1B. The power amp takes its feedback at its input
///         (here the 6V6's grid, inside its Newton solve), so V1B's gain from its cathode to its plate (about 25 with 1.4k
///         at the cathode and 69k of plate load) is folded into the tap gain, as on the Princeton Reverb; the 4 Ω
///         speaker's voltage is 1/√2 of an 8 Ω secondary's. Unverified: the output transformer (8k : 4 Ω, from the
///         replacement listings' 5k / 8k primaries, amplifiedparts.com), the volume's taper (audio; not marked on the
///         drawing), the supply's source resistance, and the 8" speaker's impedance curve.
///     </para>
/// </remarks>
public static class Champ {
    /// <summary>V1B's gain from its cathode to its plate, which the feedback's tap gain carries (see the remarks).</summary>
    const double CathodeGain = 25;

    /// <summary>An open-back 1 × 8: a high, sharp resonance, little voice-coil inductance (typical values).</summary>
    internal static readonly SpeakerDesign OpenBack1x8 = new(6.2, 0.5e-3, 30, 130, 35, 2.5);

    public static AmpDesign Design { get; } = Build("Champ 5F1", FirstStage(false, PotTaper.Audio), OpenBack1x8, -10);

    /// <summary>
    ///     V1B: 100k, 1.5k unbypassed (the feedback's return), with its grid-plate capacitance; the output is the plate,
    ///     into the power amp's 0.02 µF and the 6V6's 220k grid leak.
    /// </summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.Output = plate;
        return new("V1B", net);
    }

    /// <summary>
    ///     The 5F1-family amp: <paramref name="first" /> (V1A and the volume network) into V1B and the single-ended 6V6.
    /// </summary>
    internal static AmpDesign Build(
        string name,
        NetworkStageDesign first,
        SpeakerDesign speaker,
        double trim,
        string? tone = null
    ) =>
        new(
            name,
            [
                first,
                SecondStage()
            ],
            new(
                "6V6GT",
                PentodeModel.SixV6GT,
                1,
                340,
                1,
                0.035,
                true,
                25e-6,
                5,
                new CathodyneDesign(TriodeModel.Ecc83, 250, 56e3, 56e3, 1.5e3),
                20e-9,
                220e3,
                1,
                0,
                new(1e6, PotTaper.Linear),
                new(CathodeGain / Math.Sqrt(2), 22e3, 1.5e3, new(1e3, PotTaper.Linear), 1e-12),
                true,
                new(8e3),
                speaker,
                new(150, 16e-6, 10e3, 8e-6, 22e3, 8e-6, 2e-3, RectifierModel.FiveY3),
                PowerTopology.SingleEnded,
                CathodeResistor: 470
            ),
            trim
        ) {
            Panel = new() {
                Gain = "Volume",
                Bass = null,
                Middle = null,
                Treble = tone,
                Presence = null,
                Depth = null,
                Cut = null,
                Master = null,
                Bright = null
            }
        };

    /// <summary>
    ///     V1A (100k, 1.5k, bypassed by 25 µF when <paramref name="bypassed" />) and its 0.02 µF into the 1M volume of
    ///     <paramref name="taper" />, whose wiper is the output (V1B's grid).
    /// </summary>
    internal static NetworkStageDesign FirstStage(bool bypassed, PotTaper taper) {
        var net = new StageNetlist();
        var top = net.Node();
        FirstTriode(net, bypassed, top);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, taper), PotRole.Gain);
        return new("V1A", net, 34e3);
    }

    /// <summary>V1A and its 0.02 µF coupling cap into <paramref name="top" />.</summary>
    internal static void FirstTriode(StageNetlist net, bool bypassed, int top) {
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 250);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        if (bypassed) {
            net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        }

        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, top, 20e-9);
    }
}
