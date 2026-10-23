using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender blackface (AB763 Super Reverb-style channel): V1 into the Fender stack and the volume, V2, then the mixing
///     network (a small coupling cap and the 220k mixing resistor into the other channel's 220k) and the (added) master,
///     a 12AT7 long-tail pair, two 6L6GC at 460 V with GZ34-like sag, feedback from the 4 Ω tap with a presence control.
///     The original's V4B mixer and reverb recovery are left out: the mixing network's loss stands in for the mixer's
///     net gain into the inverter.
/// </summary>
public static class Blackface {
    public static AmpDesign Design { get; } = new(
        "Blackface",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                Supply: 270,
                CathodeResistor: 1.5e3,
                Stack: ToneStackDesign.Fender,
                Load: new(1e6, PotTaper.Audio),
                LoadRole: PotRole.Gain,
                PlateResistor: 100e3,
                CathodeBypass: 25e-6,
                InputResistance: 68e3,
                GridLeak: 1e6,
                BrightCap: 120e-12
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 270, 100e3, 1.5e3, CathodeBypass: 25e-6),
            new CouplingStageDesign("Mixer", 4.7e-9, new(1e6, PotTaper.Audio), PotRole.Master, 220e3, 220e3)
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            TubesPerSide: 1,
            Supply: 460,
            ScreenResistor: 470,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 40,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc81, 420, 82e3, 100e3, 22e3, 1e3),
            CouplingCap: 0.1e-6,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1 / Math.Sqrt(2), 820, 100, new(1e3, PotTaper.Linear), 0.47e-6, 100),
            HasFeedback: true,
            Transformer: new(4.2e3),
            Speaker: new(Resonance: 100, PeakImpedance: 35, Q: 2.5),
            PowerSupply: new(120, 70e-6, 100, 20e-6, 10e3, 16e-6)
        ),
        -3
    ) {
        Panel = AmpPanel.Standard with { Cut = null }
    };
}
