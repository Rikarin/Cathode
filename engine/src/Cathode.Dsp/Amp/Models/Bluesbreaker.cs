using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1962 Bluesbreaker reissue (2 × 12 tremolo combo), from Marshall's drawing "1962 STD Reissue Valve Tremolo
///     Combo" (file 1962.DGM; issues 3, 1989, and 4, 1993: https://www.drtube.com/schematics/marshall/1962ri89.gif and
///     1962ri93.gif): V1a (Channel II, 150k plate) and V1b (Channel I, 100k) on a shared 820 Ω with 330 µF, each 22 nF
///     into its A1M volume with 100 pF across it; Channel I's wiper through 470k with 220 pF, Channel II's through 470k
///     and, beside it, 100k into the tremolo's node and 470k on (see
///     <see cref="MarshallVintageParts.BluesbreakerInput" />); V2a (100k, 1k unbypassed), the V2b cathode follower
///     (100k) on a 220 pF / 56k / 22 nF / 22 nF stack (B220K, A1M, B22K), no master; 22 nF into a 12AX7 long-tail pair
///     (1M, 470, 10k, 82k/100k, 47 pF, 0.1 µF out to 220k), two 5881 (6L6 on the 1989 issue) with 470 Ω 5 W screen
///     resistors and no grid stoppers, a GZ34, 27k of feedback from the 16 Ω tap into the 4k7 presence pot with 0.1 µF.
///     Switch 1 jumpers the channels, switch 2 plays Channel II (the tremolo channel) alone.
/// </summary>
/// <remarks>
///     <para>
///         The tremolo is off (the library has no tremolo yet): its J174 FET, which shunts the Intensity pot's wiper to
///         ground at the oscillator's rate, is taken as open, so the tremolo node carries 0.22 µF into the 220k Intensity
///         and the 100k under it. Which state the FET rests in with the footswitch's oscillator stopped is not on the
///         drawing (a guess).
///     </para>
///     <para>
///         The presence pot is drawn as the tail's shunt to ground with 0.1 µF from its wiper, and built so
///         (<see cref="FeedbackDesign.PresenceIsShunt" />). The inverter's 47 pF and its grid-current network are left out
///         (as on the
///         JTM45, <see cref="JTM45" />). Not on the drawing (guesses): the rails (B+ 440 V, 330 V at the inverter, 300 V
///         at
///         V2 and 280 V at V1, as the JTM45 model), the 5881s' 35 mA and the output transformer's primary (784-389:
///         3.4k plate to plate, as the JTM45 model). The 2 × 12 speaker load is the open-back preset.
///     </para>
/// </remarks>
public static class Bluesbreaker {
    public static AmpDesign Design { get; } = new(
        "Bluesbreaker 1962",
        [
            MarshallVintageParts.BluesbreakerInput(280),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 1e3),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallVintageParts.BluesbreakerStack, 1e6)
        ],
        new(
            "2 x 5881",
            PentodeModel.SixL6GC,
            1,
            440,
            470,
            0.035,
            false,
            0,
            30,
            new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
            0.1e-6,
            220e3,
            0,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 27e3, 4.7e3, new(4.7e3, PotTaper.Linear), 0.1e-6, 80) { PresenceIsShunt = true },
            true,
            new(3.4e3),
            SpeakerDesign.OpenBack2x12,
            new(60, 50e-6, 200, 50e-6, 20e3, 50e-6, Rectifier: RectifierModel.GZ34),
            InverterInput: null
        ),
        -5
    ) {
        Panel = MarshallParts.TwoChannelPanel with { Gain = "Volume I", Control1 = "Volume II", Switch2 = "Channel II" }
    };
}
