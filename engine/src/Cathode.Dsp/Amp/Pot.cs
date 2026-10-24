using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>How a pot's resistance follows its rotation.</summary>
public enum PotTaper {
    /// <summary>Resistance proportional to rotation.</summary>
    Linear,

    /// <summary>Logarithmic ("A") taper: about 10 % of the track at half rotation.</summary>
    Audio,

    /// <summary>Reverse logarithmic ("C") taper: about 90 % at half rotation.</summary>
    ReverseAudio
}

/// <summary>
///     A potentiometer: its track resistance and taper; for an audio taper, <paramref name="Midpoint" /> is the fraction
///     of the track below the wiper at half rotation (Fender marks it in the part number: "250k15A" is 15 %, "100k30A"
///     30 %; 10 % is the usual A taper).
/// </summary>
public readonly record struct Pot(double Ohms, PotTaper Taper, double Midpoint = 0.1) {
    /// <summary>The fraction of the track between the wiper and the bottom end for rotation <paramref name="rotation" /> (0..1).</summary>
    public double Fraction(double rotation) {
        var p = Math.Clamp(rotation, 0, 1);
        return Taper switch {
            PotTaper.Audio => AudioLaw(p, Midpoint),
            PotTaper.ReverseAudio => 1 - AudioLaw(1 - p, Midpoint),
            _ => p
        };
    }

    /// <summary>
    ///     An exponential law through (0, 0), (0.5, <paramref name="midpoint" />) and (1, 1), like a real A-taper track:
    ///     (b^p - 1) / (b - 1) with b = ((1 - m) / m)², which is 81 for the usual 10 %.
    /// </summary>
    static double AudioLaw(double p, double midpoint) {
        var m = midpoint is > 0 and < 0.5 ? midpoint : 0.1;
        var b = m == 0.1 ? 81 : (1 - m) / m * ((1 - m) / m);
        return (Math.Pow(b, p) - 1) / (b - 1);
    }
}

/// <summary>
///     The two halves of a pot used as a voltage divider inside an R-type adaptor: top end to wiper and wiper to the
///     bottom end, as internal resistors. Moving it changes both and needs a propagate of the tree.
/// </summary>
public readonly record struct PotDivider(int Top, int Bottom, int Wiper, Pot Pot) {
    /// <summary>Adds the pot between <paramref name="top" /> and <paramref name="bottom" />; returns it with a new wiper node.</summary>
    public static PotDivider Add(WdfNetlist net, int top, int bottom, Pot pot) {
        var wiper = net.Node();
        var upper = net.Resistor(top, wiper, pot.Ohms / 2);
        var lower = net.Resistor(wiper, bottom, pot.Ohms / 2);
        return new(upper, lower, wiper, pot);
    }

    /// <summary>The resistances (top to wiper, wiper to bottom) at <paramref name="rotation" /> (0..1).</summary>
    public (double Upper, double Lower) Resistances(double rotation) {
        var fraction = Pot.Fraction(rotation);
        return ((1 - fraction) * Pot.Ohms, fraction * Pot.Ohms);
    }
}
