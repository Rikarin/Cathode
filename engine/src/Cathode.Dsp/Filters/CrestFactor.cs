namespace Cathode.Dsp.Filters;

/// <summary>
///     Program-dependent ("auto") attack and release for a compressor, from the crest factor of the detector's input
///     (after Giannoulis, Massberg and Reiss, JAES 2012, §4.2): a peak and an RMS follower over about
///     <see cref="WindowMs" />, and the time constants scaled by <c>2 / crest²</c>. A sine (crest² = 2) gets the set
///     times, transients (a high crest factor) faster ones and steady, dense signals slower ones, within
///     <see cref="MinScale" /> to <see cref="MaxScale" />.
/// </summary>
public struct CrestFactor {
    public const double WindowMs = 200;
    public const float MinScale = 0.25f;
    public const float MaxScale = 4f;

    double peak;
    double meanSquare;

    /// <summary>The followers' coefficient at <paramref name="sampleRate" />.</summary>
    public static double Coefficient(double sampleRate) => Math.Exp(-1.0 / (WindowMs * 0.001 * sampleRate));

    public void Reset() {
        peak = 0;
        meanSquare = 0;
    }

    /// <summary>Feeds one detector sample (a magnitude) and returns the time scale for this sample.</summary>
    public float Tick(float level, double coefficient) {
        var square = (double)level * level;
        peak = Math.Max(square, coefficient * peak + (1 - coefficient) * square);
        meanSquare = coefficient * meanSquare + (1 - coefficient) * square;
        if (meanSquare < 1e-20) {
            return 1f;
        }

        return Math.Clamp((float)(2 * meanSquare / peak), MinScale, MaxScale);
    }

    /// <summary>Once per block: clears state that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        if (peak < 1e-30) {
            peak = 0;
        }

        if (meanSquare < 1e-30) {
            meanSquare = 0;
        }
    }
}
