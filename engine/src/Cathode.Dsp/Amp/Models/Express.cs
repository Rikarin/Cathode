using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Trainwreck Express, version A1a of the "Trainwreck Express" redraws (schematicheaven
///     <c>newamps/trainwreck_express.pdf</c>, rev. 09-29-00, "based on designs from Ken Fischer"): versions A1 and A1a
///     "came from someone who has seen the inside of a real Express", and the collection's notes take A1a as the more
///     accurate; its red values are the corrections over A1. V1: 1M, 100k plate, 1.5k with 22 µF; the stack on V1's
///     plate (500 pF to the 1M log treble pot, 100k slope, 22 nF to the 250k linear bass pot, 22 nF to the 25k linear
///     middle pot wired as a rheostat), the treble wiper into the 1M log volume with the three-way bright switch from
///     its top to the wiper (100 pF, off, 500 pF); V2: 100k, 2.7k with 22 µF; 2 nF into 150k, V3: 100k, 10k unbypassed
///     (a cold stage); 0.1 µF straight into a 12AX7 long-tail pair (no grid stopper: the inverter blocks under drive)
///     with 1M leaks, 470 Ω bias and a 10k tail; 82k / 100k plates, 22 nF into 220k leaks and 1.5k stoppers, two EL34
///     with 1k screen resistors, fixed bias; feedback 100k from the 8 Ω tap into the tail's foot, where the 5k linear
///     presence pot goes to ground with 0.1 µF from its wiper; silicon rectifier into 160 + 160 µF stacked, 1k to the
///     screens (40 µF), then 18.2k, 9.1k and 9.1k to the inverter's, V3's and V1/V2's nodes (20 µF each). About 410 V.
/// </summary>
/// <remarks>
///     <para>
///         A redraw from hearsay, not a trace with measurements: the most reliable of the collection's ten versions by
///         its own notes, but not proven. Fractal's notes on their reference Express confirm the three-way bright
///         switch (off, 100 pF, 500 pF) and the missing grid stopper ahead of the inverter. The output transformer is
///         drawn as "similar to Hammond P-T1650H", whose primary is 6.6k plate to plate (Hammond's listing); used.
///     </para>
///     <para>
///         Assumed, not drawn: the node voltages below B+ (only "~410 V" is given; the others follow from the droppers
///         at plausible currents: 395 / 300 / 270 / 250 V), the EL34s' idle current (35 mA), the presence pot's
///         topology (the block's feedback network puts the pot and cap across a fixed shunt; here the shunt is the 5k
///         pot itself, taken as 5k). The bright switch is two panel switches: Bright the 500 pF position, Switch 1 the
///         100 pF ("Clarity"); with both on, both caps are in (the real switch takes one).
///     </para>
/// </remarks>
public static class Express {
    /// <summary>The 12AX7's grid-plate capacitance.</summary>
    const double GridPlate = 1.7e-12;

    /// <summary>V1 and V2's node (B+5).</summary>
    const double FrontSupply = 250;

    public static AmpDesign Design { get; } = new(
        "Express",
        [
            new TriodeStageDesign(
                "V1",
                TriodeModel.Ecc83,
                FrontSupply,
                100e3,
                1.5e3,
                CathodeBypass: 22e-6,
                GridLeak: 1e6
            ),
            new NetworkStageDesign("V2", StackStage()),
            new NetworkStageDesign("V3", ColdStage())
        ],
        new(
            "2 x EL34",
            PentodeModel.EL34,
            TubesPerSide: 1,
            Supply: 410,
            ScreenResistor: 1e3,
            IdleCurrent: 0.035,
            CathodeBias: false,
            CathodeBypass: 0,
            RatedPower: 50,
            PhaseInverter: new LongTailPairDesign(TriodeModel.Ecc83, 300, 82e3, 100e3, 10e3, 470),
            CouplingCap: 22e-9,
            GridLeak: 220e3,
            GridStopper: 1.5e3,
            CutCap: 0,
            CutPot: new(1e6, PotTaper.Linear),
            Feedback: new(1, 100e3, 5e3, new(5e3, PotTaper.Linear), 0.1e-6, 90),
            HasFeedback: true,
            Transformer: new(6.6e3),
            Speaker: SpeakerDesign.Closed4x12,
            // 160 + 160 µF stacked, 1k to the screens' 40 µF, then 18.2k + 9.1k to V3's node as one dropper.
            PowerSupply: new(30, 80e-6, 1e3, 40e-6, 27.3e3, 20e-6, 4e-3, RectifierModel.Silicon),
            InverterInput: new(0.1e-6, 1e6)
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
    ///     The stack on V1's plate, the volume with the bright caps, and V2 (100k, 2.7k with 22 µF, its grid-plate
    ///     capacitance).
    /// </summary>
    static StageNetlist StackStage() {
        var net = new StageNetlist();
        var (trebleTop, trebleBottom, slope, middle) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, trebleTop, 500e-12, CapacitorRole.Stack);
        var treble = net.Pot(trebleTop, trebleBottom, new(1e6, PotTaper.Audio), PotRole.Treble);
        net.Resistor(net.Input, slope, 100e3);
        net.Capacitor(slope, trebleBottom, 22e-9, CapacitorRole.Stack);
        net.Rheostat(trebleBottom, middle, new(250e3, PotTaper.Linear), PotRole.Bass);
        net.Capacitor(slope, middle, 22e-9, CapacitorRole.Stack);
        net.Rheostat(middle, StageNetlist.Ground, new(25e3, PotTaper.Linear), PotRole.Middle);
        var volume = net.Pot(treble, StageNetlist.Ground, new(1e6, PotTaper.Audio), PotRole.Gain);
        net.SwitchedCapacitor(treble, volume, 500e-12, SwitchRole.Bright, CapacitorRole.Bright);
        net.SwitchedCapacitor(treble, volume, 100e-12, SwitchRole.Switch1, CapacitorRole.Bright);
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, FrontSupply);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 2.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 22e-6);
        net.Triode(plate, volume, cathode, TriodeModel.Ecc83);
        net.Capacitor(volume, plate, GridPlate);
        net.Output = plate;
        return net;
    }

    /// <summary>2 nF into 150k, V3: 100k from 270 V, 10k unbypassed.</summary>
    static StageNetlist ColdStage() {
        var net = new StageNetlist();
        var (grid, supply, plate, cathode) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, grid, 2e-9);
        net.Resistor(grid, StageNetlist.Ground, 150e3);
        net.Supply(supply, 270);
        net.Resistor(supply, plate, 100e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 10e3);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = plate;
        return net;
    }
}
