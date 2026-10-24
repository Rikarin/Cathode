namespace Cathode.Dsp;

/// <summary>
///     The ducker of the delay and reverb blocks: while the dry input's peak is above the threshold, the wet signal
///     drops by the attenuation (in 5 ms), and it swells back over the release time once the playing stops.
/// </summary>
public struct Ducker {
    const double AttackMs = 5;

    float envelope;
    float gain;
    float attack;
    float release;
    float floor;
    float threshold;

    /// <summary>False when the attenuation is zero: <see cref="Tick" /> then always returns 1.</summary>
    public bool Active { get; private set; }

    public void Reset() {
        envelope = 0;
        gain = 1f;
    }

    /// <summary>Once per block.</summary>
    public void Configure(double sampleRate, float attenuationDb, float thresholdDb, float releaseMs) {
        Active = attenuationDb > 0;
        floor = Decibels.ToGain(-attenuationDb);
        threshold = Decibels.ToGain(thresholdDb);
        attack = OnePole(AttackMs, sampleRate);
        release = OnePole(releaseMs, sampleRate);
        if (!Active) {
            gain = 1f;
        }
    }

    /// <summary>The wet gain for an input sample whose peak (over both sides) is <paramref name="peak" />.</summary>
    public float Tick(float peak) {
        if (!Active) {
            return 1f;
        }

        envelope = peak > envelope ? peak : envelope * release;
        var target = envelope > threshold ? floor : 1f;
        gain = target + (gain - target) * (target < gain ? attack : release);
        return gain;
    }

    /// <summary>Once per block, after the ticks.</summary>
    public void FlushDenormals() {
        if (envelope < 1e-20f) {
            envelope = 0;
        }
    }

    static float OnePole(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));
}
