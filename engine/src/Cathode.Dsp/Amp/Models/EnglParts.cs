using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     What the two Engl models' power amps share on their factory drawings (645-1 and 610-2): two power tubes a side
///     in fixed bias, 2.2k stoppers, 47 nF from a long-tail pair with 82k and 100k plate resistors and 470 Ω under its
///     cathodes, 100 nF into its first grid with 100k to the bias junction, and a closed 4x12.
/// </summary>
static class EnglParts {
    /// <summary>
    ///     The output stage: its label and tube, B+ in volts, the screen resistors, the idle current per tube in
    ///     amperes, the rated power in watts and the transformer's plate-to-plate primary in ohms.
    /// </summary>
    public readonly record struct Output(
        string Name,
        PentodeModel Tube,
        double Supply,
        double ScreenResistor,
        double IdleCurrent,
        double RatedPower,
        double Primary
    );

    /// <summary>
    ///     The inverter: its rail in volts, the whole path from its bias junction to ground (the tail resistor and the
    ///     feedback shunt under it), and the power tubes' bias feed resistors.
    /// </summary>
    public readonly record struct Inverter(double Rail, double Tail, double BiasFeed);

    /// <summary>The power amp of <paramref name="output" /> behind <paramref name="inverter" />.</summary>
    public static PowerAmpDesign Power(
        Output output,
        Inverter inverter,
        FeedbackDesign feedback,
        PowerSupplyDesign supply
    ) =>
        new(
            output.Name,
            output.Tube,
            2,
            output.Supply,
            output.ScreenResistor,
            output.IdleCurrent,
            false,
            0,
            output.RatedPower,
            new LongTailPairDesign(TriodeModel.Ecc83, inverter.Rail, 82e3, 100e3, inverter.Tail, 470),
            47e-9,
            inverter.BiasFeed,
            2.2e3,
            0,
            new(1e6, PotTaper.Linear),
            feedback,
            true,
            new(output.Primary),
            SpeakerDesign.Closed4x12,
            supply,
            InverterInput: new(100e-9, 100e3)
        );
}
