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
///     Not on the drawing, so typical values (unverified): B+ 470 V, preamp nodes 310 V (V2, inverter) and 290 V (V1),
///     the output transformer's 1.7k plate to plate (half the 2204's 3.2k), the reservoir 100 µF and the choke's
///     resistance. Left out: the 47 pF across the inverter's outputs (a pole near 50 kHz). The supply's "50+50 µF" cans
///     are taken in parallel, as AmpBooks reads the 2204's.
/// </remarks>
public static class JCM2203 {
    public static AmpDesign Design { get; } = new(
        "JCM800 2203",
        MarshallParts.JCM800Preamp(290, 310),
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
            new LongTailPairDesign(TriodeModel.Ecc83, 310, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            MarshallParts.JCM800Feedback,
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 100e-6, 100, 100e-6, 10e3, 50e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -8
    ) { Panel = MarshallParts.MasterVolumePanel };
}
