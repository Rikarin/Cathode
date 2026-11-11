using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     The stages the Peavey 5150 and the 5150 II (6505+) lead channels have in common, part for part on both factory
///     drawings but for the cold clipper's coupling cap: the cold clipper, the two gain stages after it, the
///     shunt-feedback stage ahead of the tone stack, and the post stage that drives the inverter. Each takes its rail,
///     which the two amps' supplies set; both amps name the rails V1 (the supply chain's second node) and V2 (its
///     first).
/// </summary>
public static class PeaveyParts {
    /// <summary>
    ///     The V2A cold clipper on the V1 rail: <paramref name="coupling" /> and 470k from the stage before into
    ///     <paramref name="gridLeak" /> at the grid (1M on the lead channels; the 5150's rhythm channel puts 220k
    ///     beside it); 100k plate, 39k unbypassed.
    /// </summary>
    public static TriodeStageDesign ColdClipper(string name, double rail, double coupling, double gridLeak = 1e6) =>
        new(
            name,
            TriodeModel.Ecc83,
            rail,
            100e3,
            39e3,
            CouplingCap: coupling,
            SeriesResistor: 470e3,
            GridLeak: gridLeak
        ) { SupplyNode = 2 };

    /// <summary>
    ///     The stages between the cold clipper and the tone stack, on the V2 rail: V2B, V5B (22 nF, 1M over 100k; 220k
    ///     plate, 2.2k unbypassed) and V5A.
    /// </summary>
    public static AmpStageDesign[] GainToDriver(double v2Rail) => [
        new NetworkStageDesign("V2B", ThirdGain(v2Rail)) { SupplyNode = 1 },
        new TriodeStageDesign(
            "V5B",
            TriodeModel.Ecc83,
            v2Rail,
            220e3,
            2.2e3,
            InputResistance: 1e6,
            CouplingCap: 22e-9,
            GridLeak: 100e3
        ) { SupplyNode = 1 },
        new NetworkStageDesign("V5A", FeedbackStage(v2Rail)) { SupplyNode = 1 }
    ];

    /// <summary>
    ///     The stages between the pre gain's stage and the tone stack: the V2A cold clipper
    ///     (<see cref="ColdClipper" /> with <paramref name="clipperCoupling" />) on the V1 rail, then
    ///     <see cref="GainToDriver" /> on the V2 rail.
    /// </summary>
    public static AmpStageDesign[] ColdClipperToDriver(double v1Rail, double v2Rail, double clipperCoupling) => [
        ColdClipper("V2A", v1Rail, clipperCoupling), ..GainToDriver(v2Rail)
    ];

    /// <summary>The post stage V3A (<see cref="PostStage" />) on the V2 rail.</summary>
    public static AmpStageDesign Post(double rail) => new NetworkStageDesign("V3A", PostStage(rail)) { SupplyNode = 1 };

    /// <summary>
    ///     A common-cathode 12AX7 on <paramref name="grid" />: <paramref name="plateResistor" /> from
    ///     <paramref name="rail" />, <paramref name="cathodeResistor" /> to ground with <paramref name="bypass" />
    ///     across it (0: unbypassed). Returns the plate.
    /// </summary>
    public static int Triode(
        StageNetlist net,
        int grid,
        double rail,
        double plateResistor,
        double cathodeResistor,
        double bypass = 0
    ) {
        var (supply, plate, cathode) = (net.Node(), net.Node(), net.Node());
        net.Supply(supply, rail);
        net.Resistor(supply, plate, plateResistor);
        net.CathodeResistor(cathode, StageNetlist.Ground, cathodeResistor);
        if (bypass > 0) {
            net.Capacitor(cathode, StageNetlist.Ground, bypass);
        }

        net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
        return plate;
    }

    /// <summary>V2B: 22 nF into 330k ‖ 1 nF to ground, 220k to the grid; 220k plate, 1.82k ‖ 1 µF.</summary>
    static StageNetlist ThirdGain(double rail) {
        var net = new StageNetlist();
        var (coupled, grid) = (net.Node(), net.Node());
        net.Capacitor(net.Input, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 330e3);
        net.Capacitor(coupled, StageNetlist.Ground, 1e-9);
        net.Resistor(coupled, grid, 220e3);
        net.Output = Triode(net, grid, rail, 220e3, 1.82e3, 1e-6);
        return net;
    }

    /// <summary>
    ///     V5A with shunt feedback: 1M and 22 nF in, 1M grid leak; 100k plate, 2.2k unbypassed; 22 nF out, and 1M from
    ///     the coupled output back to the grid.
    /// </summary>
    static StageNetlist FeedbackStage(double rail) {
        var net = new StageNetlist();
        var (series, grid) = (net.Node(), net.Node());
        net.Resistor(net.Input, series, 1e6);
        net.Capacitor(series, grid, 22e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        var plate = Triode(net, grid, rail, 100e3, 2.2e3);
        var output = net.Node();
        net.Capacitor(plate, output, 22e-9);
        net.Resistor(output, grid, 1e6);
        net.Output = output;
        return net;
    }

    /// <summary>
    ///     The post stage V3A from the loop return: 47 nF, 1M grid leak; 68k plate, 3.3k ‖ 4.7 nF; 22 nF out, 100k in
    ///     series with the inverter's grid and its 1M leak.
    /// </summary>
    static StageNetlist PostStage(double rail) {
        var net = new StageNetlist();
        var grid = net.Node();
        net.Capacitor(net.Input, grid, 47e-9);
        net.Resistor(grid, StageNetlist.Ground, 1e6);
        var plate = Triode(net, grid, rail, 68e3, 3.3e3, 4.7e-9);
        var (coupled, inverter) = (net.Node(), net.Node());
        net.Capacitor(plate, coupled, 22e-9);
        net.Resistor(coupled, StageNetlist.Ground, 1e6);
        net.Resistor(coupled, inverter, 100e3);
        net.Output = inverter;
        return net;
    }
}
