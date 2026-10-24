namespace Cathode.Dsp.Cabinets;

/// <summary>
///     The Cab block's mic preamp, one channel: a waveshaper per <see cref="Types" /> entry
///     (<see cref="MicPreampTypes" />) driven by Drive, with Saturation biasing it for even harmonics (the DC that leaves
///     is blocked), followed by a fixed one-pole treble roll-off that gives each type its tone. The tape and Vintage
///     types put an emphasis shelf before the curve and its exact inverse after it; the Exciter sends only its high band
///     through the curve. The small-signal gain is unity whatever the Drive, so Drive changes the colour, not the level.
///     Not oversampled: at high Drive it aliases a little, as a mic preamp seldom clips hard.
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
    double preIn;
    double preOut;
    double deIn;
    double deOut;
    double split;

    /// <summary>Preamp Type choices, in order.</summary>
    public static IReadOnlyList<string> Types => MicPreampTypes.Names;

    /// <summary>
    ///     The peak level into the curve over the last <see cref="Process" />, relative to the type's onset of clipping
    ///     (<see cref="MicPreampTypes.ClipOnset" />): 1 is the onset. 0 while the preamp is off.
    /// </summary>
    public float InputPeak { get; private set; }

    public void Reset() {
        dc = 0;
        dcIn = 0;
        tone = 0;
        preIn = 0;
        preOut = 0;
        deIn = 0;
        deOut = 0;
        split = 0;
        InputPeak = 0;
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
            InputPeak = 0;
            return;
        }

        var toneHz = MicPreampTypes.ToneHz(type);
        var g = Math.Tan(Math.PI * Math.Min(toneHz, 0.45 * sampleRate) / sampleRate);

        // The emphasis shelf by the bilinear transform; the de-emphasis swaps numerator and denominator, so it is the
        // exact inverse.
        var emphasis = MicPreampTypes.Emphasis(type, out var b1, out var b0, out var a1, out var a0);
        var c = 2 * sampleRate;
        var n0 = b1 * c + b0;
        var n1 = b0 - b1 * c;
        var d0 = a1 * c + a0;
        var d1 = a0 - a1 * c;
        var exciter = type == MicPreampTypes.Exciter;
        var gs = Math.Tan(Math.PI * Math.Min(MicPreampTypes.ExciterHz, 0.45 * sampleRate) / sampleRate);
        var splitCoefficient = gs / (1 + gs);
        var peak = 0.0;
        var toneCoefficient = g / (1 + g);
        var dcCoefficient = 1 - 2 * Math.PI * DcHz / sampleRate;
        var fromDrive = Decibels.ToGain((double)fromDriveDb);
        var toDrive = Decibels.ToGain((double)toDriveDb);
        var step = 1.0 / samples.Length;
        for (var i = 0; i < samples.Length; i++) {
            var t = (i + 1) * step;
            var drive = fromDrive + (toDrive - fromDrive) * t;
            var bias = 0.4 * (fromSaturation + (toSaturation - fromSaturation) * t);
            double x = samples[i];
            if (emphasis) {
                var pre = (n0 * x + n1 * preIn - d1 * preOut) / d0;
                preIn = x;
                preOut = pre;
                x = pre;
            }

            // The Exciter shapes only its high band and passes the low band (TPT one-pole) clean.
            var low = 0.0;
            if (exciter) {
                var sv = (x - split) * splitCoefficient;
                low = sv + split;
                split = low + sv;
                x -= low;
            }

            peak = Math.Max(peak, Math.Abs(drive * x));
            var shaped = low + (Shape(type, drive * x + bias) - Shape(type, bias)) / (drive * Slope(type, bias));
            if (emphasis) {
                var de = (d0 * shaped + d1 * deIn - n1 * deOut) / n0;
                deIn = shaped;
                deOut = de;
                shaped = de;
            }

            // DC blocker for what the bias leaves, then the type's treble roll-off (TPT one-pole).
            var blocked = shaped - dcIn + dcCoefficient * dc;
            dcIn = shaped;
            dc = blocked;
            var v = (blocked - tone) * toneCoefficient;
            var toned = v + tone;
            tone = toned + v;
            samples[i] = (float)toned;
        }

        InputPeak = (float)(peak / MicPreampTypes.ClipOnset(type));

        if (Math.Abs(dc) < DenormalThreshold) {
            dc = 0;
        }

        if (Math.Abs(tone) < DenormalThreshold) {
            tone = 0;
        }

        if (Math.Abs(preOut) < DenormalThreshold && Math.Abs(deOut) < DenormalThreshold) {
            preOut = 0;
            deOut = 0;
        }

        if (Math.Abs(split) < DenormalThreshold) {
            split = 0;
        }
    }

    static double Shape(int type, double x) => MicPreampTypes.Shape(type, x);

    /// <summary>The slope of <see cref="Shape" /> at <paramref name="x" />, by a central difference.</summary>
    static double Slope(int type, double x) {
        const double h = 1e-4;
        return Math.Max(1e-3, (Shape(type, x + h) - Shape(type, x - h)) / (2 * h));
    }
}
