using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Subway Blues (20 W, 2 × EL84). V1a: the input through 1.5M ‖ 20 pF ‖ 120 pF, 1M ‖ 250 pF to ground at
///     the grid, 100k plate from 168 V, 1.6k with 3.3 µF; on its plate the Mark-style stack (250 pF to the 250k audio
///     treble, 750 pF more with Fat; 150k slope; 0.1 µF to the 250k audio bass, 47 nF to the 25k linear middle), the 1M
///     audio volume on the treble wiper (120 pF from its top to the wiper) straight into V1b's grid (5 nF and 330k from
///     grid to cathode; 100k, 1.6k with 1 µF); 47 nF and 220k into V2a (20 pF and 470k at the grid; 100k, 1.6k with
///     15 µF); 47 nF, the reverb send's load, 680k into the reverb pot (100k linear, at zero), 16k into 180 pF and
///     5.6k + 2.7k to ground, 10 nF into the emitter follower Q1 (MPSA63) and the effects loop; 1 µF with 22k to ground,
///     20 nF and 470 Ω into V2b (47k at the grid; 100k, 1.6k with 1 µF); 100k, 47 nF, 10k and 33k into 15k + 22k, 20 nF
///     into the inverter: a long-tail pair (68k and 120k plates from 334 V, a 15k tail to -15 V), with the feedback from
///     the 8 Ω tap through 56k ‖ (33k + 5 nF) into its second grid (1.6k to ground); 47 nF, 330k to the -15 V bias
///     supply through 33k, 220k into each EL84's grid with 2.2M to ground (-11 V); two EL84 at 395 V with 330 V screens
///     through 470 Ω; silicon rectifier, 15 µF (two 30 µF stacked), 5.5k to the screen node (334 V, 30 µF), 63k to the
///     preamp node (168 V, 30 µF).
/// </summary>
/// <remarks>
///     <para>
///         Source: the Mesa/Boogie factory drawings "Subway Blues" (block diagram SUBBLOCK.S01, power supplies SUBSUPP,
///         preamp SUBPRE, power amp / reverb / record out SUBPWR, and the April 1998 hum-reduction note),
///         https://el34world.com/charts/Schematics/files/Mesa_boogie/Boogie_subwayblues.pdf, read by eye; the node
///         voltages above are printed on them.
///     </para>
///     <para>
///         Not on the drawings, and left out (marked): the potted "Toning Module" between the 10 nF and Q1 (the block
///         diagram places it there; its contents are not published), so the 10 nF drives Q1 directly. Simplified,
///         marked: Q1 (a bootstrapped Darlington follower) as an ideal buffer; the effects loop empty (both ends of the
///         Mix pot then carry the same signal); the reverb at zero (its pot a 100k load) and its send (3.3M ‖ (1.6M +
///         250 pF) into V4b's 1M) as a fixed load; the 500 pF from the reverb pot's top to the inverter's feed (X) left
///         out; the handwritten 1k screen resistors (beside the printed 470 Ω) not used. The inverter's tail returns to
///         -15 V with its grids at ground; the block's long-tail pair returns to ground with its grids on a bias
///         resistor, so here a 15k tail, a 1.2k bias resistor and 350 V on the plates give the same currents (about
///         1.1 mA) and plate-to-cathode voltages (a reading of the circuit, not printed). The feedback enters the second
///         grid, here the tail's foot (the same difference); the 33k + 5 nF across the 56k is the network's fixed shelf
///         (more feedback above 360 Hz, a 0.37 ratio). Guessed: the output transformer (8k, a typical primary for two
///         EL84), the EL84s' idle current (30 mA at -11 V), the bias network as a 290k leak (330k ‖ 2.42M) behind the
///         220k series resistors.
///     </para>
/// </remarks>
public static class SubwayBlues {
    /// <summary>The preamp node C.</summary>
    const double PreampSupply = 168;

    public static AmpDesign Design { get; } = new(
        "Subway Blues",
        [
            new NetworkStageDesign("V1a", InputStage()),
            new NetworkStageDesign("V1b", StackStage()),
            new NetworkStageDesign("V2a", ThirdStage()),
            new NetworkStageDesign("Reverb mix", ReverbMix()),
            new BufferStageDesign("Q1"),
            new NetworkStageDesign("V2b", LoopReturn())
        ],
        new(
            "2 x EL84",
            PentodeModel.EL84,
            1,
            403,
            470,
            0.03,
            false,
            0,
            20,
            new LongTailPairDesign(TriodeModel.Ecc83, 350, 68e3, 120e3, 15e3, 1.2e3),
            47e-9,
            290e3,
            220e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 20.8e3, 1.6e3, new(1e6, PotTaper.Linear), 0, 965, 0.37),
            true,
            new(8e3),
            SpeakerDesign.OpenBack1x12,
            // Two 30 µF stacked, 3 x 820 + 3 x 1k to the screens' 30 µF, 33k + 15k + 15k to the preamp's 30 µF.
            new(30, 15e-6, 5.46e3, 30e-6, 63e3, 30e-6, 2.6e-3, RectifierModel.Silicon),
            InverterInput: new(20e-9, 1e6)
        ),
        -6
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Master = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Bright = null,
            Switch1 = "Fat"
        }
    };

    /// <summary>
    ///     V1a: 1.5M ‖ 20 pF ‖ 120 pF from the jack, 1M ‖ 250 pF at the grid; 100k plate from 168 V, 1.6k with 3.3 µF.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var (grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, grid, 1.5e6);
        net.Capacitor(net.Input, grid, 140e-12);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Capacitor(grid, StageNetlist.Ground, 250e-12);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.6e3);
        net.Capacitor(cathode, StageNetlist.Ground, 3.3e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     The stack on V1a's plate (Fat on Switch 1), the volume with 120 pF across its upper part, V1b at its wiper
    ///     (5 nF and 330k from grid to cathode; 100k, 1.6k with 1 µF).
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (wiper, _, _) = MesaParts.Stack(
            net,
            net.Input,
            250e-12,
            750e-12,
            SwitchRole.Switch1,
            150e3,
            0.1e-6,
            47e-9,
            new(250e3, PotTaper.Audio),
            new(250e3, PotTaper.Audio),
            new(25e3, PotTaper.Linear)
        );
        var grid = net.Pot(wiper, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(wiper, grid, 120e-12, CapacitorRole.Bright);
        var (rail, plate, cathode, local) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.6e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Capacitor(grid, local, 5e-9);
        net.Resistor(local, cathode, 330e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>V2a: 47 nF and 220k into the grid, 20 pF and 470k to ground; 100k plate, 1.6k with 15 µF.</summary>
    static StageNetlist ThirdStage() {
        var net = new StageNetlist();
        var (coupled, grid, rail, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 47e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Capacitor(grid, StageNetlist.Ground, 20e-12);
        net.Resistor(grid, StageNetlist.Ground, 470e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.6e3);
        net.Capacitor(cathode, StageNetlist.Ground, 15e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Output = plate;
        return net;
    }

    /// <summary>
    ///     47 nF from V2a; the reverb send (3.3M ‖ (1.6M + 250 pF) into V4b's 1M); 680k into the reverb pot (100k to
    ///     ground at zero); 16k into 180 pF and 8.3k to ground; 10 nF to Q1's base (its bias network, bootstrapped, as an
    ///     open circuit beside 10 MΩ).
    /// </summary>
    static StageNetlist ReverbMix() {
        var net = new StageNetlist();
        var (plate, send, sendCap, mix, pad, baseNode) =
            (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, plate, 47e-9);
        net.Resistor(plate, send, 3.3e6);
        net.Resistor(plate, sendCap, 1.6e6);
        net.Capacitor(sendCap, send, 250e-12);
        net.Resistor(send, StageNetlist.Ground, 1e6);
        net.Resistor(plate, mix, 680e3);
        net.Resistor(mix, StageNetlist.Ground, 100e3);
        net.Resistor(mix, pad, 16e3);
        net.Capacitor(pad, StageNetlist.Ground, 180e-12);
        net.Resistor(pad, StageNetlist.Ground, 8.3e3);
        net.Capacitor(pad, baseNode, 10e-9);
        net.Resistor(baseNode, StageNetlist.Ground, 10e6);
        net.Output = baseNode;
        return net;
    }

    /// <summary>
    ///     From Q1's emitter: 1 µF with 22k to ground, 20 nF and 470 Ω into V2b (47k at the grid; 100k, 1.6k with 1 µF);
    ///     100k, 47 nF, 10k and 33k into 15k + 22k to ground.
    /// </summary>
    static StageNetlist LoopReturn() {
        var net = new StageNetlist();
        var (emitter, coupled, grid, rail, plate, cathode, series, divider) = (
            net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node()
        );
        net.Capacitor(net.Input, emitter, 1e-6);
        net.Resistor(emitter, StageNetlist.Ground, 22e3);
        net.Capacitor(emitter, coupled, 20e-9);
        net.Resistor(coupled, grid, 470);
        net.Resistor(grid, StageNetlist.Ground, 47e3);
        net.Supply(rail, PreampSupply);
        net.Resistor(rail, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.6e3);
        net.Capacitor(cathode, StageNetlist.Ground, 1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Resistor(plate, series, 100e3);
        net.Capacitor(series, divider, 47e-9);
        var output = net.Node();
        net.Resistor(divider, output, 43e3);
        net.Resistor(output, StageNetlist.Ground, 37e3);
        net.Output = output;
        return net;
    }
}
