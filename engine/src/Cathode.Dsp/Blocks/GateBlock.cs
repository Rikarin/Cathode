using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Noise gate as a downward expander. A peak detector across both channels (so the image never shifts) drives a
///     gain computer: below the threshold, gain falls by (ratio - 1) dB per dB. Gain is smoothed in decibels, so the
///     release time means the same at any depth (in the linear domain, reaching -60 dB would take 7 time constants).
///     Gain opens with the attack time, is
///     held for the hold time once the signal drops, then closes with the release time. It starts open, inside a hold
///     window, so the first note is never touched while the detector catches up.
///     <para>
///         Types: the classic Expander and Gate, and their Modern versions, which add <see cref="HysteresisDb" /> of
///         hysteresis: once open, they stay open until the level falls that far below the threshold, so a note decaying
///         across the threshold never chatters. <b>Knee</b> (expanders) rounds the corner over 6 or 12 dB around the
///         threshold.
///     </para>
///     Sidechain picks what the detector hears: the block's input, the grid input or another grid row
///     (<see cref="ISidechainBlock" />), so a gate late in the chain can follow the clean guitar.
/// </summary>
public sealed class GateBlock() : DspBlock(Info), ISidechainBlock {
    public const int Threshold = 0;
    public const int Ratio = 1;
    public const int Attack = 2;
    public const int Hold = 3;
    public const int Release = 4;
    public const int Type = 5;
    public const int Attenuation = 6;
    public const int SidechainLowCut = 7;
    public const int SidechainHighCut = 8;
    public const int Detector = 9;
    public const int Level = 10;
    public const int Balance = 11;
    public const int Knee = 12;
    public const int Sidechain = 13;

    public const int Expander = 0;
    public const int Gate = 1;
    public const int ModernExpander = 2;
    public const int ModernGate = 3;
    public const int Peak = 0;
    public const int Rms = 1;

    /// <summary>Release of the level detector itself; short, so the gate reacts to the playing, not to its own tail.</summary>
    const double DetectorReleaseMs = 10;

    /// <summary>Averaging time of the RMS detector.</summary>
    const double RmsMs = 10;

    /// <summary>Deepest attenuation the gate applies, in dB.</summary>
    const float FloorDb = -100f;

    /// <summary>How far below the threshold a Modern type closes, in dB.</summary>
    public const float HysteresisDb = 6f;

    readonly SidechainSource sidechainSource = new();
    float envelope;
    float gainDb;
    int holdRemaining;
    bool open = true;
    float detectorRelease;
    float rmsCoefficient;
    float meanSquare;
    WetCuts sidechain;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "gate",
        "Gate/Expander",
        "Dynamics",
        [
            ParamDescriptor.Float("threshold", "Threshold", -100f, 0f, -60f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, 20f, 4f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.05f, 50f, 0.5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("hold", "Hold", 0f, 500f, 20f, "ms"),
            ParamDescriptor.Float("release", "Release", 5f, 2_000f, 100f, "ms", ParamTaper.Log),
            ParamDescriptor.Choice("type", "Type", ["Expander", "Gate", "Modern Expander", "Modern Gate"]),
            ParamDescriptor.Float("attenuation", "Attenuation", 0f, 100f, 60f, "dB"),
            WetCuts.LowCutParam("scLowCut", "Sidechain Low Cut"),
            WetCuts.HighCutParam("scHighCut", "Sidechain High Cut"),
            ParamDescriptor.Choice("detector", "Detector", ["Peak", "RMS"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("knee", "Knee", ["Hard", "Soft", "Softer"]) with {
                TypeLabels = ["Knee", string.Empty, "Knee", string.Empty]
            },
            SidechainSource.Param()
        ],
        maxInstances: 4
    );

    /// <summary>Current gain applied by the gate (1 = open). For meters and tests.</summary>
    public float CurrentGain => MathF.Pow(10f, gainDb / 20f);

    /// <summary>One-pole coefficient reaching about 63 % of a step in <paramref name="milliseconds" />.</summary>
    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    public void SidechainInput(int row, in StereoBuffer source) =>
        sidechainSource.Offer(row, source, (int)Params.GetTarget(Sidechain));

    protected override void OnPrepare(in ProcessSpec spec) {
        sidechainSource.Prepare(spec.MaxBlockSize);
        detectorRelease = Coefficient(DetectorReleaseMs, spec.SampleRate);
        rmsCoefficient = Coefficient(RmsMs, spec.SampleRate);
    }

    /// <summary>
    ///     The expander's gain in dB for a level <paramref name="under" /> dB from the threshold (negative below it), with
    ///     a soft knee <paramref name="knee" /> dB wide centred on the threshold.
    /// </summary>
    public static float ExpanderGainDb(float under, float slope, float knee) {
        if (knee <= 0 || 2 * under < -knee) {
            return under >= 0 ? 0f : slope * under;
        }

        if (2 * under > knee) {
            return 0f;
        }

        var t = under - knee / 2;
        return -slope * t * t / (2 * knee);
    }

    protected override void OnReset() {
        open = true;
        envelope = 0;
        meanSquare = 0;
        gainDb = 0;
        sidechain.Reset();
        output.Reset(Params.Current(Level), Params.Current(Balance));
        holdRemaining = (int)(Params.Current(Hold) * 0.001 * Spec.SampleRate);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var sampleRate = Spec.SampleRate;
        var thresholdDb = Params.Advance(Threshold, io.Length);
        var threshold = MathF.Pow(10f, thresholdDb / 20f);
        var slope = Params.Advance(Ratio, io.Length) - 1f;
        var attack = Coefficient(Params.Advance(Attack, io.Length), sampleRate);
        var release = Coefficient(Params.Advance(Release, io.Length), sampleRate);
        var holdSamples = (int)(Params.Advance(Hold, io.Length) * 0.001 * sampleRate);
        var type = (int)Params.Current(Type);
        var gate = type is Gate or ModernGate;
        var modern = type is ModernExpander or ModernGate;
        var closeThreshold = modern ? MathF.Pow(10f, (thresholdDb - HysteresisDb) / 20f) : threshold;
        var knee = gate ? 0f : 6f * (int)Params.Current(Knee);
        var closedDb = Math.Max(FloorDb, -Params.Advance(Attenuation, io.Length));
        var rms = (int)Params.Current(Detector) == Rms;
        sidechain.Design(
            sampleRate,
            Params.Advance(SidechainLowCut, io.Length),
            Params.Advance(SidechainHighCut, io.Length)
        );

        var envelope = this.envelope;
        var gainDb = this.gainDb;
        var holdRemaining = this.holdRemaining;
        var open = this.open;
        var external = sidechainSource.Active && sidechainSource.Left.Length == io.Length;
        var sideLeft = sidechainSource.Left;
        var sideRight = sidechainSource.Right;
        for (var i = 0; i < io.Length; i++) {
            var detectLeft = external ? sideLeft[i] : io.Left[i];
            var detectRight = external ? sideRight[i] : io.Right[i];
            sidechain.Tick(ref detectLeft, ref detectRight);
            var level = Math.Max(Math.Abs(detectLeft), Math.Abs(detectRight));
            if (rms) {
                meanSquare = level * level + (meanSquare - level * level) * rmsCoefficient;
                envelope = MathF.Sqrt(meanSquare);
            } else {
                envelope = level > envelope ? level : envelope * detectorRelease;
            }

            float targetDb;
            if (envelope >= threshold || (open && envelope >= closeThreshold)) {
                open = true;
                targetDb = knee > 0 && envelope >= threshold
                    ? ExpanderGainDb(20f * MathF.Log10(envelope) - thresholdDb, slope, knee)
                    : 0f;
                holdRemaining = holdSamples;
            } else if (gate) {
                open = false;
                targetDb = closedDb;
            } else {
                open = false;
                targetDb = envelope <= 0 ? FloorDb
                    : knee > 0 ? Math.Max(
                        FloorDb,
                        ExpanderGainDb(20f * MathF.Log10(envelope) - thresholdDb, slope, knee)
                    )
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

        this.envelope = envelope < 1e-20f ? 0 : envelope;
        meanSquare = meanSquare < 1e-30f ? 0 : meanSquare;
        sidechain.FlushDenormals();
        this.gainDb = gainDb;
        this.holdRemaining = holdRemaining;
        this.open = open;
        sidechainSource.Clear();
        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
