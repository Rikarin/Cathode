using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Plex delay: up to eight delay lines cross-fed through an orthogonal feedback matrix, so every echo spawns echoes
///     on the other lines and the pattern thickens into a wash. Five types share the network:
///     <list type="bullet">
///         <item>Plex Delay: the lines alone.</item>
///         <item>Plex Detune: each line detuned by a few cents in the loop, so the wash beats and spreads.</item>
///         <item>Plex Shifter: each line shifted by its interval, so the repeats climb or fall in pitch.</item>
///         <item>Plex Verb: short lines scaled by Size, dense diffusion: a reverb built from the same network.</item>
///         <item>Shimmer Verb: Plex Verb with an octave-up shifter in every loop, blended in by Shimmer Intensity.</item>
///     </list>
/// </summary>
/// <remarks>
///     <para>
///         Each line has a time (or tempo) scaled by Master Time, a level and a pan. A line's loop gain is set from the
///         Decay time, so its repeats fall by 60 dB in Decay seconds whatever its length. The feedback matrix is the
///         Householder reflection <c>I - 2/N · 11ᵀ</c>, orthogonal for any number of lines, so the network's energy
///         decays only by the loop gains. A Schroeder allpass in each loop (gain from Diffusion) smears every pass; its
///         length is taken out of the line, so with no diffusion a line's first echo arrives exactly at its time.
///     </para>
///     <para>
///         The loop also holds the low and high cut and a filter (low-pass, high-pass, band-pass or peak) whose
///         frequency an LFO moves by Mod Frequency octaves. The input passes an envelope swell, a pre-delay and an input
///         diffuser (four allpasses) before it enters every line at 1/√N. The shifters are granular
///         (<see cref="GrainShifter" />): Granule sets their grain, Crossfade how much of each grain fades, and Direction
///         Reverse plays the grains backwards. Hold loops the network at unity with the input closed, Stack with it open;
///         the delay limiter keeps every loop bounded.
///     </para>
/// </remarks>
public sealed class PlexBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Delays = 1;
    public const int MasterTime = 2;
    public const int Decay = 3;
    public const int Diffusion = 4;
    public const int InputDiffusion = 5;
    public const int PreDelay = 6;
    public const int LowCut = 7;
    public const int HighCut = 8;
    public const int Hold = 9;
    public const int MasterPan = 10;
    public const int Time1 = 11;
    public const int Tempo1 = 19;
    public const int Level1 = 27;
    public const int Pan1 = 35;
    public const int FilterType = 43;
    public const int FilterFrequency = 44;
    public const int FilterQ = 45;
    public const int FilterGain = 46;
    public const int FilterLfoRate = 47;
    public const int FilterLfoTempo = 48;
    public const int ModFrequency = 49;
    public const int EnvelopeThreshold = 50;
    public const int EnvelopeAttack = 51;
    public const int EnvelopeRelease = 52;
    public const int Detune1 = 53;
    public const int MasterDetune = 61;
    public const int Granule = 62;
    public const int Shift1 = 63;
    public const int Direction = 71;
    public const int Crossfade = 72;
    public const int VerbSize = 73;
    public const int ShimmerIntensity = 74;
    public const int LfoRate = 75;
    public const int LfoTempo = 76;
    public const int LfoDepth = 77;
    public const int DuckAttenuation = 78;
    public const int DuckThreshold = 79;
    public const int DuckRelease = 80;
    public const int Spread = 81;
    public const int Mix = 82;
    public const int Level = 83;
    public const int Balance = 84;
    public const int InputGain = 85;
    public const int InputSelect = 86;
    public const int KillDry = 87;

    public const int PlexDelay = 0;
    public const int PlexDetune = 1;
    public const int PlexShifter = 2;
    public const int PlexVerb = 3;
    public const int ShimmerVerb = 4;
    public const int TypeCount = 5;

    public const int HoldOff = 0;
    public const int HoldOn = 1;
    public const int Stack = 2;

    public const int FilterOff = 0;

    public const int MaxLines = 8;
    public const float MaxLineMs = 3_000f;
    public const float MaxTotalMs = 4_000f;
    public const float MaxPreDelayMs = 1_000f;
    public const float MaxGranuleMs = 200f;
    public const float MaxDiffusionGain = 0.7f;
    public const float LowCutOff = 20f;
    public const float HighCutOff = 20_000f;

    const float HoldDecaySeconds = 1e6f;
    const float MaxLfoMs = 10f;
    const int FilterHop = 16;

    /// <summary>Loop allpass lengths in ms; taken out of the line lengths.</summary>
    public static ReadOnlySpan<float> AllpassMs => [3.7f, 5.3f, 7.9f, 10.1f, 4.3f, 6.1f, 8.9f, 11.3f];

    /// <summary>Plex Verb line lengths in ms at Size 50 %.</summary>
    public static ReadOnlySpan<float> VerbMs => [31.1f, 37.3f, 41.9f, 47.3f, 53.9f, 59.3f, 64.7f, 71.9f];

    static readonly float[] InputDiffuserMs = [4.77f, 3.59f, 12.73f, 9.30f];
    static readonly float[] DefaultTimes = [143f, 197f, 263f, 331f, 389f, 457f, 521f, 587f];
    static readonly float[] DefaultPans = [-100f, 100f, -75f, 75f, -50f, 50f, -25f, 25f];
    static readonly float[] DefaultDetune = [-11f, 9f, -7f, 5f, -5f, 7f, -9f, 11f];
    static readonly float[] DefaultShifts = [12f, -12f, 7f, -5f, 12f, 19f, -7f, 5f];

    readonly FractionalDelayLine[] lines =
        [..Enumerable.Range(0, MaxLines).Select(static _ => new FractionalDelayLine())];

    readonly ReverbLine[] allpasses = [..Enumerable.Range(0, MaxLines).Select(static _ => new ReverbLine())];
    readonly ReverbLine[] inputDiffusers = [new(), new(), new(), new()];
    readonly GrainShifter[] shifters = [..Enumerable.Range(0, MaxLines).Select(static _ => new GrainShifter())];
    readonly FractionalDelayLine preDelay = new();
    readonly LoopFilterState[] loopState = new LoopFilterState[MaxLines];
    readonly SvfState[] filterState = new SvfState[MaxLines];
    readonly int[] allpassLength = new int[MaxLines];
    readonly int[] inputDiffuserLength = new int[4];
    readonly float[] lineDelay = new float[MaxLines];
    readonly float[] lineTarget = new float[MaxLines];
    readonly float[] lineStep = new float[MaxLines];
    readonly float[] gains = new float[MaxLines];
    readonly float[] speeds = new float[MaxLines];
    readonly float[] tapLeft = new float[MaxLines];
    readonly float[] tapRight = new float[MaxLines];
    readonly float[] outputs = new float[MaxLines];
    int activeType = -1;
    int activeLines = -1;
    float mix;
    Lfo lfo;
    Lfo filterLfo;
    int filterCounter;
    SvfCoefficients filterCoefficients;
    Ducker ducker;
    SwellEnvelope envelope;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "plex",
        "Plex Delay",
        "Delay",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Plex Delay", "Plex Detune", "Plex Shifter", "Plex Verb", "Shimmer Verb"]
            ),
            new("delays", "Number of Delays", ParamKind.Float, 1f, MaxLines, MaxLines, Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("masterTime", "Master Time", 10f, 200f, 100f, "%")
                .HiddenOn(TypeCount, PlexVerb, ShimmerVerb),
            ParamDescriptor.Float("decay", "Decay Time", 0.1f, 30f, 2f, "s", ParamTaper.Log),
            ParamDescriptor.Float("diffusion", "Diffusion", 0f, 100f, 40f, "%"),
            ParamDescriptor.Float("inputDiffusion", "Input Diffusion", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("preDelay", "Pre-Delay", 0f, MaxPreDelayMs, 0f, "ms"),
            ParamDescriptor.Float("lowCut", "Low Cut", LowCutOff, 2_000f, 80f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highCut", "High Cut", 500f, HighCutOff, 8_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("hold", "Stack/Hold", ["Off", "Hold", "Stack"]),
            ParamDescriptor.Float("masterPan", "Master Pan", -100f, 100f, 0f, "%"),
            ..Line(static i => new ParamDescriptor(
                    $"time{i + 1}",
                    $"Delay Time {i + 1}",
                    ParamKind.Float,
                    1f,
                    MaxLineMs,
                    DefaultTimes[i],
                    "ms",
                    ParamTaper.Log,
                    ParamSmoothing.Linear,
                    60f
                )
                    .HiddenOn(TypeCount, PlexVerb, ShimmerVerb)
            ),
            ..Line(static i => ParamDescriptor.Choice($"tempo{i + 1}", $"Tempo {i + 1}", TempoDivisions.Names)
                .HiddenOn(TypeCount, PlexVerb, ShimmerVerb)
            ),
            ..Line(static i => ParamDescriptor.Float($"level{i + 1}", $"Level {i + 1}", 0f, 100f, 100f, "%")),
            ..Line(static i => ParamDescriptor.Float($"pan{i + 1}", $"Pan {i + 1}", -100f, 100f, DefaultPans[i], "%")),
            ParamDescriptor.Choice("filterType", "Filter Type", ["Off", "Low Pass", "High Pass", "Band Pass", "Peak"]),
            ParamDescriptor.Float("filterFrequency", "Filter Freq", 50f, 15_000f, 2_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("filterQ", "Filter Q", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("filterGain", "Filter Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("filterLfoRate", "Filter LFO Rate", 0.05f, 10f, 0.5f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("filterLfoTempo", "Filter LFO Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("modFrequency", "Mod Frequency", 0f, 4f, 0f, "oct"),
            ParamDescriptor.Float("envelopeThreshold", "Envelope Threshold", -80f, 0f, -40f, "dB"),
            ParamDescriptor.Float("envelopeAttack", "Envelope Attack", 0f, 2_000f, 0f, "ms"),
            ParamDescriptor.Float("envelopeRelease", "Envelope Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ..Line(static i => ParamDescriptor.Float(
                $"detune{i + 1}",
                $"Detune {i + 1}",
                -50f,
                50f,
                DefaultDetune[i],
                "ct"
                )
                .ShownOn(TypeCount, PlexDetune)
            ),
            ParamDescriptor.Float("masterDetune", "Master Detune", 0f, 200f, 100f, "%").ShownOn(TypeCount, PlexDetune),
            ParamDescriptor.Float("granule", "Granule Length", 10f, MaxGranuleMs, 60f, "ms", ParamTaper.Log)
                .ShownOn(TypeCount, PlexDetune, PlexShifter, ShimmerVerb),
            ..Line(static i => ParamDescriptor.Float(
                $"shift{i + 1}",
                $"Shift {i + 1}",
                -24f,
                24f,
                DefaultShifts[i],
                "st"
                )
                .ShownOn(TypeCount, PlexShifter)
            ),
            ParamDescriptor.Choice("direction", "Direction", ["Forward", "Reverse"])
                .ShownOn(TypeCount, PlexDetune, PlexShifter, ShimmerVerb),
            ParamDescriptor.Float("crossfade", "Crossfade", 2f, 100f, 100f, "%")
                .ShownOn(TypeCount, PlexDetune, PlexShifter, ShimmerVerb),
            ParamDescriptor.Float("verbSize", "Size", 0f, 100f, 50f, "%").ShownOn(TypeCount, PlexVerb, ShimmerVerb),
            ParamDescriptor.Float("shimmerIntensity", "Shimmer Intensity", 0f, 100f, 50f, "%")
                .ShownOn(TypeCount, ShimmerVerb),
            ParamDescriptor.Float("lfoRate", "LFO Rate", 0.05f, 10f, 0.3f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfoTempo", "LFO Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("lfoDepth", "LFO Depth", 0f, MaxLfoMs, 0f, "ms"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("spread", "Stereo Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 30f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            MixLaws.KillDryParam
        ],
        maxInstances: 2,
        pages: [
            new(
                "Basic",
                [
                    "delays", "masterTime", "decay", "diffusion", "inputDiffusion", "preDelay", "lowCut", "highCut",
                    "hold", "masterPan", "masterDetune", "spread"
                ]
            ),
            new(
                "Times",
                [
                    "time1", "time2", "time3", "time4", "time5", "time6", "time7", "time8", "tempo1", "tempo2",
                    "tempo3", "tempo4", "tempo5", "tempo6", "tempo7", "tempo8"
                ]
            ),
            new(
                "Levels",
                [
                    "level1", "level2", "level3", "level4", "level5", "level6", "level7", "level8", "pan1", "pan2",
                    "pan3", "pan4", "pan5", "pan6", "pan7", "pan8"
                ]
            ),
            new(
                "Pitch",
                [
                    "detune1", "detune2", "detune3", "detune4", "detune5", "detune6", "detune7", "detune8", "shift1",
                    "shift2", "shift3", "shift4", "shift5", "shift6", "shift7", "shift8", "granule", "direction",
                    "crossfade", "verbSize", "shimmerIntensity"
                ]
            ),
            new(
                "Modulation",
                [
                    "filterType", "filterFrequency", "filterQ", "filterGain", "filterLfoRate", "filterLfoTempo",
                    "modFrequency", "lfoRate", "lfoTempo", "lfoDepth"
                ]
            ),
            new("Ducking", ["duckThreshold", "duckAttenuation", "duckRelease"]),
            new(
                "Advanced",
                [
                    "envelopeThreshold", "envelopeAttack", "envelopeRelease", "inputGain", "inputSelect", "killDry"
                ]
            )
        ]
    );

    static IEnumerable<ParamDescriptor> Line(Func<int, ParamDescriptor> parameter) =>
        Enumerable.Range(0, MaxLines).Select(parameter);

    /// <summary>Gains of the balance pan law: unity on both sides at centre, the far side fading to zero.</summary>
    static (float Left, float Right) Pan(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    int LineCount() => Math.Clamp((int)MathF.Round(Params.Current(Delays)), 1, MaxLines);

    SvfCoefficients DesignFilter(int type, float sampleRate, float frequencyScale) =>
        SvfCoefficients.Design(
            type switch {
                1 => BiquadType.LowPass,
                2 => BiquadType.HighPass,
                3 => BiquadType.BandPass,
                _ => BiquadType.Peak
            },
            sampleRate,
            Params.Current(FilterFrequency) * frequencyScale,
            Params.Current(FilterQ),
            Params.Current(FilterGain)
        );

    void ClearState() {
        for (var i = 0; i < MaxLines; i++) {
            lines[i].Reset();
            allpasses[i].Clear();
            shifters[i].Reset();
            loopState[i].Reset();
            filterState[i].Reset();
        }

        lfo.Reset();
        filterLfo.Reset();
        filterCounter = 0;
    }

    /// <summary>
    ///     Line read delays in samples: each line's time (or tempo) scaled by Master Time, or for the verbs the verb lengths
    ///     scaled by Size; less the loop allpass, so the loop is exactly that long.
    /// </summary>
    void ComputeLineTargets(int type, double tempo) {
        var rate = (float)Spec.SampleRate;
        var verb = type is PlexVerb or ShimmerVerb;
        var size = 0.3f + 1.4f * Params.Current(VerbSize) / 100f;
        var master = Params.Current(MasterTime) / 100f;
        for (var i = 0; i < MaxLines; i++) {
            float ms;
            if (verb) {
                ms = VerbMs[i] * size;
            } else {
                var division = TempoDivisions.Milliseconds((int)Params.Current(Tempo1 + i), tempo);
                ms = Math.Min(MaxTotalMs, (division > 0 ? (float)division : Params.Current(Time1 + i)) * master);
            }

            lineTarget[i] = Math.Max(FractionalDelayLine.MinDelay, ms * rate / 1_000f - allpassLength[i]);
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        var length = (int)(rate * (MaxTotalMs + 2 * MaxLfoMs + 5) / 1000) + 8;
        for (var i = 0; i < MaxLines; i++) {
            lines[i].Allocate(length);
            allpassLength[i] = (int)MathF.Round(AllpassMs[i] * 0.001f * (float)rate);
            allpasses[i].Allocate(allpassLength[i] + 2);
            shifters[i].Prepare(rate, 0.06, MaxGranuleMs / 1000.0);
        }

        for (var i = 0; i < inputDiffusers.Length; i++) {
            inputDiffuserLength[i] = (int)MathF.Round(InputDiffuserMs[i] * 0.001f * (float)rate);
            inputDiffusers[i].Allocate(inputDiffuserLength[i] + 2);
        }

        preDelay.Allocate((int)(rate * MaxPreDelayMs / 1000) + 8);
    }

    protected override void OnReset() {
        activeType = (int)Params.Current(Type);
        activeLines = LineCount();
        ClearState();
        preDelay.Reset();
        foreach (var diffuser in inputDiffusers) {
            diffuser.Clear();
        }

        envelope.Reset();
        ducker.Reset();
        ComputeLineTargets(activeType, 120);
        Array.Copy(lineTarget, lineDelay, MaxLines);
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            for (var i = 0; i < MaxLines; i++) {
                lines[i].Dispose();
                allpasses[i].Dispose();
                shifters[i].Dispose();
            }

            foreach (var diffuser in inputDiffusers) {
                diffuser.Dispose();
            }

            preDelay.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var sampleRate = (float)Spec.SampleRate;
        var tempo = context.Tempo;
        var type = (int)Params.Current(Type);
        var count = LineCount();
        for (var p = MasterTime; p < Info.Params.Count; p++) {
            // Every smoothed parameter moves on once per block; the reads below take the current values.
            if (Info.Params[p].Smoothing == ParamSmoothing.Linear) {
                Params.Advance(p, n);
            }
        }

        if (type != activeType || count != activeLines) {
            // A different network starts empty and at its own line times rather than gliding from the old ones.
            activeType = type;
            activeLines = count;
            ClearState();
            ComputeLineTargets(type, tempo);
            Array.Copy(lineTarget, lineDelay, MaxLines);
        }

        ComputeLineTargets(type, tempo);
        var holdMode = (int)Params.Current(Hold);
        var hold = holdMode != HoldOff;
        var decay = hold ? HoldDecaySeconds : Params.Current(Decay);
        var inputGain = holdMode == HoldOn ? 0f : Params.Current(InputGain) / 100f;
        var verb = type is PlexVerb or ShimmerVerb;
        var diffusion = Params.Current(Diffusion) / 100f * MaxDiffusionGain * (verb ? 1.1f : 1f);
        var inputDiffusion = Params.Current(InputDiffusion) / 100f * MaxDiffusionGain;
        var preDelaySamples = MathF.Round(Params.Current(PreDelay) * sampleRate / 1_000f);
        var masterPan = Params.Current(MasterPan) / 100f;
        var intensity = Params.Current(ShimmerIntensity) / 100f;
        var reverse = (int)Params.Current(Direction) == 1;
        var granule = Params.Current(Granule) / 1000.0;
        var crossfade = Params.Current(Crossfade) / 100f;
        var masterDetune = Params.Current(MasterDetune) / 100f;
        for (var i = 0; i < count; i++) {
            gains[i] = MathF.Pow(10f, -3f * (lineTarget[i] + allpassLength[i]) / (decay * sampleRate));
            var (left, right) = Pan(Math.Clamp(Params.Current(Pan1 + i) / 100f + masterPan, -1f, 1f));
            var level = Params.Current(Level1 + i) / 100f;
            tapLeft[i] = level * left;
            tapRight[i] = level * right;
            var speed = type switch {
                PlexDetune => MathF.Pow(2f, Params.Current(Detune1 + i) * masterDetune / 1_200f),
                PlexShifter => MathF.Pow(2f, Params.Current(Shift1 + i) / 12f),
                ShimmerVerb => 2f,
                _ => 1f
            };
            speeds[i] = reverse ? -speed : speed;
            shifters[i].SetGrain(granule);
            shifters[i].SetCrossfade(crossfade);
        }

        var loop = LoopFilter.Design(
            sampleRate,
            Params.Current(LowCut),
            CutFilter.Slope12,
            LoopFilter.ButterworthQ,
            Params.Current(HighCut),
            CutFilter.Slope12,
            LoopFilter.ButterworthQ
        );
        var filterType = (int)Params.Current(FilterType);
        var modDepth = Params.Current(ModFrequency);
        var filterRate = TempoDivisions.Rate((int)Params.Current(FilterLfoTempo), tempo, Params.Current(FilterLfoRate));
        if (filterType != FilterOff && modDepth == 0) {
            filterCoefficients = DesignFilter(filterType, sampleRate, 1f);
        }

        var lfoDepth = Params.Current(LfoDepth) * 0.001f * sampleRate;
        var lfoRate = TempoDivisions.Rate((int)Params.Current(LfoTempo), tempo, Params.Current(LfoRate));
        ducker.Configure(
            sampleRate,
            Params.Current(DuckAttenuation),
            Params.Current(DuckThreshold),
            Params.Current(DuckRelease)
        );
        envelope.Configure(
            sampleRate,
            Params.Current(EnvelopeThreshold),
            Params.Current(EnvelopeAttack),
            Params.Current(EnvelopeRelease)
        );
        var spread = Params.Current(Spread) / 100f;
        var injection = 1f / MathF.Sqrt(count);
        var reflection = 2f / count;
        var shifting = type is PlexDetune or PlexShifter or ShimmerVerb;

        for (var i = 0; i < count; i++) {
            lineStep[i] = (lineTarget[i] - lineDelay[i]) / n;
        }

        var select = (int)Params.Current(InputSelect);
        var mix = Params.Current(Mix) / 100f;
        var mixGains = new MixRamp(MixLaw.Linear, this.mix, mix, n, Params.Current(KillDry) >= 0.5f);
        for (var s = 0; s < n; s++) {
            mixGains.Advance();
            var dryLeft = io.Left[s];
            var dryRight = io.Right[s];
            var x = Stereo.Select(select, dryLeft, dryRight) * inputGain;
            if (envelope.Active) {
                x *= envelope.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
            }

            preDelay.Write(x);
            if (preDelaySamples >= 1f) {
                x = preDelay.Read(preDelaySamples + 1f); // a delay of 1 is the sample just written
            }

            if (inputDiffusion > 0) {
                for (var d = 0; d < inputDiffusers.Length; d++) {
                    x = inputDiffusers[d].Allpass(x, inputDiffuserLength[d], inputDiffusion);
                }
            }

            if (filterType != FilterOff && modDepth > 0) {
                // Coefficients move on a fixed internal hop, so the output does not depend on the host block size.
                if (filterCounter == 0) {
                    filterCoefficients = DesignFilter(
                        filterType,
                        sampleRate,
                        MathF.Pow(2f, modDepth * filterLfo.Value(LfoShape.Sine))
                    );
                }

                filterCounter = (filterCounter + 1) % FilterHop;
                filterLfo.Advance(filterRate, sampleRate);
            }

            lfo.Advance(lfoRate, sampleRate);

            // Read every line, shift and filter it, and apply its decay gain.
            float sum = 0, wetLeft = 0, wetRight = 0;
            for (var i = 0; i < count; i++) {
                lineDelay[i] += lineStep[i];
                var delay = lineDelay[i];
                if (lfoDepth > 0) {
                    delay += lfoDepth * (1f + lfo.Value(LfoShape.Sine, (double)i / count));
                }

                var y = lines[i].Read(delay);
                if (shifting) {
                    var shifted = shifters[i].Process(y, speeds[i]);
                    y = type == ShimmerVerb ? y + intensity * (shifted - y) : shifted;
                }

                if (loop.Active) {
                    y = loopState[i].Tick(loop, y);
                }

                if (filterType != FilterOff) {
                    y = (float)filterState[i].Tick(filterCoefficients, y);
                }

                wetLeft += y * tapLeft[i];
                wetRight += y * tapRight[i];
                outputs[i] = y * gains[i];
                sum += outputs[i];
            }

            // Householder feedback (I - 2/N · 11ᵀ) plus the input, through each loop's allpass, back into the lines.
            var reflected = sum * reflection;
            for (var i = 0; i < count; i++) {
                var value = outputs[i] - reflected + x * injection;
                if (diffusion > 0) {
                    value = allpasses[i].Allpass(value, allpassLength[i], diffusion);
                } else {
                    value = allpasses[i].Delay(value, allpassLength[i]);
                }

                lines[i].Write(DelayBlock.Limit(value));
            }

            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            Stereo.Spread(ref wetLeft, ref wetRight, spread);
            io.Left[s] = dryLeft * mixGains.Dry + wetLeft * mixGains.Wet;
            io.Right[s] = dryRight * mixGains.Dry + wetRight * mixGains.Wet;
        }

        Array.Copy(lineTarget, lineDelay, MaxLines);
        this.mix = mix;
        for (var i = 0; i < MaxLines; i++) {
            loopState[i].FlushDenormals();
            filterState[i].FlushDenormals();
        }

        ducker.FlushDenormals();
        envelope.FlushDenormals();
        output.Apply(io, Params.Current(Level), Params.Current(Balance));
    }
}
