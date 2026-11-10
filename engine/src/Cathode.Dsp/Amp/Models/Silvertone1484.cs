using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Silvertone 1484 "Twin Twelve" (Sears, built by Danelectro, 1960s; a head on a 2 × 12 cabinet): two channels
///     (<see cref="AmpPanel.Switch1" />: on is Channel 2), each half a 12AX7 into a passive bass and treble network and
///     a 1M Volume, then half a second 12AX7; the two plates mix through 68k each, into a 6CG7 gain stage and a 6CG7
///     cathodyne, and two 6L6GC in fixed bias at 480 V with 340 V on their screens, on a solid-state voltage doubler.
///     No global feedback. Panel: Volume, Bass, Treble. Source: Sears' "Schematic diagram of Silvertone chassis
///     185.11040" with DC voltages (schematicheaven.net/bargainbin/silvertone1484.pdf; the copy at el34world is the
///     same file); the 6CG7 is this repository's fit to Tung-Sol's 6SN7GT sheet (<see cref="TriodeModel.SixCG7" />).
/// </summary>
/// <remarks>
///     <para>
///         Read: the inputs (R1 to R4 68k from each jack, 1M to ground at each grid; one jack in use here, 68k into the
///         grid); V1 (R7, R8 220k, the two cathodes together on R6 1.5k ‖ C1 25 µF: 100 V at the plates, 1.1 V); C2,
///         C3 0.02 µF. Channel 1 has R5 1M from the far side of C3 back to its grid (local feedback); Channel 2 has R9
///         330k in series after C2, where the tremolo's photocell goes to ground. Each channel's network: R10 100k, the
///         1M Bass pot and R12 68k to ground, C4 0.0015 µF from the pot's top to its wiper and C5 0.02 µF from the
///         wiper to its bottom; C8 680 pF, the 1M Treble pot and C10 0.01 µF to ground; R16 100k from the Bass wiper to
///         the Treble wiper, which is the output, on the 1M Volume. V2 (R22, R23 4.7k with no bypass: 93 V, 1.1 V),
///         R24, R25 68k from each plate to the mix, R27 220k from there to the supply, R26 560k to the reverb
///         recovery's plate (V4B: R42 10k, 0.8 V); C12 0.01 µF and R28 1M into V3A (R34 100k, R29 1.5k with no bypass:
///         100 V, 4 V); C13 0.01 µF into the cathodyne (R33 68k, R32 2.2k above R30 68k, the grid's R31 1M to their
///         junction: 225 V at the plate, 125 V at the junction); C14, C15 0.02 µF into R35, R36 330k and −36 V; plates
///         475 V, screens 340 V, no grid stoppers or screen resistors. The supply: two full-wave doublers in series (C26
///         to C29, 100 µF each, 480 V), the screens' 340 V node (C25A 20 µF) through the choke from the junction of
///         C26 and C27, R58 68k to C25B 10 µF (V2's R27, on the drawing), R59 68k to C25C 5 µF (V1, V4A and the
///         tremolo's lamp).
///     </para>
///     <para>
///         The drawing gives no voltage for the two preamp nodes. They are taken from its own plate and cathode
///         voltages through the plate resistors: 230 V after R58 (V2: 93 V, 0.23 mA in each 68k, both halves and V4B
///         in the 220k) and 181 V after R59 (V1: 100 V, half of 1.1 V / 1.5k in each 220k). The idle current is what
///         this repository's 6L6GC draws at the drawing's −36 V with 340 V on the screens (27 mA a tube). As the tables
///         give them, V1 sits at 100 V and 1.10 V, V2 at 86 V and 1.13 V (the drawing: 93 V, 1.1 V), V3A at 106 V and
///         3.5 V (100 V, 4 V) and the cathodyne at 222 V and 118 V (225 V, 125 V).
///     </para>
///     <para>
///         Assumed: the pot tapers (not on the drawing: Volume audio, Bass and Treble linear); the output transformer
///         at 6k plate to plate and 50 W (RCA's 6L6-GC sheet 8-60, push-pull class AB1 at 450 V and 350 V on the
///         screens, its nearest column; the drawing gives only 85 Ω a half; issue #372 quotes the amp at 60 W); the
///         supply's source resistance (the drawing's winding resistances, 3.7, 5.75 and 6.2 Ω, say nothing of a
///         doubler's regulation); the cabinet's impedance curve (a closed box of twelves:
///         <see cref="SpeakerDesign.Closed4x12" />, the nearest preset). The screen node is held at 340 V
///         (<see cref="PowerAmpDesign.ScreenSupply" />, as the SVT's: a large cap, and 7.5k from B+ in place of the
///         choke, which is what drops 480 V to 340 V at the node's idle draw) with the preamp nodes under it, so only
///         the plates' supply sags; in the amp it follows three quarters of the doubler through the choke. The power
///         supply's preamp node is that same node (100 Ω, C25A); R58 and R59 are the two decoupling nodes.
///     </para>
///     <para>
///         Not modelled: the reverb (V4A, the 6CG7 driver, the tank; V4B's plate stays on the mix, its grid at ground
///         as with the footswitch closed) and the tremolo. The tremolo is a 12AX7 phase-shift oscillator whose plate,
///         through C24, R54 560k and the 100k Strength pot, drives the other half (R56 68k cathode, no bypass), whose
///         plate current lights the neon lamp of a 306-2H module; its photocell shunts Channel 2 after R9. That is not
///         the Fender circuit <see cref="OpticalTremoloDesign" /> solves (a class-C driver on a bypassed cathode, the
///         cell on the Intensity pot's wiper), and nothing is published on the module's cell, which the lamp also
///         lights faintly at idle. The cell is left out (dark), so Channel 2 runs at its full level, and the Circuit
///         Amp's generic bias tremolo stands in.
///     </para>
/// </remarks>
public static class Silvertone1484 {
    /// <summary>The screens' node, which V3A and the cathodyne also run on (the drawing: 340 V).</summary>
    const double ScreenNode = 340;

    /// <summary>The node after R58, V2's (see the remarks).</summary>
    const double MixerNode = 230;

    /// <summary>The node after R59, V1's (see the remarks).</summary>
    const double InputNode = 181;

    /// <summary>What V1's two halves draw from <see cref="InputNode" />: 1.1 V across R6 1.5k.</summary>
    const double InputDraw = 1.1 / 1.5e3;

    /// <summary>
    ///     What <see cref="MixerNode" /> draws besides R59's current: the rest of R58's, which holds it at 230 V.
    /// </summary>
    const double MixerDraw = (ScreenNode - MixerNode) / 68e3 - InputDraw;

    /// <summary>
    ///     What the 340 V node feeds besides the screens, from the drawing's voltages: V3A (4 V on 1.5k), the cathodyne
    ///     (125 V on 68k), the reverb driver (8.2 V on 820 Ω), the tremolo oscillator (205 V under 330k) and R58.
    /// </summary>
    const double ScreenNodeDraw = 4 / 1.5e3
        + 125 / 68e3
        + 8.2 / 820
        + (ScreenNode - 205) / 330e3
        + (ScreenNode - MixerNode) / 68e3;

    /// <summary>A closed switch contact.</summary>
    const double Closed = 1;

    /// <summary>
    ///     A 6SN7GT's grid-to-plate capacitance (Tung-Sol: 3.8 and 4.0 pF for its two units), taken for the 6CG7.
    /// </summary>
    const double DriverGridPlate = 4e-12;

    public static AmpDesign Design { get; } = new(
        "Twin Twelve 1484",
        [
            Channel() with { SupplyNode = 2 },
            Mixer() with { SupplyNode = 1 },
            Driver()
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            480,
            1,
            0.027,
            false,
            0,
            50,
            new CathodyneDesign(TriodeModel.SixCG7, ScreenNode, 68e3, 68e3, 2.2e3),
            0.02e-6,
            330e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 10e3, 100, new(1e3, PotTaper.Linear), 1e-12),
            false,
            new(6e3),
            SpeakerDesign.Closed4x12,
            new(120, 25e-6, 7.5e3, 0.5, 100, 20e-6, ScreenNodeDraw, RectifierModel.Silicon),
            InverterInput: new(0.01e-6, 1e6),
            ScreenSupply: ScreenNode
        ),
        0
    ) {
        Panel = new() {
            Gain = "Volume",
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null,
            Switch1 = "Channel 2"
        },
        PreampSupply = new([new(68e3, 10e-6, MixerDraw), new(68e3, 5e-6, InputDraw, 1)])
    };

    /// <summary>
    ///     V1 of the channel played, its coupling cap, Channel 1's feedback resistor or Channel 2's series resistor,
    ///     the bass and treble network and the Volume; the output is the Volume's wiper. The two halves share R6 ‖ C1,
    ///     so each draws its current through what is, for its own operating point, twice that resistor (as
    ///     <see cref="Ab763.SecondStage" />).
    /// </summary>
    static NetworkStageDesign Channel() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Resistor(net.Input, StageNetlist.Ground, 1e6);
        net.Supply(rail, InputNode);
        net.Resistor(rail, plate, 220e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2 * 1.5e3);
        net.Capacitor(cathode, StageNetlist.Ground, 25e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);

        // C3 or C2, then R5 back to the grid (Channel 1) or R9 in series (Channel 2).
        var (coupled, top) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 0.02e-6);
        net.SwitchedResistor(coupled, top, Closed, 330e3, SwitchRole.Switch1);
        net.SwitchedResistor(top, net.Input, 1e6, 0, SwitchRole.Switch1);

        // Bass: R10, the pot with C4 and C5 around its wiper, R12. Treble: C8, the pot, C10. R16 joins the wipers.
        var (bassTop, bassBottom, trebleTop, trebleBottom) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(top, bassTop, 100e3);
        var bass = net.Pot(bassTop, bassBottom, new(1e6, PotTaper.Linear), PotRole.Bass);
        net.Capacitor(bassTop, bass, 0.0015e-6, CapacitorRole.Stack);
        net.Capacitor(bass, bassBottom, 0.02e-6, CapacitorRole.Stack);
        net.Resistor(bassBottom, StageNetlist.Ground, 68e3);
        net.Capacitor(top, trebleTop, 680e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Linear), PotRole.Treble);
        net.Capacitor(trebleBottom, StageNetlist.Ground, 0.01e-6, CapacitorRole.Stack);
        net.Resistor(bass, treble, 100e3);
        net.Output = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        return new("V1", net, 68e3);
    }

    /// <summary>
    ///     V2: the half played and the idle half (its grid at ground), each with its 4.7k cathode and 68k into the mix,
    ///     R27 to the supply, and R26 to the reverb recovery's plate (V4B on its 10k, its grid at ground); C12 and R28
    ///     into V3A's grid, the output.
    /// </summary>
    static NetworkStageDesign Mixer() {
        var net = new StageNetlist();
        var (mix, rail) = (net.Node(), net.Node());
        net.Supply(rail, MixerNode);
        net.Resistor(rail, mix, 220e3);
        Unbypassed(net, TriodeModel.Ecc83, net.Input, mix, 68e3, 4.7e3, Ab763.GridPlate);
        Unbypassed(net, TriodeModel.Ecc83, StageNetlist.Ground, mix, 68e3, 4.7e3, 0);
        Unbypassed(net, TriodeModel.Ecc83, StageNetlist.Ground, mix, 560e3, 10e3, 0);

        var grid = net.Node();
        net.Capacitor(mix, grid, 0.01e-6);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Output = grid;
        return new("V2", net);
    }

    /// <summary>
    ///     V3A, half a 6CG7: R34 100k from the 340 V node, R29 1.5k with no bypass; its plate drives the cathodyne's
    ///     C13 and R31 (the power amp's <see cref="InverterInputDesign" />).
    /// </summary>
    static NetworkStageDesign Driver() {
        var net = new StageNetlist();
        var rail = net.Node();
        net.Supply(rail, ScreenNode);
        net.Output = net.DcBlock(Unbypassed(net, TriodeModel.SixCG7, net.Input, rail, 100e3, 1.5e3, DriverGridPlate));
        return new("V3A", net);
    }

    /// <summary>
    ///     A triode on an unbypassed <paramref name="cathodeResistor" />, its plate on <paramref name="plateResistor" />
    ///     from <paramref name="node" /> (a supply rail, or the mix), with <paramref name="gridPlate" /> farads from
    ///     grid to plate (0 for a grounded grid); returns the plate.
    /// </summary>
    static int Unbypassed(
        StageNetlist net,
        TriodeModel tube,
        int grid,
        int node,
        double plateResistor,
        double cathodeResistor,
        double gridPlate
    ) {
        var (plate, cathode) = (net.Node(), net.Node());
        net.Resistor(node, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Triode(plate, grid, cathode, tube);
        if (gridPlate > 0) {
            net.Capacitor(grid, plate, gridPlate);
        }

        return plate;
    }
}
