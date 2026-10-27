using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall 1959-style (a 50 W "1987" with the channels jumpered down to one path): V1A, the volume with its bright
///     cap and the 470k mixing resistor (bypassed by 470 pF), V2A with its 0.68 µF partial bypass, the V2B cathode
///     follower DC-coupled to V2A and driving the Marshall stack, the (added) master, a 12AX7 long-tail pair, two EL34
///     at 460 V on a silicon rectifier, feedback from the 16 Ω tap with presence.
/// </summary>
/// <remarks>
///     A generic type, not one drawing. The inverter is the Marshall long-tail pair of every drawing (1959 70-6-11, 2203,
///     JTM45: 470 Ω shared cathode resistor under the 10k tail, 82k / 100k plates). The bright cap is a deliberate generic
///     choice: the later Marshalls' 470 pF across the volume, switched by Bright (off by default), where a 1960s 1987's
///     bright channel has a fixed 5 nF (the <see cref="Marshall1987X" /> and <see cref="SuperLead100" /> types have the
///     drawings' circuits). Like those amps it has no depth control.
/// </remarks>
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
                250e-6,
                68e3,
                GridLeak: 1e6
            ),
            new TriodeStageDesign(
                "V2A",
                TriodeModel.Ecc83,
                300,
                100e3,
                820,
                0.68e-6,
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
                300,
                100e3,
                ToneStackDesign.Marshall,
                new(1e6, PotTaper.Audio),
                PotRole.Master
            )
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            1,
            460,
            1e3,
            0.035,
            false,
            0,
            50,
            new LongTailPairDesign(TriodeModel.Ecc83, 330, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 100e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6, 80),
            true,
            new(3.4e3),
            new(Resonance: 100, PeakImpedance: 50, Q: 3),
            new(40, 50e-6, 300, 50e-6, 10e3, 16e-6)
        ),
        -5
    ) { Panel = AmpPanel.Standard with { Depth = null, Cut = null } };
}
