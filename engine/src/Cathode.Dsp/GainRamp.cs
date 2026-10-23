namespace Cathode.Dsp;

/// <summary>Per-sample linear gain interpolation across a block, so gain changes never click.</summary>
public static class GainRamp {
    /// <summary>
    ///     Multiplies <paramref name="samples" /> by a gain moving linearly from <paramref name="from" /> to
    ///     <paramref name="to" />.
    /// </summary>
    public static void Apply(Span<float> samples, float from, float to) {
        if (from == to) {
            if (from != 1f) {
                for (var i = 0; i < samples.Length; i++) {
                    samples[i] *= from;
                }
            }

            return;
        }

        var step = (to - from) / samples.Length;
        for (var i = 0; i < samples.Length; i++) {
            samples[i] *= from + step * (i + 1);
        }
    }

    /// <summary>
    ///     <c>dry + (wet - dry) * mix</c>, with mix moving linearly from <paramref name="from" /> to <paramref name="to" />
    ///     across the block. <paramref name="wet" />
    ///     receives the result.
    /// </summary>
    public static void Mix(ReadOnlySpan<float> dry, Span<float> wet, float from, float to) {
        var step = (to - from) / wet.Length;
        for (var i = 0; i < wet.Length; i++) {
            var mix = from + step * (i + 1);
            wet[i] = dry[i] + (wet[i] - dry[i]) * mix;
        }
    }
}
