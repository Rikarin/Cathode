using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1992 Super Bass, 100 W, from Unicord's drawing 70-13-11 "Marshall 1992" (July 1970;
///     https://www.drtube.com/schematics/marshall/1992u.gif, 2009 × 1300, read by eye at 2–4×): V1's two halves (100k
///     plates) on a shared 820 with 320 µF, each through 0.022 µF into a 1M volume and a 470k mixing resistor, 500 pF
///     across the first channel's; V2A (100k, 820 unbypassed), the V2B cathode follower (100k) on a 250 pF / 56k /
///     0.022 µF / 0.022 µF stack (250k, 1M, 25k), no master; a 12AX7 long-tail pair (0.022 µF in, 1M, 470, 10k,
///     82k/100k, 47 pF, 0.1 µF out to 220k), four EL34 (5.6k stoppers, 1k screens), "47K (100K)" of feedback from the
///     16 Ω tap into the 5k presence pot with 0.1 µF. Switch 1 jumpers the channels.
/// </summary>
/// <remarks>
///     <para>
///         Against the 1959 Super Lead of the same month (<see cref="SuperLead100" />, drawing 70-6-11) the sheet has
///         no bright cap on either volume, 0.022 µF coupling on both channels (not 0.0022 µF on the first), one shared
///         and fully bypassed V1 cathode (not 2.7k with 0.68 µF on the first), no bypass on V2A's cathode, a 250 pF
///         treble cap on a 56k slope resistor (not 500 pF on 33k) and 0.1 µF from the inverter to the power tubes (not
///         0.022 µF). The power amp is otherwise the same circuit, so it is <see cref="SuperLead100" />'s with those
///         caps and this sheet's supply chain. Marshall's own drawing 78331-3 ("Standard 100 Watt valve power
///         amplifier", https://www.drtube.com/schematics/marshall/78331-3.gif) is a later power amp with a parts
///         table for Lead and Bass; this model is not built from it.
///     </para>
///     <para>
///         Readings: V2A's cathode is printed "820K (1K)" and taken as 820 Ω; the bracketed values on these Unicord
///         sheets are the 50 W model's, so the feedback is 47k (as #197 reads 70-6-11). The slope resistor and the
///         treble pot are the smallest print on the scan and read as 56k and 250k. The sheet does not name the
///         controls or the pots' tapers: the volumes are named as the two channels' (I is the upper pair of jacks,
///         with the 500 pF on its mixing resistor), and the tapers are those of the other vintage Marshalls here. The
///         47 pF between the inverter's plates is left out (a pole near 50 kHz), and so is the inverter's
///         grid-current network (<see cref="InverterInputDesign" />), as on the <see cref="JTM45" />, which has the
///         same 0.1 µF output caps.
///     </para>
///     <para>
///         The supply as drawn: a silicon bridge into 100 + 100 µF stacked (50 µF), the choke to the screens' node
///         (100 + 100 µF stacked), "20K/1W" to the inverter's node with both halves of a 50 + 50 µF can, "10K/1W" to
///         V2's node with 50 µF and "10K/1W" on to V1's with 50 µF. The sheet prints no voltages. B+ is 460 V as on
///         <see cref="SuperLead100" /> (a guess); the three rails are what those droppers leave below the screens'
///         node with the model's own idle currents: 318, 274 and 256 V at 7.0 mA in all (computed, not measured; see
///         <c>SuperBassTests</c>). That puts 140 V and about 1 W on the 20k 1 W part, so the real amp's tubes probably
///         draw a little less and its rails sit a little higher. Also not on the sheet, and as
///         <see cref="SuperLead100" />: the EL34s' 35 mA and the output transformer's 1.7k plate to plate.
///     </para>
/// </remarks>
public static class SuperBass100 {
    /// <summary>The inverter's node, from the screens' node through the 20k (see the remarks).</summary>
    public const double InverterRail = 318;

    /// <summary>V2's node, from the inverter's through 10k.</summary>
    public const double SecondRail = 274;

    /// <summary>V1's node, from V2's through 10k.</summary>
    public const double FirstRail = 256;

    /// <summary>What the inverter, V2 and V1 draw through the 20k at those rails.</summary>
    const double PreampDraw = 7.04e-3;

    /// <summary>What V2's two triodes draw from their node.</summary>
    const double SecondDraw = 2.75e-3;

    /// <summary>What V1's two triodes draw from theirs.</summary>
    const double FirstDraw = 1.72e-3;

    /// <summary>
    ///     The 1992's stack (drawing 70-13-11): 250 pF, 56k, 22 nF, 22 nF; 250k treble (linear assumed), 1M log bass,
    ///     25k linear middle with the 0.022 µF drawn into its wiper. The Super Lead's with a smaller treble cap on a
    ///     larger slope resistor.
    /// </summary>
    public static ToneStackDesign Stack { get; } =
        MarshallParts.SuperLeadStack with { Name = "Super Bass", TrebleCap = 250e-12, SlopeResistor = 56e3 };

    public static AmpDesign Design { get; } = new(
        "Super Bass 100",
        [
            MarshallParts.SharedCathodeInput(
                FirstRail,
                820,
                320e-6,
                new(0, 0, 22e-9, PotRole.Gain, 0, 470e3, 500e-12),
                new(0, 0, 22e-9, PotRole.Control1, 0, 470e3, 0)
            ) with {
                SupplyNode = 2
            },
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, SecondRail, 100e3, 820) { SupplyNode = 1 },
            MarshallParts.FollowerStack("V2B", SecondRail, 100e3, Stack, 1e6) with { SupplyNode = 1 }
        ],
        SuperLead100.Design.Power with {
            PhaseInverter = new LongTailPairDesign(TriodeModel.Ecc83, InverterRail, 82e3, 100e3, 10e3, 470),
            CouplingCap = 0.1e-6,
            PowerSupply = new(25, 50e-6, 200, 50e-6, 20e3, 100e-6, PreampDraw)
        },
        -6 // as the Super Lead, which has this power amp
    ) {
        Panel = MarshallParts.TwoChannelPanel with { Gain = "Volume I", Control1 = "Volume II" },
        PreampSupply = new([new(10e3, 50e-6, SecondDraw), new(10e3, 50e-6, FirstDraw, 1)])
    };
}
