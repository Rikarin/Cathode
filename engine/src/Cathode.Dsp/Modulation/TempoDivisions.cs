using System.Collections.Immutable;

namespace Cathode.Dsp.Modulation;

/// <summary>
///     The rhythmic values every Tempo parameter offers (docs/08-blocks-parity.md, "Tempo cross-reference"). Index 0 is
///     "Off" (the block's own time or rate applies). The first ten keep the order they always had, so stored presets
///     keep their meaning; the rest were appended.
/// </summary>
public static class TempoDivisions {
    public static readonly ImmutableArray<string> Names = [
        "Off", "1/1", "1/2", "1/4", "1/4.", "1/4T", "1/8", "1/8.", "1/8T", "1/16",
        "1/2.", "1/2T", "1/16.", "1/16T", "1/32", "1/32.", "1/32T", "1/64", "1/64T", "2/1", "3/1", "4/1"
    ];

    /// <summary>Length of each <see cref="Names" /> entry in quarter notes (index 0 unused).</summary>
    public static ReadOnlySpan<double> Beats => [
        0, 4, 2, 1, 1.5, 2.0 / 3, 0.5, 0.75, 1.0 / 3, 0.25,
        3, 4.0 / 3, 0.375, 1.0 / 6, 0.125, 0.1875, 1.0 / 12, 0.0625, 1.0 / 24, 8, 12, 16
    ];

    /// <summary>
    ///     Division <paramref name="index" /> in milliseconds at <paramref name="bpm" />, or 0 for "Off".
    /// </summary>
    public static double Milliseconds(int index, double bpm) =>
        index > 0 && index < Beats.Length && bpm > 0 ? 60_000.0 / bpm * Beats[index] : 0;

    /// <summary>
    ///     The rate in Hz that repeats once per division at <paramref name="bpm" />, or <paramref name="freeRate" />
    ///     when the division is "Off".
    /// </summary>
    public static double Rate(int index, double bpm, double freeRate) =>
        index > 0 && index < Beats.Length && bpm > 0 ? bpm / 60.0 / Beats[index] : freeRate;
}
