using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1959-style (a 50 W "1987" with the channels jumpered down to one path): V1A, the volume with its bright
///     cap and the 470k mixing resistor (bypassed by 470 pF), V2A with its 0.68 µF partial bypass, the V2B cathode
///     follower DC-coupled to V2A and driving the Marshall stack, the (added) master, a 12AX7 long-tail pair, two EL34
///     at 460 V on a silicon rectifier, feedback from the 16 Ω tap with presence.
/// </summary>
public static class Plexi {
    public static AmpDesign Design { get; } = new(
        "Plexi",
        [
            new TriodeStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                300,
                100e3,
                820,
                CathodeBypass: 250e-6,
                InputResistance: 68e3,
                GridLeak: 1e6
            ),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                300,
                100e3,
                820,
                CathodeBypass: 0.68e-6,
                CouplingCap: 22e-9,
                Volume: new Pot(1e6, PotTaper.Audio),
                VolumeRole: PotRole.Gain,
                BrightCap: 470e-12,
                SeriesResistor: 470e3,
                SeriesBypass: 470e-12,
                GridLeak: 470e3
            ),
            new ToneStackStageDesign(
                "V2B",
                TriodeModel.Ecc83,
                StackDriver.CathodeFollower,
                Supply: 300,
                CathodeResistor: 100e3,
                Stack: ToneStackDesign.Marshall,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Master
            )
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 1e3,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 820),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 5.6e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(Math.Sqrt(2), 100e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 80),
            HasFeedback: true,
            Transformer: new(3.4e3),
            Speaker: new(Resonance: 100, PeakImpedance: 50, Q: 3),
            PowerSupply: new(40, 50e-6, 300, 50e-6, 10e3, 16e-6)
        ),
        -5
    ) {
        Panel = AmpPanel.Standard with { Cut = null }
    };
}
