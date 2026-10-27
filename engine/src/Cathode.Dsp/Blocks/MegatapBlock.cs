using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Megatap: up to 128 taps on one line fed by the mono sum, spread across Time (tempo-syncable) after a pre-delay.
///     Time Alpha bends the tap spacing and Time Randomize jitters it; Level Shape, Amplitude Alpha and Randomize set
///     the tap amplitudes; Pan Shape, Pan Alpha and Spread place them in the stereo field. One tap can feed back into the
///     line. Low and high cut filter what enters the line (so every repeat through the feedback is darker), an envelope
///     can swell the input into the taps, a diffuser smears the wet, and the Axe-Fx mix law and Level are shared with the
///     other delays.
/// </summary>
/// <remarks>
///     <para>
///         Tap k of N (k = 1..N) sits at <c>Time · f(k / N)</c>, where <c>f(x) = (e^(a·x) - 1) / (e^a - 1)</c> with
///         <c>a = 4 · alpha</c>: zero alpha spaces the taps evenly, positive alpha starts them close together and
///         spreads them out (a slowing bounce), negative alpha does the reverse. The last tap always lands on Time.
///         Time Randomize moves each tap by up to half a tap spacing either way, by a fixed pseudo-random amount per tap,
///         so the pattern is repeatable.
///     </para>
///     <para>
///         Amplitude Alpha raises each shape's amplitudes to the power <c>2^(2·alpha)</c> (a steeper or gentler fade;
///         for Exponential, a deeper or shallower floor), Amplitude Randomize scales each tap down by up to its amount,
///         and Pan Alpha curves the pan positions the same way. The tap amplitudes are then normalised to unit total
///         power, so the wet level of a noise-like input does not change with the tap count.
///     </para>
/// </remarks>
public sealed class MegatapBlock() : DspBlock(Info) {
    public const int Time = 0;
    public const int Tempo = 1;
    public const int Taps = 2;
    public const int TimeAlpha = 3;
    public const int LevelShape = 4;
    public const int PanShape = 5;
    public const int Spread = 6;
    public const int LowCut = 7;
    public const int HighCut = 8;
    public const int Mix = 9;
    public const int Level = 10;
    public const int Balance = 11;
    public const int InputGain = 12;
    public const int InputSelect = 13;
    public const int PreDelay = 14;
    public const int Feedback = 15;
    public const int FeedbackTap = 16;
    public const int TimeRandom = 17;
    public const int LevelAlpha = 18;
    public const int LevelRandom = 19;
    public const int PanAlpha = 20;
    public const int DiffusionMix = 21;
    public const int DiffusionTime = 22;
    public const int DiffusionRate = 23;
    public const int DiffusionDepth = 24;
    public const int EnvelopeThreshold = 25;
    public const int EnvelopeAttack = 26;
    public const int EnvelopeRelease = 27;
    public const int GlobalMix = 28;

    public const int Flat = 0;
    public const int FadeOut = 1;
    public const int FadeIn = 2;
    public const int Swell = 3;
    public const int Exponential = 4;

    public const int Center = 0;
    public const int Alternate = 1;
    public const int LeftToRight = 2;
    public const int RightToLeft = 3;

    public const float MaxTimeMs = 4_000f;
    public const float MaxPreDelayMs = 1_000f;
    public const float MaxDiffusionMs = 100f;
    public const int MaxTaps = 128;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    /// <summary>Level of the last tap of the Exponential shape, in dB.</summary>
    public const float ExponentialFloorDb = -40f;

    readonly FractionalDelayLine line = new();
    readonly Diffuser diffuser = new();
    readonly float[] tapDelay = new float[MaxTaps];
    readonly float[] tapTarget = new float[MaxTaps];
    readonly float[] tapStep = new float[MaxTaps];
    readonly float[] tapLeft = new float[MaxTaps];
    readonly float[] tapRight = new float[MaxTaps];
    SvfState lowCut;
    SvfState highCut;
    SwellEnvelope envelope;
    int activeTaps;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "megatap",
        "Megatap Delay",
        "Delay",
        [
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
            new("taps", "Taps", ParamKind.Float, 1f, MaxTaps, 16f, Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("timeAlpha", "Time Alpha", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice(
                "levelShape",
                "Level Shape",
                ["Flat", "Fade Out", "Fade In", "Swell", "Exponential"],
                1
            ),
            ParamDescriptor.Choice(
                "panShape",
                "Pan Shape",
                ["Center", "Alternate", "Left to Right", "Right to Left"],
                1
            ),
            ParamDescriptor.Float("spread", "Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            ParamDescriptor.Float("preDelay", "Pre-Delay", 0f, MaxPreDelayMs, 0f, "ms"),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 100f, 0f, "%"),
            new("feedbackTap", "Feedback Tap", ParamKind.Float, 1f, MaxTaps, MaxTaps, Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("timeRandom", "Time Randomize", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("levelAlpha", "Amplitude Alpha", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("levelRandom", "Amplitude Randomize", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("panAlpha", "Pan Alpha", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("diffusionMix", "Diffusion Mix", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("diffusionTime", "Diffusion Time", 1f, MaxDiffusionMs, 30f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("diffusionRate", "Diffusion LFO Rate", 0.05f, 5f, 0.4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("diffusionDepth", "Diffusion LFO Depth", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("envelopeThreshold", "Envelope Threshold", -80f, 0f, -40f, "dB"),
            ParamDescriptor.Float("envelopeAttack", "Envelope Attack", 0f, 2_000f, 0f, "ms"),
            ParamDescriptor.Float("envelopeRelease", "Envelope Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            EffectsMix.Param
        ],
        maxInstances: 2,
        channels: 2,
        pages: [
            new(
                "Basic",
                [
                    "time", "tempo", "taps", "preDelay", "feedback", "feedbackTap", "spread", "lowCut", "highCut"
                ]
            ),
            new(
                "Shape",
                [
                    "timeAlpha", "timeRandom", "levelShape", "levelAlpha", "levelRandom", "panShape", "panAlpha"
                ]
            ),
            new("Diffusion", ["diffusionMix", "diffusionTime", "diffusionRate", "diffusionDepth"]),
            new(
                "Advanced",
                [
                    "envelopeThreshold", "envelopeAttack", "envelopeRelease", "inputGain", "inputSelect", "globalMix"
                ]
            )
        ]
    );

    /// <summary>The fraction of Time at which tap <paramref name="k" /> (0-based) of <paramref name="taps" /> sits.</summary>
    public static float TapPosition(int k, int taps, float alpha) {
        var x = (k + 1f) / taps;
        var a = 4f * alpha;
        return MathF.Abs(a) < 1e-4f ? x : (MathF.Exp(a * x) - 1f) / (MathF.Exp(a) - 1f);
    }

    /// <summary>The un-normalised amplitude of tap <paramref name="k" /> (0-based) of <paramref name="taps" />.</summary>
    public static float TapAmplitude(int k, int taps, int shape) {
        var u = taps > 1 ? k / (taps - 1f) : 0f;
        return shape switch {
            FadeOut => (taps - k) / (float)taps,
            FadeIn => (k + 1f) / taps,
            Swell => MathF.Sin(MathF.PI * (k + 1f) / (taps + 1f)),
            Exponential => MathF.Pow(10f, ExponentialFloorDb * u / 20f),
            _ => 1f
        };
    }

    /// <summary>A repeatable pseudo-random value in [0, 1) for tap <paramref name="k" /> and stream <paramref name="salt" />.</summary>
    public static float Jitter(int k, uint salt) {
        var x = ((uint)k * 0x9E3779B9u) ^ (salt * 0x85EBCA6Bu);
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return (x >> 8) / (float)(1 << 24);
    }

    /// <summary>The exponent an alpha of −1..1 applies: 1/4 to 4, 1 at zero.</summary>
    static float AlphaPower(float alpha) => MathF.Pow(2f, 2f * alpha);

    float TargetDelaySamples(float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(Tempo), tempo);
        var ms = division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    int TapCount() => Math.Clamp((int)MathF.Round(Params.Current(Taps)), 1, MaxTaps);

    float PreDelaySamples() => Params.Current(PreDelay) * 0.001f * (float)Spec.SampleRate;

    void ComputeTapTargets(int taps, float time, float preDelay) {
        var alpha = Params.Current(TimeAlpha) / 100f;
        var random = Params.Current(TimeRandom) / 100f;
        for (var k = 0; k < taps; k++) {
            var position = TapPosition(k, taps, alpha);
            if (random > 0) {
                position = Math.Max(0f, position + random * (Jitter(k, 1) - 0.5f) / taps);
            }

            tapTarget[k] = preDelay + time * position;
        }
    }

    void ComputeTapGains(int taps, float spread) {
        var levelShape = (int)Params.Current(LevelShape);
        var panShape = (int)Params.Current(PanShape);
        var levelPower = AlphaPower(Params.Current(LevelAlpha) / 100f);
        var levelRandom = Params.Current(LevelRandom) / 100f;
        var panPower = AlphaPower(Params.Current(PanAlpha) / 100f);
        var power = 0f;
        for (var k = 0; k < taps; k++) {
            var amplitude = Amplitude(k, taps, levelShape, levelPower, levelRandom);
            power += amplitude * amplitude;
        }

        var norm = power > 0 ? 1f / MathF.Sqrt(power) : 0f;
        for (var k = 0; k < taps; k++) {
            var u = taps > 1 ? k / (taps - 1f) : 0.5f;
            var position = panShape switch {
                Alternate => k % 2 == 0 ? -1f : 1f,
                LeftToRight => 2f * u - 1f,
                RightToLeft => 1f - 2f * u,
                _ => 0f
            };
            if (panPower != 1f) {
                position = MathF.CopySign(MathF.Pow(MathF.Abs(position), panPower), position);
            }

            // Balance law: unity on both sides at centre, the far side fading to zero.
            var pan = spread * position;
            var amplitude = Amplitude(k, taps, levelShape, levelPower, levelRandom) * norm;
            tapLeft[k] = amplitude * Math.Min(1f, 1f - pan);
            tapRight[k] = amplitude * Math.Min(1f, 1f + pan);
        }
    }

    static float Amplitude(int k, int taps, int shape, float power, float random) {
        var amplitude = TapAmplitude(k, taps, shape);
        if (power != 1f) {
            amplitude = MathF.Pow(amplitude, power);
        }

        return random > 0 ? amplitude * (1f - random * Jitter(k, 2)) : amplitude;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        line.Allocate((int)(spec.SampleRate * (MaxTimeMs + MaxPreDelayMs) / 1000) + 8);
        diffuser.Allocate(spec.SampleRate, MaxDiffusionMs);
    }

    protected override void OnReset() {
        line.Reset();
        diffuser.Clear();
        lowCut.Reset();
        highCut.Reset();
        envelope.Reset();
        activeTaps = TapCount();
        ComputeTapTargets(activeTaps, TargetDelaySamples(Params.Current(Time), 120), PreDelaySamples());
        Array.Copy(tapTarget, tapDelay, MaxTaps);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            line.Dispose();
            diffuser.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var sampleRate = (float)Spec.SampleRate;
        var taps = TapCount();
        var time = TargetDelaySamples(Params.Advance(Time, n), context.Tempo);
        Params.Advance(TimeAlpha, n);
        Params.Advance(TimeRandom, n);
        Params.Advance(PreDelay, n);
        Params.Advance(LevelAlpha, n);
        Params.Advance(LevelRandom, n);
        Params.Advance(PanAlpha, n);
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        ComputeTapTargets(taps, time, PreDelaySamples());
        ComputeTapGains(taps, Params.Advance(Spread, n) / 100f);
        if (taps != activeTaps) {
            // A different tap count re-places every tap: jump rather than glide taps across the pattern.
            activeTaps = taps;
            Array.Copy(tapTarget, tapDelay, MaxTaps);
        }

        var lowCutHz = Params.Advance(LowCut, n);
        var highCutHz = Params.Advance(HighCut, n);
        var useLowCut = lowCutHz > LowCutOff;
        var useHighCut = highCutHz < HighCutOff;
        var lowCutCoefficients =
            useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCutHz, 0.7071) : default;
        var highCutCoefficients =
            useHighCut ? SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCutHz, 0.7071) : default;

        var feedback = Params.Advance(Feedback, n) / 100f;
        var feedbackTap = Math.Clamp((int)MathF.Round(Params.Current(FeedbackTap)), 1, taps) - 1;
        diffuser.Configure(
            Params.Advance(DiffusionMix, n) / 100f,
            Params.Advance(DiffusionTime, n),
            Params.Advance(DiffusionRate, n),
            Params.Advance(DiffusionDepth, n) / 100f
        );
        envelope.Configure(
            sampleRate,
            Params.Advance(EnvelopeThreshold, n),
            Params.Advance(EnvelopeAttack, n),
            Params.Advance(EnvelopeRelease, n)
        );

        for (var k = 0; k < taps; k++) {
            tapStep[k] = (tapTarget[k] - tapDelay[k]) / n;
        }

        var inputGain = Params.Advance(InputGain, n) / 100f;
        var select = (int)Params.Current(InputSelect);
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        for (var i = 0; i < n; i++) {
            currentMix += mixStep;
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            float wetLeft = 0, wetRight = 0, fed = 0;
            for (var k = 0; k < taps; k++) {
                tapDelay[k] += tapStep[k];
                var tap = line.Read(tapDelay[k]);
                wetLeft += tap * tapLeft[k];
                wetRight += tap * tapRight[k];
                if (k == feedbackTap) {
                    fed = tap;
                }
            }

            var x = Stereo.Select(select, dryLeft, dryRight) * inputGain;
            if (envelope.Active) {
                x *= envelope.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
            }

            if (feedback > 0) {
                x += feedback * fed;
            }

            if (useLowCut) {
                x = (float)lowCut.Tick(lowCutCoefficients, x);
            }

            if (useHighCut) {
                x = (float)highCut.Tick(highCutCoefficients, x);
            }

            // Feedback reaches unity (100 %) and the input keeps adding to the loop: limit it as every other
            // recirculating block does, so it saturates instead of growing without bound.
            line.Write(DelayBlock.Limit(x));
            diffuser.Tick(ref wetLeft, ref wetRight);
            io.Left[i] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[i] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        Array.Copy(tapTarget, tapDelay, MaxTaps);
        this.mix = mix;
        lowCut.FlushDenormals();
        highCut.FlushDenormals();
        envelope.FlushDenormals();

        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
