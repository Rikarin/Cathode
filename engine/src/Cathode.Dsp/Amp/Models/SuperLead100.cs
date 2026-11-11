using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1959 Super Lead, 100 W (Unicord drawing 70-6-11, 1970; voltages from the c.1967 "Type #2" drawing):
///     V1's normal half (820 with 250 µF, 0.022 µF out) and bright half (2.7k with 0.68 µF, 0.0022 µF out, 0.005 µF
///     across its 1M volume), both volumes into 470k mixing resistors, 500 pF across the bright one; V2A (100k, 820
///     with 0.68 µF), the V2B cathode follower (100k) on the Super Lead stack (500 pF, 33k, 0.022 µF, 0.022 µF;
///     250k/1M/25k), no master; a 12AX7 long-tail pair (0.022 µF in, 1M, 470, 10k, 82k/100k, 0.022 µF out to 220k),
///     four EL34 (5.6k stoppers, 1k screens) on a silicon bridge with 50 µF (100 + 100 µF stacked), "47K (100K)" of
///     feedback into the 5k presence pot with 0.1 µF. Switch 1 jumpers the channels.
/// </summary>
/// <remarks>
///     <para>
///         The existing <see cref="Plexi" /> is the 50 W, single-path model; this one has both channels. Unverified:
///         the feedback tap (the 16 Ω one here), the output transformer's 1.7k plate to plate, the treble pot's
///         taper. Nodes 460 V (plates and screens), 375 V (inverter), 300 V and 270 V (preamp) [src]. The 47 pF
///         between the inverter's plates is left out, and the inverter's input is its 1M grid leak on the stack
///         without the grid-current network (<see cref="InverterInputDesign" />): the two-triode first stage leaves
///         no room for it in the CPU budget (with it, about 30 % of a core on loud noise).
///     </para>
///     <para>
///         The feedback (#197): the drawing has the feedback resistor "47K (100K)" into the tail's foot and, from
///         there, the 5k presence pot to ground with 0.1 µF on its wiper; no 4k7 under the tail. The bracketed values
///         on these Unicord sheets are the 50 W model's, so the 100 W amp has 47k (as Marshall's 1959SLP reissue
///         drawing; the 1987X has 100k), and the pot is the shunt (<see cref="FeedbackDesign.PresenceIsShunt" />), as
///         on <see cref="Marshall1959T" /> and <see cref="Marshall1959Slp" />. Mid-band feedback fraction 5 / 52 =
///         0.096; the model had 100k into a fixed 4k7 with the pot and cap across it (0.045, 6.6 dB less feedback).
///         The same sheet draws the dropper to the inverter's node as "20K/1W" into 50 + 50 µF where the model has
///         10k and 32 µF: not changed here (unconfirmed).
///     </para>
/// </remarks>
public static class SuperLead100 {
    public static AmpDesign Design { get; } = new(
        "Plexi 100W",
        [
            MarshallParts.TwoChannelInput(
                270,
                new(2.7e3, 0.68e-6, 2.2e-9, PotRole.Gain, 5e-9, 470e3, 500e-12),
                new(820, 250e-6, 22e-9, PotRole.Control1, 0, 470e3, 0)
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 820, 0.68e-6),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallParts.SuperLeadStack, 1e6)
        ],
        MarshallParts.SuperLeadPower(),
        -6 // as the 1959SLP and the 1959T, which have this power amp
    ) { Panel = MarshallParts.TwoChannelPanel };
}
