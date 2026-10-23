using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Megatap: up to 40 taps on one line fed by the mono sum, spread across Time (tempo-syncable). Time Shape bends the
///     tap spacing, Level Shape sets the tap amplitudes and Pan Shape places them in the stereo field. Low and high cut
///     filter what enters the line, and the Axe-Fx mix law and Level are shared with the other delays.
/// </summary>
/// <remarks>
///     <para>
///         Tap k of N (k = 1..N) sits at <c>Time · f(k / N)</c>, where <c>f(x) = (e^(a·x) - 1) / (e^a - 1)</c> with
///         <c>a = 4 · shape</c>: zero shape spaces the taps evenly, positive shape starts them close together and
///         spreads them out (a slowing bounce), negative shape does the reverse. The last tap always lands on Time.
///         The tap amplitudes are normalised to unit total power, so the wet level of a noise-like input does not
///         change with the tap count.
///     </para>
///     <para>
///         Not modelled: feedback (Megatap is a single pass, as on the Axe-Fx), the shape "alpha" fine controls, and
///         random tap jitter.
///     </para>
/// </remarks>
public sealed class MegatapBlock() : DspBlock(Info) {
    public const int Time = 0;
    public const int Tempo = 1;
    public const int Taps = 2;
    public const int TimeShape = 3;
    public const int LevelShape = 4;
    public const int PanShape = 5;
    public const int Spread = 6;
    public const int LowCut = 7;
    public const int HighCut = 8;
    public const int Mix = 9;
    public const int Level = 10;

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
    public const int MaxTaps = 40;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    /// <summary>Level of the last tap of the Exponential shape, in dB.</summary>
    public const float ExponentialFloorDb = -40f;

    /// <summary>Tempo divisions in beats (quarter notes), as in <see cref="DelayBlock" />.</summary>
    static readonly float[] Divisions = [0f, 4f, 2f, 1f, 1.5f, 2f / 3f, 0.5f, 0.75f, 1f / 3f, 0.25f];

    readonly FractionalDelayLine line = new();
    readonly float[] tapDelay = new float[MaxTaps];
    readonly float[] tapTarget = new float[MaxTaps];
    readonly float[] tapStep = new float[MaxTaps];
    readonly float[] tapLeft = new float[MaxTaps];
    readonly float[] tapRight = new float[MaxTaps];
    SvfState lowCut;
    SvfState highCut;
    int activeTaps;
    float mix;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "megatap",
        "Megatap Delay",
        "Delay",
        [
            new("time", "Time", ParamKind.Float, 10f, MaxTimeMs, 1_000f, "ms", ParamTaper.Log, ParamSmoothing.Linear, 60f),
            ParamDescriptor.Choice(
                "tempo",
                "Tempo",
                ["Off", "1/1", "1/2", "1/4", "1/4.", "1/4T", "1/8", "1/8.", "1/8T", "1/16"]
            ),
            new("taps", "Taps", ParamKind.Float, 1f, MaxTaps, 16f, Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("timeShape", "Time Shape", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("levelShape", "Level Shape", ["Flat", "Fade Out", "Fade In", "Swell", "Exponential"], 1),
            ParamDescriptor.Choice("panShape", "Pan Shape", ["Center", "Alternate", "Left to Right", "Right to Left"], 1),
            ParamDescriptor.Float("spread", "Spread", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2,
        channels: 2
    );

    /// <summary>The fraction of Time at which tap <paramref name="k" /> (0-based) of <paramref name="taps" /> sits.</summary>
    public static float TapPosition(int k, int taps, float shape) {
        var x = (k + 1f) / taps;
        var a = 4f * shape;
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

    float TargetDelaySamples(float timeMs, double tempo) {
        var division = Divisions[(int)Params.Current(Tempo)];
        var ms = division > 0 && tempo > 0 ? Math.Min(MaxTimeMs, (float)(60_000.0 / tempo * division)) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    protected override void OnPrepare(in ProcessSpec spec) =>
        line.Allocate((int)(spec.SampleRate * MaxTimeMs / 1000) + 8);

    protected override void OnReset() {
        line.Reset();
        lowCut.Reset();
        highCut.Reset();
        activeTaps = TapCount();
        ComputeTapTargets(activeTaps, TargetDelaySamples(Params.Current(Time), 120));
        Array.Copy(tapTarget, tapDelay, MaxTaps);
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            line.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var sampleRate = (float)Spec.SampleRate;
        var taps = TapCount();
        var time = TargetDelaySamples(Params.Advance(Time, io.Length), context.Tempo);
        Params.Advance(TimeShape, io.Length);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        ComputeTapTargets(taps, time);
        ComputeTapGains(taps, Params.Advance(Spread, io.Length) / 100f);
        if (taps != activeTaps) {
            // A different tap count re-places every tap: jump rather than glide taps across the pattern.
            activeTaps = taps;
            Array.Copy(tapTarget, tapDelay, MaxTaps);
        }

        var lowCutHz = Params.Advance(LowCut, io.Length);
        var highCutHz = Params.Advance(HighCut, io.Length);
        var useLowCut = lowCutHz > LowCutOff;
        var useHighCut = highCutHz < HighCutOff;
        var lowCutCoefficients =
            useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCutHz, 0.7071) : default;
        var highCutCoefficients =
            useHighCut ? SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCutHz, 0.7071) : default;

        for (var k = 0; k < taps; k++) {
            tapStep[k] = (tapTarget[k] - tapDelay[k]) / io.Length;
        }

        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;
        for (var n = 0; n < io.Length; n++) {
            currentMix += mixStep;
            var dryLeft = io.Left[n];
            var dryRight = io.Right[n];
            float wetLeft = 0, wetRight = 0;
            for (var k = 0; k < taps; k++) {
                tapDelay[k] += tapStep[k];
                var tap = line.Read(tapDelay[k]);
                wetLeft += tap * tapLeft[k];
                wetRight += tap * tapRight[k];
            }

            var x = 0.5f * (dryLeft + dryRight);
            if (useLowCut) {
                x = (float)lowCut.Tick(lowCutCoefficients, x);
            }

            if (useHighCut) {
                x = (float)highCut.Tick(highCutCoefficients, x);
            }

            line.Write(x);
            io.Left[n] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[n] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        Array.Copy(tapTarget, tapDelay, MaxTaps);
        this.mix = mix;
        lowCut.FlushDenormals();
        highCut.FlushDenormals();

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    int TapCount() => Math.Clamp((int)MathF.Round(Params.Current(Taps)), 1, MaxTaps);

    void ComputeTapTargets(int taps, float time) {
        var shape = Params.Current(TimeShape) / 100f;
        for (var k = 0; k < taps; k++) {
            tapTarget[k] = time * TapPosition(k, taps, shape);
        }
    }

    void ComputeTapGains(int taps, float spread) {
        var levelShape = (int)Params.Current(LevelShape);
        var panShape = (int)Params.Current(PanShape);
        var power = 0f;
        for (var k = 0; k < taps; k++) {
            var amplitude = TapAmplitude(k, taps, levelShape);
            power += amplitude * amplitude;
        }

        var norm = 1f / MathF.Sqrt(power);
        for (var k = 0; k < taps; k++) {
            var u = taps > 1 ? k / (taps - 1f) : 0.5f;
            var pan = spread * panShape switch {
                Alternate => k % 2 == 0 ? -1f : 1f,
                LeftToRight => 2f * u - 1f,
                RightToLeft => 1f - 2f * u,
                _ => 0f
            };

            // Balance law: unity on both sides at centre, the far side fading to zero.
            var amplitude = TapAmplitude(k, taps, levelShape) * norm;
            tapLeft[k] = amplitude * Math.Min(1f, 1f - pan);
            tapRight[k] = amplitude * Math.Min(1f, 1f + pan);
        }
    }
}
