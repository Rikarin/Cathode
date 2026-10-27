using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender blackface (AB763 Super Reverb-style channel): V1 into the Fender stack and the volume, V2, then the
///     mixing network (a small coupling cap and the 220k mixing resistor into the other channel's 220k) and the (added)
///     master, a 12AT7 long-tail pair, two 6L6GC at 460 V with GZ34-like sag, feedback from the 4 Ω tap with a presence
///     control. The original's V4B mixer and reverb recovery are left out: the mixing network's loss stands in for the
///     mixer's net gain into the inverter.
/// </summary>
public static class Blackface {
    public static AmpDesign Design { get; } = new(
        "Blackface",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                270,
                1.5e3,
                ToneStackDesign.Fender,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                68e3,
                1e6,
                120e-12
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 270, 100e3, 1.5e3, 25e-6),
            new CouplingStageDesign("Mixer", 4.7e-9, new(1e6, PotTaper.Audio), PotRole.Master, 220e3, 220e3)
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            460,
            470,
            0.035,
            false,
            0,
            40,
            new LongTailPairDesign(TriodeModel.Ecc81, 420, 82e3, 100e3, 22e3, 1e3),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 820, 100, new(1e3, PotTaper.Linear), 0.47e-6, 100),
            true,
            new(4.2e3),
            new(Resonance: 100, PeakImpedance: 35, Q: 2.5),
            new(120, 70e-6, 100, 20e-6, 10e3, 16e-6)
        ),
        -3
    ) { Panel = AmpPanel.Standard with { Cut = null } };
}
