using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM800 2203, 100 W lead master volume (drawings 2203 STD preamp issue 4 and output stage issue 6): V1A
///     with the High/Low input jacks, the 470k/470 pF attenuator and the 1M preamp volume with its 1 nF bright cap, the
///     V1B cold clipper (10k unbypassed), V2A, the V2B cathode follower on the JCM800 stack (470 pF, 33k, 22 nF, 22 nF;
///     220k/1M/22k), the 1M master, a 12AX7 long-tail pair (82k/100k, 470, 10k, 0.022 µF in with 1M), four EL34 (5k6
///     stoppers, 1k screens) on a silicon bridge, 100k feedback from the 4 Ω tap with 22k presence.
/// </summary>
/// <remarks>
///     <para>
///         Not on the drawing, so typical values (unverified): B+ 470 V, the output transformer's 1.7k plate to plate
///         (half the 2204's 3.2k), the reservoir 100 µF and the choke's resistance. Left out: the 47 pF across the
///         inverter's outputs (a pole near 50 kHz). The supply's "50+50 µF" cans are taken in parallel, as AmpBooks
///         reads the 2204's.
///     </para>
///     <para>
///         The preamp rails (#198): the drawing gives no voltages, but it gives the chain. Node X (the inverter's 82k
///         and 100k, V2a's 100k and V2b's plate) hangs from the screens' node through R29 10k 1 W alone, and V1 from X
///         through R8 10k 1 W, with half of C21 (50 + 50 µF) on each side of R8. With the stages' idle currents
///         through the model's tubes (8.3 mA in all, 1.3 mA of it V1's) that leaves 387 V at X and 374 V at V1 from
///         469 V at the screens (a computed figure, not a measured one). The model had 310 V and 290 V ("typical"),
///         which 10k droppers cannot reach at these currents: a quarter less headroom in every preamp stage and the
///         inverter, on a supply node that rested 120 V above the rail the stages were built for. V1 now runs on its
///         own node of the chain (<see cref="MarshallParts.JCM800Supply" />).
///     </para>
/// </remarks>
public static class JCM2203 {
    /// <summary>Node X (the inverter and V2), from the screens' node through R29 10k (see the remarks).</summary>
    const double Rail = 387;

    /// <summary>V1's node, from X through R8 10k.</summary>
    const double FirstRail = 374;

    /// <summary>What the inverter, V2 and V1 draw through R29 at those rails.</summary>
    const double PreampDraw = 8.25e-3;

    /// <summary>What V1's two triodes draw through R8.</summary>
    const double FirstDraw = 1.28e-3;

    public static AmpDesign Design { get; } = new(
        "JCM800 2203",
        MarshallParts.JCM800Preamp(FirstRail, Rail),
        new(
            "4 x EL34",
            PentodeModel.EL34,
            2,
            470,
            1e3,
            0.035,
            false,
            0,
            100,
            new LongTailPairDesign(TriodeModel.Ecc83, Rail, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback,
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 100e-6, 10e3, 50e-6, PreampDraw),
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) { Panel = MarshallParts.MasterVolumePanel, PreampSupply = MarshallParts.JCM800Supply(FirstDraw) };
}
