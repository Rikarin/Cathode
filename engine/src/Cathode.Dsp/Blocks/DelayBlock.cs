using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Delay with voicings sharing one core:
///     <list type="bullet">
///         <item>Digital: clean.</item>
///         <item>Analog: dark BBD-style feedback with soft saturation.</item>
///         <item>Tape: gentler filtering, saturation, wow and flutter; Motor Speed sets the tape speed.</item>
///         <item>Ping-Pong: the mono input enters the left line and every repeat crosses to the other side.</item>
///         <item>Vintage Digital: 12-bit repeats darkened at 7 kHz, like an early digital rack delay.</item>
///         <item>Dual: two independent lines (time, tempo, feedback, level, pan) with cross feedback.</item>
///         <item>Mono Tape: one tape loop with two playback heads, each with its own level, pan and feedback.</item>
///         <item>Reverse: each chunk of Time plays backwards, chunks crossfaded; Run and Trigger Restart.</item>
///         <item>Sweep: a stereo delay whose repeats pass a resonant band-pass swept by an LFO.</item>
///     </list>
///     Up to 16 s, tempo sync, a right time as a ratio of the left, a Master Feedback that scales every feedback up to
///     200 % (a limiter then keeps the loop bounded), feedback of either polarity, a feedback-path EQ (low and high cut
///     with slope and resonance, two peaking bands), drive and bit reduction, two time LFOs and a level/pan/spread LFO, a
///     diffuser, a ducker on the wet signal, echo pan, a right post delay, input gain (what the lines hear, not the dry)
///     and Hold or Stack, which loop the repeats at unity with the input closed or open. The mix follows the Axe-Fx delay
///     law: the wet reaches full level at 50 % while the dry stays at full level, then the dry fades out. Reads use cubic
///     Hermite interpolation, so integer delays are exact and time changes glide instead of clicking.
/// </summary>
/// <remarks>
///     Reverse: a new chunk starts every Time (or on a new note with Trigger Restart); the head reading
///     <c>2p + 3</c> samples back at <c>p</c> samples into the chunk plays the previous Time backwards. When a chunk
///     starts, the outgoing head keeps playing for Crossfade × Time while the new one fades in, so the gains always sum
///     to one. The repeats are fed back forward at Time, so each repeat is reversed too. The chunk is limited to a third
///     of the line (the outgoing head reads up to three chunks back).
/// </remarks>
public sealed class DelayBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Time = 1;
    public const int Tempo = 2;
    public const int Feedback = 3;
    public const int Mix = 4;
    public const int LowCut = 5;
    public const int HighCut = 6;
    public const int ModRate = 7;
    public const int ModDepth = 8;
    public const int Level = 9;
    public const int Balance = 10;
    public const int InputGain = 11;
    public const int Ratio = 12;
    public const int Drive = 13;
    public const int BitReduction = 14;
    public const int Spread = 15;
    public const int DuckAttenuation = 16;
    public const int DuckThreshold = 17;
    public const int DuckRelease = 18;
    public const int Hold = 19;
    public const int PhaseReverse = 20;
    public const int MasterFeedback = 21;
    public const int EchoPan = 22;
    public const int RightPostDelay = 23;
    public const int TimeRight = 24;
    public const int TempoRight = 25;
    public const int FeedbackRight = 26;
    public const int LevelLeft = 27;
    public const int LevelRight = 28;
    public const int PanLeft = 29;
    public const int PanRight = 30;
    public const int CrossLeftToRight = 31;
    public const int CrossRightToLeft = 32;
    public const int SweepStart = 33;
    public const int SweepStop = 34;
    public const int SweepResonance = 35;
    public const int SweepShape = 36;
    public const int SweepRate = 37;
    public const int SweepTempo = 38;
    public const int SweepPhase = 39;
    public const int MotorSpeed = 40;
    public const int Head2Ratio = 41;
    public const int Head1Level = 42;
    public const int Head2Level = 43;
    public const int Head1Pan = 44;
    public const int Head2Pan = 45;
    public const int Head2Feedback = 46;
    public const int ReverseRun = 47;
    public const int ReverseRestart = 48;
    public const int ReverseCrossfade = 49;
    public const int LowCutSlope = 50;
    public const int HighCutSlope = 51;
    public const int LowCutQ = 52;
    public const int HighCutQ = 53;
    public const int Eq1Frequency = 54;
    public const int Eq1Q = 55;
    public const int Eq1Gain = 56;
    public const int Eq2Frequency = 57;
    public const int Eq2Q = 58;
    public const int Eq2Gain = 59;
    public const int DepthRange = 60;
    public const int Lfo1Type = 61;
    public const int Lfo1Tempo = 62;
    public const int Lfo1Phase = 63;
    public const int Lfo1Target = 64;
    public const int Lfo2Type = 65;
    public const int Lfo2Rate = 66;
    public const int Lfo2Tempo = 67;
    public const int Lfo2Depth = 68;
    public const int Lfo2Phase = 69;
    public const int Lfo2Target = 70;
    public const int Lfo3Target = 71;
    public const int Lfo3Type = 72;
    public const int Lfo3Rate = 73;
    public const int Lfo3Tempo = 74;
    public const int Lfo3Depth = 75;
    public const int DiffusionMix = 76;
    public const int DiffusionTime = 77;
    public const int DiffusionRate = 78;
    public const int DiffusionDepth = 79;

    public const int Digital = 0;
    public const int Analog = 1;
    public const int Tape = 2;
    public const int PingPong = 3;
    public const int VintageDigital = 4;
    public const int Dual = 5;
    public const int MonoTape = 6;
    public const int Reverse = 7;
    public const int Sweep = 8;
    public const int TypeCount = 9;

    /// <summary>Hold choices.</summary>
    public const int HoldOff = 0;
    public const int HoldOn = 1;
    public const int Stack = 2;

    /// <summary>LFO 1/2 targets: which line's time they move.</summary>
    public const int TargetBoth = 0;
    public const int TargetLeft = 1;
    public const int TargetRight = 2;

    /// <summary>LFO 3 targets.</summary>
    public const int Lfo3Level = 0;
    public const int Lfo3Pan = 1;
    public const int Lfo3Spread = 2;

    public const float MaxTimeMs = 16_000f;
    public const float MaxPostDelayMs = 1_000f;
    public const float MaxDiffusionMs = 100f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    /// <summary>The Trigger Restart onset level in dBFS (re-armed 6 dB below).</summary>
    public const float RestartThresholdDb = -40f;

    const int VintageBits = 12;
    const int SweepHop = 16;
    const float MaxDepthMs = 10f;
    const float RunFadeMs = 5f;

    static readonly float[] DepthScale = [0.1f, 1f, 10f];

    readonly Line left = new();
    readonly Line right = new();
    readonly FractionalDelayLine postRight = new();
    readonly Diffuser diffuser = new();
    Lfo lfo1;
    Lfo lfo2;
    Lfo lfo3;
    Lfo sweepLfo;
    double wowPhase;
    double flutterPhase;
    float delaySamples;
    float rightSamples;
    float mix;
    Ducker ducker;
    OutputStage output;
    int capacity;

    // Sweep.
    int sweepCounter;
    SvfCoefficients sweepLeftCoefficients;
    SvfCoefficients sweepRightCoefficients;
    SvfState sweepLeft;
    SvfState sweepRight;

    // Reverse: samples into the current chunk, into the outgoing one, and the crossfade left.
    float reversePosition;
    float reverseOutgoing;
    float reverseFade;
    float reverseFadeLength;
    float runGain = 1f;
    float onsetEnvelope;
    bool onsetArmed = true;

    public static BlockDescriptor Info { get; } = new(
        "delay",
        "Delay",
        "Delay",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Digital", "Analog", "Tape", "Ping-Pong", "Vintage Digital", "Dual", "Mono Tape", "Reverse", "Sweep"]
            ),
            new ParamDescriptor("time", "Time", ParamKind.Float, 1f, MaxTimeMs, 375f, "ms", ParamTaper.Log, ParamSmoothing.Linear, 60f)
                .RenamedOn(TypeCount, Dual, "Left Time")
                .RenamedOn(TypeCount, MonoTape, "Head 1 Time"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names)
                .RenamedOn(TypeCount, Dual, "Left Tempo")
                .RenamedOn(TypeCount, MonoTape, "Head 1 Tempo"),
            ParamDescriptor.Float("feedback", "Feedback", -100f, 100f, 30f, "%")
                .RenamedOn(TypeCount, Dual, "Left Feedback")
                .RenamedOn(TypeCount, MonoTape, "Head 1 Feedback"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lfo1Rate", "LFO 1 Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lfo1Depth", "LFO 1 Depth", 0f, MaxDepthMs, 0f, "ms"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("ratio", "L/R Time Ratio", 1f, 100f, 100f, "%").HiddenOn(TypeCount, Dual, MonoTape),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 0f),
            ParamDescriptor.Float("bitReduction", "Bit Reduction", 0f, 20f, 0f, "bits"),
            ParamDescriptor.Float("spread", "Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Choice("hold", "Repeat Hold", ["Off", "Hold", "Stack"]),
            ParamDescriptor.Choice("phaseReverse", "Phase Reverse", ChorusBlock.PhaseOptions),
            ParamDescriptor.Float("masterFeedback", "Master Feedback", 0f, 200f, 100f, "%"),
            ParamDescriptor.Float("echoPan", "Echo Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("rightPostDelay", "Right Post Delay", 0f, MaxPostDelayMs, 0f, "ms"),
            new ParamDescriptor(
                "timeRight",
                "Right Time",
                ParamKind.Float,
                1f,
                MaxTimeMs,
                500f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ).ShownOn(TypeCount, Dual),
            ParamDescriptor.Choice("tempoRight", "Right Tempo", TempoDivisions.Names).ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("feedbackRight", "Right Feedback", -100f, 100f, 30f, "%").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("levelLeft", "Left Level", Decibels.SilenceDb, 0f, 0f, "dB").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("levelRight", "Right Level", Decibels.SilenceDb, 0f, 0f, "dB").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("panLeft", "Left Pan", -100f, 100f, -100f, "%").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("panRight", "Right Pan", -100f, 100f, 100f, "%").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("crossLeftToRight", "Cross Feed L→R", -100f, 100f, 0f, "%").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("crossRightToLeft", "Cross Feed R→L", -100f, 100f, 0f, "%").ShownOn(TypeCount, Dual),
            ParamDescriptor.Float("sweepStart", "Sweep Start", 100f, 10_000f, 300f, "Hz", ParamTaper.Log).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Float("sweepStop", "Sweep Stop", 100f, 10_000f, 3_000f, "Hz", ParamTaper.Log).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Float("sweepResonance", "Sweep Resonance", 0.5f, 10f, 3f, "", ParamTaper.Log).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Choice("sweepShape", "Sweep Type", Lfo.ShapeNames, (int)LfoShape.Triangle).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Float("sweepRate", "Sweep Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Choice("sweepTempo", "Sweep Tempo", TempoDivisions.Names).ShownOn(TypeCount, Sweep),
            ParamDescriptor.Float("sweepPhase", "Sweep Phase", 0f, 180f, 90f, "°").ShownOn(TypeCount, Sweep),
            ParamDescriptor.Float("motorSpeed", "Motor Speed", 25f, 200f, 100f, "%").ShownOn(TypeCount, Tape, MonoTape),
            ParamDescriptor.Float("head2Ratio", "Head 2 Ratio", 0f, 100f, 50f, "%").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Float("head1Level", "Head 1 Level", Decibels.SilenceDb, 0f, 0f, "dB").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Float("head2Level", "Head 2 Level", Decibels.SilenceDb, 0f, -3f, "dB").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Float("head1Pan", "Head 1 Pan", -100f, 100f, -50f, "%").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Float("head2Pan", "Head 2 Pan", -100f, 100f, 50f, "%").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Float("head2Feedback", "Head 2 Feedback", -100f, 100f, 0f, "%").ShownOn(TypeCount, MonoTape),
            ParamDescriptor.Switch("reverseRun", "Run", true).ShownOn(TypeCount, Reverse),
            ParamDescriptor.Switch("reverseRestart", "Trigger Restart").ShownOn(TypeCount, Reverse),
            ParamDescriptor.Float("reverseCrossfade", "Crossfade", 0f, 50f, 20f, "%").ShownOn(TypeCount, Reverse),
            ParamDescriptor.Choice("lowCutSlope", "Low Cut Slope", [..CutFilter.Slopes], CutFilter.Slope12),
            ParamDescriptor.Choice("highCutSlope", "High Cut Slope", [..CutFilter.Slopes], CutFilter.Slope12),
            ParamDescriptor.Float("lowCutQ", "Low Cut Q", 0.5f, 3f, LoopFilter.ButterworthQ, "", ParamTaper.Log),
            ParamDescriptor.Float("highCutQ", "High Cut Q", 0.5f, 3f, LoopFilter.ButterworthQ, "", ParamTaper.Log),
            ParamDescriptor.Float("eq1Frequency", "EQ 1 Freq", 50f, 10_000f, 400f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("eq1Q", "EQ 1 Q", 0.1f, 10f, 0.7071f, "", ParamTaper.Log),
            ParamDescriptor.Float("eq1Gain", "EQ 1 Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("eq2Frequency", "EQ 2 Freq", 50f, 10_000f, 2_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("eq2Q", "EQ 2 Q", 0.1f, 10f, 0.7071f, "", ParamTaper.Log),
            ParamDescriptor.Float("eq2Gain", "EQ 2 Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Choice("depthRange", "LFO Depth Range", ["Low (×0.1)", "Normal", "High (×10)"], 1),
            ParamDescriptor.Choice("lfo1Type", "LFO 1 Type", Lfo.ShapeNames),
            ParamDescriptor.Choice("lfo1Tempo", "LFO 1 Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfo1Phase", "LFO 1 Phase", 0f, 180f, 0f, "°"),
            ParamDescriptor.Choice("lfo1Target", "LFO 1 Target", ["Both", "Left", "Right"]),
            ParamDescriptor.Choice("lfo2Type", "LFO 2 Type", Lfo.ShapeNames),
            ParamDescriptor.Float("lfo2Rate", "LFO 2 Rate", 0.05f, 10f, 2f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfo2Tempo", "LFO 2 Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfo2Depth", "LFO 2 Depth", 0f, MaxDepthMs, 0f, "ms"),
            ParamDescriptor.Float("lfo2Phase", "LFO 2 Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Choice("lfo2Target", "LFO 2 Target", ["Both", "Left", "Right"]),
            ParamDescriptor.Choice("lfo3Target", "LFO 3 Target", ["Level", "Pan", "Spread"]),
            ParamDescriptor.Choice("lfo3Type", "LFO 3 Type", Lfo.ShapeNames),
            ParamDescriptor.Float("lfo3Rate", "LFO 3 Rate", 0.05f, 10f, 1f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfo3Tempo", "LFO 3 Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfo3Depth", "LFO 3 Depth", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("diffusionMix", "Diffusion Mix", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("diffusionTime", "Diffusion Time", 1f, MaxDiffusionMs, 30f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("diffusionRate", "Diffusion LFO Rate", 0.05f, 5f, 0.4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("diffusionDepth", "Diffusion LFO Depth", 0f, 100f, 0f, "%")
        ],
        maxInstances: 4
    );

    /// <summary>Dry and wet gains for a mix of 0..1 under the delay law: each is at full level up to 50 %.</summary>
    public static (float Dry, float Wet) MixGains(float mix) => (Math.Min(1f, 2f * (1f - mix)), Math.Min(1f, 2f * mix));

    /// <summary>
    ///     The loop limiter: transparent up to full scale, then a tanh knee that never passes 2, so feedback above unity
    ///     (Master Feedback past 100 %, Stack) saturates instead of running away.
    /// </summary>
    public static float Limit(float x) {
        var magnitude = MathF.Abs(x);
        return magnitude <= 1f ? x : MathF.CopySign(1f + MathF.Tanh(magnitude - 1f), x);
    }

    float TargetDelaySamples(int tempoParam, float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(tempoParam), tempo);
        var ms = division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
        return (float)(ms * 0.001 * Spec.SampleRate);
    }

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;

    /// <summary>Gains of the balance pan law: unity on both sides at centre, the far side fading to zero.</summary>
    static (float Left, float Right) Pan(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    protected override void OnPrepare(in ProcessSpec spec) {
        // Room for the longest time plus the widest modulation (both LFOs at the high range, kept positive) and wow.
        capacity = (int)(spec.SampleRate * (MaxTimeMs + 4 * MaxDepthMs * DepthScale[^1] + 20) / 1000) + 8;
        left.Allocate(capacity);
        right.Allocate(capacity);
        postRight.Allocate((int)(spec.SampleRate * MaxPostDelayMs / 1000) + 8);
        diffuser.Allocate(spec.SampleRate, MaxDiffusionMs);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        postRight.Reset();
        diffuser.Clear();
        lfo1.Reset();
        lfo2.Reset();
        lfo3.Reset();
        sweepLfo.Reset();
        wowPhase = 0;
        flutterPhase = 0;
        ducker.Reset();
        sweepCounter = 0;
        sweepLeft.Reset();
        sweepRight.Reset();
        reversePosition = 0;
        reverseOutgoing = 0;
        reverseFade = 0;
        reverseFadeLength = 0;
        runGain = Params.Current(ReverseRun) >= 0.5f ? 1f : 0f;
        onsetEnvelope = 0;
        onsetArmed = true;
        delaySamples = TargetDelaySamples(Tempo, Params.Current(Time), 120);
        rightSamples = TargetDelaySamples(TempoRight, Params.Current(TimeRight), 120);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            left.Dispose();
            right.Dispose();
            postRight.Dispose();
            diffuser.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var sampleRate = (float)Spec.SampleRate;
        var tempo = context.Tempo;
        var type = (int)Params.Current(Type);
        var holdMode = (int)Params.Current(Hold);
        var hold = holdMode != HoldOff;
        var stack = holdMode == Stack;
        var master = Params.Advance(MasterFeedback, n) / 100f;
        var feedback = hold ? 1f : Params.Advance(Feedback, n) / 100f * master;
        var feedbackRight = hold ? 1f : Params.Advance(FeedbackRight, n) / 100f * master;
        var head2Feedback = hold ? 0f : Params.Advance(Head2Feedback, n) / 100f * master;
        var crossToRight = hold ? 0f : Params.Advance(CrossLeftToRight, n) / 100f * master;
        var crossToLeft = hold ? 0f : Params.Advance(CrossRightToLeft, n) / 100f * master;
        var mix = Params.Advance(Mix, n) / 100f;
        var motorSpeed = Params.Advance(MotorSpeed, n) / 100f;
        var motor = type is Tape or MonoTape ? motorSpeed : 1f;
        var targetDelay = TargetDelaySamples(Tempo, Params.Advance(Time, n), tempo);
        var targetRight = TargetDelaySamples(TempoRight, Params.Advance(TimeRight, n), tempo);
        if (motor != 1f) {
            // A slower tape takes longer between the heads.
            var longest = capacity - 8 - 4 * MaxDepthMs * DepthScale[^1] * 0.001f * sampleRate;
            targetDelay = Math.Min(longest, targetDelay / motor);
        }

        var ratio = Params.Advance(Ratio, n) / 100f;
        var inputGain = hold && !stack ? 0f : Params.Advance(InputGain, n) / 100f;
        var spread = Params.Advance(Spread, n) / 100f;
        var echoPan = Params.Advance(EchoPan, n) / 100f;
        var postDelay = Params.Advance(RightPostDelay, n) * 0.001f * sampleRate;
        var (polarityLeft, polarityRight) = ChorusBlock.Polarity((int)Params.Current(PhaseReverse));

        // Time modulation: LFO 1 and 2, each on one or both lines, scaled by the depth range.
        var range = DepthScale[(int)Params.Current(DepthRange)];
        var depth1 = Params.Advance(ModDepth, n) * range * 0.001f * sampleRate;
        var depth2 = Params.Advance(Lfo2Depth, n) * range * 0.001f * sampleRate;
        var rate1 = TempoDivisions.Rate((int)Params.Current(Lfo1Tempo), tempo, Params.Advance(ModRate, n));
        var rate2 = TempoDivisions.Rate((int)Params.Current(Lfo2Tempo), tempo, Params.Advance(Lfo2Rate, n));
        var shape1 = (LfoShape)(int)Params.Current(Lfo1Type);
        var shape2 = (LfoShape)(int)Params.Current(Lfo2Type);
        var phase1 = Params.Advance(Lfo1Phase, n) / 360.0;
        var phase2 = Params.Advance(Lfo2Phase, n) / 360.0;
        var target1 = (int)Params.Current(Lfo1Target);
        var target2 = (int)Params.Current(Lfo2Target);
        var (left1, right1) = (target1 != TargetRight ? depth1 : 0f, target1 != TargetLeft ? depth1 : 0f);
        var (left2, right2) = (target2 != TargetRight ? depth2 : 0f, target2 != TargetLeft ? depth2 : 0f);

        // LFO 3: the wet level, the echo pan or the spread.
        var depth3 = Params.Advance(Lfo3Depth, n) / 100f;
        var rate3 = TempoDivisions.Rate((int)Params.Current(Lfo3Tempo), tempo, Params.Advance(Lfo3Rate, n));
        var shape3 = (LfoShape)(int)Params.Current(Lfo3Type);
        var lfo3Target = (int)Params.Current(Lfo3Target);

        // Feedback-path filters. Analog, Tape and Vintage Digital add their own fixed darkening to the user's high cut;
        // a slower tape is darker still.
        var lowCut = Params.Advance(LowCut, n);
        var highCut = Math.Min(
            Params.Advance(HighCut, n),
            type switch {
                Analog => 2_800f,
                Tape or MonoTape => 6_500f * motor,
                VintageDigital => 7_000f,
                _ => HighCutOff
            }
        );
        var filter = LoopFilter.Design(
            sampleRate,
            lowCut,
            (int)Params.Current(LowCutSlope),
            Params.Advance(LowCutQ, n),
            highCut,
            (int)Params.Current(HighCutSlope),
            Params.Advance(HighCutQ, n),
            (Params.Advance(Eq1Frequency, n), Params.Advance(Eq1Q, n), Params.Advance(Eq1Gain, n)),
            (Params.Advance(Eq2Frequency, n), Params.Advance(Eq2Q, n), Params.Advance(Eq2Gain, n))
        );
        var drive = Params.Advance(Drive, n);
        var bits = Params.Advance(BitReduction, n);
        var dual = type == Dual;
        var tapeHeads = type == MonoTape;
        var loopGain = dual
            ? Math.Max(MathF.Abs(feedback) + MathF.Abs(crossToLeft), MathF.Abs(feedbackRight) + MathF.Abs(crossToRight))
            : tapeHeads
                ? MathF.Abs(feedback) + MathF.Abs(head2Feedback)
                : MathF.Abs(feedback);
        var loop = new LoopSettings(
            filter,
            type is Analog or Tape or MonoTape && !hold,
            hold ? 0f : drive,
            Quantum(type == VintageDigital ? VintageBits : 24 - bits),
            stack || loopGain > 1f
        );

        ducker.Configure(
            sampleRate,
            Params.Advance(DuckAttenuation, n),
            Params.Advance(DuckThreshold, n),
            Params.Advance(DuckRelease, n)
        );
        diffuser.Configure(
            Params.Advance(DiffusionMix, n) / 100f,
            Params.Advance(DiffusionTime, n),
            Params.Advance(DiffusionRate, n),
            Params.Advance(DiffusionDepth, n) / 100f
        );

        // Dual: each line's level and pan. Mono Tape: each head's.
        var (levelLeft, levelRight) = dual
            ? (Decibels.ToGain(Params.Advance(LevelLeft, n)), Decibels.ToGain(Params.Advance(LevelRight, n)))
            : (Decibels.ToGain(Params.Advance(Head1Level, n)), Decibels.ToGain(Params.Advance(Head2Level, n)));
        var (panLeft, panRight) = dual
            ? (Params.Advance(PanLeft, n) / 100f, Params.Advance(PanRight, n) / 100f)
            : (Params.Advance(Head1Pan, n) / 100f, Params.Advance(Head2Pan, n) / 100f);
        if (!dual) {
            Params.Advance(LevelLeft, n);
            Params.Advance(LevelRight, n);
            Params.Advance(PanLeft, n);
            Params.Advance(PanRight, n);
        } else {
            Params.Advance(Head1Level, n);
            Params.Advance(Head2Level, n);
            Params.Advance(Head1Pan, n);
            Params.Advance(Head2Pan, n);
        }

        var head2Ratio = Params.Advance(Head2Ratio, n) / 100f;
        var (l1Left, l1Right) = Pan(panLeft);
        var (l2Left, l2Right) = Pan(panRight);

        // Sweep: the band-pass and its LFO.
        var sweepStart = Params.Advance(SweepStart, n);
        var sweepStop = Params.Advance(SweepStop, n);
        var sweepQ = Params.Advance(SweepResonance, n);
        var sweepRate = TempoDivisions.Rate((int)Params.Current(SweepTempo), tempo, Params.Advance(SweepRate, n));
        var sweepPhase = Params.Advance(SweepPhase, n) / 360f;
        var sweepShape = (LfoShape)(int)Params.Current(SweepShape);

        // Reverse: run, restart on a new note, crossfade.
        var run = Params.Current(ReverseRun) >= 0.5f;
        var restart = Params.Current(ReverseRestart) >= 0.5f;
        var crossfade = Params.Advance(ReverseCrossfade, n) / 100f;
        var runCoefficient = MathF.Exp(-1f / (RunFadeMs * 0.001f * sampleRate));
        var onsetThreshold = Decibels.ToGain(RestartThresholdDb);
        var onsetRelease = MathF.Exp(-1f / (0.05f * sampleRate));
        var longestChunk = (capacity - 8) / 3f;

        // Tape transport: slow wow plus faster flutter, a few tenths of a millisecond deep, faster on a faster tape.
        var tape = type is Tape or MonoTape;
        var wowDepth = tape ? 0.0006f * sampleRate : 0f;
        var flutterDepth = tape ? 0.00012f * sampleRate : 0f;
        var wowStep = 2 * Math.PI * 0.55 * motor / sampleRate;
        var flutterStep = 2 * Math.PI * 6.3 * motor / sampleRate;

        var delayStep = (targetDelay - delaySamples) / n;
        var rightStep = (targetRight - rightSamples) / n;
        var mixStep = (mix - this.mix) / n;
        var delay = delaySamples;
        var delayRight = rightSamples;
        var currentMix = this.mix;
        var pingPong = type == PingPong;
        var offsetDepth = Math.Max(left1, right1) + Math.Max(left2, right2) + wowDepth + flutterDepth;
        for (var i = 0; i < n; i++) {
            delay += delayStep;
            delayRight += rightStep;
            currentMix += mixStep;
            lfo1.Advance(rate1, sampleRate);
            lfo2.Advance(rate2, sampleRate);
            lfo3.Advance(rate3, sampleRate);
            var tapeModulation = wowDepth * (float)Math.Sin(wowPhase) + flutterDepth * (float)Math.Sin(flutterPhase);
            wowPhase = Wrap(wowPhase + wowStep);
            flutterPhase = Wrap(flutterPhase + flutterStep);
            var modulationLeft = tapeModulation;
            var modulationRight = tapeModulation;
            if (depth1 > 0) {
                modulationLeft += left1 * lfo1.Value(shape1);
                modulationRight += right1 * lfo1.Value(shape1, phase1);
            }

            if (depth2 > 0) {
                modulationLeft += left2 * lfo2.Value(shape2);
                modulationRight += right2 * lfo2.Value(shape2, phase2);
            }

            // The offset keeps the modulated delay above the dry time, so modulation never reads ahead of it.
            var dl = Math.Max(3f, delay + modulationLeft + offsetDepth);
            var dr = dual
                ? Math.Max(3f, delayRight + modulationRight + offsetDepth)
                : ratio == 1f && modulationRight == modulationLeft
                    ? dl
                    : Math.Max(3f, delay * ratio + modulationRight + offsetDepth);

            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            float wetLeft, wetRight;
            switch (type) {
                case MonoTape: {
                    var head1 = left.Read(dl, loop);
                    var head2 = head2Ratio > 0 ? left.Read(Math.Max(3f, dl * head2Ratio), loop) : 0f;
                    var fed = left.Feedback(feedback * head1 + head2Feedback * head2, loop);
                    left.Write(0.5f * (dryLeft + dryRight) * inputGain + fed, loop);
                    head1 *= levelLeft;
                    head2 *= levelRight;
                    wetLeft = head1 * l1Left + head2 * l2Left;
                    wetRight = head1 * l1Right + head2 * l2Right;
                    break;
                }
                case Reverse: {
                    var chunk = Math.Clamp(delay, 4f, longestChunk);
                    var peak = Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight));
                    onsetEnvelope = peak > onsetEnvelope ? peak : onsetEnvelope * onsetRelease;
                    var onset = false;
                    if (onsetArmed && onsetEnvelope > onsetThreshold) {
                        onsetArmed = false;
                        onset = restart;
                    } else if (!onsetArmed && onsetEnvelope < 0.5f * onsetThreshold) {
                        onsetArmed = true;
                    }

                    runGain = (run ? 1f : 0f) + (runGain - (run ? 1f : 0f)) * runCoefficient;
                    if (!run && runGain < 1e-4f) {
                        // Stopped: the next Run starts a fresh chunk.
                        runGain = 0;
                        reversePosition = 0;
                        reverseFade = 0;
                    } else if (reversePosition >= chunk || onset) {
                        reverseOutgoing = reversePosition;
                        reversePosition = 0;
                        reverseFadeLength = reverseFade = crossfade * chunk;
                    }

                    wetLeft = runGain * ReverseHeads(left, loop);
                    wetRight = runGain * ReverseHeads(right, loop);
                    reversePosition += 1f;
                    reverseOutgoing += 1f;
                    reverseFade = Math.Max(0f, reverseFade - 1f);

                    left.Write(dryLeft * inputGain + left.Feedback(left.Read(chunk, loop), loop) * feedback, loop);
                    right.Write(dryRight * inputGain + right.Feedback(right.Read(chunk, loop), loop) * feedback, loop);
                    break;
                }
                default: {
                    wetLeft = left.Read(dl, loop);
                    wetRight = right.Read(dr, loop);
                    var fedLeft = left.Feedback(wetLeft, loop);
                    var fedRight = right.Feedback(wetRight, loop);
                    if (pingPong) {
                        left.Write(0.5f * (dryLeft + dryRight) * inputGain + fedRight * feedback, loop);
                        right.Write(fedLeft * feedback, loop);
                    } else if (dual) {
                        left.Write(dryLeft * inputGain + fedLeft * feedback + fedRight * crossToLeft, loop);
                        right.Write(dryRight * inputGain + fedRight * feedbackRight + fedLeft * crossToRight, loop);
                        wetLeft *= levelLeft;
                        wetRight *= levelRight;
                        (wetLeft, wetRight) = (wetLeft * l1Left + wetRight * l2Left, wetLeft * l1Right + wetRight * l2Right);
                    } else {
                        left.Write(dryLeft * inputGain + fedLeft * feedback, loop);
                        right.Write(dryRight * inputGain + fedRight * feedback, loop);
                    }

                    if (type == Sweep) {
                        // Coefficients move on a fixed internal hop, so the output does not depend on the host block size.
                        if (sweepCounter == 0) {
                            sweepLeftCoefficients = SweepCoefficients(sweepShape, sweepStart, sweepStop, sweepQ, 0, sampleRate);
                            sweepRightCoefficients =
                                SweepCoefficients(sweepShape, sweepStart, sweepStop, sweepQ, sweepPhase, sampleRate);
                        }

                        sweepCounter = (sweepCounter + 1) % SweepHop;
                        sweepLfo.Advance(sweepRate, sampleRate);
                        wetLeft = (float)sweepLeft.Tick(sweepLeftCoefficients, wetLeft);
                        wetRight = (float)sweepRight.Tick(sweepRightCoefficients, wetRight);
                    }

                    break;
                }
            }

            diffuser.Tick(ref wetLeft, ref wetRight);

            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            var pan = echoPan;
            var width = spread;
            if (depth3 > 0) {
                var value = lfo3.Value(shape3);
                switch (lfo3Target) {
                    case Lfo3Level:
                        var gain = 1f - depth3 * 0.5f * (1f - value);
                        wetLeft *= gain;
                        wetRight *= gain;
                        break;
                    case Lfo3Pan:
                        pan = Math.Clamp(pan + depth3 * value, -1f, 1f);
                        break;
                    default:
                        width *= 1f - depth3 * 0.5f * (1f - value);
                        break;
                }
            }

            Stereo.Spread(ref wetLeft, ref wetRight, width);
            if (pan != 0f) {
                var (toLeft, toRight) = Pan(pan);
                wetLeft *= toLeft;
                wetRight *= toRight;
            }

            postRight.Write(wetRight);
            if (postDelay >= 1f) {
                wetRight = postRight.Read(postDelay + 1f); // a delay of 1 is the sample just written
            }

            var (dryGain, wetGain) = MixGains(currentMix);
            io.Left[i] = dryLeft * dryGain + polarityLeft * wetLeft * wetGain;
            io.Right[i] = dryRight * dryGain + polarityRight * wetRight * wetGain;
        }

        delaySamples = targetDelay;
        rightSamples = targetRight;
        this.mix = mix;
        ducker.FlushDenormals();
        left.FlushDenormals();
        right.FlushDenormals();
        sweepLeft.FlushDenormals();
        sweepRight.FlushDenormals();
        if (onsetEnvelope < 1e-20f) {
            onsetEnvelope = 0;
        }

        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    /// <summary>The incoming and outgoing reverse heads of one line, crossfaded so the pair sums to unity.</summary>
    float ReverseHeads(Line line, in LoopSettings loop) {
        var incoming = line.Read(2f * reversePosition + 3f, loop);
        if (reverseFade <= 0f) {
            return incoming;
        }

        var outgoing = reverseFade / reverseFadeLength;
        return incoming + (line.Read(2f * reverseOutgoing + 3f, loop) - incoming) * outgoing;
    }

    /// <summary>The band-pass at the LFO's position, swept logarithmically from start (LFO -1) to stop (LFO +1).</summary>
    SvfCoefficients SweepCoefficients(LfoShape shape, float start, float stop, float q, float phase, float sampleRate) {
        var position = 0.5f * (sweepLfo.Value(shape, phase) + 1f);
        var frequency = start * MathF.Pow(stop / start, position);
        return SvfCoefficients.Design(BiquadType.BandPass, sampleRate, frequency, q);
    }

    /// <summary>The quantisation step for a word length of <paramref name="bits" /> (0 when 24 bits or more).</summary>
    static float Quantum(float bits) => bits >= 23.99f ? 0f : MathF.Pow(2f, 1f - MathF.Max(1f, bits));

    /// <summary>What the lines' feedback paths do this block.</summary>
    readonly record struct LoopSettings(LoopFilter Filter, bool Saturate, float Drive, float Quantum, bool Limit);

    sealed class Line : IDisposable {
        readonly FractionalDelayLine delay = new();
        LoopFilterState filter;

        public void Allocate(int capacity) => delay.Allocate(capacity);

        public void Dispose() => delay.Dispose();

        public void Reset() {
            delay.Reset();
            filter.Reset();
        }

        /// <summary>The repeat <paramref name="samples" /> back, bit-reduced if asked.</summary>
        public float Read(float samples, in LoopSettings loop) {
            var wet = delay.Read(samples);
            return loop.Quantum > 0 ? MathF.Round(wet / loop.Quantum) * loop.Quantum : wet;
        }

        /// <summary>The repeat through the feedback path's filters.</summary>
        public float Feedback(float wet, in LoopSettings loop) => loop.Filter.Active ? filter.Tick(loop.Filter, wet) : wet;

        public void Write(float value, in LoopSettings loop) {
            if (loop.Drive > 0) {
                var gain = 1f + loop.Drive;
                value = MathF.Tanh(value * gain) / gain;
            }

            if (loop.Saturate) {
                value = MathF.Tanh(value * 0.8f) / 0.8f; // BBD/tape headroom: repeats compress instead of building up
            }

            if (loop.Limit) {
                value = DelayBlock.Limit(value);
            }

            delay.Write(value);
        }

        public void FlushDenormals() => filter.FlushDenormals();
    }
}
