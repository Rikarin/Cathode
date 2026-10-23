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

/// <summary>A potentiometer: its track resistance and taper.</summary>
public readonly record struct Pot(double Ohms, PotTaper Taper) {
    /// <summary>The fraction of the track between the wiper and the bottom end for rotation <paramref name="rotation" /> (0..1).</summary>
    public double Fraction(double rotation) {
        var p = Math.Clamp(rotation, 0, 1);
        return Taper switch {
            PotTaper.Audio => AudioLaw(p),
            PotTaper.ReverseAudio => 1 - AudioLaw(1 - p),
            _ => p
        };
    }

    /// <summary>An exponential law through (0, 0), (0.5, 0.1) and (1, 1), like a real A-taper track.</summary>
    static double AudioLaw(double p) => (Math.Pow(81, p) - 1) / 80;
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
