using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The grid's Input cell (docs/08-blocks-parity.md §2): the interface input, then a noise gate and an input level.
///     The graph compiler runs it right after reading the input; it cannot be bypassed.
///     <list type="bullet">
///         <item>Off: no gate (the default, so a plain Input is a wire).</item>
///         <item>
///             Classic: a downward expander on a peak detector linked across both channels. Below the threshold the gain
///             falls by (ratio − 1) dB per dB, opening with the attack time and closing with the release time after a
///             short hold.
///         </item>
///         <item>
///             Intelligent: Classic, with the detector deaf to mains hum: notches at the mains frequency and its second
///             and third harmonics, so a buzzing single-coil does not hold the gate open.
///         </item>
///         <item>
///             Noise Reducer: the same gain computer acts only above a 2 kHz two-pole split, so hiss fades with the note
///             while
///             the body of the sound is never cut.
///         </item>
///     </list>
///     The rig's global noise gate offset (<see cref="BlockContext.GateOffsetDb" />) is added to the threshold.
///     Ahead of the gate, <b>Input Impedance</b> loads a pickup model (<see cref="PickupLoad" />): its resonance as the
///     chosen impedance, the cable and the Pickup Buffer shape it, relative to the high-impedance input the DI came
///     through. With the pickup Off (the default) or at Auto without the buffer the signal is untouched.
/// </summary>
public sealed class InputBlock() : DspBlock(Info) {
    public const int GateType = 0;
    public const int Threshold = 1;
    public const int Ratio = 2;
    public const int Attack = 3;
    public const int Release = 4;
    public const int Mains = 5;
    public const int Level = 6;
    public const int Impedance = 7;
    public const int PickupType = 8;
    public const int PickupInductance = 9;
    public const int PickupResistance = 10;
    public const int PickupCapacitance = 11;
    public const int CableCapacitance = 12;
    public const int PickupBuffer = 13;

    /// <summary>Values of the Pickup choice after Off; the last one takes the custom L, R and C.</summary>
    public const int PickupCustom = 4;

    public const int Off = 0;
    public const int Classic = 1;
    public const int Intelligent = 2;
    public const int NoiseReducer = 3;

    /// <summary>Where the Noise Reducer splits off the band it gates.</summary>
    public const float NoiseSplitHz = 2_000f;

    const double DetectorReleaseMs = 10;
    const double HoldMs = 15;
    const float FloorDb = -100f;
    const int HumNotches = 3;

    readonly SvfState[] hum = new SvfState[HumNotches];
    readonly SvfCoefficients[] humCoefficients = new SvfCoefficients[HumNotches];
    int designedMains = -1;
    float envelope;
    float gainDb;
    int holdRemaining;
    float detectorRelease;
    float split;
    float lowLeft;
    float lowRight;
    float lowLeft2;
    float lowRight2;
    float level = 1f;
    PickupFilter pickupLeft;
    PickupFilter pickupRight;
    (int Impedance, int Type, float L, float R, float C, float Cable, bool Buffer) pickupDesigned = (-1, 0, 0, 0, 0, 0, false);
    (double B0, double B1, double B2, double A1, double A2) pickupCoefficients;
    bool pickupActive;

    public static BlockDescriptor Info { get; } = new(
        "input",
        "Input",
        "I/O",
        [
            ParamDescriptor.Choice("type", "Gate Type", ["Off", "Classic", "Intelligent", "Noise Reducer"]),
            ParamDescriptor.Float("threshold", "Threshold", -100f, 0f, -60f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, 20f, 2f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 50f, 1f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 5f, 2_000f, 100f, "ms", ParamTaper.Log),
            ParamDescriptor.Choice("mains", "Mains Frequency", ["50 Hz", "60 Hz"]),
            ParamDescriptor.Float("level", "Input Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Choice("impedance", "Input Impedance", [..PickupLoad.LoadNames]),
            ParamDescriptor.Choice("pickup", "Pickup", ["Off", "Single Coil", "Humbucker", "P90", "Custom"]),
            ParamDescriptor.Float("pickupL", "Pickup Inductance", 0.5f, 15f, 2.5f, "H", ParamTaper.Log),
            ParamDescriptor.Float("pickupR", "Pickup Resistance", 1f, 25f, 6f, "kΩ", ParamTaper.Log),
            ParamDescriptor.Float("pickupC", "Pickup Capacitance", 10f, 500f, 100f, "pF", ParamTaper.Log),
            ParamDescriptor.Float("cable", "Cable Capacitance", 0f, 2_000f, 500f, "pF"),
            ParamDescriptor.Switch("buffer", "Pickup Buffer")
        ],
        maxInstances: 4
    );

    /// <summary>Whether the pickup load filter is running (it is skipped while it would be flat).</summary>
    public bool PickupActive => pickupActive;

    /// <summary>The pickup the Pickup choice <paramref name="type" /> names (custom values for Custom), or null for Off.</summary>
    public static Pickup? PickupOf(int type, float henries, float kiloohms, float picofarads) =>
        type switch {
            1 => Pickup.SingleCoil,
            2 => Pickup.Humbucker,
            3 => Pickup.P90,
            PickupCustom => new(henries, kiloohms * 1e3, picofarads * 1e-12),
            _ => null
        };

    /// <summary>The gate's current gain (1 = open). For meters and tests.</summary>
    public float CurrentGain => MathF.Pow(10f, gainDb / 20f);

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    void Gate(in StereoBuffer io, int type, float thresholdDb, float slope, float attack, float release) {
        var threshold = MathF.Pow(10f, thresholdDb / 20f);
        var holdSamples = (int)(HoldMs * 0.001 * Spec.SampleRate);
        var intelligent = type == Intelligent;
        if (intelligent) {
            DesignHumNotches((int)Params.Current(Mains));
        }

        var envelope = this.envelope;
        var gainDb = this.gainDb;
        var holdRemaining = this.holdRemaining;
        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var detect = Math.Max(Math.Abs(left), Math.Abs(right));
            if (intelligent) {
                double x = 0.5f * (left + right);
                for (var h = 0; h < HumNotches; h++) {
                    x = hum[h].Tick(humCoefficients[h], x);
                }

                detect = (float)Math.Abs(x);
            }

            envelope = detect > envelope ? detect : envelope * detectorRelease;
            float targetDb;
            if (envelope >= threshold) {
                targetDb = 0f;
                holdRemaining = holdSamples;
            } else {
                targetDb = envelope <= 0
                    ? FloorDb
                    : Math.Max(FloorDb, slope * (20f * MathF.Log10(envelope) - thresholdDb));
            }

            if (targetDb > gainDb) {
                gainDb = targetDb + (gainDb - targetDb) * attack;
            } else if (holdRemaining > 0) {
                holdRemaining--;
            } else {
                gainDb = targetDb + (gainDb - targetDb) * release;
            }

            var gain = gainDb >= -1e-4f ? 1f : MathF.Pow(10f, gainDb / 20f);
            if (type == NoiseReducer) {
                // Two one-poles: the highs are what is left, so the bands always sum back to the input.
                lowLeft += split * (left - lowLeft);
                lowRight += split * (right - lowRight);
                lowLeft2 += split * (lowLeft - lowLeft2);
                lowRight2 += split * (lowRight - lowRight2);
                io.Left[i] = lowLeft2 + (left - lowLeft2) * gain;
                io.Right[i] = lowRight2 + (right - lowRight2) * gain;
            } else {
                io.Left[i] = left * gain;
                io.Right[i] = right * gain;
            }
        }

        this.envelope = envelope < 1e-20f ? 0 : envelope;
        this.gainDb = gainDb;
        this.holdRemaining = holdRemaining;
        lowLeft = MathF.Abs(lowLeft) < 1e-20f ? 0 : lowLeft;
        lowRight = MathF.Abs(lowRight) < 1e-20f ? 0 : lowRight;
        lowLeft2 = MathF.Abs(lowLeft2) < 1e-20f ? 0 : lowLeft2;
        lowRight2 = MathF.Abs(lowRight2) < 1e-20f ? 0 : lowRight2;
        foreach (ref var state in hum.AsSpan()) {
            state.FlushDenormals();
        }
    }

    /// <summary>Redesigns the pickup load when its settings change (they are not smoothed: a choice and a few values).</summary>
    void DesignPickup() {
        var settings = (
            Impedance: (int)Params.Current(Impedance),
            Type: (int)Params.Current(PickupType),
            L: Params.Current(PickupInductance),
            R: Params.Current(PickupResistance),
            C: Params.Current(PickupCapacitance),
            Cable: Params.Current(CableCapacitance),
            Buffer: Params.Current(PickupBuffer) >= 0.5f
        );
        if (settings == pickupDesigned) {
            return;
        }

        pickupDesigned = settings;
        var pickup = PickupOf(settings.Type, settings.L, settings.R, settings.C);
        var load = PickupLoad.Loads[Math.Clamp(settings.Impedance, 0, PickupLoad.Loads.Count - 1)];
        var active = pickup is not null && (settings.Buffer || load != PickupLoad.ReferenceLoad);
        if (active && !pickupActive) {
            pickupLeft = default;
            pickupRight = default;
        }

        pickupActive = active;
        if (active) {
            pickupCoefficients = PickupLoad.Design(
                pickup!.Value,
                load,
                settings.Cable * 1e-12,
                settings.Buffer,
                Spec.SampleRate
            );
        }
    }

    /// <summary>Notches at the mains frequency (50 or 60 Hz) and its second and third harmonics.</summary>
    void DesignHumNotches(int mains) {
        if (mains == designedMains) {
            return;
        }

        designedMains = mains;
        var fundamental = mains == 1 ? 60.0 : 50.0;
        for (var h = 0; h < HumNotches; h++) {
            humCoefficients[h] = SvfCoefficients.Design(BiquadType.Notch, Spec.SampleRate, fundamental * (h + 1), 2.0);
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        detectorRelease = Coefficient(DetectorReleaseMs, spec.SampleRate);
        split = 1f - MathF.Exp(-2f * MathF.PI * NoiseSplitHz / (float)spec.SampleRate);
        designedMains = -1;
    }

    protected override void OnReset() {
        foreach (ref var state in hum.AsSpan()) {
            state.Reset();
        }

        envelope = 0;
        gainDb = 0;
        holdRemaining = (int)(HoldMs * 0.001 * Spec.SampleRate);
        lowLeft = 0;
        lowRight = 0;
        lowLeft2 = 0;
        lowRight2 = 0;
        level = Decibels.ToGain(Params.Current(Level));
        pickupLeft = default;
        pickupRight = default;
        pickupDesigned = (-1, 0, 0, 0, 0, 0, false);
        pickupActive = false;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        DesignPickup();
        if (pickupActive) {
            pickupLeft.Process(pickupCoefficients, io.Left);
            pickupRight.Process(pickupCoefficients, io.Right);
        }

        var type = (int)Params.Current(GateType);
        var thresholdDb = Params.Advance(Threshold, io.Length) + context.GateOffsetDb;
        var slope = Params.Advance(Ratio, io.Length) - 1f;
        var attack = Coefficient(Params.Advance(Attack, io.Length), Spec.SampleRate);
        var release = Coefficient(Params.Advance(Release, io.Length), Spec.SampleRate);
        if (type != Off) {
            Gate(io, type, thresholdDb, slope, attack, release);
        }

        var target = Decibels.ToGain(Params.Advance(Level, io.Length));
        if (target != 1f || level != 1f) {
            GainRamp.Apply(io.Left, level, target);
            GainRamp.Apply(io.Right, level, target);
        }

        level = target;
    }

    /// <summary>A transposed direct-form II biquad in double precision (its coefficients only change on edits).</summary>
    struct PickupFilter {
        double s1;
        double s2;

        public void Process(in (double B0, double B1, double B2, double A1, double A2) c, Span<float> samples) {
            for (var i = 0; i < samples.Length; i++) {
                double x = samples[i];
                var y = c.B0 * x + s1;
                s1 = c.B1 * x - c.A1 * y + s2;
                s2 = c.B2 * x - c.A2 * y;
                samples[i] = (float)y;
            }

            if (Math.Abs(s1) < 1e-25) {
                s1 = 0;
            }

            if (Math.Abs(s2) < 1e-25) {
                s2 = 0;
            }
        }
    }
}
