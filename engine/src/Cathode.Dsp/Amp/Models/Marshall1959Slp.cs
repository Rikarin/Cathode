using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1959SLP reissue (100 W Super Lead), from the factory drawing 1959-01-60-02 issue 2 (M Ballinger, 2002;
///     https://www.drtube.com/schematics/marshall/1959-01-60-02.pdf): V1's Inputs 2 half (100k, 820 with 330 µF, 22 nF
///     into Loudness 2) and Inputs 1 half (100k, 820 with 680 nF, 3n3 into Loudness 1 with 4n7 across it), both A1M
///     volumes into 470k mixing resistors, 470 pF across Loudness 1's; V2A (100k, 820 with 680 nF), the V2B cathode
///     follower (100k) on a 470 pF / 33k / 22 nF / 22 nF stack (B250K, A1M, B25K), no master; a 12AX7 long-tail pair
///     (22 nF in, 1M, 470, 10k, 82k/100k, 47 pF, 22 nF out to 220k), four EL34 (1k5 stoppers, 1k 5 W screens), 47k of
///     feedback from the 16 Ω tap into the B5K presence pot with 100 nF.
/// </summary>
/// <remarks>
///     <para>
///         Against the 1968–70 Super Lead (<see cref="SuperLead100" />, drawing 70-6-11) the reissue drawing has 820 Ω
///         (not 2.7k) on the bright half's cathode, 3n3 (not 2n2) bright coupling, 4n7 across the volume, 470 pF on the
///         stack and the mixer, 1k5 stoppers and 47k of feedback into the presence pot alone (no 4k7 shunt): the pot is
///         drawn from the tail's foot to ground with 100 nF from its top to its wiper, as built
///         (<see cref="FeedbackDesign.PresenceIsShunt" />: the cap across the part of the track the knob turns in, as
///         the JTM45's). The effects card on W3/W4 is taken
///         as bypassed: the treble wiper drives the inverter's 22 nF and 1M. The 47 pF between the inverter's plates is
///         left out (a pole near 50 kHz), as on the other Marshalls, and so is the inverter's grid-current network
///         (<see cref="InverterInputDesign" />), for the CPU budget, as on <see cref="SuperLead100" />.
///     </para>
///     <para>
///         Not on the drawing (guesses, as <see cref="SuperLead100" />): the nodes (460 V plates and screens, 375 V
///         inverter, 300 V and 270 V preamp), the EL34s' 35 mA and the output transformer's 1.7k plate to plate. The
///         supply is drawn: 100 + 100 µF stacked (50 µF) on a silicon bridge, a choke, 10k droppers with 50 + 50 µF.
///     </para>
/// </remarks>
public static class Marshall1959Slp {
    public static AmpDesign Design { get; } = new(
        "1959SLP",
        [
            MarshallParts.TwoChannelInput(
                270,
                new(820, 0.68e-6, 3.3e-9, PotRole.Gain, 4.7e-9, 470e3, 470e-12),
                new(820, 330e-6, 22e-9, PotRole.Control1, 0, 470e3, 0)
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 820, 0.68e-6),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallVintageParts.ReissueStack, 1e6)
        ],
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            460,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, 375, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 47e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6, 80) { PresenceIsShunt = true },
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 50e-6, 200, 50e-6, 10e3, 32e-6),
            InverterInput: null
        ),
        -6
    ) { Panel = MarshallParts.TwoChannelPanel with { Gain = "Loudness 1", Control1 = "Loudness 2" } };
}
