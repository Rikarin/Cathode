namespace Cathode.Dsp;

/// <summary>
///     The Envelope page of the delays: a volume swell on what enters the lines. Once the input's peak crosses the
///     threshold, the gain rises from silence to unity over the attack along an S-curve, like a hand on a volume knob; once
///     the input falls 6 dB below the threshold, the next note is armed and the gain falls back over the release.
/// </summary>
public struct Swell {
    const float RearmRatio = 0.5f;
    const double DetectorReleaseMs = 50;

    float envelope;
    float level;
    bool open;
    float threshold;
    float attackStep;
    float release;
    float detectorRelease;

    /// <summary>False with an attack of zero: <see cref="Tick" /> then always returns 1.</summary>
    public bool Active { get; private set; }

    public void Reset() {
        envelope = 0;
        level = 0;
        open = false;
    }

    /// <summary>Once per block. An <paramref name="attackMs" /> of zero turns the swell off.</summary>
    public void Configure(double sampleRate, float thresholdDb, float attackMs, float releaseMs) {
        Active = attackMs > 0;
        threshold = Decibels.ToGain(thresholdDb);
        attackStep = (float)(1 / Math.Max(1, attackMs * 0.001 * sampleRate));
        release = (float)Math.Exp(-1 / Math.Max(1, releaseMs * 0.001 * sampleRate));
        detectorRelease = (float)Math.Exp(-1 / (DetectorReleaseMs * 0.001 * sampleRate));
    }

    /// <summary>The gain for an input sample whose peak over both sides is <paramref name="peak" />.</summary>
    public float Tick(float peak) {
        if (!Active) {
            return 1f;
        }

        envelope = peak > envelope ? peak : envelope * detectorRelease;
        if (!open && envelope > threshold) {
            open = true;
            level = 0;
        } else if (open && envelope < threshold * RearmRatio) {
            open = false;
        }

        level = open ? Math.Min(1f, level + attackStep) : level * release;
        return level * level * (3 - 2 * level);
    }

    /// <summary>Once per block, after the ticks.</summary>
    public void FlushDenormals() {
        if (envelope < 1e-20f) {
            envelope = 0;
        }

        if (level < 1e-20f) {
            level = 0;
        }
    }
}
