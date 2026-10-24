namespace Cathode.Dsp.Cabinets;

/// <summary>
///     The Cab block's mic preamp types (<see cref="MicPreamp" />): each a transfer curve that passes 0 with slope 1, a
///     treble roll-off, and for some a linear network around the curve that decides which frequencies reach it hardest.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             <b>Tube</b>: a triode, softer on the positive swing than on the grid-conduction side. <b>Bipolar</b>: a
///             long, soft knee (<c>x / (1 + |x|)</c>). <b>FET 1</b>: a symmetric soft clip (<c>tanh</c>).
///         </item>
///         <item>
///             <b>FET 2</b>: a JFET common-source stage on the square law, <c>I ∝ (1 − V/Vp)²</c>: the positive swing
///             compresses toward pinch-off while the negative one expands until the drain hits its rail, so the second
///             harmonic dominates.
///         </item>
///         <item><b>Transformer</b>: a cubic that saturates hard at ±1, as a core does.</item>
///         <item>
///             <b>Tape 70 / 50 / 35 µs</b>: a tape stage. The record amplifier pre-emphasises the treble with the
///             equalisation's time constant (a shelf <c>(1 + sτ) / (1 + sτ / 10)</c>, corner 2.27, 3.18 or 4.55 kHz),
///             the tape saturates softly, and playback de-emphasises by the exact inverse, so small signals come out flat
///             while loud treble compresses first, as on tape.
///         </item>
///         <item>
///             <b>Vintage</b>: a transformer-coupled triode preamp. A transformer core saturates on flux, the integral
///             of the voltage, so lows saturate first: a low shelf (+12 dB below 80 Hz) before the triode and its
///             inverse after it. Darker (12 kHz).
///         </item>
///         <item>
///             <b>Modern</b>: an op-amp preamp: clean until a sharp knee at its rails
///             (<c>x / (1 + x⁴)^¼</c>), and flat.
///         </item>
///         <item>
///             <b>Exciter</b>: only the band above 3 kHz goes through the curve (a triode's, so Saturation adds even
///             harmonics there); the lows pass clean. Small signals come out unchanged, loud treble gains harmonics.
///         </item>
///     </list>
/// </remarks>
public static class MicPreampTypes {
    public const int None = 0;
    public const int Tube = 1;
    public const int Bipolar = 2;
    public const int Fet1 = 3;
    public const int Fet2 = 4;
    public const int Transformer = 5;
    public const int Tape70 = 6;
    public const int Tape50 = 7;
    public const int Tape35 = 8;
    public const int Vintage = 9;
    public const int Modern = 10;
    public const int Exciter = 11;

    /// <summary>The tape emphasis shelf's ceiling, as a ratio of its two time constants (+20 dB).</summary>
    public const double TapeShelf = 10;

    /// <summary>Vintage's low shelf: gain below <see cref="VintageShelfHz" /> (+12 dB).</summary>
    public const double VintageShelf = 4;

    public const double VintageShelfHz = 80;

    /// <summary>Exciter: the band above this goes through the curve.</summary>
    public const double ExciterHz = 3_000;

    /// <summary>Deviation from linear taken as the onset of clipping: 1 dB.</summary>
    const double OnsetDb = 1;

    static readonly double[] Onsets = [..Enumerable.Range(0, 12).Select(ComputeOnset)];

    /// <summary>Preamp Type choices, in order.</summary>
    public static IReadOnlyList<string> Names { get; } = [
        "None", "Tube", "Bipolar", "FET 1", "FET 2", "Transformer", "Tape 70 µs", "Tape 50 µs", "Tape 35 µs",
        "Vintage", "Modern", "Exciter"
    ];

    /// <summary>The type's treble roll-off (a one-pole low-pass), Hz.</summary>
    public static double ToneHz(int type) =>
        type switch {
            Tube => 14_000,
            Bipolar => 18_000,
            Fet1 or Exciter => 20_000,
            Fet2 or Tape70 or Tape50 or Tape35 => 16_000,
            Transformer => 15_000,
            Vintage => 12_000,
            _ => 22_000
        };

    /// <summary>The tape equalisation's time constant, seconds, or 0 for a type without one.</summary>
    public static double TapeTau(int type) =>
        type switch {
            Tape70 => 70e-6,
            Tape50 => 50e-6,
            Tape35 => 35e-6,
            _ => 0
        };

    /// <summary>
    ///     The level into the curve (after Drive, at no Saturation) where it first departs from linear by 1 dB, either
    ///     way: 0 on the preamp's input VU. 1 (full scale) for <see cref="None" />.
    /// </summary>
    public static double ClipOnset(int type) => (uint)type < (uint)Onsets.Length ? Onsets[type] : 1;

    /// <summary>The type's transfer curve; all are bounded and pass 0 with slope 1.</summary>
    public static double Shape(int type, double x) =>
        type switch {
            Tube or Exciter => x >= 0 ? Math.Tanh(x) : Math.Tanh(1.25 * x) / 1.25,
            Bipolar => x / (1 + Math.Abs(x)),
            Fet1 => Math.Tanh(x),
            Fet2 => x >= 2 ? 1 : x <= -2 ? -3 : x - 0.25 * x * x,
            Transformer => Math.Abs(x) >= 1.5 ? Math.Sign(x) : x - 4 * x * x * x / 27,
            Tape70 or Tape50 or Tape35 => Math.Tanh(0.8 * x) / 0.8,
            Vintage => x >= 0 ? Math.Tanh(x) : Math.Tanh(1.6 * x) / 1.6,
            Modern => x / Math.Sqrt(Math.Sqrt(1 + x * x * x * x)),
            _ => x
        };

    /// <summary>
    ///     The type's emphasis shelf as the analog <c>(b1 s + b0) / (a1 s + a0)</c>, or false when it has none. The
    ///     de-emphasis is its inverse.
    /// </summary>
    public static bool Emphasis(int type, out double b1, out double b0, out double a1, out double a0) {
        var tau = TapeTau(type);
        if (tau > 0) {
            (b1, b0, a1, a0) = (tau, 1, tau / TapeShelf, 1);
            return true;
        }

        if (type == Vintage) {
            var w = 2 * Math.PI * VintageShelfHz;
            (b1, b0, a1, a0) = (1, VintageShelf * w, 1, w);
            return true;
        }

        (b1, b0, a1, a0) = (0, 1, 0, 1);
        return false;
    }

    static double ComputeOnset(int type) {
        if (type == None) {
            return 1;
        }

        var limit = Math.Pow(10, OnsetDb / 20);
        bool Clips(double x) =>
            Math.Abs(Shape(type, x) / x) is var r && (r < 1 / limit || r > limit)
            || Math.Abs(Shape(type, -x) / -x) is var s && (s < 1 / limit || s > limit);

        double low = 1e-3, high = 64;
        for (var i = 0; i < 60; i++) {
            var mid = Math.Sqrt(low * high);
            if (Clips(mid)) {
                high = mid;
            } else {
                low = mid;
            }
        }

        return high;
    }
}
