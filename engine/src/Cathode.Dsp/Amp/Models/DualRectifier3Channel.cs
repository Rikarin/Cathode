using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Dual Rectifier Solo Head, three-channel revision, channel 3 (red) with Raw, Vintage and Modern: V1a
///     (220k from 413 V, 1.8k with 1 µF through 100 Ω), 20 nF into the voicing network (2.2M to ground; 2.2M ‖ 82 pF,
///     and 680k ‖ 2 nF except in Raw) and the 250k audio channel-3 gain (1 nF from its top to the wiper), 475k with
///     20 pF into V2a (100k from 394 V, 1.8k with 1 µF through 100 Ω), 20 nF and 475k into the V2b cold clipper (1M,
///     39k, 100k plate with 1 nF to the supply), 20 nF with 330k to ground and 220k into V3a (220k from 435 V, 1.8k with
///     1 µF through 100 Ω), the cathode follower V3b (100k) into channel 3's stack (500 pF, and 180 pF more in Modern, to
///     the 250k linear treble; 47k; 20 nF to the 1M audio bass; 20 nF to the 25k linear middle's wiper), the presence
///     shunt on the treble wiper (22k, 3 nF and the 100k linear presence pot) and the 1M audio master; 20 nF into a
///     long-tail pair (82k / 91k from 435 V, 470 Ω, 10k + 4.7k tail), feedback 47k with 0.1 µF into the 4.7k except in
///     Modern; four 6L6GC at 475 V with 1k screens, the rectifier switch (silicon or two 5U4) and Bold/Spongy.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawing set "Dual Rectifier Solo Head 3 Channel" (block diagram, power
///         supplies, preamp parts 1-3, power amp, front and rear panel parts lists; "Preliminary Schematic"),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_3ch_dual_rectifier.pdf, read by eye. Node
///         voltages: A 475, B 471, C 435, D 394, E 413 V; bias -52 V.
///     </para>
///     <para>
///         Against the two-channel drawing (<see cref="DualRectifier" />): the gain pot is 250k (not 1M), the cathodes
///         carry 100 Ω in series with their 1 µF bypasses, the grid stoppers are 475k, the stack's treble cap is 500 pF
///         with 180 pF added in Modern (RY7a), Modern takes the feedback out (RY7b) and moves the presence pot from the
///         feedback into the preamp shunt (RY9), and Raw drops the 680k ‖ 2 nF branch (RY6a; Raw and Vintage share the
///         other relays). As read from the relay contacts: in Raw and Vintage the 22k and 3 nF stay on the treble wiper
///         and the presence pot sits in the feedback; here the presence pot is the preamp shunt's rheostat in every mode
///         (Control 2) and the feedback's presence is fixed at noon (simplified, marked).
///     </para>
///     <para>
///         Guesses, marked: the feedback tap (8 Ω); the 120 pF across each inverter plate resistor, the 75 pF between the
///         plates and the master's 10 pF are left out; the effects loop and the Output/Solo controls are bypassed. Not
///         printed: the output transformer (1.9k), the idle current (40 mA). Raw is on the Bright switch (the panel's
///         three-way is Raw / Vintage / Modern; with Raw and Modern both on, Raw's voicing and Modern's stack, feedback
///         and presence apply together).
///     </para>
/// </remarks>
public static class DualRectifier3Channel {
    public static AmpDesign Design { get; } = new(
        "Dual Rectifier 3-Ch",
        [
            new NetworkStageDesign("V1a", DualRectifier.InputStage(413, 100)),
            new NetworkStageDesign(
                "V2a",
                DualRectifier.GainStage(new(250e3, PotTaper.Audio), 475e3, 394, 100, SwitchRole.Bright)
            ),
            new NetworkStageDesign("V2b", DualRectifier.ColdClipper(475e3, 394)),
            new NetworkStageDesign("V3a", DualRectifier.FourthStage(435, 100)),
            new NetworkStageDesign(
                "V3b",
                DualRectifier.FollowerAndStack(435, 500e-12, 180e-12, new(100e3, PotTaper.Linear))
            )
        ],
        DualRectifier.Design.Power with {
            Supply = 475,
            PhaseInverter = new LongTailPairDesign(TriodeModel.Ecc83, 435, 82e3, 91e3, 14.7e3, 470),
            Feedback = new(1, 47e3, 4.7e3, new(100e3, PotTaper.Linear), 0.1e-6, 90),
            // 220 + 220 µF stacked, the choke to B's 30 µF, 2.7k to C (10 µF parts, about 20 µF on the node).
            PowerSupply = new(30, 110e-6, 100, 30e-6, 2.7e3, 20e-6, 6e-3, RectifierModel.Silicon)
        },
        -7
    ) {
        Panel = DualRectifier.Design.Panel with { Bright = "Raw" }
    };
}
