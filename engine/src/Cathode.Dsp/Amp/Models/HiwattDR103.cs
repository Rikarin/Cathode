using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Hiwatt DR103 (Custom 100), from Mark Huss's redraws on hiwatt.org: "Mid-70s Four-Input Preamp" rev 1.4
///     (https://hiwatt.org/Schematics/DR_Pre4Input_v2.pdf, whose stages it shares with the DR504:
///     <see cref="HiwattParts" />) and "100 Watt Output Stage" v1.1
///     (https://hiwatt.org/Schematics/DR_100wOutput.pdf): V1's two triodes (220k plates, a shared cathode of two 2k2
///     with 47 nF) into the Brilliant (1 nF) and Normal (22 nF) 470k-A volumes and their 470k mixing resistors; V2A
///     (100k, 1k with 47 nF) driving the Hiwatt tone network from its plate (no follower), 22k into the 220k-A master;
///     V2B (100k, 1k5); 22 nF into a 12AT7 long-tail pair (82k/91k) biased from a cathode follower; four EL34 (22k
///     stoppers, 100k leaks, 100 Ω screens) on a Partridge transformer; 10k of feedback from the 16 Ω tap into 2k2, the
///     100k presence with 0.1 µF and the fixed 470 Ω with 10 nF.
/// </summary>
/// <remarks>
///     <para>
///         The tone network, from V2A's plate: 1 nF to a node A, which reaches the 220k Treble's top through 220 pF and
///         its foot through 220k; the Treble's foot through 22k to the 100k Middle (a rheostat with 1 nF across it) and
///         on to the 470k-A Bass (a rheostat to ground); the plate also feeds 100k to a node with 47 nF to ground and
///         47 nF to the Bass's top. The Treble's wiper goes through 22k to the Master. The middle's taper is not given
///         (linear here).
///     </para>
///     <para>
///         The inverter: its first grid has 100k and its second 1M to a cathode follower (V3: a 1M/220k divider from
///         the preamp rail, 220k cathode) at about 50 V; the cathodes go through 22k to the feedback node, which has
///         2k2 to ground and 0.1 µF to the second grid. Here it is the library's long-tail pair with the same rest
///         point: a 22.6k tail under a 1.6k bias resistor (about 3.5 V of grid bias at 2.2 mA), with the 22 nF and 100k
///         taken into V2B's stage as its load (the follower is stiff, so the 100k returns to AC ground, not
///         bootstrapped). The presence pot sits between 1 nF from V2B's plate and 0.1 µF to the feedback node, its
///         wiper through 100 Ω to ground: its feedback half is the library's presence (pot and cap across the shunt),
///         which acts only near the top of the knob (100k across 2k2), and its plate-side half is in V2B's stage (1 nF
///         into the pot's lower part and 100 Ω: <see cref="HiwattParts.PresencePlateSide" />), the treble cut that does
///         the work through the rest of the travel. The fixed 470 Ω with 10 nF across the shunt is the feedback's
///         fixed branch (<see cref="HiwattParts.FeedbackBranch" />): above about 6 kHz it takes the shunt toward 2k2
///         ∥ 470 Ω, less feedback and so a treble shelf whatever the knob.
///     </para>
///     <para>
///         Not on the drawings (guesses): the rails (B+ 496 V at the plates, as AmpBooks gives; 450 V at the inverter
///         and, through the 22k and 10k droppers, 320 V at V2A and 285 V at V1, V2B and V3), the EL34s' 30 mA, the
///         output transformer's 1.7k plate to plate, the supply's reservoir and choke. The supply's preamp node is the
///         drawing's far one: both droppers (32k) and its 16 µF. The 47 nF across V2B's 1k5 is drawn dashed (fitted on
///         some) and left out.
///     </para>
/// </remarks>
public static class HiwattDR103 {
    const double V1Supply = 285;
    const double V2ASupply = 320;

    public static AmpDesign Design { get; } = HiwattParts.WithFeedbackBranch(
        new(
            "Hiwatt DR103",
            [HiwattParts.Input(V1Supply), HiwattParts.V2A(V2ASupply), HiwattParts.V2B(V1Supply)],
            new(
                "4 x EL34",
                PentodeModel.EL34,
                2,
                496,
                100,
                0.030,
                false,
                0,
                100,
                new LongTailPairDesign(TriodeModel.Ecc81, 450, 82e3, 91e3, 22.6e3, 1.6e3),
                47e-9,
                100e3,
                22e3,
                0,
                new(1e6, PotTaper.Linear),
                new(Math.Sqrt(2), 10e3, 2.2e3, new(100e3, PotTaper.Linear), 0.1e-6),
                true,
                new(1.7e3),
                SpeakerDesign.Closed4x12,
                new(25, 100e-6, 100, 50e-6, 32e3, 16e-6, 6e-3),
                InverterInput: null
            ),
            -6
        ) { Panel = HiwattParts.Panel }
    );
}
