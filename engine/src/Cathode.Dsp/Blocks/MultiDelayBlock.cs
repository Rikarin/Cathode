using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Multi-delay with six types sharing a master Time (tempo-syncable), Feedback, feedback-path low and high cut, the
///     Axe-Fx mix law and Level. Reads use cubic Hermite interpolation, so integer delays are exact and time changes
///     glide.
/// </summary>
/// <remarks>
///     <para>
///         Quad Tap: one line fed by the mono sum, four taps placed at a percentage of Time with their own level and
///         pan; the feedback re-enters at Time, so the whole pattern repeats. Ten Tap: ten taps evenly spaced across
///         Time, with shuffle (swing on the off-beat taps), a level taper and alternating pan spread.
///     </para>
///     <para>
///         Plex: four lines at fixed, mutually prime-ish fractions of Time, cross-fed through an orthogonal 4×4
///         Hadamard matrix, with a Schroeder allpass in each loop whose gain is the Diffusion. Each repeat passes the
///         allpasses again, so the echoes smear progressively into a reverb-like wash. Ducking: a ping-pong delay
///         (feedback applies on every hop) whose wet signal is attenuated while the input is above a threshold and
///         recovers over the release time once it falls silent. Reverse: each channel's line, with forward feedback at
///         Time, is played back in reversed chunks of Time by two heads half a chunk apart under complementary sin²
///         windows, so the repeats stay reversed. Sweep: a stereo delay whose wet output runs through a resonant
///         band-pass swept logarithmically between two frequencies by a triangle LFO, phase-offset on the right.
///     </para>
///     <para>
///         Not modelled: the Plex pitch shifters and per-line detune, per-tap filters and feedback, modulation of the
///         tap times, and the Axe-Fx's many "rhythm" and "band" multi-tap variants (Quad Tap covers them by settings).
///     </para>
/// </remarks>
public sealed class MultiDelayBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Time = 1;
    public const int Tempo = 2;
    public const int Feedback = 3;
    public const int Tap1Time = 4;
    public const int Tap1Level = 8;
    public const int Tap1Pan = 12;
    public const int Shuffle = 16;
    public const int Taper = 17;
    public const int Spread = 18;
    public const int Diffusion = 19;
    public const int DuckThreshold = 20;
    public const int DuckAttenuation = 21;
    public const int DuckRelease = 22;
    public const int SweepRate = 23;
    public const int SweepStart = 24;
    public const int SweepStop = 25;
    public const int SweepResonance = 26;
    public const int SweepPhase = 27;
    public const int LowCut = 28;
    public const int HighCut = 29;
    public const int Mix = 30;
    public const int Level = 31;
    public const int Balance = 32;
    public const int InputGain = 33;
    public const int InputSelect = 34;
    public const int GlobalMix = 35;

    public const int QuadTap = 0;
    public const int TenTap = 1;
    public const int Plex = 2;
    public const int Ducking = 3;
    public const int Reverse = 4;
    public const int Sweep = 5;

    public const float MaxTimeMs = 2_000f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;
    public const int QuadTaps = 4;
    public const int TenTaps = 10;

    const int Lines = 4;
    const int SweepHop = 16;
    const float DuckAttackMs = 5f;
    const float DuckDetectorReleaseMs = 50f;
    const float MaxDiffusionGain = 0.7f;

    /// <summary>Plex line lengths as fractions of Time.</summary>
    public static readonly float[] PlexRatios = [1f, 0.887f, 0.743f, 0.613f];

    /// <summary>Plex loop allpass lengths in ms; subtracted from the line lengths so each loop is exactly its ratio of Time.</summary>
    static readonly float[] PlexAllpassMs = [3.7f, 5.3f, 7.9f, 10.1f];

    readonly FractionalDelayLine[] lines = [new(), new(), new(), new()];
    readonly ReverbLine[] allpasses = [new(), new(), new(), new()];
    readonly int[] allpassLength = new int[Lines];
    readonly SvfState[] lowCutState = new SvfState[Lines];
    readonly SvfState[] highCutState = new SvfState[Lines];
    readonly float[] tapDelay = new float[TenTaps];
    readonly float[] tapTarget = new float[TenTaps];
    readonly float[] tapStep = new float[TenTaps];
    readonly float[] tapLeft = new float[TenTaps];
    readonly float[] tapRight = new float[TenTaps];
    readonly float[] plexOut = new float[Lines];
    int activeType = -1;
    float delaySamples;
    float mix;
    OutputStage output;

    // Per-callback settings read by the per-sample ticks.
    float feedback;
    bool useLowCut;
    bool useHighCut;
    SvfCoefficients lowCutCoefficients;
    SvfCoefficients highCutCoefficients;
    float diffusionGain;
    float duckThreshold;
    float duckFloor;
    float duckAttack;
    float duckRelease;
    float detectorRelease;

    // Running state of the Ducking, Reverse and Sweep types.
    float duckEnvelope;
    float duckGain = 1f;
    float reversePosition;
    Lfo sweepLfo;
    int sweepCounter;
    SvfCoefficients sweepLeftCoefficients;
    SvfCoefficients sweepRightCoefficients;
    SvfState sweepLeft;
    SvfState sweepRight;

    public static BlockDescriptor Info { get; } = new(
        "multidelay",
        "Multi-Delay",
        "Delay",
        [
            ParamDescriptor.Choice("type", "Type", ["Quad Tap", "Ten Tap", "Plex", "Ducking", "Reverse", "Sweep"]),
            new("time", "Time", ParamKind.Float, 1f, MaxTimeMs, 400f, "ms", ParamTaper.Log, ParamSmoothing.Linear, 60f),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("feedback", "Feedback", 0f, 100f, 20f, "%"),
            ParamDescriptor.Float("tap1Time", "Tap 1 Time", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("tap2Time", "Tap 2 Time", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("tap3Time", "Tap 3 Time", 0f, 100f, 75f, "%"),
            ParamDescriptor.Float("tap4Time", "Tap 4 Time", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("tap1Level", "Tap 1 Level", Decibels.SilenceDb, 0f, 0f, "dB"),
            ParamDescriptor.Float("tap2Level", "Tap 2 Level", Decibels.SilenceDb, 0f, -3f, "dB"),
            ParamDescriptor.Float("tap3Level", "Tap 3 Level", Decibels.SilenceDb, 0f, -6f, "dB"),
            ParamDescriptor.Float("tap4Level", "Tap 4 Level", Decibels.SilenceDb, 0f, -9f, "dB"),
            ParamDescriptor.Float("tap1Pan", "Tap 1 Pan", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("tap2Pan", "Tap 2 Pan", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("tap3Pan", "Tap 3 Pan", -100f, 100f, -50f, "%"),
            ParamDescriptor.Float("tap4Pan", "Tap 4 Pan", -100f, 100f, 50f, "%"),
            ParamDescriptor.Float("shuffle", "Shuffle", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("taper", "Level Taper", -100f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Spread", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("diffusion", "Diffusion", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("duckThreshold", "Duck Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckAttenuation", "Duck Attenuation", 0f, 40f, 12f, "dB"),
            ParamDescriptor.Float("duckRelease", "Duck Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("sweepRate", "Sweep Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("sweepStart", "Sweep Start", 100f, 10_000f, 300f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("sweepStop", "Sweep Stop", 100f, 10_000f, 3_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("sweepResonance", "Sweep Resonance", 0.5f, 10f, 3f, "", ParamTaper.Log),
            ParamDescriptor.Float("sweepPhase", "Sweep Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            EffectsMix.Param
        ],
        maxInstances: 2
    );

    /// <summary>Gains of the balance pan law: unity on both sides at centre, the far side fading to zero.</summary>
    static (float Left, float Right) Pan(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    static float OnePole(float ms, float sampleRate) => MathF.Exp(-1f / (ms * 0.001f * sampleRate));

    float TargetDelaySamples(float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(Tempo), tempo);
        var ms = division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        // Lines 0 and 1 also serve Reverse, whose heads read up to twice Time back.
        var time = (int)(spec.SampleRate * MaxTimeMs / 1000) + 8;
        for (var i = 0; i < Lines; i++) {
            lines[i].Allocate(i < 2 ? 2 * time : time);
            allpassLength[i] = (int)MathF.Round(PlexAllpassMs[i] * 0.001f * (float)spec.SampleRate);
            allpasses[i].Allocate(allpassLength[i] + 2);
        }
    }

    protected override void OnReset() {
        activeType = (int)Params.Current(Type);
        ClearState();
        delaySamples = TargetDelaySamples(Params.Current(Time), 120);
        ComputeTapTargets(activeType, delaySamples);
        Array.Copy(tapTarget, tapDelay, TenTaps);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var line in lines) {
                line.Dispose();
            }

            foreach (var allpass in allpasses) {
                allpass.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var sampleRate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var targetDelay = TargetDelaySamples(Params.Advance(Time, io.Length), context.Tempo);
        var mix = EffectsMix.Apply(Params.Advance(Mix, io.Length), Params.Current(GlobalMix), context) / 100f;
        feedback = Params.Advance(Feedback, io.Length) / 100f;
        for (var i = Tap1Time; i <= Spread; i++) {
            Params.Advance(i, io.Length); // tap times, levels and pans are read back by the tap computations below
        }

        if (type != activeType) {
            // The type that takes over starts empty and at its own tap times rather than gliding from the old ones.
            activeType = type;
            ClearState();
            delaySamples = targetDelay;
            ComputeTapTargets(type, targetDelay);
            Array.Copy(tapTarget, tapDelay, TenTaps);
        }

        var lowCut = Params.Advance(LowCut, io.Length);
        var highCut = Params.Advance(HighCut, io.Length);
        useLowCut = lowCut > LowCutOff;
        useHighCut = highCut < HighCutOff;
        lowCutCoefficients = useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCut, 0.7071) : default;
        highCutCoefficients =
            useHighCut ? SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCut, 0.7071) : default;

        ComputeTapTargets(type, targetDelay);
        ComputeTapGains(type);
        diffusionGain = Params.Advance(Diffusion, io.Length) / 100f * MaxDiffusionGain;
        duckThreshold = Decibels.ToGain(Params.Advance(DuckThreshold, io.Length));
        duckFloor = Decibels.ToGain(-Params.Advance(DuckAttenuation, io.Length));
        duckRelease = OnePole(Params.Advance(DuckRelease, io.Length), sampleRate);
        duckAttack = OnePole(DuckAttackMs, sampleRate);
        detectorRelease = OnePole(DuckDetectorReleaseMs, sampleRate);
        var sweepRate = Params.Advance(SweepRate, io.Length);
        var sweepStart = Params.Advance(SweepStart, io.Length);
        var sweepStop = Params.Advance(SweepStop, io.Length);
        var sweepQ = Params.Advance(SweepResonance, io.Length);
        var sweepPhase = Params.Advance(SweepPhase, io.Length) / 360f;

        var delayStep = (targetDelay - delaySamples) / io.Length;
        for (var k = 0; k < TenTaps; k++) {
            tapStep[k] = (tapTarget[k] - tapDelay[k]) / io.Length;
        }

        var inputGain = Params.Advance(InputGain, io.Length) / 100f;
        var select = (int)Params.Current(InputSelect);
        var mixStep = (mix - this.mix) / io.Length;
        var delay = delaySamples;
        var currentMix = this.mix;
        for (var n = 0; n < io.Length; n++) {
            delay += delayStep;
            for (var k = 0; k < TenTaps; k++) {
                tapDelay[k] += tapStep[k];
            }

            currentMix += mixStep;
            var dryLeft = io.Left[n];
            var dryRight = io.Right[n];
            var mono = Stereo.Select(select, dryLeft, dryRight) * inputGain;

            if (type == Sweep) {
                // Coefficients move on a fixed internal hop, so the output does not depend on the host block size.
                if (sweepCounter == 0) {
                    sweepLeftCoefficients = SweepCoefficients(sweepStart, sweepStop, sweepQ, 0, sampleRate);
                    sweepRightCoefficients = SweepCoefficients(sweepStart, sweepStop, sweepQ, sweepPhase, sampleRate);
                }

                sweepCounter = (sweepCounter + 1) % SweepHop;
                sweepLfo.Advance(sweepRate, sampleRate);
            }

            var (wetLeft, wetRight) = type switch {
                QuadTap => TickTaps(mono, delay, QuadTaps),
                TenTap => TickTaps(mono, delay, TenTaps),
                Plex => TickPlex(mono),
                Ducking => TickDucking(mono, delay),
                Reverse => TickReverse(dryLeft * inputGain, dryRight * inputGain, delay),
                _ => TickSweep(dryLeft * inputGain, dryRight * inputGain, delay)
            };

            io.Left[n] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[n] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        delaySamples = targetDelay;
        Array.Copy(tapTarget, tapDelay, TenTaps);
        this.mix = mix;
        FlushDenormals();

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    void ClearState() {
        for (var i = 0; i < Lines; i++) {
            lines[i].Reset();
            allpasses[i].Clear();
            lowCutState[i].Reset();
            highCutState[i].Reset();
        }

        duckEnvelope = 0;
        duckGain = 1f;
        reversePosition = 0;
        sweepLfo.Reset();
        sweepCounter = 0;
        sweepLeft.Reset();
        sweepRight.Reset();
    }

    /// <summary>Tap (or Plex line) read delays in samples for master delay <paramref name="time" />.</summary>
    void ComputeTapTargets(int type, float time) {
        switch (type) {
            case QuadTap:
                for (var k = 0; k < QuadTaps; k++) {
                    tapTarget[k] = time * Params.Current(Tap1Time + k) / 100f;
                }

                break;
            case TenTap:
                // Shuffle delays the off-beat taps (1, 3, 5, ... counting the dry note as beat 0) by up to half a step.
                var swing = Params.Current(Shuffle) / 100f * 0.5f;
                for (var k = 0; k < TenTaps; k++) {
                    tapTarget[k] = time * (k + 1 + (k % 2 == 0 ? swing : 0f)) / TenTaps;
                }

                break;
            case Plex:
                for (var i = 0; i < Lines; i++) {
                    tapTarget[i] = time * PlexRatios[i] - allpassLength[i];
                }

                break;
        }
    }

    void ComputeTapGains(int type) {
        if (type == QuadTap) {
            for (var k = 0; k < QuadTaps; k++) {
                var level = Decibels.ToGain(Params.Current(Tap1Level + k));
                var (left, right) = Pan(Params.Current(Tap1Pan + k) / 100f);
                tapLeft[k] = level * left;
                tapRight[k] = level * right;
            }
        } else if (type == TenTap) {
            // Positive taper fades the taps out across the pattern, negative fades them in.
            var taper = Params.Current(Taper) / 100f;
            var spread = Params.Current(Spread) / 100f;
            for (var k = 0; k < TenTaps; k++) {
                var u = k / (TenTaps - 1f);
                var level = taper >= 0 ? 1f - taper * u : 1f + taper * (1f - u);
                var (left, right) = Pan(k % 2 == 0 ? -spread : spread);
                tapLeft[k] = level * left;
                tapRight[k] = level * right;
            }
        }
    }

    float Damp(int line, float x) {
        if (useLowCut) {
            x = (float)lowCutState[line].Tick(lowCutCoefficients, x);
        }

        if (useHighCut) {
            x = (float)highCutState[line].Tick(highCutCoefficients, x);
        }

        return x;
    }

    (float Left, float Right) TickTaps(float x, float delay, int taps) {
        var line = lines[0];
        var echo = line.Read(delay);
        float left = 0, right = 0;
        for (var k = 0; k < taps; k++) {
            var tap = line.Read(tapDelay[k]);
            left += tap * tapLeft[k];
            right += tap * tapRight[k];
        }

        line.Write(Damp(0, x + feedback * echo));
        return (left, right);
    }

    (float Left, float Right) TickPlex(float x) {
        for (var i = 0; i < Lines; i++) {
            plexOut[i] = lines[i].Read(tapDelay[i]);
        }

        // Feedback through the orthogonal Hadamard matrix H4 / 2 (a butterfly), scaled by the feedback amount.
        var a = plexOut[0] + plexOut[1];
        var b = plexOut[0] - plexOut[1];
        var c = plexOut[2] + plexOut[3];
        var d = plexOut[2] - plexOut[3];
        var g = 0.5f * feedback;
        FeedPlex(0, 0.5f * x + g * (a + c));
        FeedPlex(1, 0.5f * x + g * (b + d));
        FeedPlex(2, 0.5f * x + g * (a - c));
        FeedPlex(3, 0.5f * x + g * (b - d));
        return (plexOut[0] + plexOut[2], plexOut[1] + plexOut[3]);
    }

    void FeedPlex(int line, float value) =>
        lines[line].Write(allpasses[line].Allpass(Damp(line, value), allpassLength[line], diffusionGain));

    (float Left, float Right) TickDucking(float x, float delay) {
        var left = lines[0].Read(delay);
        var right = lines[1].Read(delay);
        lines[0].Write(Damp(0, x + feedback * right));
        lines[1].Write(Damp(1, feedback * left));

        // Peak detector with instant attack, then a gain that falls fast and recovers over the release time.
        var level = MathF.Abs(x);
        duckEnvelope = level > duckEnvelope ? level : duckEnvelope * detectorRelease;
        var target = duckEnvelope > duckThreshold ? duckFloor : 1f;
        duckGain = target + (duckGain - target) * (target < duckGain ? duckAttack : duckRelease);
        return (left * duckGain, right * duckGain);
    }

    (float Left, float Right) TickReverse(float xLeft, float xRight, float delay) {
        // Head A starts a chunk every `delay` samples, head B half a chunk later. Reading 2p + 3 samples back at
        // position p plays the previous chunk backwards; sin² + cos² windows cross-fade the heads to unity.
        var positionA = reversePosition;
        var positionB = positionA + 0.5f * delay;
        if (positionB >= delay) {
            positionB -= delay;
        }

        var window = MathF.Sin(MathF.PI * positionA / delay);
        var weightA = window * window;
        var left = ReverseHeads(0, positionA, positionB, weightA);
        var right = ReverseHeads(1, positionA, positionB, weightA);

        // Forward feedback inside each line, so every repeat is also played reversed.
        lines[0].Write(Damp(0, xLeft + feedback * lines[0].Read(delay)));
        lines[1].Write(Damp(1, xRight + feedback * lines[1].Read(delay)));

        reversePosition += 1f;
        if (reversePosition >= delay) {
            reversePosition -= delay * MathF.Floor(reversePosition / delay);
        }

        return (left, right);
    }

    float ReverseHeads(int line, float positionA, float positionB, float weightA) =>
        weightA * lines[line].Read(2f * positionA + 3f) + (1f - weightA) * lines[line].Read(2f * positionB + 3f);

    (float Left, float Right) TickSweep(float xLeft, float xRight, float delay) {
        var left = lines[0].Read(delay);
        var right = lines[1].Read(delay);
        lines[0].Write(Damp(0, xLeft + feedback * left));
        lines[1].Write(Damp(1, xRight + feedback * right));
        return (
            (float)sweepLeft.Tick(sweepLeftCoefficients, left),
            (float)sweepRight.Tick(sweepRightCoefficients, right)
        );
    }

    /// <summary>The band-pass at the LFO's position, swept logarithmically from start (LFO -1) to stop (LFO +1).</summary>
    SvfCoefficients SweepCoefficients(float start, float stop, float q, float phase, float sampleRate) {
        var position = 0.5f * (sweepLfo.Value(LfoShape.Triangle, phase) + 1f);
        var frequency = start * MathF.Pow(stop / start, position);
        return SvfCoefficients.Design(BiquadType.BandPass, sampleRate, frequency, q);
    }

    void FlushDenormals() {
        for (var i = 0; i < Lines; i++) {
            lowCutState[i].FlushDenormals();
            highCutState[i].FlushDenormals();
        }

        sweepLeft.FlushDenormals();
        sweepRight.FlushDenormals();
        if (duckEnvelope < 1e-20f) {
            duckEnvelope = 0;
        }
    }
}
