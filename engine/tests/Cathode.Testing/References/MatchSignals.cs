namespace Cathode.Testing.References;

/// <summary>
///     Deterministic stimuli for matching, in volts at the guitar jack: a guitar-like performance (Karplus–Strong plucks
///     and chords at three dynamics), steady sines for the harmonic profile, and a quiet sweep for the small-signal
///     frequency response.
/// </summary>
public static class MatchSignals {
    /// <summary>Peak volts of the small-signal sweep.</summary>
    public const double SweepVolts = 0.01;

    /// <summary>Silence at the start of every stimulus, so both models settle and latency can be measured.</summary>
    public const double LeadInSeconds = 0.1;

    const double HarmonicSeconds = 0.4;

    /// <summary>The bin count of the window <see cref="HarmonicTone" /> is analysed over.</summary>
    public const int HarmonicWindow = 8_192;

    /// <summary>Peak volts of the three dynamics in <see cref="Performance" />: soft single-coil to hard humbucker.</summary>
    public static readonly double[] PerformancePeaks = [0.05, 0.2, 0.6];

    /// <summary>Peak volts of the sines in the harmonic profile.</summary>
    public static readonly double[] HarmonicLevels = [0.03, 0.1, 0.3, 1.0];

    /// <summary>Notes (Hz) and chords of one phrase.</summary>
    static readonly double[][] Phrase = [
        [82.41],
        [110.0],
        [146.83, 196.0, 246.94],
        [329.63],
        [82.41, 123.47, 164.81],
        [196.0]
    ];

    /// <summary>
    ///     A phrase of plucks and chords at each of <paramref name="peaks" /> (volts), <paramref name="noteSeconds" />
    ///     per event.
    /// </summary>
    public static float[] Performance(double sampleRate, IReadOnlyList<double>? peaks = null, double noteSeconds = 0.45) {
        peaks ??= PerformancePeaks;
        var lead = (int)(LeadInSeconds * sampleRate);
        var note = (int)(noteSeconds * sampleRate);
        var signal = new float[lead + peaks.Count * Phrase.Length * note];
        var seed = 1u;
        var offset = lead;
        foreach (var peak in peaks) {
            var phrase = new float[Phrase.Length * note];
            for (var e = 0; e < Phrase.Length; e++) {
                for (var s = 0; s < Phrase[e].Length; s++) {
                    // A strum: strings 12 ms apart, slightly softer towards the top.
                    var start = e * note + (int)(s * 0.012 * sampleRate);
                    Pluck(phrase.AsSpan(start, note - (start - e * note)), Phrase[e][s], sampleRate, ref seed, 1 - 0.1 * s);
                }
            }

            var scale = (float)(peak / Math.Max(Analysis.Peak(phrase), 1e-9));
            for (var i = 0; i < phrase.Length; i++) {
                signal[offset + i] = phrase[i] * scale;
            }

            offset += phrase.Length;
        }

        return signal;
    }

    /// <summary>
    ///     A steady sine near 200 Hz at <paramref name="peakVolts" />, whose last <see cref="HarmonicWindow" /> samples
    ///     hold a whole number (<paramref name="cycles" />) of periods.
    /// </summary>
    public static float[] HarmonicTone(double sampleRate, double peakVolts, out int cycles) {
        cycles = (int)Math.Round(200.0 * HarmonicWindow / sampleRate);
        var length = (int)(HarmonicSeconds * sampleRate) + HarmonicWindow;
        var signal = new float[length];
        var fade = (int)(0.02 * sampleRate);
        for (var i = 0; i < length; i++) {
            var ramp = Math.Min(1.0, (double)i / fade);
            signal[i] = (float)(peakVolts * ramp * Math.Sin(2 * Math.PI * cycles * i / HarmonicWindow));
        }

        return signal;
    }

    /// <summary>A 2 s exponential sweep, 20 Hz to 20 kHz, at <see cref="SweepVolts" />, after a short lead-in.</summary>
    public static float[] Sweep(double sampleRate) =>
        Signals.Concat(
            new float[(int)(LeadInSeconds * sampleRate)],
            Signals.ExponentialSweep(20, Math.Min(20_000, 0.45 * sampleRate), sampleRate, 2, (float)SweepVolts),
            new float[(int)(0.2 * sampleRate)]
        );

    /// <summary>Karplus–Strong string, added into <paramref name="target" />.</summary>
    static void Pluck(Span<float> target, double frequency, double sampleRate, ref uint seed, double velocity) {
        var period = Math.Max(2, (int)Math.Round(sampleRate / frequency));
        var line = new double[period];
        double previous = 0;
        for (var i = 0; i < period; i++) {
            seed = seed * 1_664_525u + 1_013_904_223u;
            var noise = (seed >> 8) / (double)(1 << 24) * 2 - 1;
            previous = 0.5 * previous + 0.5 * noise; // a pick, not a needle: soften the excitation
            line[i] = previous * velocity;
        }

        var position = 0;
        for (var i = 0; i < target.Length; i++) {
            var next = (position + 1) % period;
            var sample = line[position];
            line[position] = 0.996 * 0.5 * (line[position] + line[next]);
            target[i] += (float)sample;
            position = next;
        }
    }
}
