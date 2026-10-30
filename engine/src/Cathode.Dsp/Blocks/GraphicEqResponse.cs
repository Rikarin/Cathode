using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The Graphic EQ block's frequency response, for the editor's chart. It is measured rather than derived: an
///     impulse runs through a <see cref="GraphicEqualizer" /> set up as the block sets its own (the passive layouts on
///     their component-level networks, whose bands interact), and the left channel's spectrum is read at the asked
///     frequencies. Level is included; Balance is not, as it places the sound rather than shaping it.
/// </summary>
public static class GraphicEqResponse {
    /// <summary>The rate the response is measured at.</summary>
    public const double SampleRate = 48_000;

    /// <summary>
    ///     The impulse response's length: 0.68 s, long enough for the narrowest 31 Hz band (Master Q 4, ±12 dB) to
    ///     ring down by about 100 dB.
    /// </summary>
    public const int Length = 32_768;

    /// <summary>The floor a silent frequency reads, dB.</summary>
    public const float FloorDb = -200f;

    /// <summary>
    ///     <paramref name="count" /> frequencies spaced evenly on a log scale from <paramref name="low" /> to
    ///     <paramref name="high" /> hertz, both ends included.
    /// </summary>
    public static float[] Frequencies(int count = 256, float low = 20f, float high = 20_000f) {
        var ratio = Math.Log(high / low);
        return [..Enumerable.Range(0, count).Select(i => (float)(low * Math.Exp(ratio * i / Math.Max(1, count - 1))))];
    }

    /// <summary>The band layout the Type of <paramref name="values" /> picks.</summary>
    public static GraphicEqLayout Layout(ParamBank values) {
        var layouts = GraphicEqualizer.Layouts;
        return layouts[Math.Clamp((int)values.Current(GraphicEqBlock.Type), 0, layouts.Count - 1)];
    }

    /// <summary>
    ///     The response in dB at each of <paramref name="frequencies" /> (hertz) for a Graphic EQ block's
    ///     <paramref name="values" /> (their current values; the targets once the bank is snapped to them).
    /// </summary>
    public static float[] Of(ParamBank values, ReadOnlySpan<float> frequencies) {
        var left = new float[Length];
        var right = new float[Length];
        left[0] = right[0] = 1f;

        var gains = new float[GraphicEqBlock.Bands];
        for (var b = 0; b < gains.Length; b++) {
            gains[b] = values.Current(b);
        }

        var equalizer = new GraphicEqualizer(true);
        equalizer.Prepare(SampleRate);
        equalizer.Process(
            new(left, right),
            SampleRate,
            (int)values.Current(GraphicEqBlock.Type),
            gains,
            new bool[GraphicEqBlock.Bands],
            values.Current(GraphicEqBlock.MasterQ),
            (int)values.Current(GraphicEqBlock.QMode) == 1,
            values.Current(GraphicEqBlock.Shelves) >= 0.5f
        );

        var level = Decibels.ToGain(values.Current(GraphicEqBlock.Level));
        var end = Length; // the DFT stops where the tail has died away
        while (end > 1 && MathF.Abs(left[end - 1]) < 1e-9f) {
            end--;
        }

        var db = new float[frequencies.Length];
        for (var f = 0; f < frequencies.Length; f++) {
            var w = 2 * Math.PI * frequencies[f] / SampleRate;
            var (stepRe, stepIm) = (Math.Cos(w), -Math.Sin(w));
            double re = 0, im = 0, zRe = 1, zIm = 0;
            for (var n = 0; n < end; n++) {
                re += left[n] * zRe;
                im += left[n] * zIm;
                (zRe, zIm) = (zRe * stepRe - zIm * stepIm, zRe * stepIm + zIm * stepRe);
            }

            var magnitude = Math.Sqrt(re * re + im * im) * level;
            db[f] = magnitude > 0 ? Math.Max(FloorDb, (float)(20 * Math.Log10(magnitude))) : FloorDb;
        }

        return db;
    }
}
