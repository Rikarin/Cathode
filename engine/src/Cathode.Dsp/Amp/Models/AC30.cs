using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Vox AC30/6 with the JMI top-boost unit (drawings OS/065, the AC30/6 "Normal" amplifier, 1960-64, and OS/010, the
///     top-boost unit, 1961-65; https://www.voxac30.org.uk/vox_ac30_circuit_diagrams.html): the Brilliant channel
///     through the top boost, or (Switch 1) the Normal channel. Both channels start in V1, a 12AX7 whose halves share a
///     1.5k cathode with 25 µF (220k plates at 170 V); the Brilliant half couples through 500 pF into its 500k log volume,
///     which feeds the top boost's gain stage (100k plate, 1.5k with 25 µF, a 100 pF bright cap across the volume), its
///     DC-coupled cathode follower (56k) and the treble/bass network; the Normal half couples through 47 nF into its
///     500k log volume. The channels mix through 220k each (the idle channel's volume at zero) into the inverter's grid
///     (47 nF, 1M bootstrapped leak): a 12AX7 long-tail pair (100k plates, 1.2k shared bias, 47k tail), 0.1 µF into
///     220k leaks and 1.5k stoppers, the cut (250k log and 4.7 nF across the grids), four EL84 on a shared 50 Ω with
///     250 µF (10 V at idle), 100 Ω screen resistors, a 4k plate-to-plate transformer, no feedback, GZ34 and a choke.
/// </summary>
/// <remarks>
///     <para>
///         The top-boost network as drawn on OS/010 is not the Fender/Marshall stack: from the follower's cathode,
///         50 pF to the treble pot (1M log, wiper out) and 100k to a node M; the treble pot's far end through 22 nF to
///         M, M through 22 nF to the bass pot's wiper, which has 10k to ground; the bass pot (1M log) from the treble
///         pot's far end to ground. Turning the bass up lengthens the track between the treble pot and the wiper and
///         shorts the 10k. There is no middle control (the panel hides it) and no master (the AC30 has none).
///     </para>
///     <para>
///         The power tubes' cathode resistor is bypassed on the JMI drawings (C11, 250 µF 25 V), not unbypassed as
///         some later analyses have it. Read from the scans by eye: the inverter's output caps (".15" on OS/065;
///         AmpBooks gives 0.1 µF, used here) and the EL84 grid leaks (220k). Inferred, not drawn: the supply's
///         source resistance and the plates' B+ (about 340 V, between the drawing's 320 V screen node and AmpBooks'
///         345 V); the idle channel's volume taken at zero, so its 220k mixing resistor goes to ground.
///     </para>
/// </remarks>
public static class AC30 {
    /// <summary>The preamp node feeding V1 and the inverter (OS/065: about 290 V).</summary>
    internal const double PreampSupply = 290;

    public static AmpDesign Design { get; } = new(
        "AC30",
        [
            FirstStage("V1"),
            new ChannelSwitchStageDesign(
                "Channel",
                SwitchRole.Switch1,
                [TopBoostGain(), TopBoostStack()],
                [NormalVolume("Normal volume", 47e-9)]
            )
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            2,
            340,
            100,
            0.045,
            true,
            250e-6,
            30,
            new LongTailPairDesign(TriodeModel.Ecc83, PreampSupply, 100e3, 100e3, 47e3, 1.2e3),
            0.1e-6,
            220e3,
            1.5e3,
            4.7e-9,
            new(250e3, PotTaper.ReverseAudio), // log track, its resistance falling fastest as the cut comes in
            new(1, 47e3, 4.7e3, new(5e3, PotTaper.Linear), 0.1e-6),
            false,
            new(4e3),
            SpeakerDesign.OpenBack2x12,
            // R10 22k drops the screen node's 320 V to the preamp's 290 V: about 1.4 mA.
            new(60, 16e-6, 120, 16e-6, 22e3, 8e-6, 1.4e-3, RectifierModel.GZ34),
            InverterInput: new(47e-9, 1e6),
            CathodeResistor: 50
        ),
        -8
    ) {
        Panel = AmpPanel.Standard with {
            Middle = null,
            Master = null,
            Presence = null,
            Depth = null,
            Bright = null,
            Switch1 = "Normal Ch"
        }
    };

    /// <summary>
    ///     The top boost's gain stage: the Brilliant half's 500 pF into its 500k log volume (Gain) with the unit's 100 pF
    ///     across the volume's upper part, the wiper to the grid; 100k plate from the unit's 290 V node, 1.5k with 25 µF.
    /// </summary>
    static NetworkStageDesign TopBoostGain() {
        var net = new StageNetlist();
        var (top, plate, cathode, supply) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, top, 500e-12);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(top, wiper, 100e-12, CapacitorRole.Bright);
        net.Supply(supply, PreampSupply);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, wiper, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return new("TB V1A", net);
    }

    /// <summary>
    ///     The top boost's cathode follower (56k, grid DC-coupled to the gain stage's plate) and its treble/bass network
    ///     (see the remarks), the treble wiper through the 220k mixing resistor to the node with the Normal channel's
    ///     220k to ground. The output is the node.
    /// </summary>
    static NetworkStageDesign TopBoostStack() {
        var net = new StageNetlist();
        var (supply, cathode, mix) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, PreampSupply);
        net.Resistor(cathode, StageNetlist.Ground, 56e3);
        net.Triode(supply, net.Input, cathode, TriodeModel.Ecc83);
        var (trebleTop, trebleBottom, slope) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(cathode, trebleTop, 50e-12, CapacitorRole.Stack);
        var output = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(cathode, slope, 100e3);
        net.Capacitor(trebleBottom, slope, 22e-9, CapacitorRole.Stack);
        // The bass pot: the track from the treble pot's far end to the wiper grows with the knob, the rest to ground
        // shrinks.
        var bassWiper = net.Pot(StageNetlist.Ground, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, bassWiper, 22e-9, CapacitorRole.Stack);
        net.Resistor(bassWiper, StageNetlist.Ground, 10e3);
        net.Resistor(output, mix, 220e3);
        net.Resistor(mix, StageNetlist.Ground, 220e3);
        net.Output = mix;
        return new("TB V1B", net);
    }

    /// <summary>
    ///     V1, one half of the 12AX7 both channels start in: 68k input resistor, 1M grid leak, 220k plate. The halves
    ///     share a 1.5k cathode with 25 µF; with the other half idling beside it, one half sees twice the resistance
    ///     and half the cap.
    /// </summary>
    internal static TriodeStageDesign FirstStage(string name) =>
        new(
            name,
            TriodeModel.Ecc83,
            PreampSupply,
            220e3,
            3e3,
            12.5e-6,
            68e3,
            GridLeak: 1e6
        );

    /// <summary>
    ///     A channel's volume into the mixing node: <paramref name="couplingCap" /> into a 500k log volume (the Gain
    ///     knob), the wiper through 220k to the node, the other channel's 220k mixing resistor from the node to ground
    ///     (its volume at zero). The output is the node.
    /// </summary>
    internal static NetworkStageDesign NormalVolume(string name, double couplingCap) {
        var net = new StageNetlist();
        var (top, mix) = (net.Node(), net.Node());
        net.Capacitor(net.Input, top, couplingCap);
        var wiper = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Resistor(wiper, mix, 220e3);
        net.Resistor(mix, StageNetlist.Ground, 220e3);
        net.Output = mix;
        return new(name, net);
    }
}
