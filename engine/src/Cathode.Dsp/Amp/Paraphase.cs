using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     A fixed paraphase inverter (the Supro Thunderbolt): the preamp's last plate drives one output tube through its
///     coupling cap, and after that cap a divider to ground (<paramref name="DividerTop" /> over
///     <paramref name="DividerBottom" />, which together are that tube's grid leak) feeds a small part of the same
///     signal to a second triode, whose plate drives the other output tube. Nothing balances the two sides but the
///     divider's ratio against the second triode's gain. The design is the second triode and the divider; output A is
///     the driving plate itself, so the power amp's input is that plate's AC voltage.
/// </summary>
/// <param name="Tube">The second triode.</param>
/// <param name="Supply">Its plate supply.</param>
/// <param name="PlateResistor">Its plate resistor.</param>
/// <param name="CathodeResistor">Its cathode resistor, unbypassed.</param>
/// <param name="DividerTop">From the driven output tube's grid node to the second triode's grid.</param>
/// <param name="DividerBottom">From the second triode's grid to ground.</param>
/// <param name="SourceResistance">
///     The driving plate's output resistance (the stage ahead: its plate resistor in parallel with the triode's plate
///     resistance), which is output A's impedance and what the divider loads.
/// </param>
/// <param name="InputRange">
///     The table's span (see <see cref="PhaseInverter.Range" />): the driving plate's whole swing, since output A
///     passes it on.
/// </param>
public sealed record ParaphaseDesign(
    TriodeModel Tube,
    double Supply,
    double PlateResistor,
    double CathodeResistor,
    double DividerTop,
    double DividerBottom,
    double SourceResistance,
    double InputRange = 240
) : PhaseInverterDesign(Tube, Supply) {
    /// <summary>
    ///     The second grid's share of the driving plate's open-circuit swing: the divider, loading the plate's output
    ///     resistance (the coupling cap as a short).
    /// </summary>
    public double Tap => DividerBottom / (SourceResistance + DividerTop + DividerBottom);
}

/// <summary>
///     A <see cref="ParaphaseDesign" />'s table: output A is the input itself (the driving plate's AC voltage), output
///     B the second triode's plate for its grid at <see cref="ParaphaseDesign.Tap" /> of the input, from the exact DC
///     solution of the triode on its plate and cathode resistors.
/// </summary>
/// <remarks>
///     The coupling cap ahead of the divider is taken as a short, so the map is memoryless like the other inverters'.
///     Left out: the second triode's grid current (its grid sees the divider's low resistance, and its unbypassed
///     cathode follows the grid), and the cross term of the output impedance: when the first output tube's grid
///     conducts and pulls its grid node down, the divider's tap falls with it in the amp, and here it does not (as in
///     <see cref="FloatingParaphase" />). Output A rests wherever the stage ahead leaves its plate; the table holds
///     only its AC part, so <see cref="PhaseInverter.RestPlateA" /> is 0.
/// </remarks>
public sealed class Paraphase : PhaseInverter {
    public ParaphaseDesign Design { get; }

    /// <summary>The second triode's plate current at rest.</summary>
    public double RestCurrent { get; }

    internal Paraphase(ParaphaseDesign design) : base(design.InputRange) {
        Design = design;
        var (rp, rk) = (design.PlateResistor, design.CathodeResistor);
        var rest = OperatingPoint.CommonCathode(design.Tube, 0, design.Supply, rp, rk);
        RestCurrent = rest.Current;
        RestPlateB = rest.Plate;
        RestCathode = rest.Cathode;
        RestGrid = 0;
        RestPlateA = 0;

        // The second plate's output impedance: a small current drawn from it is the supply lowered behind the plate
        // resistor.
        const double Probe = 1e-6;
        var drawn = OperatingPoint.CommonCathode(design.Tube, 0, design.Supply - Probe * rp, rp, rk).Plate;
        OutputImpedance = (design.SourceResistance, 0, 0, (RestPlateB - drawn) / Probe);

        for (var n = 0; n < Points; n++) {
            var vd = -Range + n * step;
            var grid = vd * design.Tap;
            var (_, plate, cathode) = OperatingPoint.CommonCathode(design.Tube, grid, design.Supply, rp, rk);
            a[n] = vd;
            b[n] = plate - RestPlateB;
            gridVoltages[n] = grid - cathode;
            leakReturns[n] = 0;
            cathodeResistances[n] = rk;
        }

        Differentiate();
    }
}
