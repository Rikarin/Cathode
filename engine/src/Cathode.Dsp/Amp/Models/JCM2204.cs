using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM800 2204, 50 W lead master volume (drawings 2204 STD preamp issue 4 and output stage issue 2): the
///     2203's preamp and inverter (<see cref="JCM2203" />), two EL34 (5k6 stoppers, 1k screens), 100k feedback from the
///     4 Ω tap with 22k presence.
/// </summary>
/// <remarks>
///     AmpBooks' 2204 power amp: 345-0-345 V, about 460 V at the plates, 450 V screens, -40 V bias for 40 mA, a 40 H
///     choke, 100 µF, 3.2k plate to plate [src]. The preamp rails follow from the drawing's chain as the 2203's do
///     (R29 10k from the screens to node X, R8 10k on to V1; see <see cref="JCM2203" />): 378 V at X and 366 V at V1
///     from 459 V at the screens, drawing 8.1 mA.
/// </remarks>
public static class JCM2204 {
    /// <summary>Node X (the inverter and V2), from the screens' node through R29 10k (see the remarks).</summary>
    const double Rail = 378;

    /// <summary>V1's node, from X through R8 10k.</summary>
    const double FirstRail = 366;

    /// <summary>What the inverter, V2 and V1 draw through R29 at those rails.</summary>
    const double PreampDraw = 8.1e-3;

    /// <summary>What V1's two triodes draw through R8.</summary>
    const double FirstDraw = 1.26e-3;

    public static AmpDesign Design { get; } = new(
        "JCM800 2204",
        MarshallParts.JCM800Preamp(FirstRail, Rail),
        new(
            "2 x EL34",
            PentodeModel.EL34,
            1,
            460,
            1e3,
            0.040,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, Rail, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback,
            true,
            new(3.2e3),
            SpeakerDesign.Closed4x12,
            new(40, 100e-6, 100, 100e-6, 10e3, 50e-6, PreampDraw),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) { Panel = MarshallParts.MasterVolumePanel, PreampSupply = MarshallParts.JCM800Supply(FirstDraw) };
}
