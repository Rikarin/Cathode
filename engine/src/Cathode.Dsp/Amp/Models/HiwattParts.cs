using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     The Hiwatt DR-series four-input preamp (Mark Huss's hiwatt.org redraw "Mid-70s Four-Input Preamp" rev 1.4,
///     https://hiwatt.org/Schematics/DR_Pre4Input_v2.pdf), which the DR103 (100 W) and DR504 (50 W) share, and the
///     panel of an amp built on it.
/// </summary>
/// <remarks>
///     A copy of the stages in <see cref="HiwattDR103" />, which keeps its own (its file is owned by another stream):
///     the same circuits with the rails as parameters. See that model for the reading of the tone network.
/// </remarks>
public static class HiwattParts {
    const double Closed = 10;
    const double Open = 1e12;

    /// <summary>The panel of a four-input Hiwatt: Brilliant and Normal volumes, the jumper and the normal input.</summary>
    public static AmpPanel Panel { get; } = AmpPanel.Standard with {
        Gain = "Brilliant Vol",
        Control1 = "Normal Vol",
        Master = "Master Vol",
        Cut = null,
        Depth = null,
        Bright = null,
        Switch1 = "Jumper",
        Switch2 = "Normal Input"
    };

    /// <summary>
    ///     V1: the guitar into the Brilliant channel's high input (34k: both 68k in parallel, 1M at the jack), into both
    ///     with the Jumper (switch 1), into the Normal channel alone with switch 2; two triodes on 220k plates and a
    ///     shared 1k1 with 47 nF; each plate through its coupling cap (Brilliant 1 nF, Normal 22 nF) to its 470k-A
    ///     volume and through 470k from the wiper to the mixing node, V2A's grid (the output). An idle channel's grid
    ///     rests on its jack's 1M.
    /// </summary>
    public static NetworkStageDesign Input(double supply) {
        var net = new StageNetlist();
        var (rail, cathode, mix) = (net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-9);

        var brilliantJack = net.Node();
        net.SwitchedResistor(net.Input, brilliantJack, Closed, Open, SwitchRole.Switch2);
        net.SwitchedResistor(net.Input, brilliantJack, 0, Closed, SwitchRole.Switch1);
        var normalJack = net.Node();
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch1);
        net.SwitchedResistor(net.Input, normalJack, 0, Closed, SwitchRole.Switch2);
        foreach (var (jack, coupling, volume) in new[] { (brilliantJack, 1e-9, PotRole.Gain), (normalJack, 22e-9, PotRole.Control1) }) {
            var (grid, plate, top) = (net.Node(), net.Node(), net.Node());
            net.Resistor(jack, StageNetlist.Ground, 1e6);
            net.Resistor(jack, grid, 34e3);
            net.Resistor(rail, plate, 220e3);
            net.Triode(plate, grid, cathode, TriodeModel.Ecc83);
            net.Capacitor(plate, top, coupling);
            var wiper = net.Pot(top, StageNetlist.Ground, new(470e3, PotTaper.Audio), volume);
            net.Resistor(wiper, mix, 470e3);
        }

        net.Output = mix;
        return new("V1", net);
    }

    /// <summary>
    ///     V2A from the mixing node (100k, 1k with 47 nF, its grid-plate capacitance behind the mixers) on the tone
    ///     network: 1 nF from the plate to a node A, which reaches the 220k Treble's top through 220 pF and its foot
    ///     through 220k; the Treble's foot through 22k to the 100k Middle (a rheostat with 1 nF across it) and on to the
    ///     470k-A Bass (a rheostat to ground); the plate also feeds 100k to a node with 47 nF to ground and 47 nF to the
    ///     Bass's top. The Treble's wiper goes through 22k to the 220k-A master, whose wiper is the output.
    /// </summary>
    public static NetworkStageDesign V2A(double supply) {
        var net = new StageNetlist();
        var (rail, plate, cathode, a, trebleTop, trebleFoot) = (net.Node(), net.Node(), net.Node(), net.Node(), net.Node(), net.Node());
        var (middleTop, bassTop, shunt, master) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.Capacitor(net.Input, plate, Ab763.GridPlate);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1e3);
        net.Capacitor(cathode, StageNetlist.Ground, 47e-9);

        net.Capacitor(plate, a, 1e-9, CapacitorRole.Stack);
        net.Capacitor(a, trebleTop, 220e-12, CapacitorRole.Stack);
        net.Resistor(a, trebleFoot, 220e3);
        var treble = net.Pot(trebleTop, trebleFoot, new(220e3, PotTaper.Linear), PotRole.Treble);
        net.Resistor(trebleFoot, middleTop, 22e3);
        net.Rheostat(middleTop, bassTop, new(100e3, PotTaper.Linear), PotRole.Middle);
        net.Capacitor(middleTop, bassTop, 1e-9, CapacitorRole.Stack);
        net.Rheostat(bassTop, StageNetlist.Ground, new(470e3, PotTaper.Audio), PotRole.Bass);
        net.Resistor(plate, shunt, 100e3);
        net.Capacitor(shunt, StageNetlist.Ground, 47e-9, CapacitorRole.Stack);
        net.Capacitor(shunt, bassTop, 47e-9, CapacitorRole.Stack);

        net.Resistor(treble, master, 22e3);
        var wiper = net.Pot(master, StageNetlist.Ground, new(220e3, PotTaper.Audio), PotRole.Master);
        net.Output = wiper;
        return new("V2A", net);
    }

    /// <summary>V2B (100k, 1k5 unbypassed), 22 nF into the inverter's 100k grid resistor (the output).</summary>
    public static NetworkStageDesign V2B(double supply) {
        var net = new StageNetlist();
        var (rail, plate, cathode, grid) = (net.Node(), net.Node(), net.Node(), net.Node());
        net.Supply(rail, supply);
        net.Resistor(rail, plate, 100e3);
        net.Triode(plate, net.Input, cathode, TriodeModel.Ecc83);
        net.CathodeResistor(cathode, StageNetlist.Ground, 1.5e3);
        net.Capacitor(plate, grid, 22e-9);
        net.Resistor(grid, StageNetlist.Ground, 100e3);
        net.Output = grid;
        return new("V2B", net);
    }
}
