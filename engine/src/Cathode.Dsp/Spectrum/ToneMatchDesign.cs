namespace Cathode.Dsp.Spectrum;

/// <summary>
///     Designs Tone Match correction filters: the ratio of a reference's long-term spectrum to the rig's, smoothed,
///     limited and turned into a minimum-phase FIR for <see cref="Blocks.ToneMatchBlock" />.
/// </summary>
public static class ToneMatchDesign {
    /// <summary>The analysis grid: 4096-point spectra (the telemetry scope window), 11.7 Hz bins at 48 kHz.</summary>
    public const int GridSize = 4_096;

    /// <summary>Below and above these the correction is held flat: a guitar has little to match there.</summary>
    public const double LowHz = 40, HighHz = 16_000;

    /// <summary>
    ///     The correction filter. <paramref name="reference" /> and <paramref name="rig" /> are mean power spectra on
    ///     the <see cref="GridSize" /> grid (<see cref="PowerSpectrum.Mean" />). If the rig was measured through an
    ///     earlier correction, pass it as <paramref name="previous" />: the new filter replaces it rather than stacking.
    ///     The result is level-neutral between 100 Hz and 5 kHz.
    /// </summary>
    public static float[] Design(
        ReadOnlySpan<double> reference,
        ReadOnlySpan<double> rig,
        double sampleRate,
        float[]? previous = null,
        double amountPercent = 100,
        double smoothingOctaves = 1.0 / 3,
        double maxDb = 18,
        int taps = 2_048
    ) {
        var bins = GridSize / 2 + 1;
        if (reference.Length != bins || rig.Length != bins) {
            throw new ArgumentException($"Spectra must have {bins} bins.");
        }

        var binHz = sampleRate / GridSize;
        var floorReference = Max(reference) * 1e-9;
        var floorRig = Max(rig) * 1e-9;
        var db = new double[bins];
        for (var k = 0; k < bins; k++) {
            db[k] = reference[k] > floorReference && rig[k] > floorRig ? 10 * Math.Log10(reference[k] / rig[k]) : 0;
        }

        if (previous is not null) {
            var before = MinimumPhase.MagnitudeDb(previous, GridSize);
            for (var k = 0; k < bins; k++) {
                db[k] += before[k];
            }
        }

        db = FractionalOctave.Smooth(db, smoothingOctaves);
        var low = Math.Max(1, (int)Math.Round(LowHz / binHz));
        var high = Math.Min(bins - 1, (int)Math.Round(HighHz / binHz));
        for (var k = 0; k < low; k++) {
            db[k] = db[low];
        }

        for (var k = high + 1; k < bins; k++) {
            db[k] = db[high];
        }

        // Level-neutral: equal weight per octave (1/f per bin) across the guitar's core range.
        double sum = 0, weight = 0;
        for (var k = (int)(100 / binHz); k <= (int)(5_000 / binHz); k++) {
            sum += db[k] / k;
            weight += 1.0 / k;
        }

        var offset = sum / weight;
        var amount = Math.Clamp(amountPercent, 0, 100) / 100;
        for (var k = 0; k < bins; k++) {
            db[k] = Math.Clamp(db[k] - offset, -maxDb, maxDb) * amount;
        }

        return MinimumPhase.Design(db, taps);
    }

    static double Max(ReadOnlySpan<double> values) {
        var max = 0.0;
        foreach (var v in values) {
            max = Math.Max(max, v);
        }

        return max;
    }
}
