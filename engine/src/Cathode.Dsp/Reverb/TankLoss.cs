namespace Cathode.Dsp.Reverb;

/// <summary>
///     The decay settings a tank loop needs, worked out once per block: decay time, the Low and High Freq Time
///     multipliers with their one-pole crossover coefficients, and the High Cut damping coefficient.
/// </summary>
readonly record struct DecayShape(
    float Seconds,
    float LowTime,
    float HighTime,
    float LowCoefficient,
    float HighCoefficient,
    float Damp,
    float Rate
) {
    /// <summary>Whether either band multiplier differs from 1, so the loop needs its crossover splits.</summary>
    public bool Bands => LowTime != 1f || HighTime != 1f;

    public static DecayShape Create(
        float seconds,
        float dampingHz,
        float lowHz,
        float lowTime,
        float highHz,
        float highTime,
        float rate
    ) =>
        new(
            seconds,
            lowTime,
            highTime,
            1f - MathF.Exp(-2f * MathF.PI * lowHz / rate),
            1f - MathF.Exp(-2f * MathF.PI * Math.Max(lowHz, highHz) / rate),
            MathF.Exp(-2f * MathF.PI * dampingHz / rate),
            rate
        );
}

/// <summary>
///     The loss of one recirculating path, as the network types do it: a one-pole damping low-pass (High Cut), then a
///     gain from the path's length so the T60 is analytic, and when asked the Low/High Freq Time corrections through
///     one-pole splits, <c>g·v + (gl - g)·low + (gh - g)·high</c>.
/// </summary>
struct TankLoss {
    float gain;
    float lowDelta;
    float highDelta;
    float damping;
    float low;
    float high;

    /// <summary>Once per block: the path is <paramref name="samples" /> long (its share of the loop).</summary>
    public void Configure(float samples, in DecayShape shape) {
        gain = MathF.Pow(10f, -3f * samples / (shape.Seconds * shape.Rate));
        if (shape.Bands) {
            lowDelta = MathF.Pow(10f, -3f * samples / (shape.Seconds * shape.LowTime * shape.Rate)) - gain;
            highDelta = MathF.Pow(10f, -3f * samples / (shape.Seconds * shape.HighTime * shape.Rate)) - gain;
        } else {
            lowDelta = highDelta = 0;
        }
    }

    public float Tick(float y, in DecayShape shape) {
        var v = y + (damping - y) * shape.Damp;
        damping = v;
        if (!shape.Bands) {
            return v * gain;
        }

        low += (v - low) * shape.LowCoefficient;
        high += (v - high) * shape.HighCoefficient;
        return v * gain + lowDelta * low + highDelta * (v - high);
    }

    public void Clear() => damping = low = high = 0;

    public void FlushDenormals() {
        if (MathF.Abs(damping) < 1e-20f) {
            damping = 0;
        }

        if (MathF.Abs(low) < 1e-20f) {
            low = 0;
        }

        if (MathF.Abs(high) < 1e-20f) {
            high = 0;
        }
    }
}
