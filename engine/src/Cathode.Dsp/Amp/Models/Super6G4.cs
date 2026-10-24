using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Fender Super 6G4 (brownface "Super-Amp", 40 W, 2 × 10): the Concert's circuit (<see cref="Brownface" />: the Normal
///     or Vibrato channel on <see cref="AmpPanel.Switch1" />, the harmonic vibrato with Speed, Intensity and the pedal on
///     <see cref="AmpPanel.Switch2" />, a 7025 long-tail pair), two 6L6GC in fixed bias at 460 V on a GZ34, and 10k of
///     feedback from the 4 Ω tap into 1.5k with the 5k Presence and 0.1 µF across it. Source: the Fender drawing and
///     layout "Super-Amp 6G4" (A-FJ; schematicheaven.net/fenderamps/super_6g4_schem.pdf), the same drawing family as the
///     6G12's, every preamp, vibrato and inverter value the same.
/// </summary>
/// <remarks>
///     Read: 456 V plates, 458 / 430 V nodes, 470 Ω 1 W screens, −55 V bias, the GZ34 into 2 × 20 µF, the 125C1A choke,
///     the 10k feedback resistor. Unverified: the 6L6 idle current (35 mA, as the Concert), the output transformer
///     45216's primary (4.2k assumed), the supply's source resistance, the 2 × 10's impedance curve (the 4 × 10 preset).
/// </remarks>
public static class Super6G4 {
    public static AmpDesign Design { get; } = new(
        "Super 6G4",
        [Brownface.Channels()],
        Concert6G12.Design.Power with {
            Feedback = new(1 / Math.Sqrt(2), 10e3, 1.5e3, new(5e3, PotTaper.Linear), 0.1e-6),
            PowerSupply = new(100, 40e-6, 100, 20e-6, 4.7e3, 20e-6, 6e-3, RectifierModel.GZ34)
        },
        -5
    ) {
        Panel = Brownface.Panel
    };
}
