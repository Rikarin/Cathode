namespace Cathode.Dsp.Filters;

/// <summary>
///     A stereo bit crusher and sample-rate reducer: each side is quantised to a word length and held for the period of
///     a lower sample rate (sample-and-hold, no anti-alias filter, as the lo-fi effect wants). Either part at its "off"
///     setting passes the signal untouched.
/// </summary>
public struct Decimator {
    /// <summary>The word length of a full-scale signal before any Bit Reduce.</summary>
    public const int FullBits = 24;

    /// <summary>A sample rate at or above this is off.</summary>
    public const float RateOff = 48_000f;

    double phase;
    float holdLeft;
    float holdRight;

    /// <summary>True when <paramref name="bitReduce" /> and <paramref name="rateHz" /> leave the signal alone.</summary>
    public static bool IsOff(int bitReduce, float rateHz) => bitReduce <= 0 && rateHz >= RateOff;

    public void Reset() {
        phase = 1;
        holdLeft = 0;
        holdRight = 0;
    }

    /// <summary>
    ///     Quantises to <see cref="FullBits" /> − <paramref name="bitReduce" /> bits and holds each sample for
    ///     1 / <paramref name="rateHz" /> seconds.
    /// </summary>
    public void Process(in StereoBuffer io, double sampleRate, int bitReduce, float rateHz) {
        var hold = rateHz < RateOff;
        var step = hold ? Math.Min(1.0, rateHz / sampleRate) : 1.0;
        var bits = FullBits - Math.Clamp(bitReduce, 0, FullBits - 1);
        var levels = bitReduce > 0 ? MathF.Pow(2f, bits - 1) : 0f;
        for (var i = 0; i < io.Length; i++) {
            phase += step;
            if (phase >= 1) {
                phase -= Math.Floor(phase);
                holdLeft = Quantise(io.Left[i], levels);
                holdRight = Quantise(io.Right[i], levels);
            }

            io.Left[i] = holdLeft;
            io.Right[i] = holdRight;
        }
    }

    /// <summary><paramref name="x" /> rounded to the nearest of <paramref name="levels" /> steps per unit (0 = untouched).</summary>
    public static float Quantise(float x, float levels) => levels > 0 ? MathF.Round(x * levels) / levels : x;
}
