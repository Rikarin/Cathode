using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Dual Rectifier Solo Head, three-channel revision, channel 3 (red) with Raw, Vintage and Modern: V1a
///     (220k from 413 V, 1.8k with 1 µF through 100 Ω), 20 nF into the voicing network (2.2M to ground; 2.2M ‖ 82 pF,
///     and 680k ‖ 2 nF except in Raw) and the 250k audio channel-3 gain (1 nF from its top to the wiper), 475k with
///     20 pF into V2a (100k from 394 V, 1.8k with 1 µF through 100 Ω), 20 nF and 475k into the V2b cold clipper (1M,
///     39k, 100k plate with 1 nF to the supply), 20 nF with 330k to ground and 220k into V3a (220k from 435 V, 1.8k
///     with 1 µF through 100 Ω), the cathode follower V3b (100k) into channel 3's stack (500 pF, and 180 pF more in
///     Modern, to the 250k linear treble; 47k; 20 nF to the 1M audio bass; 20 nF to the 25k linear middle's wiper), the
///     presence shunt on the treble wiper (22k and 3 nF, in Modern into the 100k linear presence pot) and the 1M audio
///     master; 20 nF into a long-tail pair (82k / 91k from 435 V, 470 Ω, 10k + 4.7k tail), feedback 47k with 0.1 µF
///     into the 4.7k except in Modern, with the presence pot and 0.1 µF across the 4.7k in Raw and Vintage; four 6L6GC
///     at 475 V with 1k screens, the rectifier switch (silicon or two 5U4) and Bold/Spongy. Channel 2 (orange) and
///     channel 1 (Clean / Pushed) are types of their own, <see cref="Orange" /> and <see cref="Clean" />.
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawing set "Dual Rectifier Solo Head 3 Channel" (block diagram, power
///         supplies, preamp parts 1-3, power amp, front and rear panel parts lists; "Preliminary Schematic"),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_3ch_dual_rectifier.pdf, read by eye. Node
///         voltages: A 475, B 471, C 435, D 394, E 413 V; bias -52 V.
///     </para>
///     <para>
///         The supply's decoupling, from the power supplies sheet: the choke to B (30 µF), 2.7k 2 W to C (three of the
///         seven 10 µF 450 V parts: V3, the loop, the inverter), and from C both 15k to D (two parts: V2) and 22k to E
///         (two parts: V1a); each node's draw is what its drawn voltage drops across its resistor (C 13.3 mA in all,
///         D 2.7 mA, E 1.0 mA).
///     </para>
///     <para>
///         Against the two-channel drawing (<see cref="DualRectifier" />): the gain pot is 250k (not 1M), the cathodes
///         carry 100 Ω in series with their 1 µF bypasses, the grid stoppers are 475k, the stack's treble cap is 500 pF
///         with 180 pF added in Modern (RY7a), Modern takes the feedback out (RY7b), and Raw drops the 680k ‖ 2 nF
///         branch (RY6a).
///     </para>
///     <para>
///         The presence pot (100k linear, Control 2) is moved by RY9. In Raw and Vintage it sits in the feedback: its
///         wiper through 0.1 µF to the top of the tail's 4.7k (FB'), its top to ground, so the 0.1 µF and the pot as a
///         rheostat lie across the 4.7k, and the 22k and 3 nF on the treble wiper go straight to ground. In Modern,
///         which has no feedback, the pot is the rheostat under the 22k and 3 nF. The modes are the handwritten notes
///         on the contacts ("raw vin" and "mod"). The printed relay driver on the voicing sheet would instead release
///         RY9 in Raw alone, which leaves Modern a presence pot in a feedback that RY7b has opened, so the notes are
///         followed.
///     </para>
///     <para>
///         Channel 2 (<see cref="Orange" />) shares the five triodes, the voicing network (RY6a in Raw) and RY7b
///         (Modern takes the feedback out). RY3 gives it a gain pot of its own with the same values and RY4 a stack of
///         its own: 500 pF with no 180 pF, the same 47k, 20 nF, 20 nF and pots, and a presence of 33k, 3 nF and a 25k
///         linear pot, moved by RY8. By the notes on RY8's contacts the pot is the preamp shunt's rheostat in Raw and
///         sits in the feedback in Vintage and Modern, so in Modern, without feedback, it has next to nothing to act
///         on (the printed driver holds RY8 on whenever the channel plays, which gives the same in Modern). In Raw the
///         feedback has no presence branch; here the pot's whole track stays in series with the 0.1 µF across the 4.7k
///         (4.7k ‖ 25k above 60 Hz: simplified, marked).
///     </para>
///     <para>
///         Guesses, marked: the feedback tap (8 Ω); the 120 pF across each inverter plate resistor, the 75 pF between
///         the plates and the master's 10 pF are left out; the effects loop and the Output/Solo controls are bypassed.
///         Not printed: the output transformer (1.9k), the idle current (40 mA). Raw is on the Bright switch (the
///         amp's three-way is Raw / Vintage / Modern, here Raw and Modern with both off for Vintage; with both on
///         the model is in Modern, <see cref="AmpPanel.ThreeWay" />).
///     </para>
///     <para>
///         Channel 1 (<see cref="Clean" />) is the Rect-o-Verb's rhythm channel with this drawing's values
///         (<see cref="RectoVerb" />): RY1a takes V1a's plate, with 1 nF to ground, into a stack of its own (250 pF,
///         150k, 0.1 µF, 47 nF; 250k audio treble and bass, 25k audio middle) and the 1M audio gain (120 pF from its
///         top to the wiper), then V2a with 20 pF at its grid and 15 µF straight across its cathode (RY1'a), 47 nF
///         and 3.3M ‖ 39 pF into V1b (330k and 47 pF from grid to ground; 100k from 394 V; 1.5k unbypassed), 47 nF
///         and 220k into the 1M audio master, with 250 pF and the channel's presence (2 nF into a 250k linear
///         rheostat, Control 2) from the master's top to ground. Pushed (RY5, RY5') puts 2 nF beside the 250 pF
///         treble cap, 2 nF across the 3.3M, and 1.5M ‖ (330k + 47 pF) ahead of the master, which Clean shorts.
///         RY2a releases RY7, RY8 and RY9 on this channel, so the feedback is in (47k into the 4.7k) with neither
///         presence pot in it. Guesses, marked: the J175 across V2a's 475k grid resistor is read as on (a closed
///         contact) on channel 1, as the Rect-o-Verb's drawing wires its rhythm gain straight to the grid; the 10M
///         and 20M beside RY5's caps (they keep them charged) are left out.
///     </para>
/// </remarks>
public static class DualRectifier3Channel {
    /// <summary>Channel 3 (red).</summary>
    public static AmpDesign Design { get; } = new(
        "Dual Rectifier 3-Ch",
        Preamp(180e-12, new(100e3, PotTaper.Linear), 22e3, SwitchRole.Switch1),
        Power(new(100e3, PotTaper.Linear), null),
        -7
    ) {
        Panel = DualRectifier.Design.Panel with {
            Bright = "Raw",
            // One Raw / Vintage / Modern switch on the amp: with both on it is Modern.
            ThreeWay = [new(SwitchRole.Bright, SwitchRole.Switch1)]
        },
        // From C: 15k to D (V2) and 22k to E (V1a), two 10 µF parts on each.
        PreampSupply = DualRectifier.Decoupling(394, 413, 20e-6, 435)
    };

    /// <summary>
    ///     Channel 2 (orange): channel 3's preamp into its own stack and presence (33k, 3 nF, 25k linear; the pot in
    ///     the shunt in Raw, in the feedback otherwise).
    /// </summary>
    public static AmpDesign Orange { get; } = Design with {
        Name = "Dual Rectifier 3-Ch Orange",
        Preamp = Preamp(null, new(25e3, PotTaper.Linear), 33e3, SwitchRole.Bright),
        Power = Power(new(25e3, PotTaper.Linear), new(SwitchRole.Bright))
    };

    /// <summary>
    ///     Channel 1 (Clean, and Pushed on Switch 1): V1a into its stack and gain, V2a, V1b and the master, with the
    ///     channel's presence a preamp shunt on Control 2 and a feedback that no presence pot is in.
    /// </summary>
    public static AmpDesign Clean { get; } = Design with {
        Name = "Dual Rectifier 3-Ch Clean",
        Preamp = [
            new NetworkStageDesign(
                "V1a",
                RectoVerb.RhythmInputAndStack(413, 100, 25e3, 20e-12, SwitchRole.Switch1)
            ) { SupplyNode = 2 },
            // The J175 across the 475k, as a closed contact.
            new NetworkStageDesign("V2a", RectoVerb.SecondStage(0, 394, 1, 100)) { SupplyNode = 1 },
            new NetworkStageDesign("V1b", CleanOutput()) { SupplyNode = 1 }
        ],
        Power = Power(new(25e3, PotTaper.Linear), null) with {
            // No presence branch across the 4.7k: a placeholder cap (FeedbackDesign.SmallestPresenceCap).
            Feedback = new(1, 47e3, 4.7e3, new(25e3, PotTaper.Linear), 1e-12),
            PresencePot = null,
            FeedbackOffSwitch = null
        },
        Panel = DualRectifier.Design.Panel with { Switch1 = "Pushed" }
    };

    /// <summary>
    ///     Channel 1's V1b and master: the Rect-o-Verb's rhythm triode from 394 V (no 1.5M after the 47 nF, no cap
    ///     across the plate resistor), 47 nF and 220k, then 1.5M ‖ (330k + 47 pF) that RY5'a shorts in Clean, into the
    ///     1M audio master with 250 pF and the presence (2 nF and a 250k linear rheostat) from its top to ground.
    /// </summary>
    static StageNetlist CleanOutput() {
        var net = new StageNetlist();
        var pad = RectoVerb.RhythmStage(net, 394, 0, 0, SwitchRole.Switch1, 220e3);
        var (top, branch, presence) = (net.Node(), net.Node(), net.Node());
        net.Resistor(pad, top, 1.5e6);
        net.Resistor(pad, branch, 330e3);
        net.Capacitor(branch, top, 47e-12);
        // RY5'a's closed contact in Clean.
        net.SwitchedResistor(pad, top, 1, 0, SwitchRole.Switch1);
        net.Capacitor(top, presence, 2e-9);
        net.Rheostat(presence, StageNetlist.Ground, new(250e3, PotTaper.Linear), PotRole.Control2);
        net.Capacitor(top, StageNetlist.Ground, 250e-12);
        net.Output = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        return net;
    }

    /// <summary>
    ///     V1a to V3b with a channel's stack: <paramref name="modern" /> beside the 500 pF treble cap in Modern, and
    ///     the presence shunt (<paramref name="shunt" />, 3 nF, <paramref name="presence" />) with the pot in it while
    ///     <paramref name="shuntMode" /> is on.
    /// </summary>
    static AmpStageDesign[] Preamp(double? modern, Pot presence, double shunt, SwitchRole shuntMode) => [
        new NetworkStageDesign("V1a", DualRectifier.InputStage(413, 100)) { SupplyNode = 2 },
        new NetworkStageDesign(
            "V2a",
            DualRectifier.GainStage(new(250e3, PotTaper.Audio), 475e3, 394, 100, SwitchRole.Bright)
        ) { SupplyNode = 1 },
        new NetworkStageDesign("V2b", DualRectifier.ColdClipper(475e3, 394)) { SupplyNode = 1 },
        new NetworkStageDesign("V3a", DualRectifier.FourthStage(435, 100)),
        new NetworkStageDesign(
            "V3b",
            DualRectifier.FollowerAndStack(435, 500e-12, modern, presence, shunt: shunt, shuntMode: shuntMode)
        )
    ];

    /// <summary>
    ///     The power amp with a channel's <paramref name="presence" /> pot (Control 2) and 0.1 µF across the tail's
    ///     4.7k, the pot out of the feedback while <paramref name="presenceOut" /> is on.
    /// </summary>
    static PowerAmpDesign Power(Pot presence, PresenceOutDesign? presenceOut) =>
        DualRectifier.Design.Power with {
            Supply = 475,
            PhaseInverter = new LongTailPairDesign(TriodeModel.Ecc83, 435, 82e3, 91e3, 14.7e3, 470),
            Feedback = new(1, 47e3, 4.7e3, presence, 0.1e-6),
            // 220 + 220 µF stacked, the choke to B's 30 µF, 2.7k to C's three 10 µF parts: 471 V to 435 V is 13.3 mA.
            PowerSupply = new(30, 110e-6, 100, 30e-6, 2.7e3, 30e-6, 13.3e-3, RectifierModel.Silicon),
            PresencePot = PotRole.Control2,
            PresenceOut = presenceOut
        };
}
