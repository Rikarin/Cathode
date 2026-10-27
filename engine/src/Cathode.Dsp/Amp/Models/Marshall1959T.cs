using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JMP 1959T Super Lead Tremolo (100 W), from Unicord's drawing 70-11-11 (July 1970;
///     https://www.drtube.com/schematics/marshall/1959tu.gif): the Super Lead's preamp (V1's normal half on 820 with
///     250 µF, 0.022 µF into its 1M volume; the bright half on 2.7k with 0.68 µF, 0.0022 µF into its 1M volume with
///     0.005 µF across it; 470k mixing resistors, 500 pF across the bright one; V2A 100k, 820 (1k) with 0.68 µF; the V2B
///     follower, 100k, on the 500 pF / 33k / 0.022 µF / 0.022 µF stack with 250k, 1M and 25k pots), and the tremolo
///     channel's load: the normal volume's wiper has 0.1 µF, 10k, the 50k Intensity and 33k in series to ground, with a
///     transistor (its type is illegible on the scan) from the Intensity's wiper to ground switched by the tremolo's
///     oscillator (a 12AX7 phase
///     shift oscillator with the 2M Speed). The power amp as 70-6-11: a 12AX7 long-tail pair (0.022 µF in, 1M, 470,
///     10k, 82k/100k, 47 pF, 0.022 µF out to 220k), four EL34 (5.6k stoppers, 1k screens), "47K (100K)" of feedback
///     from the 16 Ω tap into the 5k presence pot with 0.1 µF.
/// </summary>
/// <remarks>
///     <para>
///         The tremolo is off (the library has no tremolo yet): the transistor is off, so the normal volume carries the
///         0.1 µF and 93k of the Intensity chain as a fixed load, which is what sets this amp's normal channel apart from
///         the 1959 Super Lead (<see cref="SuperLead100" />) at the same settings (about 6 dB less at noon, where the
///         wiper's source resistance, about 90k, meets the load's 93k). With the tremolo on, the transistor would chop
///         that load's lower
///         part at the oscillator's rate.
///     </para>
///     <para>
///         The feedback resistor is taken as 47k (the drawing's first value; the 1959SLP reissue drawing also has 47k
///         into the presence pot alone). The presence pot is drawn with no 4k7 under it: here, as on
///         <see cref="Marshall1959Slp" />, the pot is the shunt (<see cref="FeedbackDesign.PresenceIsShunt" />). Not on the drawing
///         (guesses, as on <see cref="SuperLead100" />): the nodes (460, 375, 300 and 270 V), the idle current, the 1.7k
///         output transformer, the treble pot's taper. The inverter's 47 pF and its grid-current network are left out
///         as on <see cref="SuperLead100" />.
///     </para>
/// </remarks>
public static class Marshall1959T {
    public static AmpDesign Design { get; } = new(
        "1959T Tremolo",
        [
            MarshallVintageParts.TwoChannelInput(
                270,
                new(2.7e3, 0.68e-6, 2.2e-9, PotRole.Gain, 5e-9, 470e3, 500e-12),
                new(820, 250e-6, 22e-9, PotRole.Control1, 0, 470e3, 0),
                0.1e-6,
                93e3
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 820, 0.68e-6),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallParts.SuperLeadStack, 1e6)
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
            5.6e3,
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
    ) { Panel = MarshallParts.TwoChannelPanel };
}
