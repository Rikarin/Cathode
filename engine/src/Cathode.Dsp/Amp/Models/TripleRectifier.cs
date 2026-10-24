namespace Cathode.Dsp.Amp.Models;

/// <summary>
///     Mesa/Boogie Triple Rectifier, red channel: the <see cref="DualRectifier" /> model's preamp (Mesa built the Triple
///     on the three-channel Dual Rectifier's preamp) with six 6L6GC, 150 W, and the same rectifier and Spongy switches
///     (in the Triple, three 5U4 tubes).
/// </summary>
/// <remarks>
///     No Triple Rectifier schematic was found (docs/07). Music Electronics Forum service threads on it (10035, 45273,
///     47453 in https://music-electronics-forum.com/forum/amplification/guitar-amps/) point to the three-channel Dual
///     Rectifier drawing and name only supply-side differences. Guesses, marked: the 1.3k primary (two thirds of the
///     Dual Rectifier model's, for three tubes a side), a larger reservoir (160 µF), the same 470 V B+, idle current and
///     inverter, and three 5U4GB in parallel for the tube rectifier.
/// </remarks>
public static class TripleRectifier {
    /// <summary>Three 5U4GB in parallel.</summary>
    public static RectifierModel FiveU4Trio { get; } = new("3 x 5U4", 3 * RectifierModel.FiveU4.Perveance);

    public static AmpDesign Design { get; } = DualRectifier.Design with {
        Name = "Triple Rectifier",
        Power = DualRectifier.Design.Power with {
            Name = "6 x 6L6GC",
            TubesPerSide = 3,
            RatedPower = 150,
            Transformer = new(1.3e3),
            PowerSupply = DualRectifier.Design.Power.PowerSupply with { Reservoir = 160e-6 },
            SupplySwitches = [
                new(SwitchRole.Switch2, FiveU4Trio),
                new(SwitchRole.Switch3, Variac: 0.9)
            ]
        }
    };
}
