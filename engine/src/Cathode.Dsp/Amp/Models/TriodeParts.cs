using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>A gain stage's triode as the Engl and the Peavey models have it, in a netlist that wires the rest.</summary>
public static class TriodeParts {
    /// <summary>
    ///     A common-cathode 12AX7 on <paramref name="grid" />: <paramref name="plateResistor" /> from
    ///     <paramref name="rail" />, <paramref name="cathodeResistor" /> to ground with <paramref name="bypass" />
    ///     across it (0: unbypassed). Returns the plate.
    /// </summary>
    public static int CommonCathode(
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
}
