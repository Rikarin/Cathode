using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1987X reissue (50 W lead), from the factory drawing 1987-01-60-02 issue 4 (M Ballinger, 2002–03;
///     https://www.drtube.com/schematics/marshall/1987-01-60-02_iss4.pdf): V1's Inputs 2 half (100k, 820 with 330 µF,
///     22 nF into Loudness 2) and Inputs 1 half (100k, 820 with 680 nF, 22 nF into Loudness 1 with 4n7 across it), both
///     A1M volumes into 470k mixing resistors, 470 pF across Loudness 1's; V2A (100k, 820 unbypassed), the V2B cathode
///     follower (100k) on a 470 pF / 33k / 22 nF / 22 nF stack (B250K, A1M, B25K), no master; a 12AX7 long-tail pair
///     (22 nF in, 1M, 470, 10k, 82k/100k, 47 pF, 22 nF out to 220k), two EL34 (1k5 stoppers, 1k 5 W screens) on the
///     D2507 transformer, 100k of feedback from the 16 Ω tap into the B5K presence pot with 100 nF.
/// </summary>
/// <remarks>
///     <para>
///         The drawing differs from the 1959SLP reissue's (<see cref="Marshall1959Slp" />) in the bright half's 22 nF
///         coupling (the 1959SLP has 3n3), V2A's cathode (no 680 nF), two EL34 and 100k of feedback (47k). The presence
///         pot, the effects card, the inverter's 47 pF and its input network are taken as on the 1959SLP. The
///         "tone-stack mod" some reissues are said to have is not on the drawing and not modelled.
///     </para>
///     <para>
///         Not on the drawing (guesses): the nodes (460 V plates and screens, 375 V inverter, 300 V and 270 V preamp, as
///         the 100 W models), the EL34s' 35 mA and the D2507's primary (3.4k plate to plate, as the Plexi model). The
///         supply is drawn: four 1N4007 on a centre-tapped winding, 50 + 50 µF, a choke, 10k droppers.
///     </para>
/// </remarks>
public static class Marshall1987X {
    public static AmpDesign Design { get; } = new(
        "1987X",
        [
            MarshallParts.TwoChannelInput(
                270,
                new(820, 0.68e-6, 22e-9, PotRole.Gain, 4.7e-9, 470e3, 470e-12),
                new(820, 330e-6, 22e-9, PotRole.Control1, 0, 470e3, 0)
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 820),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallVintageParts.ReissueStack, 1e6)
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            1,
            460,
            1e3,
            0.035,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, 375, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 100e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6, 80),
            true,
            new(3.4e3),
            SpeakerDesign.Closed4x12,
            new(40, 100e-6, 200, 50e-6, 10e3, 32e-6),
            InverterInput: null
        ),
        -5
    ) { Panel = MarshallParts.TwoChannelPanel with { Gain = "Loudness 1", Control1 = "Loudness 2" } };
}
