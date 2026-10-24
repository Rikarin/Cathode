using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JTM45 (reissue drawing JTM45.DGM issue 7): V1A and V1B (100k plates, a shared 820 with 250 µF), each
///     channel's 0.02 µF into a 1M-A volume and a 270k mixing resistor, the high-treble channel with 500 pF across its
///     volume and its mixing resistor; V2A (100k, 820 unbypassed), the V2B cathode follower (100k) on the JTM45 stack
///     (270 pF, 56k, 0.02 µF, 0.02 µF; 250k-B/1M-A/25k-B), no master; a 12AX7 long-tail pair (0.02 µF in, 1M, 470,
///     10k, 82k/100k, 0.1 µF out to 220k), two KT66 without grid stoppers, 470 screen resistors, a GZ34 with 32 µF, a
///     20 H choke and 32 µF; 27k feedback from the 16 Ω tap with the 5k presence.
/// </summary>
/// <remarks>
///     Not on the drawing (unverified): the KT66 (historical), B+ 440 V and the preamp nodes (330 V, 300 V), the output
///     transformer's 3.4k. The presence is a 5k pot from the tail's foot to ground with 0.1 µF on its wiper; here it is
///     the
///     usual 5k shunt with the pot and cap across it, which has the same ends. The 47 pF between the inverter's plates is
///     left out (a pole near 50 kHz). The inverter's input is its 1M grid leak on the stack, without the grid-current
///     network (<see cref="InverterInputDesign" />): with it and the 0.1 µF output caps, the power amp's Newton solve
///     failed to converge on a few samples a second under full-scale square-wave drive at full gain, hot bias and full
///     sag.
/// </remarks>
public static class JTM45 {
    public static AmpDesign Design { get; } = new(
        "JTM45",
        [
            MarshallParts.SharedCathodeInput(
                300,
                820,
                250e-6,
                new(0, 0, 20e-9, PotRole.Gain, 500e-12, 270e3, 500e-12),
                new(0, 0, 20e-9, PotRole.Control1, 0, 270e3, 0)
            ),
            new TriodeStageDesign("V2A", TriodeModel.Ecc83, 300, 100e3, 820),
            MarshallParts.FollowerStack("V2B", 300, 100e3, MarshallParts.JTM45Stack, 1e6)
        ],
        new(
            "2 x KT66",
            PentodeModel.KT66,
            1,
            440,
            470,
            0.035,
            false,
            0,
            45,
            new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
            0.1e-6,
            220e3,
            0,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 27e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6, 80),
            true,
            new(3.4e3),
            SpeakerDesign.Closed4x12,
            new(60, 32e-6, 200, 32e-6, 18.2e3, 16e-6, Rectifier: RectifierModel.GZ34),
            InverterInput: null
        ),
        -5
    ) { Panel = MarshallParts.TwoChannelPanel };
}
