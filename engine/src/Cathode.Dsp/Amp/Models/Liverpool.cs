using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Trainwreck Liverpool: the Express preamp with a smaller treble cap ahead of four EL84. V1a: 1M, 100k plate, 1.5k
///     with 22 µF; on its plate a Marshall-topology stack (50 pF to the 1M treble pot, 100k slope, 22 nF to the 250k
///     audio bass, 22 nF to the 25k middle wired as a rheostat), the treble wiper into the 1M volume with the three-way
///     bright switch from its top to the wiper (100 pF, off, 500 pF); 2.2k into V1b (100k, 2.7k with 22 µF); C8 (2.2 nF)
///     into R7 (68k) at V2 (100k, 10k unbypassed: a cold stage) straight into a 12AX7 long-tail pair (0.1 µF in, 1M
///     leaks, 1k bias, 39k + 4.7k tail; 100k plates, 100 pF between them), 22 nF into 220k leaks and 1.5k stoppers, four
///     EL84 with 1k screen resistors on a shared 50 Ω cathode resistor with 220 µF; feedback 100k from the 8 Ω tap into
///     the 4.7k, with the presence pot (25k, 0.1 µF) across it; silicon rectifier, 80 µF, 1k to the screens (40 µF),
///     18k, 9.1k and 9.1k to the inverter's, V2's and V1's nodes (20 µF each): 331 / 318 / 269 / 256 / 244 V.
/// </summary>
/// <remarks>
///     <para>
///         Source: Mark Abbott's hand-drawn "Liverpool pre amp", "Liverpool output stage" and "Liverpool power supply"
///         (schematicheaven <c>newamps/trainwreck_liverpool.pdf</c>, three sheets), drawn from the two Liverpools he
///         saw, with the node voltages he measured. A builder's drawing of two amps, not a factory document: Ken
///         Fischer tuned each amp, and Abbott says so for two parts. R7 and C8 "are where the response of the amp is
///         tuned": C8 from 1 nF to 10 nF and R7 from 33k up; he recommends 68k and 2 nF to 4.7 nF and saw 2 nF (1 nF
///         ceramic beside 1 nF polypropylene) in both amps, so 68k and 2.2 nF here. Rgs (V1b's grid stopper) "varies from
///         820 Ω to 2.2k" (2.2k in the amp he saw).
///     </para>
///     <para>
///         Guesses, marked: the presence pot ("possibly 25k" on the drawing) as a 25k linear pot; the output transformer
///         (not drawn; 4k plate to plate, a typical value for four EL84 at this supply, the AC30's); the EL84s' idle
///         current follows from the drawn 50 Ω (Abbott: "the stock Vox value"); the treble pot's taper (drawn as "1MΩ",
///         audio as on the Express). The inverter's 39k is drawn between the tail's foot and the bias junction, the 1k
///         between the junction and the cathodes: here a 1k bias resistor over a 43.7k tail, the feedback entering at
///         the 4.7k as the block's feedback network has it.
///     </para>
/// </remarks>
public static class Liverpool {
    /// <summary>The 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    /// <summary>V1's node (B+5, measured).</summary>
    const double FrontSupply = 244;

    public static AmpDesign Design { get; } = new(
        "Liverpool",
        [
            new TriodeStageDesign(
                "V1a",
                TriodeModel.Ecc83,
                FrontSupply,
                100e3,
                1.5e3,
                CathodeBypass: 22e-6,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V1b", StackStage()),
            new NetworkStageDesign("V2", ColdStage())
        ],
        new(
            "4 x EL84",
            PentodeModel.EL84,
            TubesPerSide: 2,
            Supply: 331,
            ScreenResistor: 1e3,
            IdleCurrent: 0.04,
            CathodeBias: true,
            CathodeBypass: 220e-6,
            RatedPower: 30,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 269, 100e3, 100e3, 43.7e3, 1e3),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 100e3, 4.7e3, new(25e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: true,
            Transformer: new(4e3),
            Speaker: SpeakerDesign.OpenBack2x12,
            // 80 µF, 1k to the screens' 40 µF, then 18k + 9.1k to V2's node as one dropper.
            PowerSupply: new(25, 80e-6, 1e3, 40e-6, 27.1e3, 20e-6, 3e-3, RectifierModel.Silicon),
            InverterInput: new(0.1e-6, 1e6),
            CathodeResistor: 50
        ),
        -10
    ) {
        Panel = AmpPanel.Standard with {
            Gain = "Volume",
            Master = null,
            Depth = null,
            Cut = null,
            Switch1 = "Clarity"
        }
    };

    /// <summary>
    ///     The stack on V1a's plate, the volume with the bright caps, 2.2k into V1b (100k from 244 V, 2.7k with 22 µF, its
    ///     grid-plate capacitance).
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (trebleTop, trebleBottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, trebleTop, 50e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(net.Input, slope, 100e3);
        net.Capacitor(slope, trebleBottom, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Audio), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, volume, 500e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.SwitchedCapacitor(treble, volume, 100e-12, SwitchRole.Switch1, CapacitorRole.Bright);
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Resistor(volume, grid, 2.2e3);
        net.Supply(supply, FrontSupply);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>C8 (2.2 nF) into R7 (68k), V2: 100k from 256 V, 10k unbypassed.</summary>
    static StageNetlist ColdStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, grid, 2.2e-9);
        net.Resistor(grid, StageNetlist.Ground, 68e3);
        net.Supply(supply, 256);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }
}
