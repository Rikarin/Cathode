using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Ten-Tap delay: up to ten taps on one line fed by the mono sum, with a feedback that re-enters at Time (so the
///     pattern repeats), feedback-path low and high cut, a constant-power mix and Level.
/// </summary>
/// <remarks>
///     <para>
///         Ten Tap: the taps evenly spaced across Time, with Shuffle (swing on the off-beat taps), a Decay taper and a
///         level per tap. Rhythm Tap: each tap at its own percentage of Time, optionally quantised to a tempo division,
///         or learned from the playing: with Learn on Listen, the first note after a silence of Time starts a pattern
///         and each further note within Time places the next tap there (up to ten); Hold keeps the last pattern
///         learned. A learned pattern lives in the block (the tap time parameters are not rewritten), and Off returns
///         to the parameters.
///     </para>
///     <para>
///         Stereo places the taps by Pan Shape (alternating, left to right, right to left or centre), curved by Pan
///         Alpha and scaled by Spread; the right side's taps sit at Ratio times the left's. Mono puts every tap in the
///         centre at the same time on both sides.
///     </para>
/// </remarks>
public sealed class TenTapBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Time = 1;
    public const int Tempo = 2;
    public const int Feedback = 3;
    public const int Taps = 4;
    public const int Mode = 5;
    public const int Decay = 6;
    public const int Shuffle = 7;
    public const int Spread = 8;
    public const int Ratio = 9;
    public const int PanShape = 10;
    public const int PanAlpha = 11;
    public const int LowCut = 12;
    public const int HighCut = 13;
    public const int Tap1Level = 14;
    public const int Tap1Time = 24;
    public const int Quantize = 34;
    public const int Learn = 35;
    public const int Mix = 36;
    public const int Level = 37;
    public const int Balance = 38;
    public const int InputGain = 39;
    public const int InputSelect = 40;

    public const int TenTap = 0;
    public const int RhythmTap = 1;
    public const int TypeCount = 2;

    public const int Mono = 0;
    public const int StereoMode = 1;

    public const int Alternate = 0;
    public const int LeftToRight = 1;
    public const int RightToLeft = 2;
    public const int Center = 3;

    public const int LearnOff = 0;
    public const int Listen = 1;
    public const int LearnHold = 2;

    public const int MaxTaps = 10;
    public const float MaxTimeMs = 4_000f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    /// <summary>The Learn onset level in dBFS (re-armed 6 dB below).</summary>
    public const float LearnThresholdDb = -40f;

    readonly FractionalDelayLine line = new();
    readonly float[] tapDelay = new float[MaxTaps];
    readonly float[] tapTarget = new float[MaxTaps];
    readonly float[] tapStep = new float[MaxTaps];
    readonly float[] tapLeft = new float[MaxTaps];
    readonly float[] tapRight = new float[MaxTaps];
    readonly float[] learned = new float[MaxTaps];
    int learnedCount;
    long learnStart = -1;
    long sampleClock;
    float onsetEnvelope;
    bool onsetArmed = true;
    float delaySamples;
    float mix;
    LoopFilterState loopState;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "tentap",
        "Ten-Tap Delay",
        "Delay",
        [
            ParamDescriptor.Choice("type", "Type", ["Ten Tap", "Rhythm Tap"]),
            new(
                "time",
                "Time",
                ParamKind.Float,
                10f,
                MaxTimeMs,
                1_000f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 100f, 0f, "%"),
            new("taps", "Number of Taps", ParamKind.Float, 1f, MaxTaps, MaxTaps, Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Choice("mode", "Mono/Stereo", ["Mono", "Stereo"], StereoMode),
            ParamDescriptor.Float("decay", "Decay", -100f, 100f, 50f, "%"),
            ParamDescriptor.Float("shuffle", "Shuffle", 0f, 100f, 0f, "%").ShownOn(TypeCount, TenTap),
            ParamDescriptor.Float("spread", "Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("ratio", "Ratio", 50f, 100f, 100f, "%"),
            ParamDescriptor.Choice("panShape", "Pan Shape", ["Alternate", "Left to Right", "Right to Left", "Center"]),
            ParamDescriptor.Float("panAlpha", "Pan Alpha", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ..Enumerable.Range(0, MaxTaps)
                .Select(static k => ParamDescriptor.Float($"tap{k + 1}Level", $"Tap {k + 1} Level", 0f, 100f, 100f, "%")
                ),
            ..Enumerable.Range(0, MaxTaps)
                .Select(static k => ParamDescriptor.Float(
                    $"tap{k + 1}Time",
                    $"Tap {k + 1} Time",
                    0f,
                    100f,
                    10f * (k + 1),
                    "%"
                    )
                    .ShownOn(TypeCount, RhythmTap)
                ),
            ParamDescriptor.Choice("quantize", "Quantize", TempoDivisions.Names).ShownOn(TypeCount, RhythmTap),
            ParamDescriptor.Choice("learn", "Learn", ["Off", "Listen", "Hold"]).ShownOn(TypeCount, RhythmTap),
            BlockParams.Mix(25f),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions)
        ],
        maxInstances: 2,
        pages: [
            new("Basic", ["time", "tempo", "feedback", "taps", "decay", "shuffle", "ratio", "lowCut", "highCut"]),
            new(
                "Taps",
                [
                    "tap1Time", "tap2Time", "tap3Time", "tap4Time", "tap5Time", "tap6Time", "tap7Time", "tap8Time",
                    "tap9Time", "tap10Time", "tap1Level", "tap2Level", "tap3Level", "tap4Level", "tap5Level",
                    "tap6Level", "tap7Level", "tap8Level", "tap9Level", "tap10Level", "quantize", "learn"
                ]
            ),
            new("Advanced", ["mode", "spread", "panShape", "panAlpha", "inputGain", "inputSelect"])
        ]
    );

    /// <summary>The fractions of Time the learned pattern's taps sit at, in order (for tests and display).</summary>
    public ReadOnlySpan<float> LearnedPattern => learned.AsSpan(0, learnedCount);

    /// <summary>Gains of the balance pan law: unity on both sides at centre, the far side fading to zero.</summary>
    static (float Left, float Right) Pan(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    float TargetDelaySamples(float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(Tempo), tempo);
        var ms = division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    /// <summary>Learn: a note after a silence of Time starts a pattern; later notes within Time add taps.</summary>
    void LearnOnset(float peak, float threshold, float release, float time) {
        onsetEnvelope = peak > onsetEnvelope ? peak : onsetEnvelope * release;
        if (onsetArmed && onsetEnvelope > threshold) {
            onsetArmed = false;
            var elapsed = learnStart < 0 ? double.MaxValue : sampleClock - learnStart;
            if (elapsed >= time) {
                learnStart = sampleClock;
                learnedCount = 0;
            } else if (learnedCount < MaxTaps && elapsed > 0) {
                learned[learnedCount++] = (float)(elapsed / time);
            }
        } else if (!onsetArmed && onsetEnvelope < 0.5f * threshold) {
            onsetArmed = true;
        }
    }

    void ComputeTapTargets(float time, double tempo) {
        if ((int)Params.Current(Type) == TenTap) {
            // Shuffle delays the off-beat taps (1, 3, 5, ... counting the dry note as beat 0) by up to half a step.
            // The pattern ends at Time, where the feedback re-enters: with an odd tap count the last tap is an off-beat
            // and stays on Time rather than swinging past it (and, at the longest Time, past the line).
            var taps = Math.Clamp((int)MathF.Round(Params.Current(Taps)), 1, MaxTaps);
            var swing = Params.Current(Shuffle) / 100f * 0.5f;
            for (var k = 0; k < MaxTaps; k++) {
                tapTarget[k] = Math.Clamp(
                    time * (k + 1 + (k % 2 == 0 ? swing : 0f)) / taps,
                    FractionalDelayLine.MinDelay,
                    time
                );
            }

            return;
        }

        // Rhythm Tap: the learned pattern while Learn is on, otherwise the tap times, quantised if asked.
        var useLearned = (int)Params.Current(Learn) != LearnOff && learnedCount > 0;
        var grid = TempoDivisions.Milliseconds((int)Params.Current(Quantize), tempo) * 0.001 * Spec.SampleRate;
        for (var k = 0; k < MaxTaps; k++) {
            var position = !useLearned ? Params.Current(Tap1Time + k) / 100f : k < learnedCount ? learned[k] : 1f;
            var samples = time * position;
            if (grid > 0 && !useLearned) {
                samples = (float)(Math.Round(samples / grid) * grid);
            }

            tapTarget[k] = Math.Clamp(samples, FractionalDelayLine.MinDelay, time);
        }
    }

    void ComputeTapGains(int taps, bool stereo) {
        // Positive decay fades the taps out across the pattern, negative fades them in.
        var decay = Params.Current(Decay) / 100f;
        var spread = Params.Current(Spread) / 100f;
        var shape = (int)Params.Current(PanShape);
        var power = MathF.Pow(2f, 2f * Params.Current(PanAlpha) / 100f);
        var learnedTaps = (int)Params.Current(Type) == RhythmTap
            && (int)Params.Current(Learn) != LearnOff
            && learnedCount > 0;
        for (var k = 0; k < MaxTaps; k++) {
            var u = taps > 1 ? k / (taps - 1f) : 0f;
            var level = decay >= 0 ? 1f - decay * u : 1f + decay * (1f - u);
            level *= Params.Current(Tap1Level + k) / 100f;
            if (learnedTaps && k >= learnedCount) {
                level = 0f;
            }

            var position = shape switch {
                Alternate => k % 2 == 0 ? -1f : 1f,
                LeftToRight => 2f * u - 1f,
                RightToLeft => 1f - 2f * u,
                _ => 0f
            };
            if (power != 1f) {
                position = MathF.CopySign(MathF.Pow(MathF.Abs(position), power), position);
            }

            var (left, right) = stereo ? Pan(spread * position) : (1f, 1f);
            tapLeft[k] = level * left;
            tapRight[k] = level * right;
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) =>
        line.Allocate((int)(spec.SampleRate * MaxTimeMs / 1000) + 8);

    protected override void OnReset() {
        line.Reset();
        loopState.Reset();
        learnedCount = 0;
        learnStart = -1;
        sampleClock = 0;
        onsetEnvelope = 0;
        onsetArmed = true;
        delaySamples = TargetDelaySamples(Params.Current(Time), 120);
        ComputeTapTargets(delaySamples, 120);
        Array.Copy(tapTarget, tapDelay, MaxTaps);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            line.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var sampleRate = (float)Spec.SampleRate;
        var targetDelay = TargetDelaySamples(Params.Advance(Time, n), context.Tempo);
        var feedback = Params.Advance(Feedback, n) / 100f;
        var mix = Params.Advance(Mix, n) / 100f;
        for (var p = Decay; p <= PanAlpha; p++) {
            Params.Advance(p, n);
        }

        for (var p = Tap1Level; p < Quantize; p++) {
            Params.Advance(p, n);
        }

        var ratio = Params.Current(Ratio) / 100f;
        var stereo = (int)Params.Current(Mode) == StereoMode;
        var rightRatio = stereo ? ratio : 1f;
        var taps = Math.Clamp((int)MathF.Round(Params.Current(Taps)), 1, MaxTaps);
        ComputeTapTargets(targetDelay, context.Tempo);
        ComputeTapGains(taps, stereo);
        var loop = LoopFilter.Design(
            sampleRate,
            Params.Advance(LowCut, n),
            CutFilter.Slope12,
            LoopFilter.ButterworthQ,
            Params.Advance(HighCut, n),
            CutFilter.Slope12,
            LoopFilter.ButterworthQ
        );

        var listening = (int)Params.Current(Type) == RhythmTap && (int)Params.Current(Learn) == Listen;
        var onsetThreshold = Decibels.ToGain(LearnThresholdDb);
        var onsetRelease = MathF.Exp(-1f / (0.05f * sampleRate));

        var delayStep = (targetDelay - delaySamples) / n;
        for (var k = 0; k < MaxTaps; k++) {
            tapStep[k] = (tapTarget[k] - tapDelay[k]) / n;
        }

        var inputGain = Params.Advance(InputGain, n) / 100f;
        var select = (int)Params.Current(InputSelect);
        var gains = new MixRamp(MixLaw.ConstantPower, this.mix, mix, n);
        var delay = delaySamples;
        for (var i = 0; i < n; i++) {
            delay += delayStep;
            gains.Advance();
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var x = Stereo.Select(select, dryLeft, dryRight) * inputGain;
            if (listening) {
                LearnOnset(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)), onsetThreshold, onsetRelease, delay);
            }

            float wetLeft = 0, wetRight = 0;
            for (var k = 0; k < taps; k++) {
                tapDelay[k] += tapStep[k];
                var tap = line.Read(tapDelay[k]);
                wetLeft += tap * tapLeft[k];
                wetRight += (rightRatio == 1f ? tap : line.Read(tapDelay[k] * rightRatio)) * tapRight[k];
            }

            for (var k = taps; k < MaxTaps; k++) {
                tapDelay[k] += tapStep[k];
            }

            var fed = x + feedback * line.Read(delay);
            line.Write(DelayBlock.Limit(loop.Active ? loopState.Tick(loop, fed) : fed));
            sampleClock++;

            io.Left[i] = dryLeft * gains.Dry + wetLeft * gains.Wet;
            io.Right[i] = dryRight * gains.Dry + wetRight * gains.Wet;
        }

        delaySamples = targetDelay;
        Array.Copy(tapTarget, tapDelay, MaxTaps);
        this.mix = mix;
        loopState.FlushDenormals();
        if (onsetEnvelope < 1e-20f) {
            onsetEnvelope = 0;
        }

        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
