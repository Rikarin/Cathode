namespace Cathode.Dsp.Filters;

/// <summary>
///     Coefficients for a four-pole transistor-ladder low-pass (the Moog topology): four identical one-pole low-passes
///     in series with the last one's output fed back, inverted, to the input. The stages are trapezoidal (TPT/ZDF)
///     integrators and the feedback loop is solved without a unit delay, after Zavalishin's "The Art of VA Filter
///     Design", so the cut-off and the resonance stay where they are set up to the top of the audio band and the filter
///     can be swept quickly.
/// </summary>
/// <remarks>
///     <para>
///         <b>Resonance</b> is given as a Q like the other filters: 0.7071 is no feedback (four real poles, −12 dB at
///         the cut-off) and the feedback rises towards the edge of self-oscillation (a loop gain of 4) as Q grows. The
///         input is scaled by one plus the feedback, so the pass band stays at unity instead of dropping as the
///         resonance rises.
///     </para>
///     <para>
///         The signal entering the first stage is soft-clipped (tanh) at <see cref="Headroom" />, which is what bounds
///         a ringing ladder. <b>Drive</b> is gain into that clipper, taken off again at the output, so quiet signals
///         keep their level and loud ones compress and gain odd harmonics.
///     </para>
/// </remarks>
public readonly record struct LadderCoefficients(double G, double Feedback, double Solve, double In, double Out) {
    /// <summary>
    ///     Level at which the ladder's input stage saturates: +12 dBFS, so 0 dB of drive is close to clean.
    /// </summary>
    public const double Headroom = 4;

    /// <summary>
    ///     The highest feedback, as a share of the loop gain of 4 at which the ladder oscillates on its own.
    /// </summary>
    public const double MaxResonance = 0.98;

    public static LadderCoefficients Design(double sampleRate, double frequency, double q, double driveDb = 0) {
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        var g = Math.Tan(Math.PI * f0 / sampleRate);
        var stage = g / (1 + g);
        var feedback = 4 * Math.Clamp(1 - Math.Sqrt(0.5) / Math.Max(q, 1e-3), 0, MaxResonance);
        var drive = Math.Pow(10.0, driveDb / 20.0);
        var loop = stage * stage * stage * stage;
        return new(stage, feedback, 1 / (1 + feedback * loop), drive * (1 + feedback), 1 / drive);
    }
}

/// <summary>State of one <see cref="LadderCoefficients" /> filter channel (four trapezoidal integrators).</summary>
public struct LadderState {
    double s1;
    double s2;
    double s3;
    double s4;

    public void Reset() => this = default;

    /// <summary>
    ///     One sample. <paramref name="twoPole" /> takes the output after the second stage (12 dB/oct) instead of the
    ///     fourth (24 dB/oct); the resonance loop is the same.
    /// </summary>
    public double Tick(in LadderCoefficients c, double x, bool twoPole) {
        var g = c.G;

        // What the fourth stage puts out with nothing coming in: each state reaches it through the stages after it.
        var states = (1 - g) * (g * (g * (g * s1 + s2) + s3) + s4);
        var u = (c.In * x - c.Feedback * states) * c.Solve;
        u = LadderCoefficients.Headroom * Math.Tanh(u / LadderCoefficients.Headroom);

        var v = (u - s1) * g;
        var y1 = v + s1;
        s1 = y1 + v;
        v = (y1 - s2) * g;
        var y2 = v + s2;
        s2 = y2 + v;
        v = (y2 - s3) * g;
        var y3 = v + s3;
        s3 = y3 + v;
        v = (y3 - s4) * g;
        var y4 = v + s4;
        s4 = y4 + v;

        return (twoPole ? y2 : y4) * c.Out;
    }

    public void FlushDenormals() {
        const double tiny = 1e-30;
        if (Math.Abs(s1) < tiny) {
            s1 = 0;
        }

        if (Math.Abs(s2) < tiny) {
            s2 = 0;
        }

        if (Math.Abs(s3) < tiny) {
            s3 = 0;
        }

        if (Math.Abs(s4) < tiny) {
            s4 = 0;
        }
    }
}
