using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Vox AC30 top-boost-style: V1 (220k plate), the volume into the top-boost gain stage V2A, the V2B cathode follower
///     driving the treble/bass stack, the (added) master, a 12AX7 long-tail pair with the cut control across its plates,
///     four cathode-biased EL84 (two per side) at 330 V on a GZ34-like rectifier, no global feedback. The stack's
///     middle pot stands in for the original's fixed 10k at noon, so the panel has no Middle.
/// </summary>
public static class AC30 {
    public static AmpDesign Design { get; } = new(
        "AC30",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                250,
                220e3,
                1.5e3,
                CathodeBypass: 25e-6,
                InputResistance: 68e3,
                GridLeak: 1e6
            ),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                250,
                220e3,
                1.8e3,
                CouplingCap: 47e-9,
                Volume: new Pot(1e6, PotTaper.Audio),
                VolumeRole: PotRole.Gain,
                BrightCap: 100e-12
            ),
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Supply: 250,
                CathodeResistor: 100e3,
                Stack: ToneStackDesign.Vox,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Master
            )
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            TubesPerSide: 2,
            Supply: 330,
            ScreenResistor: 470,
            IdleCurrent: 0.034,
            CathodeBias: true,
            CathodeBypass: 250e-6,
            RatedPower: 30,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 300, 100e3, 100e3, 22e3, 1.5e3),
            CouplingCap: 47e-9,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 2.2e-9,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: false,
            Transformer: new(4e3),
            Speaker: new(Resonance: 75, PeakImpedance: 35, Q: 2.5),
            PowerSupply: new(150, 32e-6, 100, 32e-6, 10e3, 16e-6)
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with { Middle = null }
    };
}
