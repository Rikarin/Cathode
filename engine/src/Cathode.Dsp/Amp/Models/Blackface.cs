using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender blackface (AB763 Super Reverb-style channel): V1 (34k stopper) into the AB763 stack and the volume, V2,
///     then the Normal channel's mixing network (0.047 µF and the 220k mixing resistor into the other channel's 220k)
///     and the (added) master, the AB763 inverter input (1 nF, the 1M grid resistor bootstrapped) and 12AT7 long-tail
///     pair (<see cref="Ab763" />), two 6L6GC at 460 V with GZ34-like sag, feedback from the 4 Ω tap with a presence
///     control. The original's V4B mixer and reverb recovery are left out: the mixing network's loss stands in for
///     the mixer's net gain into the inverter.
/// </summary>
/// <remarks>
///     <para>
///         The preamp runs on the AB763 Twin and Super Reverb drawings' 410 V node and the inverter on their 450 V
///         node (see <see cref="TwinReverb" />); the 270 V of Robinette's model differences is the voltage at the
///         7025 plates.
///     </para>
///     <para>
///         The model first had a 4.7 nF cap into the mixing resistors (a high-pass near 80 Hz, just under the low E)
///         and no inverter input network. No AB763 has that: the drawings have 0.047 µF into the 220k and 1 nF into
///         the inverter's 1M, which returns to the tail's junction and so is bootstrapped by the pair and by the
///         feedback. With the drawings' parts the roll-off sits about an octave lower (3 dB down near 40 Hz), and the
///         inverter's grid conducts under drive as on the other AB763 types.
///     </para>
/// </remarks>
public static class Blackface {
    public static AmpDesign Design { get; } = new(
        "Blackface",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                410,
                1.5e3,
                ToneStackDesign.Fender,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                34e3,
                1e6,
                120e-12
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 410, 100e3, 1.5e3, 25e-6),
            new CouplingStageDesign("Mixer", 47e-9, new(1e6, PotTaper.Audio), PotRole.Master, 220e3, 220e3)
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
            Ab763.PhaseInverter(450),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1 / Math.Sqrt(2), 820, 100, new(1e3, PotTaper.Linear), 0.47e-6, 100),
            true,
            new(4.2e3),
            new(Resonance: 100, PeakImpedance: 35, Q: 2.5),
            new(120, 70e-6, 100, 20e-6, 10e3, 16e-6),
            InverterInput: Ab763.InverterInput
        ),
        -3
    ) { Panel = AmpPanel.Standard with { Cut = null } };
}
