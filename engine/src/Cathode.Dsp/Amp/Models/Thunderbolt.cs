using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Supro Thunderbolt S6420 (Valco, 1966; 35 W, a 15 in speaker): two input jacks through 47k each into 0.005 µF
///     and a 270k grid leak, V1 (½ 12AX7: 100k, 3.9k ‖ 35 µF, 0.001 µF from its plate to ground), 0.05 µF into the
///     500k audio Volume, the Tone network (0.003 µF from the Volume's wiper to one end of the 500k Tone pot, which
///     100k returns to ground; 150k from the wiper to the pot's other end, which has 0.01 µF to ground; the Tone's
///     wiper on V2A's grid), V2A (½ 12AX7: 270k, 2.2k ‖ 35 µF), a fixed paraphase inverter on V2B (270k, 3.9k
///     unbypassed, its grid on a 270k / 12k divider after V2A's 0.05 µF), 0.05 µF and 47k into two 6L6GC on a shared
///     200 Ω cathode resistor with no bypass cap, a silicon bridge, and no global feedback. Panel: Volume, Tone.
/// </summary>
/// <remarks>
///     <para>
///         Sources: no factory drawing was found; the model is read from four traces of 1966 amps, which agree on the
///         topology and on most values. The main one is Jim Mitchell's "Supro 6420 Thunderbolt Bass Amp, 35 Watts, Late
///         1966" (dated 05/13/2007; el34world.com/charts/Schematics/files/Supro/Supro_6420.pdf), the only one with DC
///         voltages at the tube pins: every value here is his unless it says otherwise. The other three are the
///         sheets of schematicheaven.net/bargainbin/supro_thunderbolt_s6420.pdf: C.J. Poulos's hand trace "#S-6420"
///         (5/3/04), Sean Weatherford's redraw (3/3/2003) with Poulos's corrections of 3/22/06, and Rik Shoup's
///         drawing.
///     </para>
///     <para>
///         Where the traces differ. V2's cathodes: Mitchell has 2.2k ‖ 35 µF under V2A and 3.9k unbypassed under
///         V2B; the other three have them the other way round. Mitchell's is taken: his cathode voltages fit it (1.4 V on
///         V2A and
///         2.0 V on V2B, where a 12AX7 on 270k from 337 V settles on 2.2k and on 3.9k: 1.44 V and 1.97 V on the
///         tables), and so does the divider: 12k in 282k is 1 in 23.5, against a gain in the thirties for a 12AX7 on
///         an unbypassed 3.9k and about sixty on a bypassed 2.2k. Either way the inverted side is the hotter one. V1's
///         cathode: 3.9k (Mitchell, Shoup; 3.3k on the other two). The resistor
///         from the Tone pot's 0.003 µF end to ground: 100k (Mitchell; 110k on Poulos's and Weatherford's; Shoup
///         draws it at the pot's other end). The last filter cap: 4 µF (Mitchell; Poulos "4 mfd ?", Weatherford "8 ?",
///         Shoup 40). The input jacks: plain (Shoup draws shorting jacks, which would ground the unused 47k and halve
///         the signal). The supply nodes: Mitchell's 397 / 390 / 337 / 259 V (Poulos: 380 / 370 / 330 / 230 V).
///     </para>
///     <para>
///         As the tables give it: V1 207 V and 2.0 V (the drawing: 197 V and 2.2 V), V2A 160 V and 1.44 V (138 V
///         and 1.4 V), V2B 201 V and 1.97 V (167 V and 2.0 V), the 6L6s' cathode 29 V (30 V: 150 mA through the
///         200 Ω for the pair, plates and screens together) and their screens 383 V (390 V). V2's plate readings on
///         the drawing do not follow from its cathode readings through its own resistors (1.4 V on 2.2k is 0.64 mA,
///         which leaves 165 V under the 270k, not 138 V), so the model is held to the cathodes.
///     </para>
///     <para>
///         Assumed, none of it on the drawings: the output transformer, 6.6k plate to plate (Tung-Sol's 6L6GC data of
///         April 1960, push-pull class AB1 at 360 V on the plates; the amp has about 367 V across its tubes); the
///         15 in speaker's impedance curve (<see cref="SpeakerDesign.OpenBack1x15" />); the Tone pot's direction and
///         taper (both pots are marked audio; up is taken as the wiper toward the 0.003 µF); the 12AX7's grid-to-plate
///         capacitance (<see cref="Ab763.GridPlate" />). The supply's source resistance is from Mitchell's own
///         figures: 322 V AC on the open secondary is a 455 V peak, and the reservoir sits at 397 V with the amp
///         idling at about 152 mA, so 380 Ω.
///     </para>
///     <para>
///         What the library does not do, and what stands in for it. The output tubes' cathode resistor has no bypass
///         cap, so in the amp the cathode follows the two tubes' summed current at once; the power amp integrates its
///         cathode node on a cap, and the model gives it 1 µF (a 0.2 ms time constant on the 200 Ω), small enough for
///         the cathode to follow the envelope and most of the even-harmonic current of a note, not all of it. The two
///         output tubes have different grid leaks (the 282k divider on one side, 470k on the other); the power amp has
///         one value, and the model uses 282k. The inverter is a <see cref="ParaphaseDesign" />: the second triode's
///         grid current and the pull of the first output tube's grid current on the divider are left out (see
///         <see cref="Paraphase" />).
///     </para>
/// </remarks>
public static class Thunderbolt {
    /// <summary>Node C, which feeds both halves of V2 through 27k from the screens' node (390 V).</summary>
    const double Driver = 337;

    /// <summary>Node D, V1's, through 100k from node C.</summary>
    const double Input = 259;

    /// <summary>
    ///     V2A's plate as a source: its 270k in parallel with the 12AX7's plate resistance where the stage rests.
    /// </summary>
    internal static double DriverPlate { get; } = PlateOutput(Driver, 270e3, 2.2e3);

    public static AmpDesign Design { get; } = new(
        "Thunderbolt S6420",
        [
            new NetworkStageDesign("V1", InputStage(), 47e3) { SupplyNode = 1 },
            new NetworkStageDesign("V2A", ToneStage()) { SupplyNode = 0 }
        ],
        new(
            "2 x 6L6GC",
            PentodeModel.SixL6GC,
            1,
            397,
            1,
            0.07,
            true,
            1e-6,
            35,
            new ParaphaseDesign(TriodeModel.Ecc83, Driver, 270e3, 3.9e3, 270e3, 12e3, DriverPlate),
            0.05e-6,
            282e3,
            47e3,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 10e3, 100, new(1e3, PotTaper.Linear), 1e-12),
            false,
            new(6.6e3),
            SpeakerDesign.OpenBack1x15,
            new(380, 20e-6, 1e3, 30e-6, 27e3, 10e-6, (390 - Driver) / 27e3, RectifierModel.Silicon),
            CathodeResistor: 200
        ),
        -6
    ) {
        PreampSupply = new([new(100e3, 4e-6, (Driver - Input) / 100e3)]),
        Panel = new() {
            Gain = "Volume",
            Treble = "Tone",
            Bass = null,
            Middle = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null
        }
    };

    /// <summary>
    ///     The output resistance at the plate of a 12AX7 stage with a bypassed cathode: its plate resistor in parallel
    ///     with the triode's plate resistance at the stage's rest point.
    /// </summary>
    static double PlateOutput(double supply, double plateResistor, double cathodeResistor) {
        var (_, plate, cathode) = OperatingPoint.CommonCathode(
            TriodeModel.Ecc83,
            0,
            supply,
            plateResistor,
            cathodeResistor
        );
        var (resistance, _) = OperatingPoint.SmallSignal(TriodeModel.Ecc83, -cathode, plate - cathode);
        return resistance * plateResistor / (resistance + plateResistor);
    }

    /// <summary>
    ///     The played jack's 47k (the stage's input resistance), 0.005 µF and the 270k grid leak, V1: 100k from node
    ///     D, 3.9k ‖ 35 µF; the output is its plate.
    /// </summary>
    static StageNetlist InputStage() {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Capacitor(net.Input, grid, 0.005e-6);
        net.Resistor(grid, StageNetlist.Ground, 270e3);
        net.Output = Half(net, grid, Input, 100e3, 3.9e3);
        return net;
    }

    /// <summary>
    ///     A 12AX7 half as both V1 and V2A have it: <paramref name="plateResistor" /> from a rail at
    ///     <paramref name="supply" /> volts, <paramref name="cathodeResistor" /> ‖ 35 µF, and the grid-to-plate
    ///     capacitance. Returns the plate.
    /// </summary>
    static int Half(StageNetlist net, int grid, double supply, double plateResistor, double cathodeResistor) {
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        net.Capacitor(cathode, StageNetlist.Ground, 35e-6);
        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        net.Capacitor(grid, plate, Ab763.GridPlate);
        return plate;
    }

    /// <summary>
    ///     On V1's plate (a Thévenin cut: the plate's resistance carries this load): 0.001 µF to ground, 0.05 µF into
    ///     the Volume, the Tone network on its wiper, and V2A (270k from node C, 2.2k ‖ 35 µF) with the Tone's wiper
    ///     on its grid; the output is V2A's plate, the inverter's input.
    /// </summary>
    static StageNetlist ToneStage() {
        var net = new StageNetlist();
        var (top, bright, dark) = (net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, StageNetlist.Ground, 0.001e-6, CapacitorRole.Plate);
        net.Capacitor(net.Input, top, 0.05e-6);
        var volume = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Capacitor(volume, bright, 0.003e-6, CapacitorRole.Stack);
        net.Resistor(bright, StageNetlist.Ground, 100e3);
        net.Resistor(volume, dark, 150e3);
        net.Capacitor(dark, StageNetlist.Ground, 0.01e-6, CapacitorRole.Stack);
        var grid = net.Pot(bright, dark, new(500e3, PotTaper.Audio), PotRole.Treble);
        net.Output = net.DcBlock(Half(net, grid, Driver, 270e3, 2.2e3));
        return net;
    }
}
