using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The Graphic EQ block's frequency response, for the editor's chart (<see cref="EqResponse" />): an impulse at
///     <see cref="EqResponse.SampleRate" /> runs through a <see cref="GraphicEqualizer" /> set up as the block sets its
///     own (the passive layouts on their component-level networks, whose bands interact), and the left channel is
///     read. Level is included; Balance is not, as it places the sound rather than shaping it.
/// </summary>
public static class GraphicEqResponse {
    /// <summary>The band layout the Type of <paramref name="values" /> picks.</summary>
    public static GraphicEqLayout Layout(ParamBank values) {
        var layouts = GraphicEqualizer.Layouts;
        return layouts[Math.Clamp((int)values.Current(GraphicEqBlock.Type), 0, layouts.Count - 1)];
    }

    /// <summary>The centres of the bands of <paramref name="values" />' layout, hertz.</summary>
    public static float[] Centers(ParamBank values) => Layout(values).Centers;

    /// <summary>
    ///     The response in dB at each of <paramref name="frequencies" /> (hertz) for a Graphic EQ block's
    ///     <paramref name="values" /> (their current values; the targets once the bank is snapped to them).
    /// </summary>
    public static float[] Of(ParamBank values, ReadOnlySpan<float> frequencies) {
        var left = EqResponse.Impulse();
        var right = EqResponse.Impulse();
        var gains = new float[GraphicEqBlock.Bands];
        for (var b = 0; b < gains.Length; b++) {
            gains[b] = values.Current(b);
        }

        var equalizer = new GraphicEqualizer(true);
        equalizer.Prepare(EqResponse.SampleRate);
        equalizer.Process(
            new(left, right),
            EqResponse.SampleRate,
            (int)values.Current(GraphicEqBlock.Type),
            gains,
            new bool[GraphicEqBlock.Bands],
            values.Current(GraphicEqBlock.MasterQ),
            (int)values.Current(GraphicEqBlock.QMode) == 1,
            values.Current(GraphicEqBlock.Shelves) >= 0.5f
        );

        var level = Decibels.ToGain(values.Current(GraphicEqBlock.Level));
        return EqResponse.Spectrum(left, EqResponse.SampleRate, frequencies, level);
    }
}
