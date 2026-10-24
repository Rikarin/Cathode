using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Multitap delay: four delay voices in one of five arrangements, sharing a master Time (tempo-syncable) and Master
///     Feedback. Each voice has its own time (a percentage of the master time, or its own tempo), feedback, level (either
///     polarity), pan, filter, chorus, comb and ring modulator; two LFOs move every voice's time, a third the filters.
///     Reads use cubic Hermite interpolation, so integer delays are exact and time changes glide.
/// </summary>
/// <remarks>
///     <para>
///         Quad Tap: one line fed by the mono sum, read by four taps; each tap's processed signal can feed back into
///         the line. Quad Tape: the same with a tape transport (wow, flutter, saturation, a darker loop and a Motor
///         Speed). Quad Parallel: four independent lines, each feeding back into itself and, by its cross feedback, into
///         the next (1→2→3→4→1). Quad Series: the lines in a chain, each voice sending into the next at the Send
///         level; the last returns into the first by the Series Feedback, and the first three are heard at the Return
///         level. Quad Diffusor: four long Schroeder allpasses in parallel, each voice's feedback its allpass gain, so the
///         echoes smear into a diffuse wash with no colouration.
///     </para>
///     <para>
///         Each voice reads its line, then runs its filter (low-pass, high-pass, band-pass or peak, its frequency moved
///         by the filter LFO in octaves), a normalised feedback comb (peaks at unity, so it never adds gain), and a ring
///         modulator (a blend towards the signal times a sine). The processed voice is what feeds back, so its
///         filtering accumulates on every repeat. Every line write goes through the loop's low and high cut, the drive,
///         and the delay limiter, which is transparent below full scale and keeps resonant or over-unity loops bounded.
///         The master scalers multiply every voice's filter frequency and Q, chorus depth, comb feedback and ring mix,
///         and the LFOs' rate and depth.
///     </para>
///     <para>
///         The Quad Tap "band" variants are Quad Tap with band-pass voice filters. The ten-tap and rhythm delays are the
///         <see cref="TenTapBlock" />, the plex delays the <see cref="PlexBlock" />, and the reverse, sweep and ducking
///         delays types of the <see cref="DelayBlock" />.
///     </para>
/// </remarks>
public sealed class MultiDelayBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Time = 1;
    public const int Tempo = 2;
    public const int MasterFeedback = 3;
    public const int Tap1Time = 4;
    public const int Tap1Tempo = 8;
    public const int Tap1Feedback = 12;
    public const int Tap1Level = 16;
    public const int Tap1Pan = 20;
    public const int Cross1 = 24;
    public const int Filter1Type = 28;
    public const int Filter1Frequency = 32;
    public const int Filter1Q = 36;
    public const int Filter1Gain = 40;
    public const int Chorus1Rate = 44;
    public const int Chorus1Depth = 48;
    public const int Comb1Time = 52;
    public const int Comb1Feedback = 56;
    public const int Ring1Frequency = 60;
    public const int Ring1Mix = 64;
    public const int MasterPan = 68;
    public const int MasterLfoRate = 69;
    public const int MasterLfoDepth = 70;
    public const int MasterFilterFrequency = 71;
    public const int MasterFilterQ = 72;
    public const int MasterChorus = 73;
    public const int MasterComb = 74;
    public const int MasterRing = 75;
    public const int Drive = 76;
    public const int LowCut = 77;
    public const int HighCut = 78;
    public const int LowCutSlope = 79;
    public const int HighCutSlope = 80;
    public const int Lfo1Type = 81;
    public const int Lfo1Rate = 82;
    public const int Lfo1Tempo = 83;
    public const int Lfo1Depth = 84;
    public const int Lfo1Phase = 85;
    public const int Lfo2Type = 86;
    public const int Lfo2Rate = 87;
    public const int Lfo2Tempo = 88;
    public const int Lfo2Depth = 89;
    public const int Lfo2Phase = 90;
    public const int FilterLfoType = 91;
    public const int FilterLfoRate = 92;
    public const int FilterLfoDepth = 93;
    public const int DiffusionMix = 94;
    public const int DiffusionTime = 95;
    public const int DiffusionRate = 96;
    public const int DiffusionDepth = 97;
    public const int DuckThreshold = 98;
    public const int DuckAttenuation = 99;
    public const int DuckRelease = 100;
    public const int EnvelopeThreshold = 101;
    public const int EnvelopeAttack = 102;
    public const int EnvelopeRelease = 103;
    public const int SeriesFeedback = 104;
    public const int SeriesSend = 105;
    public const int SeriesReturn = 106;
    public const int MotorSpeed = 107;
    public const int Mix = 108;
    public const int Level = 109;
    public const int Balance = 110;
    public const int InputGain = 111;
    public const int InputSelect = 112;
    public const int GlobalMix = 113;

    public const int QuadTap = 0;
    public const int QuadParallel = 1;
    public const int QuadSeries = 2;
    public const int QuadDiffusor = 3;
    public const int QuadTape = 4;
    public const int TypeCount = 5;

    /// <summary>Voice filter types.</summary>
    public const int FilterOff = 0;

    public const int FilterLowPass = 1;
    public const int FilterHighPass = 2;
    public const int FilterBandPass = 3;
    public const int FilterPeak = 4;

    public const float MaxTimeMs = 2_000f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;
    public const float MaxCombMs = 20f;
    public const float MaxCombFeedback = 0.95f;
    public const float MaxDiffusorGain = 0.9f;
    public const float MaxDiffusionMs = 100f;
    public const int Voices = 4;

    const int FilterHop = 16;
    const float MaxChorusMs = 10f;
    const float MaxLfoMs = 10f;
    const float MaxScale = 4f;

    static readonly string[] VoiceFilterTypes = ["Off", "Low Pass", "High Pass", "Band Pass", "Peak"];

    readonly FractionalDelayLine[] lines = [new(), new(), new(), new()];
    readonly FractionalDelayLine[] combs = [new(), new(), new(), new()];
    readonly LoopFilterState[] loopState = new LoopFilterState[Voices];
    readonly SvfState[] filterState = new SvfState[Voices];
    readonly SvfCoefficients[] filterCoefficients = new SvfCoefficients[Voices];
    readonly float[] tapDelay = new float[Voices];
    readonly float[] tapTarget = new float[Voices];
    readonly float[] tapStep = new float[Voices];
    readonly float[] tapLeft = new float[Voices];
    readonly float[] tapRight = new float[Voices];
    readonly float[] feedback = new float[Voices];
    readonly float[] cross = new float[Voices];
    readonly int[] filterType = new int[Voices];
    readonly float[] filterFrequency = new float[Voices];
    readonly float[] filterQ = new float[Voices];
    readonly float[] filterGain = new float[Voices];
    readonly float[] chorusRate = new float[Voices];
    readonly float[] chorusDepth = new float[Voices];
    readonly double[] chorusPhase = new double[Voices];
    readonly float[] combDelay = new float[Voices];
    readonly float[] combFeedback = new float[Voices];
    readonly float[] ringStep = new float[Voices];
    readonly float[] ringMix = new float[Voices];
    readonly double[] ringPhase = new double[Voices];
    readonly float[] voice = new float[Voices];
    readonly Diffuser diffuser = new();
    int activeType = -1;
    int capacity;
    float mix;
    Lfo lfo1;
    Lfo lfo2;
    Lfo filterLfo;
    int filterCounter;
    double wowPhase;
    double flutterPhase;
    Ducker ducker;
    SwellEnvelope envelope;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "multidelay",
        "Multitap Delay",
        "Delay",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Quad Tap", "Quad Parallel", "Quad Series", "Quad Diffusor", "Quad Tape"]
            ),
            new(
                "time",
                "Master Time",
                ParamKind.Float,
                1f,
                MaxTimeMs,
                400f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ),
            ParamDescriptor.Choice("tempo", "Master Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("masterFeedback", "Master Feedback", 0f, 200f, 100f, "%"),
            ..Voice(k => ParamDescriptor.Float($"tap{k + 1}Time", $"Time {k + 1}", 0f, 100f, 25f * (k + 1), "%")),
            ..Voice(k => ParamDescriptor.Choice($"tap{k + 1}Tempo", $"Tempo {k + 1}", TempoDivisions.Names)),
            ..Voice(k => ParamDescriptor.Float(
                    $"tap{k + 1}Feedback",
                    $"Feedback {k + 1}",
                    -100f,
                    100f,
                    k == 3 ? 20f : 0f,
                    "%"
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                    $"tap{k + 1}Level",
                    $"Level {k + 1}",
                    -100f,
                    100f,
                    100f * Decibels.ToGain(-3f * k),
                    "%"
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                    $"tap{k + 1}Pan",
                    $"Pan {k + 1}",
                    -100f,
                    100f,
                    (k % 2 == 0 ? -1f : 1f) * (k < 2 ? 100f : 50f),
                    "%"
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                $"cross{k + 1}",
                $"Cross Feedback {k + 1}→{(k + 1) % Voices + 1}",
                -100f,
                100f,
                0f,
                "%"
                )
                .ShownOn(TypeCount, QuadParallel, QuadDiffusor)
            ),
            ..Voice(k => ParamDescriptor.Choice($"filter{k + 1}Type", $"Filter {k + 1} Type", VoiceFilterTypes)),
            ..Voice(k => ParamDescriptor.Float(
                    $"filter{k + 1}Frequency",
                    $"Filter {k + 1} Freq",
                    50f,
                    15_000f,
                    500f * (1 << k),
                    "Hz",
                    ParamTaper.Log
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                    $"filter{k + 1}Q",
                    $"Filter {k + 1} Q",
                    0.1f,
                    10f,
                    0.7071f,
                    string.Empty,
                    ParamTaper.Log
                )
            ),
            ..Voice(k => ParamDescriptor.Float($"filter{k + 1}Gain", $"Filter {k + 1} Gain", -12f, 12f, 0f, "dB")),
            ..Voice(k => ParamDescriptor.Float(
                    $"chorus{k + 1}Rate",
                    $"Chorus {k + 1} Rate",
                    0.05f,
                    10f,
                    0.5f + 0.13f * k,
                    "Hz",
                    ParamTaper.Log
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                    $"chorus{k + 1}Depth",
                    $"Chorus {k + 1} Depth",
                    0f,
                    MaxChorusMs,
                    0f,
                    "ms"
                )
            ),
            ..Voice(k => ParamDescriptor.Float(
                    $"comb{k + 1}Time",
                    $"Comb {k + 1} Time",
                    0.1f,
                    MaxCombMs,
                    2f + 1.5f * k,
                    "ms",
                    ParamTaper.Log
                )
            ),
            ..Voice(k => ParamDescriptor.Float($"comb{k + 1}Feedback", $"Comb {k + 1} Feedback", -95f, 95f, 0f, "%")),
            ..Voice(k => ParamDescriptor.Float(
                    $"ring{k + 1}Frequency",
                    $"Ring Mod {k + 1} Freq",
                    1f,
                    5_000f,
                    100f * (k + 1),
                    "Hz",
                    ParamTaper.Log
                )
            ),
            ..Voice(k => ParamDescriptor.Float($"ring{k + 1}Mix", $"Ring Mod {k + 1} Mix", 0f, 100f, 0f, "%")),
            ParamDescriptor.Float("masterPan", "Master Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("masterLfoRate", "Master LFO Rate", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("masterLfoDepth", "Master LFO Depth", 0f, 400f, 100f, "%"),
            ParamDescriptor.Float("masterFilterFrequency", "Master Filter Freq", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("masterFilterQ", "Master Filter Q", 25f, 400f, 100f, "%", ParamTaper.Log),
            ParamDescriptor.Float("masterChorus", "Master Chorus", 0f, 400f, 100f, "%"),
            ParamDescriptor.Float("masterComb", "Master Comb", 0f, 200f, 100f, "%"),
            ParamDescriptor.Float("masterRing", "Master Ring Mod", 0f, 200f, 100f, "%"),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 0f),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lowCutSlope", "Low Cut Slope", [..CutFilter.Slopes], CutFilter.Slope12),
            ParamDescriptor.Choice("highCutSlope", "High Cut Slope", [..CutFilter.Slopes], CutFilter.Slope12),
            ParamDescriptor.Choice("lfo1Type", "LFO 1 Type", Lfo.ShapeNames),
            ParamDescriptor.Float("lfo1Rate", "LFO 1 Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfo1Tempo", "LFO 1 Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfo1Depth", "LFO 1 Depth", 0f, MaxLfoMs, 0f, "ms"),
            ParamDescriptor.Float("lfo1Phase", "LFO 1 Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Choice("lfo2Type", "LFO 2 Type", Lfo.ShapeNames, (int)LfoShape.Triangle),
            ParamDescriptor.Float("lfo2Rate", "LFO 2 Rate", 0.05f, 10f, 2f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfo2Tempo", "LFO 2 Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfo2Depth", "LFO 2 Depth", 0f, MaxLfoMs, 0f, "ms"),
            ParamDescriptor.Float("lfo2Phase", "LFO 2 Phase", 0f, 180f, 90f, "°"),
            ParamDescriptor.Choice("filterLfoType", "Filter LFO Type", Lfo.ShapeNames),
            ParamDescriptor.Float("filterLfoRate", "Filter LFO Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("filterLfoDepth", "Filter LFO Depth", 0f, 4f, 0f, "oct"),
            ParamDescriptor.Float("diffusionMix", "Diffusion Mix", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("diffusionTime", "Diffusion Time", 1f, MaxDiffusionMs, 30f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("diffusionRate", "Diffusion LFO Rate", 0.05f, 5f, 0.4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("diffusionDepth", "Diffusion LFO Depth", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("envelopeThreshold", "Envelope Threshold", -80f, 0f, -40f, "dB"),
            ParamDescriptor.Float("envelopeAttack", "Envelope Attack", 0f, 2_000f, 0f, "ms"),
            ParamDescriptor.Float("envelopeRelease", "Envelope Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("seriesFeedback", "Series Feedback", -100f, 100f, 0f, "%")
                .ShownOn(TypeCount, QuadSeries),
            ParamDescriptor.Float("seriesSend", "Series Send", 0f, 100f, 100f, "%").ShownOn(TypeCount, QuadSeries),
            ParamDescriptor.Float("seriesReturn", "Series Return", 0f, 100f, 100f, "%").ShownOn(TypeCount, QuadSeries),
            ParamDescriptor.Float("motorSpeed", "Motor Speed", 25f, 200f, 100f, "%").ShownOn(TypeCount, QuadTape),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 25f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            EffectsMix.Param
        ],
        maxInstances: 2
    );

    static IEnumerable<ParamDescriptor> Voice(Func<int, ParamDescriptor> parameter) =>
        Enumerable.Range(0, Voices).Select(parameter);

    /// <summary>Gains of the balance pan law: unity on both sides at centre, the far side fading to zero.</summary>
    static (float Left, float Right) Pan(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    float Milliseconds(int tempoParam, float timeMs, double tempo) {
        var division = TempoDivisions.Milliseconds((int)Params.Current(tempoParam), tempo);
        return division > 0 ? Math.Min(MaxTimeMs, (float)division) : timeMs;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        // The longest voice plus the widest modulation (chorus and both LFOs at the master scalers' top, kept positive).
        var margin = MaxScale * (MaxChorusMs + 2 * MaxLfoMs) * 2 + 20;
        capacity = (int)(spec.SampleRate * (MaxTimeMs + margin) / 1000) + 8;
        for (var i = 0; i < Voices; i++) {
            lines[i].Allocate(capacity);
            combs[i].Allocate((int)(spec.SampleRate * MaxCombMs / 1000) + 8);
        }

        diffuser.Allocate(spec.SampleRate, MaxDiffusionMs);
    }

    protected override void OnReset() {
        activeType = (int)Params.Current(Type);
        ClearState();
        ComputeTapTargets(activeType, Milliseconds(Tempo, Params.Current(Time), 120), 120, 1f);
        Array.Copy(tapTarget, tapDelay, Voices);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var line in lines) {
                line.Dispose();
            }

            foreach (var comb in combs) {
                comb.Dispose();
            }

            diffuser.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var sampleRate = (float)Spec.SampleRate;
        var tempo = context.Tempo;
        var type = (int)Params.Current(Type);
        var masterTime = Milliseconds(Tempo, Params.Advance(Time, n), tempo);
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var masterFeedback = Params.Advance(MasterFeedback, n) / 100f;
        var motorSpeed = Params.Advance(MotorSpeed, n) / 100f;
        var motor = type == QuadTape ? motorSpeed : 1f;
        for (var i = Tap1Time; i < MasterPan; i++) {
            Params.Advance(i, n); // the voice parameters are read back below
        }

        var masterPan = Params.Advance(MasterPan, n) / 100f;
        var lfoRateScale = Params.Advance(MasterLfoRate, n) / 100f;
        var lfoDepthScale = Params.Advance(MasterLfoDepth, n) / 100f;
        var filterFrequencyScale = Params.Advance(MasterFilterFrequency, n) / 100f;
        var filterQScale = Params.Advance(MasterFilterQ, n) / 100f;
        var chorusScale = Params.Advance(MasterChorus, n) / 100f;
        var combScale = Params.Advance(MasterComb, n) / 100f;
        var ringScale = Params.Advance(MasterRing, n) / 100f;

        if (type != activeType) {
            // The type that takes over starts empty and at its own tap times rather than gliding from the old ones.
            activeType = type;
            ClearState();
            ComputeTapTargets(type, masterTime, tempo, motor);
            Array.Copy(tapTarget, tapDelay, Voices);
        }

        ComputeTapTargets(type, masterTime, tempo, motor);
        for (var k = 0; k < Voices; k++) {
            var (left, right) = Pan(Math.Clamp(Params.Current(Tap1Pan + k) / 100f + masterPan, -1f, 1f));
            var level = Params.Current(Tap1Level + k) / 100f;
            tapLeft[k] = level * left;
            tapRight[k] = level * right;
            feedback[k] = Params.Current(Tap1Feedback + k) / 100f * masterFeedback;
            cross[k] = Params.Current(Cross1 + k) / 100f * masterFeedback;
            filterType[k] = (int)Params.Current(Filter1Type + k);
            filterFrequency[k] = Params.Current(Filter1Frequency + k) * filterFrequencyScale;
            filterQ[k] = Params.Current(Filter1Q + k) * filterQScale;
            filterGain[k] = Params.Current(Filter1Gain + k);
            chorusRate[k] = Params.Current(Chorus1Rate + k);
            chorusDepth[k] = Params.Current(Chorus1Depth + k) * chorusScale * 0.001f * sampleRate;
            combDelay[k] = Params.Current(Comb1Time + k) * 0.001f * sampleRate;
            combFeedback[k] = Math.Clamp(
                Params.Current(Comb1Feedback + k) / 100f * combScale,
                -MaxCombFeedback,
                MaxCombFeedback
            );
            ringStep[k] = 2f * MathF.PI * Params.Current(Ring1Frequency + k) / sampleRate;
            ringMix[k] = Math.Min(1f, Params.Current(Ring1Mix + k) / 100f * ringScale);
        }

        var drive = Params.Advance(Drive, n);
        var tape = type == QuadTape;
        var loop = LoopFilter.Design(
            sampleRate,
            Params.Advance(LowCut, n),
            (int)Params.Current(LowCutSlope),
            LoopFilter.ButterworthQ,
            Math.Min(Params.Advance(HighCut, n), tape ? 6_500f * motor : HighCutOff),
            (int)Params.Current(HighCutSlope),
            LoopFilter.ButterworthQ
        );

        var shape1 = (LfoShape)(int)Params.Current(Lfo1Type);
        var shape2 = (LfoShape)(int)Params.Current(Lfo2Type);
        var rate1 = lfoRateScale
            * TempoDivisions.Rate((int)Params.Current(Lfo1Tempo), tempo, Params.Advance(Lfo1Rate, n));
        var rate2 = lfoRateScale
            * TempoDivisions.Rate((int)Params.Current(Lfo2Tempo), tempo, Params.Advance(Lfo2Rate, n));
        var depth1 = Params.Advance(Lfo1Depth, n) * lfoDepthScale * 0.001f * sampleRate;
        var depth2 = Params.Advance(Lfo2Depth, n) * lfoDepthScale * 0.001f * sampleRate;
        var phase1 = Params.Advance(Lfo1Phase, n) / 360.0;
        var phase2 = Params.Advance(Lfo2Phase, n) / 360.0;
        var filterShape = (LfoShape)(int)Params.Current(FilterLfoType);
        var filterRate = Params.Advance(FilterLfoRate, n);
        var filterDepth = Params.Advance(FilterLfoDepth, n);
        diffuser.Configure(
            Params.Advance(DiffusionMix, n) / 100f,
            Params.Advance(DiffusionTime, n),
            Params.Advance(DiffusionRate, n),
            Params.Advance(DiffusionDepth, n) / 100f
        );
        ducker.Configure(
            sampleRate,
            Params.Advance(DuckAttenuation, n),
            Params.Advance(DuckThreshold, n),
            Params.Advance(DuckRelease, n)
        );
        envelope.Configure(
            sampleRate,
            Params.Advance(EnvelopeThreshold, n),
            Params.Advance(EnvelopeAttack, n),
            Params.Advance(EnvelopeRelease, n)
        );
        var seriesFeedback = Params.Advance(SeriesFeedback, n) / 100f * masterFeedback;
        var seriesSend = Params.Advance(SeriesSend, n) / 100f;
        var seriesReturn = Params.Advance(SeriesReturn, n) / 100f;

        var filterMoving = filterDepth > 0;
        if (!filterMoving) {
            DesignFilters(sampleRate, 1f);
        }

        // Tape transport: slow wow plus faster flutter, a few tenths of a millisecond deep, faster on a faster tape.
        var wowDepth = tape ? 0.0006f * sampleRate : 0f;
        var flutterDepth = tape ? 0.00012f * sampleRate : 0f;
        var wowStep = 2 * Math.PI * 0.55 * motor / sampleRate;
        var flutterStep = 2 * Math.PI * 6.3 * motor / sampleRate;
        var anyChorus = false;
        for (var k = 0; k < Voices; k++) {
            anyChorus |= chorusDepth[k] > 0;
        }

        var modulated = anyChorus || depth1 > 0 || depth2 > 0 || tape;
        var offsetDepth = depth1 + depth2 + wowDepth + flutterDepth;

        for (var k = 0; k < Voices; k++) {
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
            var x = Stereo.Select(select, dryLeft, dryRight) * inputGain;
            if (envelope.Active) {
                x *= envelope.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
            }

            lfo1.Advance(rate1, sampleRate);
            lfo2.Advance(rate2, sampleRate);
            if (filterMoving) {
                // Coefficients move on a fixed internal hop, so the output does not depend on the host block size.
                if (filterCounter == 0) {
                    DesignFilters(sampleRate, MathF.Pow(2f, filterDepth * filterLfo.Value(filterShape)));
                }

                filterCounter = (filterCounter + 1) % FilterHop;
                filterLfo.Advance(filterRate, sampleRate);
            }

            var tapeModulation = 0f;
            if (tape) {
                tapeModulation = wowDepth * (float)Math.Sin(wowPhase) + flutterDepth * (float)Math.Sin(flutterPhase);
                wowPhase = Wrap(wowPhase + wowStep);
                flutterPhase = Wrap(flutterPhase + flutterStep);
            }

            // Read and process every voice.
            float wetLeft = 0, wetRight = 0;
            for (var k = 0; k < Voices; k++) {
                tapDelay[k] += tapStep[k];
                var delay = tapDelay[k];
                if (modulated) {
                    var modulation = tapeModulation + offsetDepth;
                    if (depth1 > 0) {
                        modulation += depth1 * lfo1.Value(shape1, k * phase1);
                    }

                    if (depth2 > 0) {
                        modulation += depth2 * lfo2.Value(shape2, k * phase2);
                    }

                    if (chorusDepth[k] > 0) {
                        modulation += chorusDepth[k] * (1f + (float)Math.Sin(chorusPhase[k]));
                    }

                    delay += modulation;
                }

                if (anyChorus) {
                    chorusPhase[k] = Wrap(chorusPhase[k] + 2 * Math.PI * chorusRate[k] * lfoRateScale / sampleRate);
                }

                var line = type is QuadTap or QuadTape ? lines[0] : lines[k];
                voice[k] = Process(k, line.Read(delay));
                if (type == QuadDiffusor) {
                    continue; // heard once the allpass has been applied, below
                }

                // Quad Series: the first three voices are heard at the Return level.
                var gain = type == QuadSeries && k < Voices - 1 ? seriesReturn : 1f;
                wetLeft += voice[k] * tapLeft[k] * gain;
                wetRight += voice[k] * tapRight[k] * gain;
            }

            // Write the lines.
            switch (type) {
                case QuadParallel:
                    for (var k = 0; k < Voices; k++) {
                        var previous = (k + Voices - 1) % Voices;
                        Write(k, x + feedback[k] * voice[k] + cross[previous] * voice[previous], loop, drive, false);
                    }

                    break;
                case QuadSeries:
                    Write(0, x + feedback[0] * voice[0] + seriesFeedback * voice[Voices - 1], loop, drive, false);
                    for (var k = 1; k < Voices; k++) {
                        Write(k, seriesSend * voice[k - 1] + feedback[k] * voice[k], loop, drive, false);
                    }

                    break;
                case QuadDiffusor:
                    // Each line is a Schroeder allpass: the voice heard is delayed - g · node, the node written is input + g · delayed.
                    for (var k = 0; k < Voices; k++) {
                        var previous = (k + Voices - 1) % Voices;
                        var gain = Math.Clamp(feedback[k], -MaxDiffusorGain, MaxDiffusorGain);
                        var delayed = voice[k];
                        var node = x + cross[previous] * voice[previous] + gain * delayed;
                        Write(k, node, loop, drive, false);
                        voice[k] = delayed - gain * node;
                    }

                    break;
                default: {
                    var fed = x;
                    for (var k = 0; k < Voices; k++) {
                        if (feedback[k] != 0) {
                            fed += feedback[k] * voice[k];
                        }
                    }

                    Write(0, fed, loop, drive, tape);
                    break;
                }
            }

            if (type == QuadDiffusor) {
                // The allpass outputs replace the plain reads in the wet sum.
                wetLeft = 0;
                wetRight = 0;
                for (var k = 0; k < Voices; k++) {
                    wetLeft += voice[k] * tapLeft[k];
                    wetRight += voice[k] * tapRight[k];
                }
            }

            diffuser.Tick(ref wetLeft, ref wetRight);
            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            io.Left[i] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[i] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        Array.Copy(tapTarget, tapDelay, Voices);
        this.mix = mix;
        FlushDenormals();

        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI : phase;

    /// <summary>A voice's filter, comb and ring modulator.</summary>
    float Process(int k, float y) {
        if (filterType[k] != FilterOff) {
            y = (float)filterState[k].Tick(filterCoefficients[k], y);
        }

        if (combFeedback[k] != 0) {
            // Normalised feedback comb: the peaks stay at unity whatever the feedback.
            var g = combFeedback[k];
            y = (1f - MathF.Abs(g)) * y + g * combs[k].Read(combDelay[k]);
            combs[k].Write(y);
        }

        if (ringMix[k] > 0) {
            y *= 1f - ringMix[k] + ringMix[k] * (float)Math.Sin(ringPhase[k]);
            ringPhase[k] = Wrap(ringPhase[k] + ringStep[k]);
        }

        return y;
    }

    void Write(int line, float value, in LoopFilter loop, float drive, bool tape) {
        if (loop.Active) {
            value = loopState[line].Tick(loop, value);
        }

        if (drive > 0) {
            var gain = 1f + drive;
            value = MathF.Tanh(value * gain) / gain;
        }

        if (tape) {
            value = MathF.Tanh(value * 0.8f) / 0.8f; // tape headroom: repeats compress instead of building up
        }

        lines[line].Write(DelayBlock.Limit(value));
    }

    void DesignFilters(float sampleRate, float frequencyScale) {
        for (var k = 0; k < Voices; k++) {
            if (filterType[k] == FilterOff) {
                continue;
            }

            var type = filterType[k] switch {
                FilterLowPass => BiquadType.LowPass,
                FilterHighPass => BiquadType.HighPass,
                FilterBandPass => BiquadType.BandPass,
                _ => BiquadType.Peak
            };
            filterCoefficients[k] = SvfCoefficients.Design(
                type,
                sampleRate,
                filterFrequency[k] * frequencyScale,
                filterQ[k],
                filterGain[k]
            );
        }
    }

    void ClearState() {
        for (var k = 0; k < Voices; k++) {
            lines[k].Reset();
            combs[k].Reset();
            loopState[k].Reset();
            filterState[k].Reset();
            chorusPhase[k] = 0;
            ringPhase[k] = 0;
            voice[k] = 0;
        }

        diffuser.Clear();
        ducker.Reset();
        envelope.Reset();
        lfo1.Reset();
        lfo2.Reset();
        filterLfo.Reset();
        filterCounter = 0;
        wowPhase = 0;
        flutterPhase = 0;
    }

    /// <summary>The voices' read delays in samples: a percentage of the master time, or the voice's own tempo.</summary>
    void ComputeTapTargets(int type, float masterTimeMs, double tempo, float motor) {
        var longest = capacity
            - 8
            - (MaxScale * (MaxChorusMs + 2 * MaxLfoMs) * 2 + 20) * 0.001f * (float)Spec.SampleRate;
        for (var k = 0; k < Voices; k++) {
            var ms = Milliseconds(Tap1Tempo + k, masterTimeMs * Params.Current(Tap1Time + k) / 100f, tempo);
            var samples = ms * 0.001f * (float)Spec.SampleRate;
            tapTarget[k] = Math.Min(longest, type == QuadTape && motor != 1f ? samples / motor : samples);
        }
    }

    void FlushDenormals() {
        for (var k = 0; k < Voices; k++) {
            loopState[k].FlushDenormals();
            filterState[k].FlushDenormals();
        }

        ducker.FlushDenormals();
        envelope.FlushDenormals();
    }
}
