using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The Drive's EQ 2 (its Graphic EQ page) as a frequency response, for the editor's chart
///     (<see cref="EqResponse" />): an impulse at <see cref="EqResponse.SampleRate" /> runs through a
///     <see cref="GraphicEqualizer" /> without networks on <see cref="DriveBlock.Eq2Layout" />, called as the block
///     calls it (Master Q 1, constant Q, no shelves; at the block's own rate in either location). Only EQ 2: neither
///     the Tone page's EQ 1, nor the clipping, Mix or Level. Off reads flat.
/// </summary>
public static class DriveEqResponse {
    /// <summary>EQ 2's band centres, hertz; none while it is off.</summary>
    public static float[] Centers(ParamBank values) =>
        IsOff(values) ? [] : GraphicEqualizer.Layouts[DriveBlock.Eq2Layout].Centers;

    /// <summary>
    ///     The response in dB at each of <paramref name="frequencies" /> (hertz) for a Drive block's
    ///     <paramref name="values" /> (their current values; the targets once the bank is snapped to them).
    /// </summary>
    public static float[] Of(ParamBank values, ReadOnlySpan<float> frequencies) {
        if (IsOff(values)) {
            return EqResponse.Flat(frequencies);
        }

        var left = EqResponse.Impulse();
        var right = EqResponse.Impulse();
        var gains = new float[GraphicEqualizer.MaxBands];
        for (var b = 0; b < gains.Length; b++) {
            gains[b] = values.Current(DriveBlock.Eq2Band + b);
        }

        new GraphicEqualizer().Process(
            new(left, right),
            EqResponse.SampleRate,
            DriveBlock.Eq2Layout,
            gains,
            new bool[GraphicEqualizer.MaxBands],
            1f,
            false,
            false
        );
        return EqResponse.Spectrum(left, EqResponse.SampleRate, frequencies);
    }

    static bool IsOff(ParamBank values) => (int)values.Current(DriveBlock.Eq2Location) == DriveBlock.EqOff;
}
