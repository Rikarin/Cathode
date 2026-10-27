using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Gibson GA-17RVT Scout (about 1964; 2 × 6AQ5, a 10 in speaker): V2A (½ 6EU7: 470k, 4.7k ‖ 20 µF) into 0.001 µF
///     and a bridged-T notch (220k + 220k with 0.003 µF from their junction to ground and 500 pF across both), the 500k
///     audio Volume, 470k into V2B (470k, 4.7k with only 0.1 µF across it), 0.005 µF into a 6EU7 cathodyne (100k plate,
///     2.7k + 100k cathode, the grid's 470k returned between them), two 6AQ5 on a shared 270 Ω ‖ 20 µF cathode at 300 V
///     on a 6CA4, and no global feedback. Panel: Volume. Source: Gibson's GA-17RVT schematic with DC voltages
///     (schematicheaven.net/gibsonamps/ga17rvt.pdf, via Parts is Parts); the tubes are this repository's fits to the
///     RCA 6EU7 and GE 6AQ5 datasheets (<see cref="TriodeModel.SixEU7" />, <see cref="PentodeModel.SixAQ5" />).
/// </summary>
/// <remarks>
///     <para>
///         Read: the inputs (68k, 68k, 1M: taken as Fender's two-jack network, 34k into the grid), V2A (120 V plate
///         from 257 V, 1.2 V cathode), the bridged T, R8 (500k audio, on the parts list), R9 470k, V2B (130 V plate
///         from 263 V, 1.2 V cathode); at V2B's grid the reverb recovery's 0.005 µF from V1B's plate (100k from 257 V,
///         190 V) and R15 1M to the reverb footswitch; the reverb's 2M audio drive pot across the Volume's top; the
///         cathodyne (211 V plate, 51 V cathode, 49.5 V at the junction, from 263 V); 0.01 µF and 220k into the 6AQ5
///         grids, the two grid leaks returned through 330k; the shared 270 Ω ‖ 20 µF cathode (17 V), plates and screens
///         at 300 V (3.9k from the 305 V reservoir to the screens, 4.7k to 263 V), the 6CA4 (EZ81) on 255-0-255 V AC, 2
///         × 20 µF.
///     </para>
///     <para>
///         As the tables give it the stages sit at 119 V (V2A) and 122 V (V2B) against 120 V and 130 V, and the 6AQ5s
///         at 16 V of cathode (the drawing: 17 V). Assumed: the reverb's footswitch closed (R15 to ground) and its
///         drive at 0 (the 2M pot's whole track across the Volume), the recovery plate as 100k ‖ r_p (about 37k); the
///         output transformer TF 18-01 at the 6AQ5 datasheet's 10k plate to plate for push-pull class AB1; the supply's
///         source resistance; the 1 × 12 open-back preset for the 1 × 10. The two 330k-returned grid leaks act as 220k
///         for the push-pull signal. Not modelled: the reverb (at 0). The tremolo is a bias tremolo into the grid
///         leaks' return (<see cref="Tremolo" />): Speed and Depth on the Circuit Amp's tremolo parameters, Depth 0 its
///         footswitch off.
///     </para>
/// </remarks>
public static class GibsonScout {
    /// <summary>The 6EU7's grid-to-plate capacitance (RCA: 1.5 pF).</summary>
    const double GridPlate = 1.5e-12;

    /// <summary>
    ///     The tremolo, from the drawing: a bias tremolo. V3B (½ 6EU7: R29 150k from the 263 V node, its cathode
    ///     grounded, 118 V at the plate, its grid at −0.6 V on R32 4.7M) with C14 0.022 µF from its plate to R30 330k,
    ///     C15 0.022 µF to R31 150k and the Speed rheostat R33 (1.5M reverse audio; the tremolo footswitch closes it to
    ///     ground, open it stops the oscillator), C16 0.01 µF to the grid; from the plate C13 0.047 µF and R34 1M into
    ///     the 6AQ5 grid leaks' junction, which R25 330k returns to the cathode's 20 µF (an AC ground). There is no
    ///     Intensity control: the depth scales the swing, 100 % the amp's own.
    /// </summary>
    internal static BiasTremoloDesign Tremolo { get; } = new(
        new(
            TriodeModel.SixEU7,
            263,
            150e3,
            0,
            0.022e-6,
            0.022e-6,
            0.01e-6,
            330e3,
            150e3,
            4.7e6,
            new(1.5e6, PotTaper.ReverseAudio),
            2,
            PlateLoad: 1.1e6
        ),
        1e6,
        0.047e-6,
        null,
        ReturnResistor: 330e3
    );

    public static AmpDesign Design { get; } = new(
        "Scout GA-17RVT",
        [
            FirstStage(),
            SecondStage()
        ],
        new(
            "2 x 6AQ5",
            PentodeModel.SixAQ5,
            1,
            305,
            1,
            0.03,
            true,
            20e-6,
            10,
            new CathodyneDesign(TriodeModel.SixEU7, 263, 100e3, 100e3, 2.7e3),
            0.01e-6,
            220e3,
            1,
            0,
            new(1e6, PotTaper.Linear),
            new(1, 10e3, 100, new(1e3, PotTaper.Linear), 1e-12),
            false,
            new(10e3),
            SpeakerDesign.OpenBack1x12,
            new(150, 20e-6, 3.9e3, 20e-6, 4.7e3, 20e-6, 7.9e-3, RectifierModel.EZ81),
            InverterInput: new(0.005e-6, 470e3),
            CathodeResistor: 270
        ) { BiasTremolo = Tremolo },
        1.5
    ) {
        Panel = new() {
            TremoloRate = "Speed",
            TremoloDepth = "Depth",
            Gain = "Volume",
            Bass = null,
            Middle = null,
            Treble = null,
            Presence = null,
            Depth = null,
            Cut = null,
            Master = null,
            Bright = null
        }
    };

    /// <summary>V2A: 470k from 257 V, 4.7k ‖ 20 µF; the output is its plate.</summary>
    static NetworkStageDesign FirstStage() {
        var net = new StageNetlist();
        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 257);
        net.Resistor(rail, plate, 470e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 4.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 20e-6);
        net.Triode(plate, net.Input, cathode, TriodeModel.SixEU7);
        net.Capacitor(net.Input, plate, GridPlate);
        net.Output = plate;
        return new("V2A", net, 34e3);
    }

    /// <summary>
    ///     The bridged T, the Volume (with the reverb drive pot's track beside it), 470k into V2B's grid (with the
    ///     reverb recovery's coupling and R15), V2B: 470k from 263 V, 4.7k ‖ 0.1 µF; the output is its plate.
    /// </summary>
    static NetworkStageDesign SecondStage() {
        var net = new StageNetlist();
        var (coupled, middle, top, grid, recovery) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 0.001e-6);
        net.Resistor(coupled, middle, 220e3);
        net.Resistor(middle, top, 220e3);
        net.Capacitor(middle, StageNetlist.Ground, 0.003e-6, CapacitorRole.Stack);
        net.Capacitor(coupled, top, 500e-12, CapacitorRole.Stack);
        net.Resistor(top, StageNetlist.Ground, 2e6);
        var volume = net.Pot(top, StageNetlist.Ground, new(500e3, PotTaper.Audio), PotRole.Gain);
        net.Resistor(volume, grid, 470e3);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        net.Capacitor(grid, recovery, 0.005e-6);
        net.Resistor(recovery, StageNetlist.Ground, 37e3);

        var (plate, cathode, rail) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, 263);
        net.Resistor(rail, plate, 470e3);
        net.CathodeResistor(cathode, StageNetlist.Ground, 4.7e3);
        net.Capacitor(cathode, StageNetlist.Ground, 0.1e-6);
        net.Triode(plate, grid, cathode, TriodeModel.SixEU7);
        net.Capacitor(grid, plate, GridPlate);
        net.Output = net.DcBlock(plate);
        return new("V2B", net);
    }
}
