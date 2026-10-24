namespace Cathode.Dsp.Modulation;

/// <summary>The waveforms every modulation block offers (docs/08-blocks-parity.md, "LFO waveforms").</summary>
public enum LfoShape {
    Sine,

    /// <summary>The duty cycle sets how much of the period rises: 0 % is a falling saw, 100 % a rising one.</summary>
    Triangle,

    /// <summary>
    ///     A square with softened edges (tanh of a sine), so gain and delay modulation never click. Duty sets its width.
    /// </summary>
    Square,
    SawUp,
    SawDown,

    /// <summary>Sample and hold: a new level each cycle. The phase offset has no effect on it.</summary>
    Random,

    /// <summary>Rounded tops and sharp troughs.</summary>
    Log,

    /// <summary>Sharp peaks and rounded troughs.</summary>
    Exponential,

    /// <summary>A triangle clipped at half its height, so it dwells at both extremes. Duty skews it.</summary>
    Trapezoid,

    /// <summary>
    ///     An astable multivibrator's RC charge and discharge. Duty sets the curvature (the "beta"): low is near a
    ///     triangle.
    /// </summary>
    Astable
}

/// <summary>Low-frequency oscillator with a phase in [0, 1). Allocation-free; one instance drives both channels.</summary>
public struct Lfo {
    /// <summary>Display names for <see cref="LfoShape" />, in order, for a block's LFO type parameter.</summary>
    public static readonly string[] ShapeNames =
        ["Sine", "Triangle", "Square", "Saw Up", "Saw Down", "Random", "Log", "Exponential", "Trapezoid", "Astable"];

    long cycle;

    /// <summary>The phase in cycles, [0, 1).</summary>
    public double Phase { get; private set; }

    public void Reset(double phase = 0) {
        Phase = phase - Math.Floor(phase);
        cycle = 0;
    }

    /// <summary>Advances by one sample at <paramref name="rateHz" />.</summary>
    public void Advance(double rateHz, double sampleRate) {
        Phase += rateHz / sampleRate;
        if (Phase >= 1) {
            cycle += (long)Math.Floor(Phase);
            Phase -= Math.Floor(Phase);
        }
    }

    /// <summary>
    ///     Bipolar value in [-1, 1] at the current phase plus <paramref name="offset" /> (in cycles, e.g. 0.25 = 90°).
    ///     <paramref name="duty" /> (0..1) is the symmetry of the triangle, square and trapezoid, and the curvature of the
    ///     astable; the other shapes ignore it. At the default 0.5 every shape is symmetric.
    /// </summary>
    public readonly float Value(LfoShape shape, double offset = 0, double duty = 0.5) {
        if (shape == LfoShape.Random) {
            return Hold(cycle);
        }

        var p = Phase + offset;
        return (float)Shape(shape, p - Math.Floor(p), duty);
    }

    /// <summary>
    ///     The waveform at phase <paramref name="p" /> in [0, 1), bipolar. <see cref="LfoShape.Random" /> needs the
    ///     oscillator's cycle count, so here it is a sine.
    /// </summary>
    public static double Shape(LfoShape shape, double p, double duty = 0.5) {
        var d = Math.Clamp(duty, 0.0, 1.0);
        return shape switch {
            LfoShape.Triangle => Triangle(p, d),
            LfoShape.Square => Math.Tanh(4 * Math.Sin(2 * Math.PI * Skew(p, d))) / Math.Tanh(4),
            LfoShape.SawUp => 2 * p - 1,
            LfoShape.SawDown => 1 - 2 * p,
            LfoShape.Log => 2 * Math.Sin(Math.PI * p) - 1,
            LfoShape.Exponential => 1 - 2 * Math.Sin(Math.PI * p),
            LfoShape.Trapezoid => Math.Clamp(2 * Triangle(p, d), -1, 1),
            LfoShape.Astable => Astable(p, d),
            _ => Math.Sin(2 * Math.PI * p)
        };
    }

    /// <summary>Rises over the first <paramref name="duty" /> of the cycle and falls over the rest.</summary>
    static double Triangle(double p, double duty) {
        if (duty <= 0) {
            return 1 - 2 * p;
        }

        if (duty >= 1) {
            return 2 * p - 1;
        }

        return p < duty ? 2 * p / duty - 1 : 1 - 2 * (p - duty) / (1 - duty);
    }

    /// <summary>Maps the phase so the first half of a symmetric waveform takes <paramref name="duty" /> of the cycle.</summary>
    static double Skew(double p, double duty) {
        var d = Math.Clamp(duty, 0.02, 0.98);
        return p < d ? 0.5 * p / d : 0.5 + 0.5 * (p - d) / (1 - d);
    }

    /// <summary>Exponential charge over the first half and discharge over the second, normalised to [-1, 1].</summary>
    static double Astable(double p, double beta) {
        var k = 0.5 + 7.5 * beta; // the RC time constant against the half period
        var full = 1 - Math.Exp(-k);
        var level = p < 0.5 ? (1 - Math.Exp(-2 * k * p)) / full : (Math.Exp(-k * (2 * p - 1)) - Math.Exp(-k)) / full;
        return 2 * level - 1;
    }

    /// <summary>A repeatable pseudo-random level in [-1, 1] for each cycle, so a reset replays the same sequence.</summary>
    static float Hold(long cycle) {
        var x = (ulong)cycle * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (float)((x >> 40) / (double)(1UL << 24) * 2 - 1);
    }
}
