using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Twin Reverb AB763 (85 W): the Normal or Vibrato channel (<see cref="AmpPanel.Switch1" />), each V1A into
///     the Fender stack with a mid pot on both channels and the 1M volume with its 120 pF bright switch, V1B, the AB763
///     mixing network (V4B and the reverb mix divider on the Vibrato channel), a 12AT7 long-tail pair, four 6L6GC in
///     fixed bias at 460 V on a silicon rectifier, the 125A29A transformer (2k : 4 Ω), and 820 Ω / 100 Ω feedback from
///     the 4 Ω tap. No master or presence. Sources: see <see cref="Ab763" />; docs/06 §3.1.
/// </summary>
/// <remarks>
///     Preamp node 270 V, plates 460 V (Robinette's model differences). The mid pot is taken as 10k audio
///     (unverified); the idle current (30 mA per tube), the reservoir (50 µF) and the supply's source resistance are
///     typical values, not factory figures. The 125A29A's parasitics are not published: the transformer is the
///     library's default with its inductances, resistances and capacitance scaled to the 2k primary (the same bandwidth
///     and losses relative to the load); with the default's 1 nF across a 2k primary, the four tubes cutting off under
///     the Twin's heavy feedback rang hard enough to stall the power amp's Newton solve.
/// </remarks>
public static class TwinReverb {
    static readonly ToneStackDesign Stack = new(
        "Fender AB763",
        250e-12,
        100e3,
        100e-9,
        47e-9,
        new(250e3, PotTaper.Audio),
        new(250e3, PotTaper.Audio),
        new(10e3, PotTaper.Audio)
    );

    public static AmpDesign Design { get; } = new(
        "Twin Reverb",
        [
            new ToneStackStageDesign(
                "V1A",
                TriodeModel.Ecc83,
                StackDriver.CommonCathode,
                270,
                1.5e3,
                Stack,
                new(1e6, PotTaper.Audio),
                PotRole.Gain,
                100e3,
                25e-6,
                34e3,
                BrightCap: 120e-12
            ),
            Ab763.SecondStage(270),
            Ab763.Mixer(270) with { Tremolo = Ab763.Vibrato(460) }
        ],
        new(
            "4 x 6L6GC",
            PentodeModel.SixL6GC,
            2,
            460,
            470,
            0.030,
            false,
            0,
            85,
            Ab763.PhaseInverter(420),
            0.1e-6,
            220e3,
            1.5e3,
            0,
            new(1e6, PotTaper.Linear),
            Ab763.Feedback(1 / Math.Sqrt(2), 100),
            true,
            new(2e3, 10, 5e-3, 75, 2e-9, 100e3),
            SpeakerDesign.OpenBack2x12,
            new(40, 50e-6, 100, 20e-6, 10e3, 20e-6, 8e-3, RectifierModel.Silicon),
            InverterInput: Ab763.InverterInput
        ),
        -3
    ) {
        Panel = new() {
            Gain = "Volume",
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Switch1 = "Vibrato Ch",
            TremoloRate = "Speed",
            TremoloDepth = "Intensity"
        }
    };
}
