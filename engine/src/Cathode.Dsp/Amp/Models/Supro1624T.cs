using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Supro 1624T Dual-Tone (Valco, 1960s; 2 × 6973, a 12 in speaker): two channels, each half of V1 (12AX7) with a
///     Standard and a Treble input jack and its own 500k audio Volume (500 pF across its top), mixed through 270k each
///     into V2A's grid, where the one Tone control sits (500k linear with 0.005 µF from its wiper to ground, and 500 pF
///     to ground); V2 (12AX7) is a fixed-divider paraphase inverter (270k plates; 2.2k ‖ 0.05 µF under the first
///     half, 3.9k under the second, whose grid is on the 12k foot of the first 6973's 270k + 12k grid leak); two 6973
///     on a shared 250 Ω ‖ 35 µF cathode, no global feedback, a 5Y3. The tremolo (V3, 12AX7) works on the second
///     channel only. Panel: Volume, Tone, Speed, Intensity.
/// </summary>
/// <remarks>
///     <para>
///         Source: the redraw "Supro 1624T" by Ryan at ClassicTubeAmps.com
///         (el34world.com/charts/Schematics/files/Supro/Supro_1624t_schematic_263.pdf, a vector PDF), from which every
///         part value here is read. It carries no voltages. Those are AmpBooks' ("Supro Push-Pull 6973 Power Amp",
///         ampbooks.com/mobile/classic-circuits/supro-power-amp, the same circuit part for part): 350 V on the 6973
///         plates and screens, about 23 V on the cathode at about 92 mA for the pair, 290 V on the inverter's plate
///         resistors with 167 V from plate to cathode on the 12AX7 (the second half here sits at 169 V), 23 W into
///         the 7k plate-to-plate primary it assumes. The same page gives the cathode resistor as 250 Ω 7 W, which the
///         redraw letters "250K 7W". The 6973 is this repository's fit to RCA's sheet
///         (<see cref="PentodeModel.SixtyNineSeventyThree" />); with it the pair idles at 23.3 V and 93 mA.
///     </para>
///     <para>
///         Read: each input pair (the Treble jack through 0.005 µF, the Standard jack through 100k, which that jack's
///         switch contact grounds when nothing is in it; 470k to ground); V1A (270k from a node that 100k and 0.05 µF
///         decouple, 2.2k unbypassed) with 0.005 µF to its Volume; V1B (2.2k at the grid, the same plate circuit,
///         1.5k ‖ 35 µF on the cathode it shares with the tremolo's follower) with 0.005 µF, 270k to ground and
///         0.005 µF to its Volume; the mixing resistors, the Tone and V2 as above, 0.01 µF to each 6973 grid, 470k on
///         the second; the 6973 screens on the node after 1k from the 20 µF reservoir (no screen resistors: 1 Ω
///         here), 15k on to the preamp's 10 µF node, 10 µF on the screen node.
///     </para>
///     <para>
///         The tremolo, from the drawing: V3A is a phase-shift oscillator (270k plate, 1k cathode; 0.03 µF to 100k
///         and the 500k linear Speed rheostat, 0.02 µF to 1M, 0.01 µF to 1.5M and the grid); its plate drives, through
///         0.05 µF, 1M (0.02 µF to ground after it), the switch on the Speed control, the footswitch jack and 2.2M,
///         the node of the 500k linear Intensity rheostat and 0.05 µF to ground, where V3B's grid hangs through 2.2k.
///         V3B is a cathode follower with its plate on the supply and its cathode on V1B's: it idles there (its grid
///         at 0 V) and sets most of V1B's bias, and the oscillation moves that bias. <see cref="Oscillator" /> gives
///         the plate's swing and <see cref="Drive" /> the share of it at V3B's grid; the channel's netlist holds V3B
///         and works out the rest (<see cref="GridDriveTremoloDesign" />). Speed and Intensity are the Circuit Amp's
///         tremolo parameters, Intensity 0 the switch open.
///     </para>
///     <para>
///         Assumed: the supply's source resistance; what the preamp draws (3.4 mA, which puts its node at AmpBooks'
///         290 V); the output transformer's 7k (AmpBooks' assumption; RCA's sheet has 5.5k to 6k for cathode bias at
///         300 V) and everything else about it; the speaker's impedance curve (an open-back 1 × 12's); which way the
///         Tone turns (up takes the 0.005 µF off the grid). The oscillator's table takes its 1k cathode resistor as
///         bypassed, which it is not, and its output network as a 2.4M load (its magnitude at 5 Hz). The idle
///         channel's Volume is at 0 (its 270k from the mix to ground); the unlabelled switch that joins the two 6973
///         grids is open; both 6973 grid leaks are the first one's 282k, the second's 470k accounted for in the
///         inverter's table (<see cref="FixedParaphase" />).
///     </para>
/// </remarks>
public static class Supro1624T {
    /// <summary>The preamp node (after the 15k), from AmpBooks: every 12AX7 plate circuit hangs on it.</summary>
    const double Preamp = 290;

    static readonly Pot Volume = new(500e3, PotTaper.Audio);

    /// <summary>
    ///     What V3B's grid is driven through: its 2.2k and the Intensity node's resistance with the knob at 10 (the
    ///     500k rheostat beside the 2.2M that feeds it), so that the megohms ahead of the grid limit its grid current
    ///     as they do in the amp: when the follower cannot charge the 35 µF fast enough, its grid is held at its
    ///     cathode.
    /// </summary>
    const double FollowerSource = 2.2e3 + 500e3 * 2.2e6 / (500e3 + 2.2e6);

    /// <summary>V3A, the tremolo's oscillator (see the type's remarks).</summary>
    internal static PhaseShiftOscillatorDesign Oscillator { get; } = new(
        TriodeModel.Ecc83,
        Preamp,
        270e3,
        1e3,
        0.03e-6,
        0.02e-6,
        0.01e-6,
        100e3,
        1e6,
        1.5e6,
        new(500e3, PotTaper.Linear),
        PlateLoad: 2.4e6
    );

    /// <summary>
    ///     From V3A's plate to V3B's grid: 0.05 µF, 1M, 0.02 µF, 2.2M, the Intensity rheostat and 0.05 µF.
    /// </summary>
    internal static TremoloDriveDesign Drive { get; } = new(
        0.05e-6,
        1e6,
        0.02e-6,
        2.2e6,
        new(500e3, PotTaper.Linear),
        0.05e-6
    );

    /// <summary>
    ///     The supply: a 5Y3 into the 20 µF reservoir (the 6973 plates), 1k to the screens' 10 µF, 15k to the preamp's
    ///     10 µF. The source resistance and the preamp's draw are assumed (see the type's remarks).
    /// </summary>
    static PowerSupplyDesign Supply { get; } = new(
        150,
        20e-6,
        1e3,
        10e-6,
        15e3,
        10e-6,
        3.4e-3,
        RectifierModel.FiveY3
    );

    /// <summary>
    ///     The panel: one Volume (the playing channel's), the Tone, the tremolo's Speed and Intensity; the Bright
    ///     switch is the channel's Treble jack, Switch 1 the channel with the tremolo.
    /// </summary>
    static AmpPanel Panel { get; } = new() {
        Gain = "Volume",
        Treble = "Tone",
        Bass = null,
        Middle = null,
        Presence = null,
        Depth = null,
        Cut = null,
        Master = null,
        Bright = "Treble Input",
        Switch1 = "Tremolo Ch",
        TremoloRate = "Speed",
        TremoloDepth = "Intensity"
    };

    public static AmpDesign Design { get; } = new(
        "Supro 1624T",
        [
            new ChannelSwitchStageDesign("Channel", SwitchRole.Switch1, [FirstChannel()], [TremoloChannel()]),
            Mixer()
        ],
        new(
            "2 x 6973",
            PentodeModel.SixtyNineSeventyThree,
            1,
            350,
            1,
            0.044,
            true,
            35e-6,
            23,
            new FixedParaphaseDesign(TriodeModel.Ecc83, Preamp, 270e3, 2.2e3, 3.9e3, 270e3, 12e3, 470e3),
            0.01e-6,
            282e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 10e3, 100, new(1e3, PotTaper.Linear), 1e-12),
            false,
            new(7e3),
            SpeakerDesign.OpenBack1x12,
            Supply,
            CathodeResistor: 250
        ),
        -3
    ) { Panel = Panel };

    /// <summary>
    ///     A channel's two jacks into its grid node: the Standard jack's 100k in series, or (Treble Input) the Treble
    ///     jack's 0.005 µF in series with the idle Standard jack's 100k to ground; 470k to ground. Returns the node.
    /// </summary>
    static int Jacks(StageNetlist net) {
        var node = net.Node();
        net.SwitchedResistor(net.Input, node, 100e3, 0, SwitchRole.Bright);
        net.SwitchedCapacitor(net.Input, node, 0.005e-6, SwitchRole.Bright, CapacitorRole.Bright);
        net.SwitchedResistor(node, StageNetlist.Ground, 0, 100e3, SwitchRole.Bright);
        net.Resistor(node, StageNetlist.Ground, 470e3);
        return node;
    }

    /// <summary>
    ///     A V1 half's plate circuit: 100k from the preamp node to a node with 0.05 µF to ground, 270k to the plate.
    ///     Returns the rail and the plate.
    /// </summary>
    static (int Rail, int Plate) PlateCircuit(StageNetlist net) {
        var (rail, decoupled, plate) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, Preamp);
        net.Resistor(rail, decoupled, 100e3);
        net.Capacitor(decoupled, StageNetlist.Ground, 0.05e-6);
        net.Resistor(decoupled, plate, 270e3);
        return (rail, plate);
    }

    /// <summary>
    ///     0.005 µF from <paramref name="from" /> to a channel's Volume, 500 pF from its top to its wiper (the output).
    /// </summary>
    static void VolumeControl(StageNetlist net, int from) {
        var top = net.Node();
        net.Capacitor(from, top, 0.005e-6);
        var wiper = net.Pot(top, StageNetlist.Ground, Volume, PotRole.Gain);
        net.Capacitor(top, wiper, 500e-12, CapacitorRole.Bright);
        net.Output = wiper;
    }

    /// <summary>The first channel: its jacks, V1A (2.2k unbypassed) and its Volume.</summary>
    static NetworkStageDesign FirstChannel() {
        var net = new StageNetlist();
        var grid = Jacks(net);
        var (_, plate) = PlateCircuit(net);
        var cathode = net.Node();
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);
        VolumeControl(net, plate);
        return new("V1A", net);
    }

    /// <summary>
    ///     The second channel with its tremolo: at Intensity 0 the netlist with V3B's grid at ground plays.
    /// </summary>
    static NetworkStageDesign TremoloChannel() =>
        SecondChannel(false) with {
            Tremolo = new GridDriveTremoloDesign(SecondChannel(true), 0, Oscillator) { Drive = Drive }
        };

    /// <summary>
    ///     The second channel: its jacks, 2.2k into V1B's grid, V1B and the follower V3B (plate on the preamp node,
    ///     <see cref="FollowerSource" /> at its grid) on their shared 1.5k ‖ 35 µF, 0.005 µF, 270k to ground and the
    ///     Volume. <paramref name="oscillating" /> puts the far end of V3B's grid resistance on a source (the netlist's
    ///     first element) for the tremolo to swing; otherwise it is at ground.
    /// </summary>
    static NetworkStageDesign SecondChannel(bool oscillating) {
        var net = new StageNetlist();
        var drive = StageNetlist.Ground;
        if (oscillating) {
            drive = net.Node();
            net.Supply(drive, 0);
        }

        var jacks = Jacks(net);
        var (grid, followerGrid, cathode, coupled) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(jacks, grid, 2.2e3);
        var (rail, plate) = PlateCircuit(net);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 35e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);
        net.Resistor(drive, followerGrid, FollowerSource);
        net.Triode(rail, followerGrid, cathode, TriodeModel.Ecc83);
        net.Capacitor(plate, coupled, 0.005e-6);
        net.Resistor(coupled, StageNetlist.Ground, 270e3);
        VolumeControl(net, coupled);
        return new("V1B", net);
    }

    /// <summary>
    ///     The playing channel's 270k into the mix (the idle channel's 270k from it to ground), the Tone (its wiper's
    ///     0.005 µF to ground), 500 pF to ground, and V2A, the inverter's first half (270k, 2.2k ‖ 0.05 µF); the
    ///     output is its plate, which <see cref="FixedParaphaseDesign" /> takes from there.
    /// </summary>
    static NetworkStageDesign Mixer() {
        var net = new StageNetlist();
        var (mix, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, mix, 270e3);
        net.Resistor(mix, StageNetlist.Ground, 270e3);
        var tone = net.Pot(StageNetlist.Ground, mix, new(500e3, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(tone, StageNetlist.Ground, 0.005e-6, CapacitorRole.Stack);
        net.Capacitor(mix, StageNetlist.Ground, 500e-12);
        net.Supply(rail, Preamp);
        net.Resistor(rail, plate, 270e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.2e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.05e-6);
        net.Triode(plate, mix, cathode, TriodeModel.Ecc83);
        net.Capacitor(mix, plate, Ab763.GridPlate);
        net.Output = net.DcBlock(plate);
        return new("V2A", net);
    }
}
