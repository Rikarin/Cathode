using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Marshall JCM2000 DSL100 (Dual Super Lead), both channels with their modes, Tone Shift and Deep. The input (1M,
///     68k) into V1B (220k, 1k8 ‖ 4.7 µF, 100 pF plate to cathode), 22 nF (1M to ground) to the front panel, where
///     relay RL1 feeds one channel's network and grounds the other's input. Classic Gain: Clean, 220 pF + 82k beside
///     100k into a 150k bridge, its foot on 1M and on 47 nF + 10k to ground, 100k from the bridge's top onto the 1M
///     audio Gain; Crunch, 4.7 nF and 470k ‖ 470 pF onto the Gain, 1 nF from its top to its wiper, the 47 nF's 10k on
///     1M; 100 pF from the Gain's top to its wiper in both; the wiper's 470k into V2B's grid. Ultra Gain: 4.7 nF, then
///     470k ‖ (2.2 nF + 47k) onto the 1M audio Gain, 470 pF from its top to its wiper; Lead 1 grounds the junction of
///     150k from its top and 470 pF from its wiper, Lead 2 lifts it (the two then bridge the pot, 10M at their
///     junction) and fully bypasses V1A's cathode; the wiper drives V1A (100k, 1k8, 1 µF + 10k to ground, or 1 µF in
///     Lead 2; 470 pF from the plate to the rail), 4.7 nF (1M to ground), 10k, 1k with 470 pF to ground and 470k into
///     V2B's grid (2M2 to ground), where the channels mix. V2B (100k, 4k7 unbypassed), 22 nF, 470k into V2A (470k leak;
///     100k, 2k2 unbypassed), 22 nF, 220k, 100k into V3B's grid (47k and 47k + 2k2 to ground: the reverb return's
///     mixer), V3B (100k, 1k unbypassed), DC-coupled to the V3A follower (100k), whose cathode drives the tone stack
///     directly (470 pF, 33k, 22 nF, 22 nF; 200k linear treble, 1M audio bass, 22k linear middle with the middle cap on
///     its wiper); Tone Shift puts 470 pF in series with the treble cap, 68k in series with the slope resistor and 22
///     nF in series with the middle cap. The treble wiper's 470k feeds both channels' 1M audio Volumes; relay RL2 takes
///     the playing channel's wiper, through the reverb send's 2M2 + 680k, to the inverter: 22 nF, 1M, 470 Ω and 10k
///     tail over 4k7, 82k / 100k with 47 pF across, 22 nF out, 220k leaks, 5k6 stoppers, four EL34 with 1k screens; the
///     5k linear Presence with 100 nF and the Deep switch's gyrator (a 10 µF / 270 Ω / 330 Ω RC4558 simulated inductor)
///     on the feedback node through 47 µF.
/// </summary>
/// <remarks>
///     <para>
///         Sources: Marshall drawings JCM2-60-02 issue 9 (DSL100 main board, 2003, "for parts list purposes only") and
///         JCM2-61-00 issue 5 (front panel, 2001), vector PDFs on DrTube (drtube.com/marshall-jcm2000-dsl); the boards'
///         wiring is traced from the connector cross-references printed on both sheets (main CON2 to front CON1, the
///         input; CON3 to CON2, V1B's output to the channel networks; CON6 to CON3, the Classic Gain wiper; CON4 to
///         CON5, the Ultra Gain wiper to V1A; CON5 to CON6, Lead 2's cathode switch; CON9 to CON11, V3A's cathode to
///         the stack; CON11 to CON7, the volumes to the inverter; CON13 to CON12, the presence and Deep network), which
///         JCM2-60-00 issue 1 (1997) confirms; the wiring drawing JCM2-68-02 was not needed. Every value above is read
///         from them; on issue 9 the 1997 sheet's channel-mixing LDR1 and LDR2 are fitted as 470k resistors (R79, R80),
///         and LDR3 mutes the Ultra path at R26/R25 in the Classic channel.
///     </para>
///     <para>
///         Inferred: RL1 energised selects the Classic Gain channel (its contacts feed one network and ground the
///         other's input); which position of each panel switch is which mode, from the handbook (Classic Gain "clean to
///         crunch 1959 ... up to a 2203", Lead 2 "a mid-boosted tone coupled with even higher gain", Tone Shift "shifts
///         out the mid frequencies"): Crunch is the 4.7 nF / 470k network, Lead 2 the lifted bridge with the full
///         cathode bypass (the switch's two sections are drawn in the Lead 1 position, the bridge section's contact
///         read as the one the arm touches; the other reading gave Lead 2 3 dB less gain than Lead 1), Tone Shift the
///         series parts. Guessed: the rails (none printed: 470 V B+, 440 V for the inverter
///         through its 4k7, 340 / 320 / 300 V for V3, V2 and V1), the idle current (35 mA a tube), the output
///         transformer (1.7k), the feedback resistor and tap (47k from the 16 Ω tap: the resistor is on the
///         speaker-jack wiring, not drawn). Simplified: the stack's foot (an elevated node on 1k, 22 µF and 220 nF, so
///         that the DC-coupled follower puts no DC across the pots) as ground, with 22 µF coupling the follower to the
///         stack; the reverb at zero (its return's driver as ground behind the 47k + 2k2; the send's 2M2 + 680k as a
///         load); the switching-mute JFETs (TR1, TR3) off; LDR3 as open in the Ultra channel and the Ultra path's 470k
///         as grounded in the Classic one; the Classic network's grounded wiper, in the Ultra channel, as 470k to
///         ground; the Deep switch's gyrator as the library's depth control (Depth, labelled Deep).
///     </para>
///     <para>
///         Panel: Switch 1 selects the Ultra Gain channel, Switch 2 its mode (Classic: Crunch; Ultra: Lead 2), Switch 3
///         Tone Shift; Gain and Master are the playing channel's Gain and Volume (the amp has no master).
///     </para>
/// </remarks>
public static class Dsl100 {
    const double Closed = 10;
    const double V1Rail = 300;
    const double V2Rail = 320;
    const double V3Rail = 340;

    public static AmpDesign Design { get; } = new(
        "DSL100",
        [
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [new NetworkStageDesign("V1B Classic", ClassicInput(), 68e3)],
                [
                    new NetworkStageDesign("V1B Ultra", UltraInput(), 68e3),
                    new NetworkStageDesign("V1A", UltraStage())
                ]
            ),
            new NetworkStageDesign("V2B", SecondStage()),
            new NetworkStageDesign("V2A", ThirdStage()),
            new NetworkStageDesign("V3B", MixerStage()),
            new NetworkStageDesign("V3A", Stack())
        ],
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
            new LongTailPairDesign(TriodeModel.Ecc83, 440, 82e3, 100e3, 10e3, 470),
            22e-9,
            220e3,
            5.6e3,
            0,
            new(1e6, PotTaper.Linear),
            new(Math.Sqrt(2), 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            true,
            new(1.7e3),
            SpeakerDesign.Closed4x12,
            new(25, 165e-6, 270, 50e-6, 4.7e3, 22e-6),
            InverterInput: new(22e-9, 1e6)
        ),
        -7
    ) {
        Panel = AmpPanel.Standard with {
            Master = "Volume",
            Depth = "Deep",
            Cut = null,
            Bright = null,
            Switch1 = "Ultra Gain",
            Switch2 = "Crunch / Lead 2",
            Switch3 = "Tone Shift"
        }
    };

    /// <summary>
    ///     After the input's 68k (1M before it): V1B (220k, 1k8 ‖ 4.7 µF, 100 pF plate to cathode), 22 nF and 1M to
    ///     ground, a node of the front panel's input line that the source drives; returns that node.
    /// </summary>
    static int InputStage(StageNetlist net) {
        var (rail, plate, cathode, line) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V1Rail);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, StageNetlist.Ground, 4.7e-6);
        net.Capacitor(plate, cathode, 100e-12);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, line, 22e-9);
        net.Resistor(line, StageNetlist.Ground, 1e6);
        return line;
    }

    /// <summary>
    ///     V1B and the Classic Gain network (Clean, or Crunch with switch 2), its wiper's 470k into V2B's grid (2M2 to
    ///     ground), the muted Ultra path's 470k (+ 1k) to ground there. Out at V2B's grid.
    /// </summary>
    static StageNetlist ClassicInput() {
        var net = new StageNetlist();
        var line = InputStage(net);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        var (bridgeTop, bridgeFoot, foot, crunch, top, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var trebleArm = net.Node();

        // The Clean voicing: 220 pF + 82k and 100k into the 150k bridge, its foot on 1M and 47 nF + 10k (grounded in
        // Clean, on 1M in Crunch), 100k from the bridge's top to the Gain while Clean.
        net.Capacitor(line, trebleArm, 220e-12);
        net.Resistor(trebleArm, bridgeTop, 82e3);
        net.Resistor(line, bridgeFoot, 100e3);
        net.Resistor(bridgeTop, bridgeFoot, 150e3);
        net.Resistor(bridgeFoot, StageNetlist.Ground, 1e6);
        var shunt = net.Node();
        net.Capacitor(bridgeFoot, foot, 47e-9);
        net.Resistor(foot, shunt, 10e3);
        net.Resistor(shunt, StageNetlist.Ground, 1e6);
        net.SwitchedResistor(shunt, StageNetlist.Ground, Closed, 0, SwitchRole.Switch2);
        var cleanArm = net.Node();
        net.Resistor(bridgeTop, cleanArm, 100e3);
        net.SwitchedResistor(cleanArm, top, Closed, 0, SwitchRole.Switch2);

        // The Crunch: 4.7 nF and 470k ‖ 470 pF to the Gain, 1 nF from its top to its wiper.
        net.Capacitor(line, crunch, 4.7e-9);
        var crunchArm = net.Node();
        net.Resistor(crunch, crunchArm, 470e3);
        net.Capacitor(crunch, crunchArm, 470e-12);
        net.SwitchedResistor(crunchArm, top, 0, Closed, SwitchRole.Switch2);

        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 100e-12, CapacitorRole.Bright);
        net.SwitchedCapacitor(top, wiper, 1e-9, SwitchRole.Switch2);

        net.Resistor(wiper, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 471e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V1B and the Ultra Gain network: 4.7 nF, then 470k ‖ (2.2 nF + 47k) onto the 1M audio Gain with 470 pF from
    ///     its top to its wiper; 150k from the top and 470 pF from the wiper meet on 10M to ground, the junction
    ///     grounded in Lead 1 (switch 2 off) and lifted in Lead 2. Out at the wiper, V1A's grid (10M to ground).
    /// </summary>
    static StageNetlist UltraInput() {
        var net = new StageNetlist();
        var line = InputStage(net);
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        var (e, arm, top, junction) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(line, e, 4.7e-9);
        net.Resistor(e, top, 470e3);
        net.Capacitor(e, arm, 2.2e-9);
        net.Resistor(arm, top, 47e3);
        var wiper = net.Pot(top, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 470e-12, CapacitorRole.Bright);
        net.Resistor(top, junction, 150e3);
        net.Capacitor(wiper, junction, 470e-12);
        net.Resistor(junction, StageNetlist.Ground, 10e6);
        net.SwitchedResistor(junction, StageNetlist.Ground, Closed, 0, SwitchRole.Switch2);
        net.Resistor(wiper, StageNetlist.Ground, 10e6);
        net.Output = wiper;
        return net;
    }

    /// <summary>
    ///     V1A from its grid: 100k plate, 470 pF from the plate to the rail, 1k8 with 1 µF + 10k to ground (the 10k
    ///     shorted in Lead 2); 4.7 nF (1M to ground), 10k, 1k (470 pF to ground), 470k into V2B's grid (2M2 to ground),
    ///     the idle Classic network's 470k to ground there. Out at V2B's grid.
    /// </summary>
    static StageNetlist UltraStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, bypass, coupled, ldr, filtered, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V1Rail);
        net.Resistor(rail, plate, 100e3);
        net.Capacitor(plate, rail, 470e-12);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.8e3);
        net.Capacitor(cathode, bypass, 1e-6);
        net.Resistor(bypass, StageNetlist.Ground, 10e3);
        net.SwitchedResistor(bypass, StageNetlist.Ground, 0, Closed, SwitchRole.Switch2);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 4.7e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, ldr, 10e3);
        net.Resistor(ldr, filtered, 1e3);
        net.Capacitor(filtered, StageNetlist.Ground, 470e-12);
        net.Resistor(filtered, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 2.2e6);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2B from its grid: 100k, 4k7 unbypassed; 22 nF and 470k into V2A's grid (470k leak). Out at that grid.
    /// </summary>
    static StageNetlist SecondStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, grid) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V2Rail);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 4.7e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Output = grid;
        return net;
    }

    /// <summary>
    ///     V2A from its grid: 100k, 2k2 unbypassed; 22 nF, 220k, 100k into V3B's grid, which 47k and the reverb
    ///     return's 47k + 2k2 hold to ground. Out at that grid.
    /// </summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode, coupled, send, grid) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V2Rail);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, send, 220e3);
        net.Resistor(send, grid, 100e3);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Resistor(grid, StageNetlist.Ground, 49.2e3);
        net.Output = grid;
        return net;
    }

    /// <summary>V3B from its grid: 100k, 1k unbypassed. Out at the plate, which drives V3A's grid directly.</summary>
    static StageNetlist MixerStage() {
        var net = new StageNetlist();
        var (rail, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, V3Rail);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The V3A follower (100k), 22 µF into the tone stack (Tone Shift on switch 3), the treble wiper's 470k onto
    ///     both channels' 1M audio Volumes, the playing channel's wiper (switch 1: Ultra) to the output, which the
    ///     reverb send's 2M2 + 680k and 10M load. Out there.
    /// </summary>
    static StageNetlist Stack() {
        var net = new StageNetlist();
        var (rail, cathode, line, upper, trebleTop, slope, slopeFoot) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (bassTop, middleTop, middleCap, middleArm, volumes, output) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, V3Rail);
        net.Triode(rail, net.Input, cathode, TriodeModel.Ecc83);
        net.Resistor(cathode, StageNetlist.Ground, 100e3);
        net.Capacitor(cathode, line, 22e-6);
        net.Resistor(line, StageNetlist.Ground, 10e6);

        // Treble cap: 470 pF, with Tone Shift another 470 pF in series.
        net.Capacitor(line, upper, 470e-12, CapacitorRole.Stack);
        net.Capacitor(upper, trebleTop, 470e-12, CapacitorRole.Stack);
        net.SwitchedResistor(upper, trebleTop, Closed, 0, SwitchRole.Switch3);
        net.Resistor(upper, StageNetlist.Ground, 10e6);

        // Slope: 33k, with Tone Shift 68k more.
        net.Resistor(line, slope, 33e3);
        net.Resistor(slope, slopeFoot, 68e3);
        net.SwitchedResistor(slope, slopeFoot, Closed, 0, SwitchRole.Switch3);
        net.Capacitor(slopeFoot, bassTop, 22e-9, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, bassTop, new(200e3, PotTaper.Linear), PotRole.Treble);
        net.Rheostat(bassTop, middleTop, new(1e6, PotTaper.Audio), PotRole.Bass);
        var middle = net.Pot(middleTop, StageNetlist.Ground, new(22e3, PotTaper.Linear), PotRole.Middle);

        // Middle cap: 22 nF, with Tone Shift another 22 nF in series.
        net.Capacitor(slopeFoot, middleCap, 22e-9, CapacitorRole.Stack);
        net.Resistor(middleCap, StageNetlist.Ground, 10e6);
        net.Capacitor(middleCap, middleArm, 22e-9, CapacitorRole.Stack);
        net.SwitchedResistor(middleCap, middleArm, Closed, 0, SwitchRole.Switch3);
        net.Resistor(middleArm, middle, Closed);

        net.Resistor(treble, volumes, 470e3);
        var classic = net.Pot(volumes, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        var ultra = net.Pot(volumes, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Master);
        net.SwitchedResistor(classic, output, Closed, 0, SwitchRole.Switch1);
        net.SwitchedResistor(ultra, output, 0, Closed, SwitchRole.Switch1);
        net.Resistor(output, StageNetlist.Ground, 2.88e6);
        net.Resistor(output, StageNetlist.Ground, 10e6);
        net.Output = output;
        return net;
    }
}
