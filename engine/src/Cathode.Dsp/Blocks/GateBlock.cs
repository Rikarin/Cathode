using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Noise gate as a downward expander. A peak detector across both channels (so the image never shifts) drives a
///     gain computer: below the threshold, gain falls by (ratio - 1) dB per dB. Gain is smoothed in decibels, so the
///     release time means the same at any depth (in the linear domain, reaching -60 dB would take 7 time constants).
///     Gain opens with the attack time, is
///     held for the hold time once the signal drops, then closes with the release time. It starts open, inside a hold
///     window, so the first note is never touched while the detector catches up.
/// </summary>
public sealed class GateBlock : DspBlock {
    public const int Threshold = 0;
    public const int Ratio = 1;
    public const int Attack = 2;
    public const int Hold = 3;
    public const int Release = 4;

    /// <summary>Release of the level detector itself; short, so the gate reacts to the playing, not to its own tail.</summary>
    const double DetectorReleaseMs = 10;

    /// <summary>Deepest attenuation the gate applies, in dB.</summary>
    const float FloorDb = -100f;

    float _envelope;
    float _gainDb;
    int _holdRemaining;
    float _detectorRelease;

    public static BlockDescriptor Info { get; } = new(
        "gate",
        "Gate/Expander",
        "Dynamics",
        [
            ParamDescriptor.Float("threshold", "Threshold", -100f, 0f, -60f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, 20f, 4f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.05f, 50f, 0.5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("hold", "Hold", 0f, 500f, 20f, "ms"),
            ParamDescriptor.Float("release", "Release", 5f, 2_000f, 100f, "ms", ParamTaper.Log)
        ],
        maxInstances: 4
    );

    /// <summary>Current gain applied by the gate (1 = open). For meters and tests.</summary>
    public float CurrentGain => MathF.Pow(10f, _gainDb / 20f);

    public GateBlock()
        : base(Info) { }

    /// <summary>One-pole coefficient reaching about 63 % of a step in <paramref name="milliseconds" />.</summary>
    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    protected override void OnPrepare(in ProcessSpec spec) =>
        _detectorRelease = Coefficient(DetectorReleaseMs, spec.SampleRate);

    protected override void OnReset() {
        _envelope = 0;
        _gainDb = 0;
        _holdRemaining = (int)(Params.Current(Hold) * 0.001 * Spec.SampleRate);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var sampleRate = Spec.SampleRate;
        var thresholdDb = Params.Advance(Threshold, io.Length);
        var threshold = MathF.Pow(10f, thresholdDb / 20f);
        var slope = Params.Advance(Ratio, io.Length) - 1f;
        var attack = Coefficient(Params.Advance(Attack, io.Length), sampleRate);
        var release = Coefficient(Params.Advance(Release, io.Length), sampleRate);
        var holdSamples = (int)(Params.Advance(Hold, io.Length) * 0.001 * sampleRate);

        var envelope = _envelope;
        var gainDb = _gainDb;
        var holdRemaining = _holdRemaining;
        for (var i = 0; i < io.Length; i++) {
            var level = Math.Max(Math.Abs(io.Left[i]), Math.Abs(io.Right[i]));
            envelope = level > envelope ? level : envelope * _detectorRelease;

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
            io.Left[i] *= gain;
            io.Right[i] *= gain;
        }

        _envelope = envelope < 1e-20f ? 0 : envelope;
        _gainDb = gainDb;
        _holdRemaining = holdRemaining;
    }
}
