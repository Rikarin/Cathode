using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM800 2204, 50 W lead master volume (drawings 2204 STD preamp issue 4 and output stage issue 2): the
///     2203's preamp and inverter (<see cref="JCM2203" />), two EL34 (5k6 stoppers, 1k screens), 100k feedback from the
///     4 Ω tap with 22k presence.
/// </summary>
/// <remarks>
///     AmpBooks' 2204 power amp: 345-0-345 V, about 460 V at the plates, 450 V screens, -40 V bias for 40 mA, a 40 H
///     choke, 100 µF, 3.2k plate to plate [src]. Preamp nodes as the 2203 (unverified).
/// </remarks>
public static class JCM2204 {
    public static AmpDesign Design { get; } = new(
        "JCM800 2204",
        MarshallParts.JCM800Preamp(290, 310),
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 1e3,
            IdleCurrent: 0.040,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 310, 82e3, 100e3, 10e3, 470),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 5.6e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: MarshallParts.JCM800Feedback,
            HasFeedback: true,
            Transformer: new(3.2e3),
            Speaker: SpeakerDesign.Closed4x12,
            PowerSupply: new(40, 100e-6, 100, 100e-6, 10e3, 50e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) {
        Panel = MarshallParts.MasterVolumePanel
    };
}
