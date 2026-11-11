using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Sunn Model T, first generation (150 W, four 6550): one 12AX7 as the Brite and Normal channels on a shared
///     cathode (100k plates, 680 Ω with 250 µF), each through 22 nF into a 1M linear volume and 470k to the mix node,
///     the Brite channel's 470k bridged by 470 pF; a 12AX7 gain stage (100k, 820 Ω) direct-coupled to a cathode
///     follower (100k) on the tone stack (270 pF, 56k, 22 nF, 22 nF; 250k treble, 1M bass, 25k middle with the middle
///     cap on its wiper, all linear), a 1M linear master volume, a 12AX7 long-tail pair (22 nF in with 1M, 470 Ω, 4.7k
///     to the feedback node, 82k / 120k with 270 pF between the plates), 0.25 µF into 100k bias feeds and 1k stoppers,
///     four 6550 with their screens on the transformer's taps (ultralinear), and 22k of feedback from the 16 Ω tap
///     into 10k with a 25k presence rheostat and 0.22 µF. The circuit is the 5F6-A's, as the JTM45's is, with a master
///     volume and an ultralinear output stage.
/// </summary>
/// <remarks>
///     <para>
///         Source: Sunn drawing D-1029 "Model T", revision A (ECN 441, 25 September 1973),
///         https://schematicheaven.net/newamps/sunn_model_t.pdf (the el34world file Sunn_model_t.pdf is the same scan,
///         2814 × 1887, read at 1-5×). Every value above is read from it, with the pot tapers from its notes (the tone
///         pots and R8, R9, R19 linear, the presence R25 reverse log) and the rest voltages from its chart: C 351 V
///         (V1 and V2), B 419 V (the inverter), A 519 V (the plates) with no signal; V1's plates 227 and 232 V on a
///         1.71 V cathode, V2A's plate 203 V on 1.3 V, the follower's cathode 205 V, the inverter's plates 271 and
///         251 V on a 38 V cathode. The tests hold the model's rest points to that chart.
///     </para>
///     <para>
///         Read with less certainty: V2A's cathode bypass is drawn in by hand on this revision and reads "22" with a
///         smudged unit, taken as 22 µF. C9, the presence cap, prints as "022 µf 100 V" beside ".022 µf" caps that
///         show their point; the drawing writes values of a tenth and more with a leading zero ("0.1", "0.25"), and
///         revision C of the sheet (2-18-75, "Model T Super", el34world Sunn_model_t_super.pdf) prints the same part
///         as ".22", so it is 0.22 µF. The supply's dual can prints "80-80 µf 450 V" on revision C and a blurred first
///         digit here: 80 µF on each of B and C.
///     </para>
///     <para>
///         The supply: a silicon full-wave rectifier into 20 µF, two chokes in parallel and 20 µF (A, the plates and,
///         through the transformer, the screens), 10k 2 W to B (80 µF, the inverter) and 10k 2 W to C (80 µF, V1 and
///         V2). The chart's drops give the currents: 10 mA through the first 10k and 6.8 mA through the second. In the
///         model the two 20 µF are the reservoir and the screen node's cap with the chokes between them, B is the
///         power supply's preamp node and C hangs from it (<see cref="AmpDesign.PreampSupply" />); the plates hang on
///         the first cap, not the second.
///     </para>
///     <para>
///         Guessed (not on the sheet): 40 mA a 6550 at idle (the sheet gives the bias as −55 V at the test point
///         and −53 V on the grids; this tube law needs −65 V for 40 mA with 519 V on the screens, so the sheet's
///         bias voltage is not reproduced), the ultralinear taps at 43 % as on the <see cref="MarshallMajor" />, a
///         2k plate-to-plate primary (150 W from the chart's 481 V plates at full output needs about that) and the
///         chokes' resistance (20 Ω for the pair). Fitted: the supply's source resistance, 165 Ω, so that the plates'
///         node falls from 519 V to the chart's 481 V at 150 W (the model reaches 484 V).
///     </para>
///     <para>
///         Simplified: the two V1 triodes are one triode driving both volumes
///         (<see cref="MarshallParts.SharedCathodeInput" />); the 270 pF between the inverter's plates is the power
///         amp's cut cap with no pot; the 47 Ω plate resistors of the 6550s and the line output (33k / 680 Ω from the
///         16 Ω tap) are left out. The In/Out jack pairs of each channel are one input; "Both In" (J3) is Switch 1.
///     </para>
/// </remarks>
public static class SunnModelT {
    /// <summary>Node C of the chart with no signal: V1 and V2.</summary>
    const double PreampRail = 351;

    /// <summary>Node B of the chart with no signal: the inverter.</summary>
    const double InverterRail = 419;

    /// <summary>Node A of the chart with no signal: the plates' centre tap.</summary>
    const double PlateRail = 519;

    /// <summary>What flows through R46 (10k) from A to B by the chart: the inverter's, V1's and V2's current.</summary>
    const double PreampDraw = (PlateRail - InverterRail) / 10e3;

    /// <summary>What flows through R47 (10k) from B to C by the chart: V1's and V2's current.</summary>
    const double FirstDraw = (InverterRail - PreampRail) / 10e3;

    /// <summary>
    ///     The inverter's tail below its 470 Ω: R22 4.7k to the feedback node, and from there to ground R26 10k in
    ///     parallel with R20 22k, which reaches ground through the output transformer's secondary. The chart's 38 V
    ///     cathode needs the 22k counted: the plates' 3.2 mA (148 V across 82k and 168 V across 120k) through 470 Ω
    ///     + 4.7k + 6.9k is 38 V; through 470 Ω + 14.7k it would be 48 V.
    /// </summary>
    const double Tail = 4.7e3 + 10e3 * 22e3 / (10e3 + 22e3);

    /// <summary>
    ///     The stack as drawn: C5 270 pF, R17 56k, C6 and C7 22 nF, R15 250k, R16 1M, R18 25k with C7 on its wiper;
    ///     all three pots linear (the sheet's notes). The JTM45's values with a linear bass pot.
    /// </summary>
    public static ToneStackDesign Stack { get; } = new(
        "Model T",
        270e-12,
        56e3,
        22e-9,
        22e-9,
        new(250e3, PotTaper.Linear),
        new(1e6, PotTaper.Linear),
        new(25e3, PotTaper.Linear)
    ) { MiddleOnWiper = true };

    public static AmpDesign Design { get; } = new(
        "Sunn Model T",
        [
            MarshallParts.SharedCathodeInput(
                PreampRail,
                680,
                250e-6,
                new(0, 0, 22e-9, PotRole.Gain, 0, 470e3, 470e-12, PotTaper.Linear),
                new(0, 0, 22e-9, PotRole.Control1, 0, 470e3, 0, PotTaper.Linear),
                68e3
            ) with {
                SupplyNode = 1
            },
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, PreampRail, 100e3, 820, 22e-6) { SupplyNode = 1 },
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                PreampRail,
                100e3,
                Stack,
                new(1e6, PotTaper.Linear),
                PotRole.Master
            ) { SupplyNode = 1 }
        ],
        new(
            "4 x 6550",
            PentodeModel.SixtyFiveFifty,
            2,
            PlateRail,
            1.5e3,
            0.040,
            false,
            0,
            150,
            new LongTailPairDesign(TriodeModel.Ecc83, InverterRail, 82e3, 120e3, Tail, 470),
            0.25e-6,
            100e3,
            1e3,
            270e-12,
            new(1, PotTaper.Linear),
            new(Math.Sqrt(2), 22e3, 10e3, new(25e3, PotTaper.ReverseAudio), 0.22e-6),
            true,
            new(2e3),
            SpeakerDesign.Closed4x12,
            new(165, 20e-6, 20, 20e-6, 10e3, 80e-6, PreampDraw, RectifierModel.Silicon),
            UltralinearTap: 0.43,
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Brite Vol",
            Control1 = "Normal Vol",
            Cut = null,
            Depth = null,
            Bright = null,
            Switch1 = "Both In",
            Switch2 = "Normal Input"
        },
        PreampSupply = new([new(10e3, 80e-6, FirstDraw)])
    };
}
