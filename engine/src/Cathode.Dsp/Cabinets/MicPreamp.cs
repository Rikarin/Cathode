namespace Cathode.Dsp.Cabinets;

/// <summary>
///     The Cab block's mic preamp, one channel: a memoryless waveshaper per <see cref="Types" /> entry driven by Drive,
///     with Saturation biasing it for even harmonics (the DC that leaves is blocked), followed by a fixed one-pole
///     treble roll-off that gives each type its tone. The small-signal gain is unity whatever the Drive, so Drive
///     changes the colour, not the level. Not oversampled: at high Drive it aliases a little, as a mic preamp seldom
///     clips hard.
/// </summary>
public struct MicPreamp {
    /// <summary>Type <c>None</c>: the preamp is bypassed.</summary>
    public const int None = 0;

    public const float MaxDriveDb = 24f;

    const double DcHz = 8;
    const double DenormalThreshold = 1e-30;

    double dc;
    double dcIn;
    double tone;

    /// <summary>Preamp Type choices, in order.</summary>
    public static IReadOnlyList<string> Types { get; } = ["None", "Tube", "Bipolar", "FET", "Transformer", "Tape"];

    public void Reset() {
        dc = 0;
        dcIn = 0;
        tone = 0;
    }

    /// <summary>
    ///     Shapes <paramref name="samples" /> in place, Drive and Saturation moving linearly from
    ///     <paramref name="fromDriveDb" />/<paramref name="fromSaturation" /> (0 … 1) to the new values across the span.
    /// </summary>
    public void Process(
        int type,
        double sampleRate,
        float fromDriveDb,
        float toDriveDb,
        float fromSaturation,
        float toSaturation,
        Span<float> samples
    ) {
        if (type == None || samples.IsEmpty) {
            return;
        }

        var toneHz = type switch {
            1 => 14_000.0,
            2 => 18_000.0,
            3 => 20_000.0,
            4 => 15_000.0,
            _ => 10_000.0
        };
        var g = Math.Tan(Math.PI * Math.Min(toneHz, 0.45 * sampleRate) / sampleRate);
        var toneCoefficient = g / (1 + g);
        var dcCoefficient = 1 - 2 * Math.PI * DcHz / sampleRate;
        var fromDrive = Decibels.ToGain((double)fromDriveDb);
        var toDrive = Decibels.ToGain((double)toDriveDb);
        var step = 1.0 / samples.Length;
        for (var i = 0; i < samples.Length; i++) {
            var t = (i + 1) * step;
            var drive = fromDrive + (toDrive - fromDrive) * t;
            var bias = 0.4 * (fromSaturation + (toSaturation - fromSaturation) * t);
            var shaped = (Shape(type, drive * samples[i] + bias) - Shape(type, bias)) / (drive * Slope(type, bias));

            // DC blocker for what the bias leaves, then the type's treble roll-off (TPT one-pole).
            var blocked = shaped - dcIn + dcCoefficient * dc;
            dcIn = shaped;
            dc = blocked;
            var v = (blocked - tone) * toneCoefficient;
            var low = v + tone;
            tone = low + v;
            samples[i] = (float)low;
        }

        if (Math.Abs(dc) < DenormalThreshold) {
            dc = 0;
        }

        if (Math.Abs(tone) < DenormalThreshold) {
            tone = 0;
        }
    }

    /// <summary>The type's transfer curve; all are bounded and pass 0 with slope 1.</summary>
    static double Shape(int type, double x) =>
        type switch {
            1 => x >= 0 ? Math.Tanh(x) : Math.Tanh(1.25 * x) / 1.25, // tube: softer on one side
            2 => x / (1 + Math.Abs(x)), // bipolar: a long, soft knee
            3 => Math.Tanh(x), // FET
            4 => Math.Abs(x) >= 1.5 ? Math.Sign(x) : x - 4 * x * x * x / 27, // transformer: cubic, hard at ±1
            _ => Math.Tanh(0.8 * x) / 0.8 // tape
        };

    /// <summary>The slope of <see cref="Shape" /> at <paramref name="x" />, by a central difference.</summary>
    static double Slope(int type, double x) {
        const double h = 1e-4;
        return Math.Max(1e-3, (Shape(type, x + h) - Shape(type, x - h)) / (2 * h));
    }
}
