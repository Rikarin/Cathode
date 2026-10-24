namespace Cathode.Dsp.Pitch;

/// <summary>
///     Estimates the key of a melody from the pitch classes it dwells on: the Krumhansl-Schmuckler algorithm
///     (Krumhansl, "Cognitive Foundations of Musical Pitch", 1990) on a decaying duration histogram, with hysteresis so
///     the key changes only when another one fits clearly better.
/// </summary>
/// <remarks>
///     Each <see cref="Add" /> weights one pitch class by how long it sounded; the histogram forgets with a time
///     constant of <see cref="MemorySeconds" /> of playing (it holds still through rests), so a modulation takes over
///     within a few phrases. Every call correlates the histogram (Pearson) with the 24 Krumhansl-Kessler probe-tone
///     profiles, 12 major and 12 minor keys. The first estimate needs <see cref="MinSeconds" /> of notes; after that
///     the key switches only when another one correlates better by <see cref="Hysteresis" />. No allocation, 288
///     multiply-adds per call.
/// </remarks>
public sealed class KeyEstimator {
    public const double MemorySeconds = 8;
    public const double MinSeconds = 0.75;
    public const double Hysteresis = 0.08;

    static readonly double[] MajorProfile =
        Normalise([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]);

    static readonly double[] MinorProfile =
        Normalise([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]);

    readonly double[] histogram = new double[12];
    readonly double[] centred = new double[12];

    /// <summary>The estimated tonic's pitch class (0 = C), or -1 before the first estimate.</summary>
    public int Key { get; private set; } = -1;

    /// <summary>Whether the estimated key is minor.</summary>
    public bool Minor { get; private set; }

    public void Reset() {
        Array.Clear(histogram);
        Key = -1;
        Minor = false;
    }

    /// <summary>Adds <paramref name="seconds" /> of <paramref name="pitchClass" /> and updates the estimate.</summary>
    public void Add(int pitchClass, double seconds) {
        var decay = Math.Exp(-seconds / MemorySeconds);
        var total = 0.0;
        for (var i = 0; i < 12; i++) {
            histogram[i] *= decay;
            total += histogram[i];
        }

        histogram[(pitchClass % 12 + 12) % 12] += seconds;
        total += seconds;
        if (Key < 0 && total < MinSeconds) {
            return;
        }

        var mean = total / 12;
        var energy = 0.0;
        for (var i = 0; i < 12; i++) {
            centred[i] = histogram[i] - mean;
            energy += centred[i] * centred[i];
        }

        if (energy <= 0) {
            return;
        }

        var (bestKey, bestMinor, best, current) = (-1, false, double.MinValue, double.MinValue);
        for (var mode = 0; mode < 2; mode++) {
            var profile = mode == 0 ? MajorProfile : MinorProfile;
            for (var key = 0; key < 12; key++) {
                var fit = 0.0;
                for (var i = 0; i < 12; i++) {
                    fit += centred[(i + key) % 12] * profile[i];
                }

                if (fit > best) {
                    (bestKey, bestMinor, best) = (key, mode == 1, fit);
                }

                if (key == Key && mode == 1 == Minor) {
                    current = fit;
                }
            }
        }

        // Both fits are scaled by the same histogram norm, so compare them as correlations.
        var norm = Math.Sqrt(energy);
        if (Key < 0 || (best - current) / norm > Hysteresis) {
            (Key, Minor) = (bestKey, bestMinor);
        }
    }

    /// <summary>The profile centred on zero with unit length, so a dot product with it is a correlation.</summary>
    static double[] Normalise(double[] profile) {
        var mean = profile.Average();
        var norm = Math.Sqrt(profile.Sum(p => (p - mean) * (p - mean)));
        return [..profile.Select(p => (p - mean) / norm)];
    }
}
