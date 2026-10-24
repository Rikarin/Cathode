using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;
using Cathode.Dsp.Reverb;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Algorithmic reverb. Room, Hall, Plate, Chamber and Cathedral are a feedback delay network of 4 to 16 lines (Jot
///     and Chaigne 1991) with a Householder feedback matrix; Dattorro is Dattorro's plate (<see cref="DattorroPlate" />)
///     and Spring a dispersive tank of one to six springs (<see cref="SpringTank" />). Pre-delay, early reflections,
///     the wet EQ, the pitch section, ducker, mix and level are shared.
/// </summary>
/// <remarks>
///     <para>
///         Signal flow: input select and gain, the pitch section when it sits at the input, a pre-delay with its own
///         feedback (echoes into the tank) and mix (how much of the tank's input is pre-delayed), then the early
///         reflections (a 12-tap pattern per type, scaled by Size and Early Time, decaying by Early Decay and smeared by
///         two allpasses per side) and the tank, which hears the pre-delayed input blended with the reflections by Late
///         Input Mix. The wet is Late Level × tank + Early Level × reflections, then low cut, two peaking bands (Low Mid,
///         High Mid), mic spacing (the right side delayed), ducker and stereo spread.
///     </para>
///     <para>
///         The network: four input allpass diffusers (gain and length scaled by Input Diffusion and its Time) feed the
///         lines. Each line's gain is set from the decay time so the T60 is analytic; a one-pole damping filter (High
///         Cut) shortens the high-frequency decay, and the Low and High Freq Time multipliers set the decay below the low
///         crossover and above the high one through one-pole splits, as <c>gm·y + (gl - gm)·low + (gh - gm)·high</c>.
///         Wall Diffusion puts an allpass in every loop (its length set by Echo Density, taken out of the line so the
///         loop time and decay are kept). Two lines are modulated by a sine (Mod Depth, Rate, and the phase between
///         them). Quality is the line count: Economy 4, Normal 8, High 12, Ultra High 16, with injection and output
///         scaled by √(8/N) so the level does not change. Left and right take decorrelated sign patterns of the lines.
///     </para>
///     <para>
///         Hold closes the input and stretches the decay to 600 s; Stack keeps the input open, and the network's line
///         writes then pass a soft limit so the stacked tail stays bounded. The pitch section is two granular shifters
///         (<see cref="GrainShifter" />, reversed grains for Direction Reverse) blended by Voice Balance: at the input it
///         pitches what enters the tank, at the output it pitches the wet, and in the tank it feeds the pitched wet back
///         into the tank input, so every pass climbs again (shimmer). Pitch Feedback is that tank send, or at the input
///         and output the shifters' own feedback. Stereo Spread runs from −200 % to 200 %: beyond ±100 % it widens the
///         wet by raising its side signal (M/S).
///     </para>
/// </remarks>
public sealed class ReverbBlock : DspBlock {
    public const int Type = 0;
    public const int Decay = 1;
    public const int Size = 2;
    public const int PreDelay = 3;
    public const int Damping = 4;
    public const int LowCut = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;
    public const int InputGain = 9;
    public const int InputSelect = 10;
    public const int Spread = 11;
    public const int DuckAttenuation = 12;
    public const int DuckThreshold = 13;
    public const int DuckRelease = 14;
    public const int Hold = 15;
    public const int Quality = 16;
    public const int LowCrossover = 17;
    public const int LowTime = 18;
    public const int HighCrossover = 19;
    public const int HighTime = 20;
    public const int EarlyLevel = 21;
    public const int LateLevel = 22;
    public const int EarlyDiffusion = 23;
    public const int EarlyTime = 24;
    public const int EarlyDecay = 25;
    public const int LateInputMix = 26;
    public const int LowMidFrequency = 27;
    public const int LowMidQ = 28;
    public const int LowMidGain = 29;
    public const int HighMidFrequency = 30;
    public const int HighMidQ = 31;
    public const int HighMidGain = 32;
    public const int PreDelayTempo = 33;
    public const int PreDelayFeedback = 34;
    public const int PreDelayMix = 35;
    public const int EchoDensity = 36;
    public const int WallDiffusion = 37;
    public const int InputDiffusion = 38;
    public const int InputDiffusionTime = 39;
    public const int MicSpacing = 40;
    public const int Springs = 41;
    public const int SpringTone = 42;
    public const int SpringDrive = 43;
    public const int Boing = 44;
    public const int ModDepth = 45;
    public const int ModRate = 46;
    public const int ModPhase = 47;
    public const int PitchMix = 48;
    public const int Shift1 = 49;
    public const int Shift2 = 50;
    public const int VoiceBalance = 51;
    public const int Splice = 52;
    public const int PitchFeedback = 53;
    public const int PitchDirection = 54;
    public const int PitchPosition = 55;
    public const int GlobalMix = 56;

    public const int Room = 0;
    public const int Hall = 1;
    public const int Plate = 2;
    public const int Dattorro = 3;
    public const int Spring = 4;
    public const int Chamber = 5;
    public const int Cathedral = 6;

    public const int HoldOff = 0;
    public const int HoldOn = 1;
    public const int Stack = 2;

    public const int PitchAtInput = 0;
    public const int PitchAtOutput = 1;
    public const int PitchInTank = 2;

    public const float LowCutOff = 20f;
    public const float MaxPreDelayMs = 1_000f;
    public const float MaxMicSpacingMs = 20f;

    const int MaxLines = 16;
    const int EarlyTaps = 12;

    /// <summary>The decay time Hold and Stack use: the tail is held, yet every loop stays below unity gain.</summary>
    const float HoldDecaySeconds = 600f;

    const float MaxSizeScale = 1.5f;
    const float MaxEarlyTimeScale = 2f;
    const float ModulationMs = 0.35f;
    const float MaxModDepth = 3f;
    const float MaxInputDiffusionTime = 2f;
    const float MaxDiffuserGain = 0.9f;
    const float MaxWallFraction = 0.3f;
    const float MaxSpliceSeconds = 0.2f;

    /// <summary>The off-axis gain of an early reflection on the side it does not come from.</summary>
    const float EarlyCrossGain = 0.35f;

    /// <summary>The longest <see cref="LineMs" /> entry (Cathedral's), which sizes the lines.</summary>
    const float LongestLineMs = 111.3f;

    /// <summary>The longest <see cref="EarlyMs" /> entry (Cathedral's last tap), which sizes the reflection line.</summary>
    const float LongestEarlyMs = 117.7f;

    /// <summary>Line counts of the Quality choices.</summary>
    public static readonly int[] QualityLines = [4, 8, 12, 16];

    /// <summary>Early reflection tap times in ms at size 50 % and Early Time 100 %, per ER row (see <see cref="ErRow" />).</summary>
    public static readonly float[][] EarlyMs = [
        [3.1f, 5.3f, 7.9f, 10.1f, 12.7f, 15.1f, 18.3f, 21.1f, 24.7f, 28.3f, 32.9f, 37.1f], // Room
        [7.3f, 11.9f, 17.1f, 21.7f, 26.3f, 31.9f, 37.7f, 43.1f, 49.3f, 56.9f, 63.1f, 71.3f], // Hall
        [1.3f, 2.9f, 4.1f, 5.7f, 7.3f, 8.9f, 10.7f, 12.1f, 14.3f, 16.1f, 18.7f, 21.3f], // Plate (and Dattorro)
        [4.1f, 6.7f, 9.9f, 13.1f, 16.3f, 19.7f, 23.3f, 27.1f, 31.3f, 35.9f, 40.1f, 45.7f], // Chamber
        [11.3f, 19.7f, 27.1f, 35.3f, 44.9f, 53.1f, 62.9f, 71.3f, 81.7f, 92.3f, 103.1f, 117.7f] // Cathedral
    ];

    /// <summary>Line lengths in ms at size 50 % (scale 1), per FDN row. Normal quality uses the first eight.</summary>
    static readonly float[][] LineMs = [
        [
            9.1f, 11.3f, 13.7f, 15.9f, 18.1f, 21.7f, 24.3f, 27.1f, 10.3f, 12.7f, 14.9f, 17.3f, 19.9f, 22.9f, 25.7f,
            28.3f
        ], // Room
        [
            29.7f, 37.1f, 41.1f, 43.7f, 53.3f, 59.9f, 67.1f, 71.3f, 31.3f, 34.7f, 39.1f, 47.3f, 50.9f, 57.1f, 63.7f,
            69.1f
        ], // Hall
        [
            17.3f, 19.1f, 23.3f, 27.7f, 31.1f, 35.9f, 39.7f, 43.1f, 18.3f, 21.1f, 25.3f, 29.3f, 33.7f, 37.3f, 41.9f,
            45.1f
        ], // Plate
        [
            13.3f, 16.1f, 19.7f, 22.9f, 26.3f, 29.9f, 33.1f, 36.7f, 14.7f, 17.9f, 21.1f, 24.7f, 27.9f, 31.7f, 34.9f,
            38.3f
        ], // Chamber
        [
            41.3f, 53.9f, 61.7f, 71.9f, 83.3f, 89.9f, 97.3f, 107.1f, 47.9f, 57.7f, 66.1f, 77.3f, 86.1f, 93.7f, 101.9f,
            111.3f
        ] // Cathedral
    ];

    /// <summary>The lines Economy quality keeps, spread across the Normal set.</summary>
    static readonly int[] EconomyLines = [0, 2, 5, 7];

    /// <summary>Input diffuser lengths in ms (after Dattorro's plate) and gains per FDN row.</summary>
    static readonly float[] DiffuserMs = [4.77f, 3.59f, 12.73f, 9.30f];

    static readonly float[] DiffusionGain = [0.6f, 0.65f, 0.75f, 0.7f, 0.7f];

    static readonly float[] EarlyDiffuserMs = [1.7f, 2.9f, 2.3f, 3.7f];

    static readonly float[] LeftSigns = [1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1, 1, -1];
    static readonly float[] RightSigns = [1, 1, -1, -1, 1, 1, -1, -1, 1, 1, -1, -1, 1, 1, -1, -1];

    readonly DelayBuffer preDelay = new();
    readonly Allpass[] diffusers = [new(), new(), new(), new()];
    readonly LineBank lines = new(MaxLines);
    readonly LineBank walls = new(MaxLines);
    readonly float[] lengths = new float[MaxLines];
    readonly float[] wallLengths = new float[MaxLines];
    readonly float[] gains = new float[MaxLines];
    readonly float[] lowDeltas = new float[MaxLines];
    readonly float[] highDeltas = new float[MaxLines];
    readonly float[] damping = new float[MaxLines];
    readonly float[] lowStates = new float[MaxLines];
    readonly float[] highStates = new float[MaxLines];
    readonly float[] outputs = new float[MaxLines];
    readonly float[] readLengths = new float[MaxLines];
    readonly float[] signLeft = new float[MaxLines];
    readonly float[] signRight = new float[MaxLines];
    readonly int[] slots = new int[MaxLines];
    readonly ReverbLine early = new();
    readonly ReverbLine[] earlyDiffusers = [new(), new(), new(), new()];
    readonly float[] earlyDiffuserLengths = new float[4];
    readonly float[] tapDelays = new float[EarlyTaps];
    readonly float[] tapLeft = new float[EarlyTaps];
    readonly float[] tapRight = new float[EarlyTaps];
    readonly ReverbLine mic = new();
    readonly GrainShifter voice1 = new();
    readonly GrainShifter voice2 = new();
    readonly DattorroPlate plate = new();
    readonly SpringTank spring = new();
    int activeType = -1;
    int activeQuality = -1;
    int lineCount;
    float injection;
    float outputGain;
    float diffusion;
    float damp;
    bool bands;
    float lowCoefficient;
    float highCoefficient;
    bool wall;
    float wallGain;
    bool stack;
    float modDepth;
    double modStep;
    double modOffset;
    bool modOpposed;
    double modPhase;
    SvfState lowCutLeft, lowCutRight;
    SvfState lowMidLeft, lowMidRight, highMidLeft, highMidRight;
    float preDelayOut;
    float earlyDiffusion;
    bool pitchRunning;
    float pitched;
    float mix;
    Ducker ducker;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "reverb",
        "Reverb",
        "Reverb",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Room", "Hall", "Plate", "Dattorro Plate", "Spring", "Chamber", "Cathedral"],
                1
            ),
            ParamDescriptor.Float("decay", "Decay Time", 0.1f, 20f, 2f, "s", ParamTaper.Log),
            new("size", "Size", ParamKind.Float, 0f, 100f, 50f, "%", Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Float("predelay", "Pre-Delay", 0f, MaxPreDelayMs, 0f, "ms"),
            ParamDescriptor.Float("damping", "High Cut", 1_000f, 20_000f, 6_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 1_000f, 80f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 20f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            Stereo.SpreadParam("spread"),
            ParamDescriptor.Float("duckAttenuation", "Ducker Atten", 0f, 40f, 0f, "dB"),
            ParamDescriptor.Float("duckThreshold", "Ducker Threshold", -60f, 0f, -30f, "dB"),
            ParamDescriptor.Float("duckRelease", "Ducker Release", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
            ParamDescriptor.Choice("hold", "Stack/Hold", ["Off", "Hold", "Stack"]),
            Network(ParamDescriptor.Choice("quality", "Quality", ["Economy", "Normal", "High", "Ultra High"], 1)),
            Network(ParamDescriptor.Float("lowCrossover", "Low Crossover", 50f, 1_000f, 250f, "Hz", ParamTaper.Log)),
            Network(ParamDescriptor.Float("lowTime", "Low Freq Time", 25f, 400f, 100f, "%", ParamTaper.Log)),
            Network(
                ParamDescriptor.Float("highCrossover", "High Crossover", 1_000f, 12_000f, 4_000f, "Hz", ParamTaper.Log)
            ),
            Network(ParamDescriptor.Float("highTime", "High Freq Time", 10f, 200f, 100f, "%", ParamTaper.Log)),
            Reflecting(ParamDescriptor.Float("earlyLevel", "Early Level", 0f, 100f, 0f, "%")),
            ParamDescriptor.Float("lateLevel", "Late Level", 0f, 100f, 100f, "%"),
            Reflecting(ParamDescriptor.Float("earlyDiffusion", "Early Diffusion", 0f, 100f, 50f, "%")),
            Reflecting(ParamDescriptor.Float("earlyTime", "Early Time", 25f, 200f, 100f, "%", ParamTaper.Log)),
            Reflecting(ParamDescriptor.Float("earlyDecay", "Early Decay", 0f, 100f, 50f, "%")),
            Reflecting(ParamDescriptor.Float("lateInputMix", "Late Input Mix", 0f, 100f, 0f, "%")),
            ParamDescriptor.Float("lowMidFrequency", "Low Mid Freq", 100f, 2_000f, 400f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lowMidQ", "Low Mid Q", 0.1f, 10f, 0.7f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("lowMidGain", "Low Mid Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("highMidFrequency", "High Mid Freq", 500f, 10_000f, 2_500f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highMidQ", "High Mid Q", 0.1f, 10f, 0.7f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("highMidGain", "High Mid Gain", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Choice("preDelayTempo", "Pre-Delay Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("preDelayFeedback", "Pre-Delay Feedback", 0f, 90f, 0f, "%"),
            ParamDescriptor.Float("preDelayMix", "Pre-Delay Mix", 0f, 100f, 100f, "%"),
            Network(ParamDescriptor.Float("echoDensity", "Echo Density", 0f, 100f, 50f, "%")),
            Network(ParamDescriptor.Float("wallDiffusion", "Wall Diffusion", 0f, 100f, 0f, "%")),
            Network(ParamDescriptor.Float("inputDiffusion", "Input Diffusion", 0f, 120f, 100f, "%")),
            Network(
                ParamDescriptor.Float(
                    "inputDiffusionTime",
                    "Input Diffusion Time",
                    25f,
                    200f,
                    100f,
                    "%",
                    ParamTaper.Log
                )
            ),
            ParamDescriptor.Float("micSpacing", "Mic Spacing", 0f, MaxMicSpacingMs, 0f, "ms"),
            SpringOnly(
                new(
                    "springs",
                    "Number of Springs",
                    ParamKind.Float,
                    1f,
                    SpringTank.MaxSprings,
                    2f,
                    Smoothing: ParamSmoothing.Step
                )
            ),
            SpringOnly(ParamDescriptor.Float("springTone", "Spring Tone", 0f, 100f, 50f, "%")),
            SpringOnly(ParamDescriptor.Float("springDrive", "Spring Drive", 0f, 10f, 0f)),
            SpringOnly(ParamDescriptor.Float("boing", "Boing", 0f, 100f, 0f, "%")),
            Network(ParamDescriptor.Float("modDepth", "Mod Depth", 0f, MaxModDepth * 100f, 100f, "%")),
            Network(ParamDescriptor.Float("modRate", "Mod Rate", 0.1f, 5f, 0.7f, "Hz", ParamTaper.Log)),
            Network(ParamDescriptor.Float("modPhase", "LFO Phase", 0f, 180f, 180f, "°")),
            ParamDescriptor.Float("pitchMix", "Pitch Mix", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("shift1", "Shift 1", -24f, 24f, 12f, "st"),
            ParamDescriptor.Float("shift2", "Shift 2", -24f, 24f, 7f, "st"),
            ParamDescriptor.Float("voiceBalance", "Voice Balance", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("splice", "Splice", 10f, MaxSpliceSeconds * 1_000f, 60f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("pitchFeedback", "Pitch Feedback", 0f, 100f, 0f, "%"),
            ParamDescriptor.Choice("pitchDirection", "Pitch Direction", ["Forward", "Reverse"]),
            ParamDescriptor.Choice("pitchPosition", "Pitch Position", ["Input", "Output", "Tank Feedback"], 1),
            EffectsMix.Param
        ],
        maxInstances: 2
    );

    public ReverbBlock()
        : base(Info) { }

    /// <summary>Whether <paramref name="type" /> runs on the feedback delay network.</summary>
    public static bool IsNetwork(int type) => type is not (Dattorro or Spring);

    /// <summary>The <see cref="EarlyMs" /> row of a type (Dattorro takes the plate's; Spring has none).</summary>
    public static int ErRow(int type) =>
        type switch {
            Dattorro => 2,
            Chamber => 3,
            Cathedral => 4,
            _ => Math.Clamp(type, 0, 2)
        };

    /// <summary>The <see cref="LineMs" /> row of a network type.</summary>
    static int FdnRow(int type) =>
        type switch {
            Chamber => 3,
            Cathedral => 4,
            _ => Math.Clamp(type, 0, 2)
        };

    /// <summary>The type labels that show a parameter on <paramref name="types" /> and hide it on the others.</summary>
    static ParamDescriptor ShownOn(ParamDescriptor p, params int[] types) =>
        p with {
            TypeLabels = [..Enumerable.Range(0, Cathedral + 1).Select(t => types.Contains(t) ? p.Name : string.Empty)]
        };

    static ParamDescriptor Network(ParamDescriptor p) => ShownOn(p, Room, Hall, Plate, Chamber, Cathedral);

    static ParamDescriptor Reflecting(ParamDescriptor p) => ShownOn(p, Room, Hall, Plate, Dattorro, Chamber, Cathedral);

    static ParamDescriptor SpringOnly(ParamDescriptor p) => ShownOn(p, Spring);

    /// <summary>A soft limit near ±4 (12 dBFS) for stacked tails: transparent at playing levels.</summary>
    static float Limit(float x) => 4f * MathF.Tanh(0.25f * x);

    /// <summary>A peaking band's coefficients; a band at 0 dB is skipped and starts clean when it comes back.</summary>
    static SvfCoefficients Band(
        float frequency,
        float q,
        float gainDb,
        float rate,
        ref SvfState left,
        ref SvfState right
    ) {
        if (gainDb == 0) {
            left.Reset();
            right.Reset();
            return default;
        }

        return SvfCoefficients.Design(BiquadType.Peak, rate, frequency, q, gainDb);
    }

    float Voices(float x, double speed1, double speed2, float balance) =>
        voice1.Process(x, speed1) * (1f - balance) + voice2.Process(x, speed2) * balance;

    void FlushDenormals() {
        ducker.FlushDenormals();
        spring.FlushDenormals();
        for (var i = 0; i < MaxLines; i++) {
            if (MathF.Abs(damping[i]) < 1e-20f) {
                damping[i] = 0;
            }

            if (MathF.Abs(lowStates[i]) < 1e-20f) {
                lowStates[i] = 0;
            }

            if (MathF.Abs(highStates[i]) < 1e-20f) {
                highStates[i] = 0;
            }
        }

        if (MathF.Abs(preDelayOut) < 1e-20f) {
            preDelayOut = 0;
        }

        if (MathF.Abs(pitched) < 1e-20f) {
            pitched = 0;
        }

        lowCutLeft.FlushDenormals();
        lowCutRight.FlushDenormals();
        lowMidLeft.FlushDenormals();
        lowMidRight.FlushDenormals();
        highMidLeft.FlushDenormals();
        highMidRight.FlushDenormals();
    }

    void ClearNetwork() {
        foreach (var diffuser in diffusers) {
            diffuser.Clear();
        }

        lines.Clear();
        walls.Clear();
        Array.Clear(damping);
        Array.Clear(lowStates);
        Array.Clear(highStates);
    }

    void ClearTank(int type) {
        switch (type) {
            case Dattorro:
                plate.Clear();
                break;
            case Spring:
                spring.Clear();
                break;
            default:
                ClearNetwork();
                break;
        }
    }

    void ConfigureNetwork(int type, int quality, float decay, float scale, float dampingHz, float rate, int n) {
        var row = FdnRow(type);
        quality = Math.Clamp(quality, 0, QualityLines.Length - 1);
        lineCount = QualityLines[quality];

        var norm = MathF.Sqrt(8f / lineCount);
        injection = 0.35f;
        outputGain = 0.5f * norm;

        diffusion = Math.Min(MaxDiffuserGain, DiffusionGain[row] * (Params.Advance(InputDiffusion, n) / 100f));
        var diffusionTime = Params.Advance(InputDiffusionTime, n) / 100f;
        for (var i = 0; i < diffusers.Length; i++) {
            diffusers[i].SetLength((int)(DiffuserMs[i] * diffusionTime * 0.001 * rate) + 1);
        }

        var lowTime = Params.Advance(LowTime, n) / 100f;
        var highTime = Params.Advance(HighTime, n) / 100f;
        var lowHz = Params.Advance(LowCrossover, n);
        var highHz = Math.Max(lowHz, Params.Advance(HighCrossover, n));
        bands = lowTime != 1f || highTime != 1f;
        lowCoefficient = 1f - MathF.Exp(-2f * MathF.PI * lowHz / rate);
        highCoefficient = 1f - MathF.Exp(-2f * MathF.PI * highHz / rate);

        var density = Params.Advance(EchoDensity, n) / 100f;
        wallGain = Params.Advance(WallDiffusion, n) / 100f * 0.7f;
        wall = wallGain > 0;
        // Per-line settings in the order the lines run (slot p holds line i), so the per-sample loop reads them in turn.
        for (var p = 0; p < lineCount; p++) {
            var i = quality == 0 ? EconomyLines[p] : p;
            slots[p] = i;
            signLeft[p] = LeftSigns[i];
            signRight[p] = RightSigns[i];
            lengths[p] = LineMs[row][i] * scale * 0.001f * rate;
            gains[p] = MathF.Pow(10f, -3f * lengths[p] / (decay * rate)); // -60 dB after `decay` seconds
            if (bands) {
                lowDeltas[p] = MathF.Pow(10f, -3f * lengths[p] / (decay * lowTime * rate)) - gains[p];
                highDeltas[p] = MathF.Pow(10f, -3f * lengths[p] / (decay * highTime * rate)) - gains[p];
            }

            // The wall allpass is taken out of the line, so the loop keeps its length and its decay.
            wallLengths[p] = wall ? Math.Max(2f, (0.05f + (MaxWallFraction - 0.05f) * density) * lengths[p]) : 0f;
            readLengths[p] = lengths[p] - wallLengths[p];
        }

        // One-pole damping in each line: the coefficient that puts its -3 dB point near the chosen frequency.
        damp = MathF.Exp(-2f * MathF.PI * dampingHz / rate);
        modDepth = ModulationMs * 0.001f * rate * (Params.Advance(ModDepth, n) / 100f);
        modStep = 2 * Math.PI * Params.Advance(ModRate, n) / rate;
        var phase = Params.Advance(ModPhase, n);
        modOpposed = phase == 180f;
        modOffset = Math.PI * phase / 180.0;
    }

    /// <summary>Early reflection taps for this block: times, the decay across the pattern, sides and the diffusion.</summary>
    void ConfigureEarly(int type, float scale, float rate, int n) {
        var row = ErRow(type);
        var time = Params.Advance(EarlyTime, n) / 100f;
        var decay = Params.Advance(EarlyDecay, n) / 100f;
        earlyDiffusion = Params.Advance(EarlyDiffusion, n) / 100f * 0.7f;
        var last = EarlyMs[row][EarlyTaps - 1];
        var power = 0f;
        for (var k = 0; k < EarlyTaps; k++) {
            var ms = EarlyMs[row][k];
            tapDelays[k] = 1f + ms * scale * time * 0.001f * rate; // Read(1) is the sample just written
            var gain = MathF.Pow(10f, -3f * decay * ms / last); // Early Decay 100 %: the last tap is 60 dB down
            tapLeft[k] = gain;
            power += gain * gain;
        }

        var norm = 1f / MathF.Sqrt(power);
        for (var k = 0; k < EarlyTaps; k++) {
            var gain = tapLeft[k] * norm;
            tapLeft[k] = k % 2 == 0 ? gain : gain * EarlyCrossGain;
            tapRight[k] = k % 2 == 0 ? gain * EarlyCrossGain : gain;
        }
    }

    (float Left, float Right) TickEarly(float x) {
        early.Write(x);
        float left = 0, right = 0;
        for (var k = 0; k < EarlyTaps; k++) {
            var v = early.Read(tapDelays[k]);
            left += v * tapLeft[k];
            right += v * tapRight[k];
        }

        if (earlyDiffusion > 0) {
            left = earlyDiffusers[0].Allpass(left, earlyDiffuserLengths[0], earlyDiffusion);
            left = earlyDiffusers[1].Allpass(left, earlyDiffuserLengths[1], earlyDiffusion);
            right = earlyDiffusers[2].Allpass(right, earlyDiffuserLengths[2], earlyDiffusion);
            right = earlyDiffusers[3].Allpass(right, earlyDiffuserLengths[3], earlyDiffusion);
        }

        return (left, right);
    }

    (float Left, float Right) TickNetwork(float x) {
        foreach (var diffuser in diffusers) {
            x = diffuser.Process(x, diffusion);
        }

        var modulation = modDepth * (float)Math.Sin(modPhase);
        var opposite = modOpposed ? -modulation : modDepth * (float)Math.Sin(modPhase + modOffset);
        modPhase += modStep;
        if (modPhase >= 2 * Math.PI) {
            modPhase -= 2 * Math.PI;
        }

        // Read every line (two of them modulated), damp, split into bands if asked, and apply the decay gains.
        var first = 1;
        var second = lineCount - 2;
        float sum = 0;
        for (var p = 0; p < lineCount; p++) {
            var length = readLengths[p] + (p == first ? modulation + modDepth : p == second ? modDepth + opposite : 0f);
            var y = lines.Read(slots[p], length);
            if (wall) {
                y = walls.Allpass(slots[p], y, wallLengths[p], wallGain);
            }

            var v = y + (damping[p] - y) * damp;
            damping[p] = v;
            float o;
            if (bands) {
                lowStates[p] += (v - lowStates[p]) * lowCoefficient;
                highStates[p] += (v - highStates[p]) * highCoefficient;
                o = v * gains[p] + lowDeltas[p] * lowStates[p] + highDeltas[p] * (v - highStates[p]);
            } else {
                o = v * gains[p];
            }

            outputs[p] = o;
            sum += o;
        }

        // Householder feedback (I - 2/N * 11^T) plus the diffused input, written back into the lines.
        var reflection = sum * (2f / lineCount);
        float wetLeft = 0, wetRight = 0;
        for (var p = 0; p < lineCount; p++) {
            var value = outputs[p] - reflection + x * injection;
            lines.Write(slots[p], stack ? Limit(value) : value);
            wetLeft += outputs[p] * signLeft[p];
            wetRight += outputs[p] * signRight[p];
        }

        return (wetLeft * outputGain, wetRight * outputGain);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        preDelay.Allocate((int)(MaxPreDelayMs * 0.001 * rate) + 4);
        for (var i = 0; i < diffusers.Length; i++) {
            diffusers[i].Allocate((int)(DiffuserMs[i] * MaxInputDiffusionTime * 0.001 * rate) + 2);
        }

        lines.Allocate((int)((LongestLineMs * MaxSizeScale + ModulationMs * MaxModDepth * 2) * 0.001 * rate) + 4);
        walls.Allocate((int)(LongestLineMs * MaxSizeScale * MaxWallFraction * 0.001 * rate) + 4);
        early.Allocate((int)(LongestEarlyMs * MaxSizeScale * MaxEarlyTimeScale * 0.001 * rate) + 4);
        for (var i = 0; i < earlyDiffusers.Length; i++) {
            earlyDiffuserLengths[i] = EarlyDiffuserMs[i] * 0.001f * (float)rate;
            earlyDiffusers[i].Allocate((int)earlyDiffuserLengths[i] + 4);
        }

        mic.Allocate((int)(MaxMicSpacingMs * 0.001 * rate) + 4);
        voice1.Prepare(rate, 0.06, MaxSpliceSeconds);
        voice2.Prepare(rate, 0.06, MaxSpliceSeconds);
        plate.Allocate(rate, MaxSizeScale);
        spring.Allocate(rate);
    }

    protected override void OnReset() {
        preDelay.Clear();
        ducker.Reset();
        foreach (var diffuser in diffusers) {
            diffuser.Clear();
        }

        ClearNetwork();
        early.Clear();
        foreach (var diffuser in earlyDiffusers) {
            diffuser.Clear();
        }

        mic.Clear();
        voice1.Reset();
        voice2.Reset();
        pitchRunning = false;
        pitched = 0;
        preDelayOut = 0;
        plate.Clear();
        spring.Clear();
        lowCutLeft.Reset();
        lowCutRight.Reset();
        lowMidLeft.Reset();
        lowMidRight.Reset();
        highMidLeft.Reset();
        highMidRight.Reset();
        modPhase = 0;
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            preDelay.Dispose();
            foreach (var diffuser in diffusers) {
                diffuser.Dispose();
            }

            lines.Dispose();
            walls.Dispose();
            early.Dispose();
            foreach (var diffuser in earlyDiffusers) {
                diffuser.Dispose();
            }

            mic.Dispose();
            voice1.Dispose();
            voice2.Dispose();
            plate.Dispose();
            spring.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var n = io.Length;
        var rate = (float)Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var quality = (int)Params.Current(Quality);
        var scale = 0.5f + Params.Current(Size) / 100f;
        var holdMode = (int)Params.Current(Hold);
        var closed = holdMode == HoldOn;
        stack = holdMode == Stack;
        var decay = Params.Advance(Decay, n);
        if (holdMode != HoldOff) {
            decay = HoldDecaySeconds; // an "infinite" tail: the loops lose only what their damping takes
        }

        var inputGain = closed ? 0f : Params.Advance(InputGain, n) / 100f;
        if (closed) {
            Params.Advance(InputGain, n);
        }

        var select = (int)Params.Current(InputSelect);
        var spread = Params.Advance(Spread, n) / 100f;
        ducker.Configure(
            rate,
            Params.Advance(DuckAttenuation, n),
            Params.Advance(DuckThreshold, n),
            Params.Advance(DuckRelease, n)
        );
        var preDelayMs = Params.Advance(PreDelay, n);
        var division = TempoDivisions.Milliseconds((int)Params.Current(PreDelayTempo), context.Tempo);
        if (division > 0) {
            preDelayMs = Math.Min(MaxPreDelayMs, (float)division);
        }

        var preDelay = Math.Max(1f, preDelayMs * 0.001f * rate);
        var preFeedback = Params.Advance(PreDelayFeedback, n) / 100f;
        var preMix = Params.Advance(PreDelayMix, n) / 100f;
        var mix = EffectsMix.Apply(Params.Advance(Mix, n), Params.Current(GlobalMix), context) / 100f;
        var dampingHz = Params.Advance(Damping, n);

        if (type != activeType || (IsNetwork(type) && quality != activeQuality)) {
            // The tank that takes over starts empty rather than replaying whatever it held when last used.
            if (activeType >= 0) {
                ClearTank(type);
            }

            activeType = type;
            activeQuality = quality;
        }

        switch (type) {
            case Dattorro:
                plate.Configure(decay, scale, dampingHz);
                break;
            case Spring:
                spring.Voice(
                    (int)MathF.Round(Params.Current(Springs)),
                    Math.Pow(2, (Params.Advance(SpringTone, n) - 50f) / 50f),
                    Params.Advance(SpringDrive, n),
                    Params.Advance(Boing, n) / 100f
                );
                spring.Configure(decay, scale);
                break;
            default:
                ConfigureNetwork(type, quality, decay, scale, dampingHz, rate, n);
                break;
        }

        var earlyLevel = Params.Advance(EarlyLevel, n) / 100f;
        var lateLevel = Params.Advance(LateLevel, n) / 100f;
        var lateInputMix = Params.Advance(LateInputMix, n) / 100f;
        var reflections = type != Spring && (earlyLevel > 0 || lateInputMix > 0);
        if (reflections) {
            ConfigureEarly(type, scale, rate, n);
        } else {
            Params.Advance(EarlyTime, n);
            Params.Advance(EarlyDecay, n);
            Params.Advance(EarlyDiffusion, n);
        }

        var lowCut = Params.Advance(LowCut, n);
        var useLowCut = lowCut > LowCutOff;
        var lowCutCoefficients =
            useLowCut ? SvfCoefficients.Design(BiquadType.HighPass, rate, lowCut, 0.7071) : default;
        var lowMid = Band(
            Params.Advance(LowMidFrequency, n),
            Params.Advance(LowMidQ, n),
            Params.Advance(LowMidGain, n),
            rate,
            ref lowMidLeft,
            ref lowMidRight
        );
        var highMid = Band(
            Params.Advance(HighMidFrequency, n),
            Params.Advance(HighMidQ, n),
            Params.Advance(HighMidGain, n),
            rate,
            ref highMidLeft,
            ref highMidRight
        );
        var useLowMid = Params.Current(LowMidGain) != 0;
        var useHighMid = Params.Current(HighMidGain) != 0;
        var micDelay = Params.Advance(MicSpacing, n) * 0.001f * rate;

        // The pitch section: two shifters, running only while they are heard or feed the tank.
        var pitchMix = Params.Advance(PitchMix, n) / 100f;
        var pitchFeedback = Params.Advance(PitchFeedback, n) / 100f;
        var position = (int)Params.Current(PitchPosition);
        var direction = Params.Current(PitchDirection) >= 0.5f ? -1.0 : 1.0;
        var speed1 = direction * Math.Pow(2, Params.Advance(Shift1, n) / 12.0);
        var speed2 = direction * Math.Pow(2, Params.Advance(Shift2, n) / 12.0);
        var balance = Params.Advance(VoiceBalance, n) / 100f;
        var splice = Params.Advance(Splice, n) * 0.001;
        var pitch = pitchMix > 0 || (position == PitchInTank && pitchFeedback > 0);
        if (pitch) {
            if (!pitchRunning) {
                voice1.Reset();
                voice2.Reset();
                pitched = 0;
                pitchRunning = true;
            }

            voice1.SetGrain(splice);
            voice2.SetGrain(splice);
        } else {
            pitchRunning = false;
        }

        // The shifters' own feedback at the input or output is kept below unity so it cannot run away.
        var selfFeedback = position == PitchInTank ? 0f : 0.9f * pitchFeedback;
        var tankFeedback = position == PitchInTank ? pitchFeedback : 0f;

        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;

        for (var i = 0; i < n; i++) {
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var input = Stereo.Select(select, dryLeft, dryRight) * inputGain;
            if (pitch && position == PitchAtInput) {
                pitched = Voices(input + selfFeedback * pitched, speed1, speed2, balance);
                input += (pitched - input) * pitchMix;
            }

            float x;
            if (preFeedback > 0) {
                x = this.preDelay.ReadWrite(input + preFeedback * preDelayOut, preDelay);
                preDelayOut = x;
            } else {
                x = this.preDelay.ReadWrite(input, preDelay);
            }

            if (preMix < 1f) {
                x = input + (x - input) * preMix;
            }

            float earlyLeft = 0, earlyRight = 0;
            if (reflections) {
                (earlyLeft, earlyRight) = TickEarly(x);
                if (lateInputMix > 0) {
                    x += (0.5f * (earlyLeft + earlyRight) - x) * lateInputMix;
                }
            }

            if (pitch && position == PitchInTank) {
                x += Limit(tankFeedback * pitched);
            }

            var (wetLeft, wetRight) = type switch {
                Dattorro => plate.Tick(x),
                Spring => spring.Tick(x),
                _ => TickNetwork(x)
            };

            if (lateLevel != 1f) {
                wetLeft *= lateLevel;
                wetRight *= lateLevel;
            }

            if (pitch && position != PitchAtInput) {
                var mono = 0.5f * (wetLeft + wetRight);
                pitched = Voices(mono + selfFeedback * pitched, speed1, speed2, balance);
                wetLeft += (pitched - wetLeft) * pitchMix;
                wetRight += (pitched - wetRight) * pitchMix;
            }

            if (earlyLevel > 0) {
                wetLeft += earlyLevel * earlyLeft;
                wetRight += earlyLevel * earlyRight;
            }

            if (useLowCut) {
                wetLeft = (float)lowCutLeft.Tick(lowCutCoefficients, wetLeft);
                wetRight = (float)lowCutRight.Tick(lowCutCoefficients, wetRight);
            }

            if (useLowMid) {
                wetLeft = (float)lowMidLeft.Tick(lowMid, wetLeft);
                wetRight = (float)lowMidRight.Tick(lowMid, wetRight);
            }

            if (useHighMid) {
                wetLeft = (float)highMidLeft.Tick(highMid, wetLeft);
                wetRight = (float)highMidRight.Tick(highMid, wetRight);
            }

            if (micDelay > 0) {
                mic.Write(wetRight);
                wetRight = mic.Read(1f + micDelay);
            }

            if (ducker.Active) {
                var duck = ducker.Tick(Math.Max(MathF.Abs(dryLeft), MathF.Abs(dryRight)));
                wetLeft *= duck;
                wetRight *= duck;
            }

            Stereo.Spread(ref wetLeft, ref wetRight, spread);

            currentMix += mixStep;
            io.Left[i] = dryLeft * (1f - currentMix) + wetLeft * currentMix;
            io.Right[i] = dryRight * (1f - currentMix) + wetRight * currentMix;
        }

        this.mix = mix;
        FlushDenormals();
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    /// <summary>
    ///     Equal-length circular lines in one native buffer (one object for all the network's lines, so a preset load
    ///     allocates little). Reads match <see cref="DelayBuffer" />; the allpass matches <see cref="ReverbLine" />.
    /// </summary>
    sealed class LineBank(int count) : IDisposable {
        readonly int[] writes = new int[count];
        NativeBuffer buffer = NativeBuffer.Empty;
        int length = 1;

        public void Allocate(int lineLength) {
            buffer.Dispose();
            length = lineLength;
            buffer = new(count * lineLength);
            Array.Clear(writes);
        }

        public void Clear() {
            buffer.Clear();
            Array.Clear(writes);
        }

        public void Dispose() => buffer.Dispose();

        /// <summary>A linear fractional read <paramref name="delay" /> samples back (1 = the most recent write).</summary>
        public float Read(int line, float delay) {
            var position = writes[line] - delay;
            var index = (int)MathF.Floor(position);
            var frac = position - index;
            var a = At(line, index);
            var b = At(line, index + 1);
            return a + (b - a) * frac;
        }

        public void Write(int line, float value) {
            ref var write = ref writes[line];
            buffer[line * length + write] = MathF.Abs(value) < 1e-20f ? 0f : value;
            if (++write == length) {
                write = 0;
            }
        }

        /// <summary>A Schroeder allpass of fractional length <paramref name="delay" /> on line <paramref name="line" />.</summary>
        public float Allpass(int line, float value, float delay, float gain) {
            var whole = (int)delay;
            var frac = delay - whole;
            var a = Tap(line, whole);
            var b = Tap(line, whole + 1);
            var delayed = a + (b - a) * frac;
            var node = value + gain * delayed;
            Write(line, node);
            return delayed - gain * node;
        }

        float Tap(int line, int delay) {
            var index = writes[line] - delay;
            return buffer[line * length + (index < 0 ? index + length : index)];
        }

        float At(int line, int index) {
            index %= length;
            return buffer[line * length + (index < 0 ? index + length : index)];
        }
    }

    /// <summary>Circular buffer with a fractional (linear) read behind the most recent write.</summary>
    sealed class DelayBuffer : IDisposable {
        NativeBuffer buffer = NativeBuffer.Empty;
        int write;

        public void Allocate(int length) {
            buffer.Dispose();
            buffer = new(length);
            write = 0;
        }

        public void Clear() {
            buffer.Clear();
            write = 0;
        }

        public void Dispose() => buffer.Dispose();

        public float Read(float delay) {
            var position = write - delay;
            var index = (int)MathF.Floor(position);
            var frac = position - index;
            var a = At(index);
            var b = At(index + 1);
            return a + (b - a) * frac;
        }

        public void Write(float value) {
            buffer[write] = MathF.Abs(value) < 1e-20f ? 0f : value;
            if (++write == buffer.Length) {
                write = 0;
            }
        }

        public float ReadWrite(float value, float delay) {
            Write(value);
            return Read(delay);
        }

        float At(int index) {
            index %= buffer.Length;
            return buffer[index < 0 ? index + buffer.Length : index];
        }
    }

    /// <summary>Schroeder allpass whose length can be set up to the allocated maximum.</summary>
    sealed class Allpass : IDisposable {
        NativeBuffer buffer = NativeBuffer.Empty;
        int length;
        int position;

        public void Allocate(int maxLength) {
            buffer.Dispose();
            buffer = new(maxLength);
            length = maxLength;
            position = 0;
        }

        public void Dispose() => buffer.Dispose();

        public void Clear() {
            buffer.Clear();
            position = 0;
        }

        /// <summary>Once per block; a change restarts the cursor inside the new length.</summary>
        public void SetLength(int value) {
            value = Math.Clamp(value, 1, buffer.Length);
            if (value != length) {
                length = value;
                position %= length;
            }
        }

        public float Process(float x, float gain) {
            var delayed = buffer[position];
            var v = x + gain * delayed;
            buffer[position] = MathF.Abs(v) < 1e-20f ? 0f : v;
            if (++position == length) {
                position = 0;
            }

            return delayed - gain * v;
        }
    }
}
