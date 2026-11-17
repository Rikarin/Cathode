using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Victory Kraken VX, the Lead and Crunch channels. The jack's 100 nF onto 1M and the 10k stopper into VL1.1
///     (220k, 1k5 ‖ 2.2 µF, 100 pF from plate to cathode), whose plate feeds both channels: Lead, 470 pF and 180k onto
///     120k at VL1.2's grid (100k, 1k5 ‖ 2.2 µF), then 2.2 nF and 330k ‖ 470 pF onto the 1M audio Lead Gain pot;
///     Crunch, 2.2 nF and 1M ‖ 470 pF onto the 1M audio Crunch Gain pot. The channel relay takes either wiper to VL3.1
///     (2M2 leak, 100k with 1 nF across it, 1k5 unbypassed), then 47 nF, 330k and 330k into VL2.1 (220k, 1k5 ‖ 33 nF),
///     DC-coupled to the VL2.2 follower (100k), which drives the Kraken's stack (330 pF, 47k slope, 22 nF to the bass,
///     33 nF to the middle; 250k linear treble, 1M audio bass on 10k, 25k linear middle, each leg to ground on its
///     own); the treble wiper through 680k onto 10k, into VL3.2 (1M leak, 220k, 1k5 unbypassed), 33 nF onto the 1M
///     audio Master 1 or Master 2, the wiper through 680k and 22 nF into the 12AX7 long-tail pair (91k/100k, 470 Ω,
///     10k tail on the 4k7 feedback shunt, 1M leaks), 33 nF to two 6L6 on 10k stoppers and 220k leaks, 1k screens.
///     Feedback from the 8 Ω tap: 39k, then 220k with 4.7 nF across it (and Bass Focus's 47k), then 15k that the
///     channel relay shorts on Lead.
/// </summary>
/// <remarks>
///     <para>
///         Source: "Victory Kraken VX", preamp section and poweramp section, a two-sheet trace by Aznaur A. Gishian
///         (attached to issue #487; not a Victory drawing: Victory publishes none). It gives every value above, the
///         supply's 445 V B+ ("HIGH 445V", with a 160 V Low tap on the standby switch), its 100 Ω to the screens and
///         four 10k droppers (nodes 3 to 6), the relay map (RLA2 and RLA3 Crunch/Lead, RLA4 Master 1/2, RLA1 and
///         RLA5 the loop) and the bias supply's 6L6/EL34 switch.
///     </para>
///     <para>
///         Inferred: which way the channel relays rest. RLA2 is drawn resting on the Crunch Gain wiper and RLA3
///         open, so Crunch is the de-energised channel and has the 15k in its feedback, and Lead (the energised one)
///         shorts it: Lead has a third more feedback at mid frequencies. The default here is Lead, with Switch 1
///         the Crunch channel. C40, drawn dotted across the 330 pF with no value, is taken as unfitted.
///     </para>
///     <para>
///         Derived, as the trace prints no voltage below B+: the preamp rails, from the drawn dropping chain at the
///         stages' own rest currents (the inverter and VL3.2 on node 3 at 359 V, VL2 on node 4 at 310 V, VL1.2 and
///         VL3.1 on node 5 at 284 V, VL1.1 on node 6 at 277 V; 8.5 mA through R43). Guessed: the idle current (40 mA),
///         the output transformer (4.4k plate to plate, as on the other 50 W 6L6 heads here) and the transformer's
///         source resistance. Simplified: the 470 pF between the inverter's plates is the power amp's cut cap on a pot
///         of 1 Ω (a fixed pole, which the Cut knob, absent on this panel, cannot move); the 33 nF from the inverter's
///         second grid goes to the feedback node, not to ground, which the inverter's tables take as the usual
///         AC-grounded grid with the feedback in the differential input; the 1 µF in series with the feedback (a corner
///         under 1 Hz) is left out; the effects loop's relays are closed straight through; the Low tap and the
///         single-ended switch (S2) are not modelled.
///     </para>
///     <para>
///         Panel: Gain is the playing channel's gain pot, Master is Master 1, and <b>Master 2</b> (Switch 2) picks
///         the second master, Control 1. <b>Bass Focus</b> (Switch 3) is S1, the 47k across the feedback's 220k:
///         more feedback below a few hundred hertz, a tighter low end. The feedback network is a
///         <see cref="FeedbackVariantDesign" /> per setting of the channel and Bass Focus. The amp has no presence,
///         depth, cut or bright switch.
///     </para>
/// </remarks>
public static class VictoryKraken {
    const double Closed = 10;
    const double Open = 1e12;

    /// <summary>Drawing node 3, the 10k below the screens: the inverter and VL3.2.</summary>
    const double Node3Rail = 359;

    /// <summary>Node 4: VL2.1 and the follower.</summary>
    const double Node4Rail = 310;

    /// <summary>Node 5: VL1.2 and VL3.1.</summary>
    const double Node5Rail = 284;

    /// <summary>Node 6: VL1.1.</summary>
    const double Node6Rail = 277;

    /// <summary>The feedback shunt, R23, under the inverter's 10k tail.</summary>
    const double Shunt = 4.7e3;

    /// <summary>The panel. (Declared before the design, which reads it as it is initialised.)</summary>
    public static AmpPanel Panel { get; } = AmpPanel.Standard with {
        Presence = null,
        Depth = null,
        Cut = null,
        Bright = null,
        Master = "Master 1",
        Switch1 = "Crunch Ch",
        Switch2 = "Master 2",
        Switch3 = "Bass Focus",
        Control1 = "Master 2"
    };

    /// <summary>The Kraken VX: two 6L6.</summary>
    public static AmpDesign Design { get; } = new(
        "Kraken VX",
        [
            new NetworkStageDesign("VL1.1", FirstStage()) { SupplyNode = 3 },
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("VL1.2 VL3.1", Channel(false)) { SupplyNode = 2 }],
                [new NetworkStageDesign("VL3.1 Crunch", Channel(true)) { SupplyNode = 2 }]
            ),
            new NetworkStageDesign("VL2", FollowerStack()) { SupplyNode = 1 },
            new NetworkStageDesign("VL3.2", Recovery())
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            445,
            1e3,
            0.04,
            false,
            0,
            50,
            // The 10k tail stands on the 4k7 feedback shunt: 14.7k from the bias junction to ground.
            new LongTailPairDesign(TriodeModel.Ecc83, Node3Rail, 91e3, 100e3, 10e3 + Shunt, 470),
            33e-9,
            220e3,
            10e3,
            // C24, 470 pF between the inverter's plates, as a cut cap on a 1 Ω pot (see the remarks).
            470e-12,
            new(1, PotTaper.Linear),
            Feedback(false, false),
            true,
            new(4.4e3),
            SpeakerDesign.Closed4x12,
            // The bridge into C8 100 µF, R 100 Ω to the screens' 47 µF, R43 10k to node 3's 47 µF, which carries
            // every preamp stage's current.
            new(40, 100e-6, 100, 47e-6, 10e3, 47e-6, 8.48e-3, RectifierModel.Silicon),
            InverterInput: new(22e-9, 1e6)
        ) {
            FeedbackVariants = [
                new([SwitchRole.Switch1, SwitchRole.Switch3], Feedback(true, true)),
                new([SwitchRole.Switch1], Feedback(true, false)),
                new([SwitchRole.Switch3], Feedback(false, true))
            ]
        },
        -8
    ) {
        Panel = Panel,
        // R44, R2 and R3, 10k into 47 µF each: node 4 (VL2), node 5 (VL1.2, VL3.1) and node 6 (VL1.1).
        PreampSupply = new([new(10e3, 47e-6, 2.18e-3), new(10e3, 47e-6, 1.97e-3, 1), new(10e3, 47e-6, 0.70e-3, 2)])
    };

    /// <summary>
    ///     The feedback from the 8 Ω tap into the 4k7 shunt: 39k, then 220k with 4.7 nF across it (and, with
    ///     <paramref name="focus" />, 47k), then on <paramref name="crunch" /> the 15k the Lead relay shorts. Above
    ///     the 4.7 nF's corner only the fixed resistors count; below it the 220k (or 220k ‖ 47k) adds to them: a low
    ///     shelf with its pole at <see cref="FeedbackDesign.DepthFrequency" /> and its zero at the 4.7 nF's own
    ///     corner. The 1 µF in series (under 1 Hz) is left out.
    /// </summary>
    static FeedbackDesign Feedback(bool crunch, bool focus) {
        var fixedArm = 39e3 + (crunch ? 15e3 : 0);
        var leg = focus ? 1 / (1 / 220e3 + 1 / 47e3) : 220e3;
        var (mid, low) = (fixedArm + Shunt, fixedArm + leg + Shunt);
        var zero = 1 / (2 * Math.PI * leg * 4.7e-9);
        return new(1, fixedArm, Shunt, new(1e3, PotTaper.Linear), 1e-12, zero * low / mid, mid / low);
    }

    /// <summary>
    ///     The jack's 100 nF onto R8 1M, R7 10k into VL1.1: 220k from node 6, 1k5 with 2.2 µF, and C26 100 pF from
    ///     the plate to the cathode. Out at the plate.
    /// </summary>
    static StageNetlist FirstStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, grid, jack) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, Node6Rail);
        net.Resistor(rail, plate, 220e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 2.2e-6);
        net.Capacitor(plate, cathode, 100e-12);

        // The input: the jack's 100 nF onto 1M, and the 10k stopper.
        net.Capacitor(net.Input, jack, 100e-9);
        net.Resistor(jack, StageNetlist.Ground, 1e6);
        net.Resistor(jack, grid, 10e3);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     VL1.1's plate into both channels' networks, and the playing channel's gain wiper into VL3.1 (R19 2M2 leak,
    ///     100k with C4 1 nF across it from node 5, 1k5 unbypassed, with its grid-plate capacitance behind the pot).
    ///     Crunch: C38B 2.2 nF, R45 1M ‖ C37 470 pF onto VR1, the 1M audio Crunch Gain. Lead: C38A 470 pF and R27
    ///     180k onto R31 120k at VL1.2's grid (100k, 1k5 ‖ 2.2 µF), C2 2.2 nF, R30 330k ‖ C12 470 pF onto VR2, the
    ///     1M audio Lead Gain. The idle channel's network stays on VL1.1's plate (the Lead's as far as VL1.2's grid).
    ///     Then C36 47 nF and R34 330k onto R35 330k; out there, at VL2.1's grid.
    /// </summary>
    static StageNetlist Channel(bool crunch) {
        var net = new StageNetlist();

        // Crunch: 2.2 nF, then 1M with 470 pF across it onto the gain pot's top.
        var (crunchFeed, crunchTop) = (net.Node(), net.Node());
        net.Capacitor(net.Input, crunchFeed, 2.2e-9);
        net.Resistor(crunchFeed, crunchTop, 1e6);
        net.Capacitor(crunchFeed, crunchTop, 470e-12);

        // Lead: 470 pF and 180k onto 120k at VL1.2's grid.
        var (leadFeed, leadGrid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, leadFeed, 470e-12);
        net.Resistor(leadFeed, leadGrid, 180e3);
        net.Resistor(leadGrid, StageNetlist.Ground, 120e3);

        int wiper;
        if (crunch) {
            wiper = net.Pot(crunchTop, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        } else {
            net.Resistor(crunchTop, StageNetlist.Ground, 1e6);
            var plate = net.CommonCathode(leadGrid, TriodeModel.Ecc83, Node5Rail, 100e3, 1.5e3, 2.2e-6);
            var (feed, top) = (net.Node(), net.Node());
            net.Capacitor(plate, feed, 2.2e-9);
            net.Resistor(feed, top, 330e3);
            net.Capacitor(feed, top, 470e-12);
            wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        }

        net.Resistor(wiper, StageNetlist.Ground, 2.2e6);
        var third = net.CommonCathode(wiper, TriodeModel.Ecc83, Node5Rail, 100e3, 1.5e3, 0, Ab763.GridPlate, 1e-9);

        // C36 47 nF and R34 330k onto R35 330k: VL2.1's grid, where the stage ends (VL3.1's plate, with C4 across
        // its load, is a poor Thévenin source to cut at).
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(third, coupled, 47e-9);
        net.Resistor(coupled, grid, 330e3);
        net.Resistor(grid, StageNetlist.Ground, 330e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     VL2.1 from its grid (220k from node 4, 1k5 with C13 33 nF; the divider ahead of it ends the channel stage),
    ///     DC-coupled to
    ///     the VL2.2 follower (100k), the stack on its cathode, and the treble wiper through R9 680k onto R10 10k
    ///     (the output).
    /// </summary>
    static StageNetlist FollowerStack() {
        var net = new StageNetlist();
        var (cathode, output) = (net.Node(), net.Node());
        var (plate, rail) = net.CommonCathodeOnRail(net.Input, TriodeModel.Ecc83, Node4Rail, 220e3, 1.5e3, 33e-9);
        net.Triode(rail, plate, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        var treble = Stack(net, cathode);
        net.Resistor(treble, output, 680e3);
        net.Resistor(output, StageNetlist.Ground, 10e3);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The Kraken's stack on <paramref name="source" />: C16 330 pF to the top of VR5 (250k linear treble) and
    ///     R39 47k to the slope node; from it C10 22 nF to the treble pot's foot, under which VR4 (1M audio bass, a
    ///     rheostat) and R5 10k go to ground, and C18 33 nF to VR3 (25k linear middle, a rheostat) to ground. Unlike
    ///     the Marshall stack the bass leg does not stand on the middle pot. Returns the treble wiper.
    /// </summary>
    static int Stack(StageNetlist net, int source) {
        var (top, slope, foot, bassFoot, middle) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(source, top, 330e-12, CapacitorRole.Stack);
        net.Resistor(source, slope, 47e3);
        var wiper = net.Pot(top, foot, new(250e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(slope, foot, 22e-9, CapacitorRole.Stack);
        net.Rheostat(foot, bassFoot, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Resistor(bassFoot, StageNetlist.Ground, 10e3);
        net.Capacitor(slope, middle, 33e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        return wiper;
    }

    /// <summary>
    ///     VL3.2 (R12 1M leak, R40 220k from node 3, R41 1k5 unbypassed), C19 33 nF to the master RLA4 picks (VR6
    ///     Master 1, or with Switch 2 VR7 Master 2, both 1M audio; the other is lifted at both ends), its wiper
    ///     through R56 680k (the output, which the inverter's 22 nF and 1M take).
    /// </summary>
    static StageNetlist Recovery() {
        var net = new StageNetlist();
        var (coupled, topOne, topTwo, picked, output) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        var plate = net.CommonCathode(net.Input, TriodeModel.Ecc83, Node3Rail, 220e3, 1.5e3);
        net.Capacitor(plate, coupled, 33e-9);
        net.SwitchedResistor(coupled, topOne, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(coupled, topTwo, Open, Closed, SwitchRole.Switch2);
        var one = net.Pot(topOne, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        var two = net.Pot(topTwo, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Control1);
        net.SwitchedResistor(one, picked, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(two, picked, Open, Closed, SwitchRole.Switch2);
        net.Resistor(picked, output, 680e3);
        net.Output = output;
        return net;
    }
}
