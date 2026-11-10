using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM2000 TSL100 (Triple Super Lead): Clean, Crunch and Lead with the Clean channel's Mid Boost, the
///     overdrive channels' Tone Shift, and Deep. The input (1M and 47 pF at the jack, 68k) into V1B (100k, 1k8 ‖
///     4.7 µF, 100 pF plate to cathode), 22 nF (1M to ground) to the front boards, where all three channels' networks
///     hang on the line. Clean: its tone controls come first, 220 pF onto the top of the 200k audio Treble and 100k
///     onto its foot, where the 200k audio Bass (a rheostat over 10k) and 47 nF + the 22k linear Middle (a rheostat)
///     go to ground; Mid Boost lifts the Middle's foot onto 220k ‖ 2.2 nF; the Treble's wiper drives the 1M audio
///     Gain (100 pF top to wiper), whose wiper goes through 470k to V2B's grid. Crunch: 4.7 nF and 470k onto a 200k
///     audio Gain, 470 pF top to wiper. Lead: 4.7 nF, then 470k ‖ (2.2 nF + 47k) onto a 1M audio Gain, 470 pF top to
///     wiper and 470 pF from the wiper to ground. The playing overdrive Gain's wiper drives V1A (1M leak; 100k, 1k8, 1
///     µF + 10k to ground, the 10k shorted in Lead; 470 pF from the plate to the rail), 2.2 nF (1M to ground), 10k,
///     1k with 100 pF to ground and 470k into V2B's grid (2M2 to ground), where the paths mix. V2B (100k, 4k7
///     unbypassed), 22 nF, 470k into V2A (470k leak; 100k, 2k2, and 10 nF across it in Clean), 22 nF, 220k to the
///     reverb send's node (100k to ground), 100k into V3B's grid (47k and 47k + 2k2 to ground), V3B (100k, 1k
///     unbypassed), DC-coupled to the V3A follower (100k), 100 nF to the front boards. Clean: 47k onto the 200k audio
///     Volume, its wiper through 470k to the output. Crunch and Lead: each its own stack, the DSL100's with Tone Shift
///     (<see cref="Jcm2000Parts.ToneShiftStack" />), the treble wiper's 470k onto a 1M audio Volume, its wiper to
///     the output. From there 22 nF and 1M into the inverter and the DSL100's power amp
///     (<see cref="Jcm2000Parts.Design" />), with 47k from the 4 Ω tap to the feedback node, where each channel
///     group has a 5k linear Presence with 220 nF and a Deep network into an RC4558 gyrator (270 Ω, 330 Ω, 10 µF).
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawings TL10-60-02 issue 7 (TSL100/TSL122 main board, 2003), TL10-61-02 issue 4
///         (overdrive front board, 1998: the Lead channel's controls, the routing LDRs and relays, both gyrators),
///         TL10-65-02 issue 4 (Clean channel board), TL10-66-01 issue 2 (Crunch channel board, which also carries
///         the Clean Presence), TL10-62-02 issue 6 (rear board: the 47k feedback resistor on the 4 Ω tap) and
///         TL10-63-02 issue 4 (reverb board: its drive's 100k + 1M input), vector PDFs on DrTube
///         (drtube.com/marshall-jcm2000-tsl), and the 2004 handbook from the same page. Every value above is read
///         from them. Issue 20 of the main board (2007) draws V1B's plate resistor as 220k; issue 7's 100k is used.
///     </para>
///     <para>
///         Inferred, because these sheets print no connector cross-references: which connector meets which, from
///         pin counts, grounds and function (main CON3, V1B's output, to the Clean board and on to the other two;
///         main CON6, the 470k into V2B, from the Clean Gain's wiper; main CON4, V1A's grid, from the overdrive
///         board's two LDRs; main CON5, V1A's bypass foot, to relay RL2's contact; main CON9, V3A's output, to the
///         overdrive board, which passes it through 47k to the Clean Volume and through an LDR each to the Crunch
///         and Lead stacks; the three Volumes' wipers to main CON11, the inverter; main CON13, the feedback node,
///         to relay RL4, which picks the Clean or the overdrive Presence and Deep network). Which state is which
///         channel: the main board's control line lights LDR3 (which grounds V1A's path at the 10k / 1k junction)
///         and turns on TR2 (10 nF across V2A's cathode resistor) together, taken as Clean; RL2's contact shorts
///         V1A's 10k in Lead, the channel the handbook gives "a lot more gain". That each rheostat's resistance
///         rises with its knob.
///     </para>
///     <para>
///         Guessed: the 470 V B+, the idle current and the output transformer, as on the DSL100 (no drawing prints
///         them). The rails follow from that B+ through the drawn chain (270 Ω to the screens, 4k7 to the
///         inverter, 1k to V3, 4k7 to V2A, then 10k to V1B, and 10k to V2B with 2k2 on to V1A) at the stages' idle
///         currents: 415 V at the inverter, 405, 390, 375, 370 and 370 V. Simplified: a lit LDR and a conducting
///         JFET as a closed contact (10 Ω), a dark LDR as 100 MΩ; the reverbs at zero and the effects loops out
///         (the reverb return as ground behind its 47k + 2k2; the sends' 2M2 + 680k as loads); the switching mutes
///         off; the Virtual Power Reduction and Output Mute switches out; the gyrator's op-amp as ideal, and the
///         47 µF between the feedback node and the 4k7 as a wire. One feedback network serves all three channels:
///         the overdrive one (Deep shorts 10k). The Clean one differs only in that its Deep switch shorts 4k7; the
///         100k from each feedback node to ground is left out.
///     </para>
///     <para>
///         Panel: Switch 1 selects the overdrive channels (off: Clean), Switch 2 Lead instead of Crunch, as the
///         amp's two mode switches do; Switch 3 is the playing channel's voicing switch (Mid Boost on Clean, Tone
///         Shift on Crunch and Lead); Deep is the block's Bright switch, as on the DSL100. Gain, Volume, Bass,
///         Middle and Treble are the playing channel's own.
///     </para>
/// </remarks>
public static class Tsl100 {
    const double Closed = 10; // a lit LDR, a conducting JFET, a closed contact
    const double Dark = 100e6; // a dark LDR
    const double V1BRail = 375;
    const double V1ARail = 370;
    const double V2BRail = 370;
    const double V2ARail = 390;
    const double V3Rail = 405;

    public static AmpDesign Design { get; } = Jcm2000Parts.Design(
        "TSL100",
        new(
            "Channel",
            SwitchRole.Switch1,
            [new NetworkStageDesign("V1B Clean", CleanInput())],
            [
                new NetworkStageDesign("V1B Overdrive", OverdriveInput()),
                new NetworkStageDesign("V1A", Jcm2000Parts.LeadStage(V1ARail, 2.2e-9, 100e-12))
            ]
        ),
        (V2BRail, V3Rail, 415),
        ThirdStage(),
        Output(),
        Math.Sqrt(0.5),
        0.22e-6,
        ("Overdrive", "Lead", "Mid Boost / Tone Shift")
    );

    /// <summary>
    ///     The input jack (1M and 47 pF to ground), 68k and V1B, with both overdrive Gain networks on its line as
    ///     they always are. Returns the line and the Crunch and Lead Gains' wipers.
    /// </summary>
    static (int Line, int Crunch, int Lead) InputStage(StageNetlist net) {
        var grid = net.Node();
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Capacitor(net.Input, StageNetlist.Ground, 47e-12);
        net.Resistor(net.Input, grid, 68e3);
        var line = Jcm2000Parts.InputStage(net, grid, V1BRail, 100e3);

        // Crunch: 4.7 nF and 470k onto the 200k audio Gain, 470 pF from its top to its wiper.
        var (crunchIn, crunchTop) = (net.Node(), net.Node());
        net.Capacitor(line, crunchIn, 4.7e-9);
        net.Resistor(crunchIn, crunchTop, 470e3);
        var crunch = net.Pot(crunchTop, StageNetlist.Ground, new(200e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(crunchTop, crunch, 470e-12, CapacitorRole.Bright);

        // Lead: 4.7 nF, 470k ‖ (2.2 nF + 47k) onto the 1M audio Gain, 470 pF top to wiper, 470 pF wiper to ground.
        var (leadIn, leadArm, leadTop) = (net.Node(), net.Node(), net.Node());
        var lead = Jcm2000Parts.LeadGain(net, line, leadIn, leadArm, leadTop);
        net.Capacitor(lead, StageNetlist.Ground, 470e-12);
        return (line, crunch, lead);
    }

    /// <summary>
    ///     V1B and the Clean channel's network: the lit LDR, the tone controls (Mid Boost on switch 3), the Gain,
    ///     its wiper's 470k into V2B's grid (2M2 to ground), the muted V1A path's 470k + 1k to ground there. Out at
    ///     V2B's grid.
    /// </summary>
    static StageNetlist CleanInput() {
        var net = new StageNetlist();
        var (line, _, _) = InputStage(net);
        var (feed, trebleTop, foot, bassFoot, middleTop, middleFoot, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(line, feed, Closed);
        net.Capacitor(feed, trebleTop, 220e-12, CapacitorRole.Stack);
        net.Resistor(feed, foot, 100e3);
        var treble = net.Pot(trebleTop, foot, new(200e3, PotTaper.Audio), PotRole.Treble);
        net.Rheostat(foot, bassFoot, new(200e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(bassFoot, StageNetlist.Ground, 10e3);

        // Middle: 47 nF and the 22k rheostat to ground; Mid Boost opens the contact across 220k ‖ 2.2 nF.
        net.Capacitor(foot, middleTop, 47e-9, CapacitorRole.Stack);
        net.Rheostat(middleTop, middleFoot, new(22e3, PotTaper.Linear), PotRole.Middle);
        net.Resistor(middleFoot, StageNetlist.Ground, 220e3);
        net.Capacitor(middleFoot, StageNetlist.Ground, 2.2e-9);
        net.SwitchedResistor(middleFoot, StageNetlist.Ground, Closed, 0, SwitchRole.Switch3);

        var wiper = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(treble, wiper, 100e-12, CapacitorRole.Bright);
        net.Resistor(wiper, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 471e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V1B and the overdrive Gains: an LDR takes the Crunch wiper, or with switch 2 the Lead one, to V1A's grid
    ///     (1M to ground). Out there. The Clean network sits behind its dark LDR and is left out.
    /// </summary>
    static StageNetlist OverdriveInput() {
        var net = new StageNetlist();
        var (_, crunch, lead) = InputStage(net);
        var grid = net.Node();
        net.SwitchedResistor(crunch, grid, Closed, Dark, SwitchRole.Switch2);
        net.SwitchedResistor(lead, grid, Dark, Closed, SwitchRole.Switch2);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2A (<see cref="Jcm2000Parts.ThirdStage" />) with what this board adds: 10 nF from the cathode to a JFET
    ///     that grounds it in Clean (100k across the JFET), and at the reverb send's node 100k to ground and the
    ///     reverb drive's 100k + 1M.
    /// </summary>
    static StageNetlist ThirdStage() {
        var (net, cathode, send) = Jcm2000Parts.ThirdStage(V2ARail);
        var foot = net.Node();
        net.Capacitor(cathode, foot, 10e-9);
        net.Resistor(foot, StageNetlist.Ground, 100e3);
        net.SwitchedResistor(foot, StageNetlist.Ground, Closed, 0, SwitchRole.Switch1);
        net.Resistor(send, StageNetlist.Ground, 100e3);
        net.Resistor(send, StageNetlist.Ground, 1.1e6);
        return net;
    }

    /// <summary>
    ///     The V3A follower (100k) and 100 nF to the front boards. Clean: 47k onto the 200k audio Volume, its wiper
    ///     through an LDR and 470k to the output (a JFET grounds the wiper in the other channels). Crunch and Lead
    ///     (switch 1): an LDR into the playing channel's stack (the two are alike, so one stands for both), the
    ///     treble wiper's 470k onto the 1M audio Volume, its wiper through an LDR to the output, which the reverb
    ///     and effects sends (2M2 + 680k, 2M2) and the loop relay's 2M2 load. Out there.
    /// </summary>
    static StageNetlist Output() {
        var net = new StageNetlist();
        var (rail, cathode, line, stackIn) = (net.Node(), net.Node(), net.Node(), net.Node());
        var stack = Jcm2000Parts.StackNodes.Of(net);
        var (volumes, cleanTop, cleanOut, output) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V3Rail);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Capacitor(cathode, line, 100e-9);

        net.SwitchedResistor(line, stackIn, Dark, Closed, SwitchRole.Switch1);
        var treble = Jcm2000Parts.ToneShiftStack(net, stackIn, stack);
        net.Resistor(treble, volumes, 470e3);
        var overdrive = net.Pot(volumes, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.SwitchedResistor(overdrive, output, Dark, Closed, SwitchRole.Switch1);

        net.Resistor(line, cleanTop, 47e3);
        var clean = net.Pot(cleanTop, StageNetlist.Ground, new(200e3, PotTaper.Audio), PotRole.Master);
        net.SwitchedResistor(clean, StageNetlist.Ground, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(clean, cleanOut, Closed, Dark, SwitchRole.Switch1);
        net.Resistor(cleanOut, output, 470e3);

        net.Resistor(output, StageNetlist.Ground, 2.88e6);
        net.Resistor(output, StageNetlist.Ground, 2.2e6);
        net.Resistor(output, StageNetlist.Ground, 2.2e6);
        net.Output = output;
        return net;
    }
}
