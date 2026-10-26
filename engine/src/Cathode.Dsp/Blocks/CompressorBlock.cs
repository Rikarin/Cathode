using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Feed-forward compressor after Giannoulis, Massberg and Reiss, "Digital Dynamic Range Compressor Design"
///     (JAES 2012): a soft-knee gain computer in dB followed by a smooth, decoupled attack/release detector on the gain.
///     Detection is linked across channels.
///     <list type="bullet">
///         <item><b>Pedal</b>: instantaneous peak level (VCA style, punchy).</item>
///         <item><b>Studio</b>: RMS level over about 5 ms (smoother, program-like).</item>
///         <item>
///             <b>Opto</b>: peak level with program-dependent release; the deeper the gain reduction, the slower it
///             recovers, like a photocell. <b>Light Type</b> picks the cell: Classic, a Fast LED (half the attack, a
///             shallower law) or a Slow vactrol (twice the attack, a steeper law).
///         </item>
///         <item>
///             <b>Sustainer</b>: upward compression on the Studio detector. Below the threshold, quiet signals are raised
///             by (1 - 1 / ratio) dB per dB, at most <see cref="MaxSustainDb" />, so notes sustain as they fade.
///         </item>
///         <item>
///             <b>Dynamics</b>: a dynamics processor. Above the threshold the slope is 1 - <b>Dynamics</b>: positive
///             values compress (100 % limits), negative ones expand upwards (-100 % doubles every dB over), at most
///             <see cref="MaxSustainDb" /> of boost.
///         </item>
///         <item>
///             <b>Feedback</b>: the classic feedback topology. The detector hears the compressed output (the input times
///             the last gain), so the effective ratio is softer and the release follows the program.
///         </item>
///         <item>
///             <b>JFET</b>, after the 1176: a feedback peak detector driving a JFET gain cell. The JFET is a voltage
///             controlled resistor only for small signals across it; the more it is pinched (the deeper the reduction),
///             the more it bends the signal, so the gain cell adds odd harmonics that grow with the gain reduction
///             (<see cref="JfetColour" />).
///         </item>
///         <item>
///             <b>Tube</b>, after the variable-mu compressors (Altec 436C): a remote-cutoff tube whose gain falls
///             gradually with its grid bias, so the knee is at least <see cref="TubeKneeDb" /> wide and the ratio grows
///             with the level; the detector is RMS and hears the output (the 436C feeds its rectifier from the output),
///             and the tube's asymmetric transfer adds a little second harmonic (DC-blocked).
///         </item>
///         <item>
///             <b>Compander</b>: a compressor and a downward expander in one. Above Threshold it compresses by Ratio;
///             below <b>Expander Threshold</b> it expands downward by <b>Expander Ratio</b> (at most 80 dB), the
///             expander closing with the release and opening with the attack, so the noise between notes drops while the
///             notes are levelled.
///         </item>
///     </list>
///     <b>Controls</b> Compression replaces Threshold and Ratio by one knob, as the guide's pedal and optical types
///     have: <b>Compression</b> 0–100 % lowers the threshold from −6 to −50 dB and raises the ratio from 1:1 to 20:1
///     together (<see cref="SingleKnob" />).
///     <b>Detector</b> overrides the type's level detector (peak, RMS, or the mean of the two in dB). <b>Ratio</b> at
///     <see cref="InfiniteRatio" /> is ∞:1, a limiter. <b>Auto Attack/Release</b> scales both times by the crest factor
///     (<see cref="CrestFactor" />). The detector can be fed through a sidechain low and high cut, an <b>Emphasis</b>
///     high shelf (so the bass pumps less) and a sidechain EQ band; <b>Input Level</b> Line takes 12 dB off the detector
///     for line-level sources. Auto Makeup adds back half the gain reduction a 0 dBFS signal would get. The mix follows
///     the Axe-Fx law (50 % = -6 dB each) for parallel compression.
///     <para>
///         Sidechain picks what the detector hears: the block's input, the grid input, another grid row
///         (a tap, <see cref="SideInputs.Taps" />) or one of the interface's inputs (<see cref="HardwareInputs" />); the
///         sidechain cuts, emphasis and EQ then filter that signal. Look Ahead
///         delays the audio (wet and dry alike) while the detector hears it undelayed, so the gain is already down when
///         a transient arrives; the block reports the delay as latency, so the compiler aligns parallel rows, and
///         changing it rebuilds the graph (<see cref="ParamDescriptor.Recompile" />).
///     </para>
/// </summary>
public sealed class CompressorBlock() : DspBlock(Info), ISideInputBlock {
    public const int Type = 0;
    public const int Threshold = 1;
    public const int Ratio = 2;
    public const int Attack = 3;
    public const int Release = 4;
    public const int Knee = 5;
    public const int Makeup = 6;
    public const int Mix = 7;
    public const int Level = 8;
    public const int Balance = 9;
    public const int AutoMakeup = 10;
    public const int SidechainLowCut = 11;
    public const int SidechainHighCut = 12;
    public const int Detector = 13;
    public const int AutoTime = 14;
    public const int Dynamics = 15;
    public const int LightType = 16;
    public const int Emphasis = 17;
    public const int EmphasisFrequency = 18;
    public const int SidechainFilter = 19;
    public const int SidechainFrequency = 20;
    public const int SidechainQ = 21;
    public const int SidechainGain = 22;
    public const int InputLevel = 23;
    public const int Sidechain = 24;
    public const int LookAhead = 25;
    public const int Controls = 26;
    public const int Compression = 27;
    public const int ExpanderThreshold = 28;
    public const int ExpanderRatio = 29;

    public const int Pedal = 0;
    public const int Studio = 1;
    public const int Opto = 2;
    public const int Sustainer = 3;
    public const int DynamicsProcessor = 4;
    public const int Feedback = 5;
    public const int Jfet = 6;
    public const int Tube = 7;
    public const int Compander = 8;

    /// <summary>How many types there are.</summary>
    public const int TypeCount = 9;

    /// <summary>The Tube type's narrowest knee, dB: a variable-mu tube's gain bends over a wide range.</summary>
    public const float TubeKneeDb = 18f;

    // Values of Detector.
    public const int DetectorOfType = 0;
    public const int DetectorPeak = 1;
    public const int DetectorRms = 2;
    public const int DetectorRmsPeak = 3;

    /// <summary>The most the Sustainer and the Dynamics type raise a signal, in dB.</summary>
    public const float MaxSustainDb = 24f;

    /// <summary>A ratio at or above this is ∞:1.</summary>
    public const float InfiniteRatio = 100f;

    /// <summary>Emphasis at 100 %: a high shelf of this gain on the detector, in dB.</summary>
    public const float MaxEmphasisDb = 12f;

    /// <summary>What Input Level Line takes off the detector, in dB.</summary>
    public const float LineLevelDb = 12f;

    const double RmsWindowMs = 5;

    /// <summary>How much the Tube type's transfer bends (its asymmetric share).</summary>
    const float TubeColour = 0.2f;

    /// <summary>The Tube type's grid bias, as an offset into its curve.</summary>
    const float TubeBias = 0.3f;

    /// <summary>The deepest the Compander's expander goes, dB.</summary>
    const float ExpanderFloorDb = -80f;

    /// <summary>The Look Ahead choices, in ms (a choice, not a knob: each change rebuilds the graph).</summary>
    public static readonly float[] LookAheadMs = [0f, 0.5f, 1f, 2f, 3f, 5f, 10f];

    readonly SidechainSource sidechainSource = new();
    float[] aheadLeft = [];
    float[] aheadRight = [];
    int ahead;
    int aheadPosition;
    double meanSquare;
    float expansion; // the Compander's expander gain, dB (0 or negative)
    float dcLeft, dcRight, dcInLeft, dcInRight; // the Tube type's DC blocker
    float mix = 1f;
    float lastGain = 1f;
    WetCuts sidechain;
    SvfState emphasisLeft, emphasisRight, filterLeft, filterRight;
    CrestFactor crest;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "compressor",
        "Compressor",
        "Dynamics",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Pedal", "Studio", "Opto", "Sustainer", "Dynamics", "Feedback", "JFET", "Tube", "Compander"]
            ),
            ParamDescriptor.Float("threshold", "Threshold", -60f, 0f, -20f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, InfiniteRatio, 4f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 100f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 10f, 2_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("knee", "Knee", 0f, 24f, 6f, "dB"),
            ParamDescriptor.Float("makeup", "Makeup", 0f, 30f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Switch("autoMakeup", "Auto Makeup"),
            WetCuts.LowCutParam("scLowCut", "Sidechain Low Cut"),
            WetCuts.HighCutParam("scHighCut", "Sidechain High Cut"),
            ParamDescriptor.Choice("detector", "Detector", ["Type Default", "Peak", "RMS", "RMS+Peak"]),
            ParamDescriptor.Switch("autoTime", "Auto Attack/Release"),
            Only(ParamDescriptor.Float("dynamics", "Dynamics", -100f, 100f, 50f, "%"), DynamicsProcessor),
            Only(ParamDescriptor.Choice("light", "Light Type", ["Classic", "Fast LED", "Slow Vactrol"]), Opto),
            ParamDescriptor.Float("emphasis", "Emphasis", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("emphasisFreq", "Emphasis Freq", 200f, 10_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("scFilter", "Sidechain Filter", ["Off", "Low Shelf", "High Shelf", "Peak"]),
            ParamDescriptor.Float("scFreq", "Sidechain Freq", 20f, 20_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("scQ", "Sidechain Q", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("scGain", "Sidechain Gain", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Choice("inputLevel", "Input Level", ["Instrument", "Line"]),
            SidechainSource.Param(),
            ParamDescriptor.Choice(
                "lookAhead",
                "Look Ahead",
                ["Off", "0.5 ms", "1 ms", "2 ms", "3 ms", "5 ms", "10 ms"]
            )
                with {
                Recompile = true
            },
            ParamDescriptor.Choice("controls", "Controls", ["Threshold/Ratio", "Compression"]),
            ParamDescriptor.Float("compression", "Compression", 0f, 100f, 50f, "%"),
            Only(ParamDescriptor.Float("expThreshold", "Expander Threshold", -90f, 0f, -60f, "dB"), Compander),
            Only(ParamDescriptor.Float("expRatio", "Expander Ratio", 1f, 10f, 2f, ":1", ParamTaper.Log), Compander)
        ],
        maxInstances: 4,
        pages: [
            new(
                "Basic",
                [
                    "threshold", "ratio", "attack", "release", "knee", "makeup", "autoMakeup", "compression",
                    "inputLevel", "controls", "light"
                ]
            ),
            new("Sidechain", ["scSource", "scLowCut", "scHighCut", "scFilter", "scFreq", "scQ", "scGain", "detector"]),
            new(
                "Advanced",
                [
                    "autoTime", "dynamics", "emphasis", "emphasisFreq", "lookAhead", "expThreshold", "expRatio"
                ]
            )
        ]
    );

    public InputPort? Inputs { get; set; }

    /// <summary>The look-ahead delay, fixed when the block is prepared (the graph compiler reads it then).</summary>
    public override int LatencySamples => ahead;

    /// <summary>Current gain reduction in dB (0 or negative; positive for upward types). For meters and tests.</summary>
    public float GainReductionDb { get; private set; }

    /// <summary>
    ///     The static curve: output level for input level <paramref name="x" />, all in dB. A ratio of
    ///     <see cref="InfiniteRatio" /> or more is ∞:1; a ratio below 1 expands.
    /// </summary>
    public static float StaticCurve(float x, float threshold, float ratio, float knee) {
        if (ratio >= InfiniteRatio) {
            return Limit(x, threshold, knee);
        }

        var over = x - threshold;
        if (2 * over < -knee) {
            return x;
        }

        if (knee > 0 && 2 * MathF.Abs(over) <= knee) {
            var t = over + knee / 2;
            return x + (1 / ratio - 1) * t * t / (2 * knee);
        }

        return threshold + over / ratio;
    }

    /// <summary>
    ///     The Dynamics type's ratio for <paramref name="dynamics" /> (-100..100 %): the slope over the threshold is 1 -
    ///     it.
    /// </summary>
    public static float DynamicsRatio(float dynamics) {
        var slope = 1f - dynamics / 100f;
        return slope <= 1f / InfiniteRatio ? InfiniteRatio : 1f / slope;
    }

    /// <summary>
    ///     The single Compression knob (0–100 %) as a threshold and ratio: −6 dB and 1:1 at 0, −50 dB and 20:1 at 100,
    ///     the ratio rising with the square so the first half stays gentle.
    /// </summary>
    public static (float Threshold, float Ratio) SingleKnob(float compression) {
        var c = Math.Clamp(compression / 100f, 0f, 1f);
        return (-6f - 44f * c, 1f + 19f * c * c);
    }

    /// <summary>
    ///     The JFET gain cell's distortion: <paramref name="v" /> through a curve that bends by
    ///     <paramref name="pinch" /> (0 = linear … 1), unity for small signals and monotonic, so it is bounded by the
    ///     input.
    /// </summary>
    public static float JfetColour(float v, float pinch) => (1f - pinch) * v + pinch * MathF.Tanh(v);

    /// <summary>The grid input and the row taps for the sidechain, and the hardware inputs (<see cref="Inputs" />).</summary>
    public SideInputs SideInputs => SideInputs.Taps | SideInputs.Hardware;

    /// <summary>Audio thread: keeps the tap the Sidechain parameter selects (<see cref="SidechainSource" />).</summary>
    public void SideInput(SideInputKind kind, int row, in StereoBuffer signal, in StereoBuffer destination) =>
        sidechainSource.Offer(row, signal, (int)Params.GetTarget(Sidechain));

    /// <summary>
    ///     The Tube type's output stage: a biased tanh blended in by <see cref="TubeColour" />, scaled back to unity
    ///     small-signal gain by <paramref name="slope" />, then a 5 Hz DC blocker for what the asymmetry leaves.
    /// </summary>
    static float TubeStage(float v, float slope, float coefficient, ref float input, ref float output) {
        var bias = MathF.Tanh(TubeBias);
        var shaped = ((1f - TubeColour) * v + TubeColour * (MathF.Tanh(v + TubeBias) - bias)) / slope;
        var blocked = shaped - input + coefficient * output;
        input = shaped;
        output = blocked;
        return blocked;
    }

    static float Limit(float x, float threshold, float knee) {
        var over = x - threshold;
        if (2 * over < -knee) {
            return x;
        }

        if (knee > 0 && 2 * MathF.Abs(over) <= knee) {
            var t = over + knee / 2;
            return x - t * t / (2 * knee);
        }

        return threshold;
    }

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    /// <summary>The parameter shown only on <paramref name="type" /> (the editor hides it elsewhere).</summary>
    static ParamDescriptor Only(ParamDescriptor descriptor, int type) =>
        descriptor with {
            TypeLabels = [..Enumerable.Range(0, TypeCount).Select(t => t == type ? descriptor.Name : string.Empty)]
        };

    protected override void OnPrepare(in ProcessSpec spec) {
        sidechainSource.Prepare(spec.MaxBlockSize);
        ahead = (int)Math.Round(LookAheadMs[(int)Params.GetTarget(LookAhead)] * 0.001 * spec.SampleRate);
        aheadLeft = new float[Math.Max(1, ahead)];
        aheadRight = new float[Math.Max(1, ahead)];
    }

    protected override void OnReset() {
        Array.Clear(aheadLeft);
        Array.Clear(aheadRight);
        aheadPosition = 0;
        GainReductionDb = 0;
        meanSquare = 0;
        expansion = 0;
        dcLeft = dcRight = dcInLeft = dcInRight = 0;
        lastGain = 1f;
        sidechain.Reset();
        emphasisLeft.Reset();
        emphasisRight.Reset();
        filterLeft.Reset();
        filterRight.Reset();
        crest.Reset();
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        sidechainSource.OfferHardware(Inputs, (int)Params.GetTarget(Sidechain), io.Length);
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var threshold = Params.Advance(Threshold, io.Length);
        var ratio = Params.Advance(Ratio, io.Length);
        var compression = Params.Advance(Compression, io.Length);
        if (Params.Current(Controls) >= 0.5f) {
            (threshold, ratio) = SingleKnob(compression);
        }

        var expanderThreshold = Params.Advance(ExpanderThreshold, io.Length);
        var expanderSlope = Params.Advance(ExpanderRatio, io.Length) - 1f;
        var dynamics = Params.Advance(Dynamics, io.Length);
        if (type == DynamicsProcessor) {
            ratio = DynamicsRatio(dynamics);
        }

        var knee = Params.Advance(Knee, io.Length);
        if (type == Tube) {
            knee = Math.Max(knee, TubeKneeDb);
        }

        var attackMs = Params.Advance(Attack, io.Length);
        var releaseMs = Params.Advance(Release, io.Length);
        var light = (int)Params.Current(LightType);
        if (type == Opto) {
            attackMs *= light switch {
                1 => 0.5f,
                2 => 2f,
                _ => 1f
            };
        }

        var attack = Coefficient(attackMs, rate);
        var release = Coefficient(releaseMs, rate);
        var makeup = Params.Advance(Makeup, io.Length);
        if (Params.Current(AutoMakeup) >= 0.5f) {
            makeup -= 0.5f * StaticCurve(0f, threshold, ratio, knee); // half the reduction at 0 dBFS
        }

        var upward = type == Sustainer;
        var boosting = type == DynamicsProcessor && ratio < 1f;
        var feedback = type is Feedback or Jfet or Tube;
        var compander = type == Compander;
        var detector = (int)Params.Current(Detector);
        if (detector == DetectorOfType) {
            detector = type is Pedal or Opto or Jfet ? DetectorPeak : DetectorRms;
        }

        var dcCoefficient = 1f - (float)(2 * Math.PI * 5 / rate);
        var tubeSlope = 1f - TubeColour + TubeColour * (1f - MathF.Tanh(TubeBias) * MathF.Tanh(TubeBias));
        var expansion = this.expansion;

        // Opto: this many dB of reduction double the release (the light type's law).
        var optoDb = light switch {
            1 => 12f,
            2 => 3f,
            _ => 6f
        };
        var auto = Params.Current(AutoTime) >= 0.5f;
        var crestCoefficient = CrestFactor.Coefficient(rate);
        var lineDb = Params.Current(InputLevel) >= 0.5f ? LineLevelDb : 0f;
        sidechain.Design(rate, Params.Advance(SidechainLowCut, io.Length), Params.Advance(SidechainHighCut, io.Length));
        var emphasisDb = Params.Advance(Emphasis, io.Length) / 100f * MaxEmphasisDb;
        var emphasisHz = Params.Advance(EmphasisFrequency, io.Length);
        var emphasis = emphasisDb > 0f;
        var emphasisShelf = emphasis
            ? SvfCoefficients.Design(BiquadType.HighShelf, rate, emphasisHz, 0.7071, emphasisDb)
            : default;
        var filterType = (int)Params.Current(SidechainFilter);
        var filterHz = Params.Advance(SidechainFrequency, io.Length);
        var filterQ = Params.Advance(SidechainQ, io.Length);
        var filterDb = Params.Advance(SidechainGain, io.Length);
        var filtered = filterType != 0 && filterDb != 0f;
        var filter = filtered
            ? SvfCoefficients.Design(
                filterType switch {
                    1 => BiquadType.LowShelf,
                    2 => BiquadType.HighShelf,
                    _ => BiquadType.Peak
                },
                rate,
                filterHz,
                filterQ,
                filterDb
            )
            : default;
        if (!emphasis) {
            emphasisLeft.Reset();
            emphasisRight.Reset();
        }

        if (!filtered) {
            filterLeft.Reset();
            filterRight.Reset();
        }

        var mix = Params.Advance(Mix, io.Length) / 100f;
        var rmsCoefficient = Coefficient(RmsWindowMs, rate);

        var reduction = GainReductionDb;
        var meanSquare = this.meanSquare;
        var lastGain = this.lastGain;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;
        var external = sidechainSource.Active && sidechainSource.Left.Length == io.Length;
        var sideLeft = sidechainSource.Left;
        var sideRight = sidechainSource.Right;
        var position = aheadPosition;

        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var detectLeft = external ? sideLeft[i] : left;
            var detectRight = external ? sideRight[i] : right;
            if (ahead > 0) {
                // The detector hears the input now; the audio comes out ahead samples later.
                (left, aheadLeft[position]) = (aheadLeft[position], left);
                (right, aheadRight[position]) = (aheadRight[position], right);
                position = position + 1 == ahead ? 0 : position + 1;
            }

            sidechain.Tick(ref detectLeft, ref detectRight);
            if (emphasis) {
                detectLeft = (float)emphasisLeft.Tick(emphasisShelf, detectLeft);
                detectRight = (float)emphasisRight.Tick(emphasisShelf, detectRight);
            }

            if (filtered) {
                detectLeft = (float)filterLeft.Tick(filter, detectLeft);
                detectRight = (float)filterRight.Tick(filter, detectRight);
            }

            if (feedback) {
                detectLeft *= lastGain; // the detector hears the output
                detectRight *= lastGain;
            }

            float levelDb;
            if (detector == DetectorRms) {
                var square = 0.5 * ((double)detectLeft * detectLeft + (double)detectRight * detectRight);
                meanSquare = square + (meanSquare - square) * rmsCoefficient;
                levelDb = meanSquare > 1e-20 ? (float)(10 * Math.Log10(meanSquare)) : -200f;
            } else if (detector == DetectorPeak) {
                var peak = Math.Max(MathF.Abs(detectLeft), MathF.Abs(detectRight));
                levelDb = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
            } else {
                var square = 0.5 * ((double)detectLeft * detectLeft + (double)detectRight * detectRight);
                meanSquare = square + (meanSquare - square) * rmsCoefficient;
                var rmsDb = meanSquare > 1e-20 ? (float)(10 * Math.Log10(meanSquare)) : -200f;
                var peak = Math.Max(MathF.Abs(detectLeft), MathF.Abs(detectRight));
                levelDb = 0.5f * (rmsDb + (peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f));
            }

            levelDb -= lineDb;
            var target = upward
                ? Math.Min(MaxSustainDb, -StaticCurve(-levelDb, -threshold, ratio, knee) - levelDb) // >= 0
                : StaticCurve(levelDb, threshold, ratio, knee) - levelDb; // <= 0 (>= 0 when expanding)
            if (boosting) {
                target = Math.Min(MaxSustainDb, target);
            }

            var attackCoefficient = attack;
            var releaseCoefficient = release;
            var autoReleaseMs = releaseMs;
            if (auto) {
                var scale = crest.Tick(Math.Max(MathF.Abs(detectLeft), MathF.Abs(detectRight)), crestCoefficient);
                attackCoefficient = Coefficient(attackMs * scale, rate);
                autoReleaseMs = releaseMs * scale;
                releaseCoefficient = Coefficient(autoReleaseMs, rate);
            }

            // Upward expansion follows a louder signal on the attack, as downward compression does.
            if (boosting ? target > reduction : target < reduction) {
                reduction = target + (reduction - target) * attackCoefficient;
            } else {
                var coefficient = type == Opto
                    ? Coefficient(autoReleaseMs * (1 - reduction / optoDb), rate) // deeper reduction, slower release
                    : releaseCoefficient;
                reduction = target + (reduction - target) * coefficient;
            }

            if (compander) {
                // The expander: below its threshold, every dB down drops by the expander ratio more.
                var under = Math.Max(ExpanderFloorDb, Math.Min(0f, expanderSlope * (levelDb - expanderThreshold)));
                expansion = under + (expansion - under) * (under > expansion ? attack : release);
            }

            var gain = MathF.Pow(10f, (reduction + expansion + makeup) / 20f);
            var cell = MathF.Pow(10f, reduction / 20f);
            lastGain = feedback ? cell : 1f;
            currentMix += mixStep;
            var wetLeft = left * gain;
            var wetRight = right * gain;
            if (type == Jfet) {
                // The FET sees the signal before its own attenuation: the deeper it is pinched, the more it bends.
                var pinch = 0.5f * (1f - cell);
                wetLeft = gain * JfetColour(left, pinch);
                wetRight = gain * JfetColour(right, pinch);
            } else if (type == Tube) {
                wetLeft = TubeStage(wetLeft, tubeSlope, dcCoefficient, ref dcInLeft, ref dcLeft);
                wetRight = TubeStage(wetRight, tubeSlope, dcCoefficient, ref dcInRight, ref dcRight);
            }

            io.Left[i] = left * (1 - currentMix) + wetLeft * currentMix;
            io.Right[i] = right * (1 - currentMix) + wetRight * currentMix;
        }

        this.expansion = expansion < -1e-6f ? expansion : 0f;
        dcLeft = MathF.Abs(dcLeft) < 1e-20f ? 0f : dcLeft;
        dcRight = MathF.Abs(dcRight) < 1e-20f ? 0f : dcRight;
        dcInLeft = MathF.Abs(dcInLeft) < 1e-20f ? 0f : dcInLeft;
        dcInRight = MathF.Abs(dcInRight) < 1e-20f ? 0f : dcInRight;

        aheadPosition = position;
        sidechainSource.Clear();
        GainReductionDb = MathF.Abs(reduction) < 1e-6f ? 0 : reduction;
        sidechain.FlushDenormals();
        emphasisLeft.FlushDenormals();
        emphasisRight.FlushDenormals();
        filterLeft.FlushDenormals();
        filterRight.FlushDenormals();
        crest.FlushDenormals();
        this.meanSquare = meanSquare < 1e-30 ? 0 : meanSquare;
        this.lastGain = lastGain;
        this.mix = mix;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
