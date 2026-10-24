using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Filter block. The biquad types use a trapezoidal state-variable filter (RBJ-equivalent responses) because their
///     parameters are smoothed and modulated; a direct-form biquad produced +60 dBFS transients under fast sweeps.
///     <list type="bullet">
///         <item>Order 4th cascades two identical sections (pass, stop and all-pass types).</item>
///         <item>Tilt EQ: a low shelf cut and a high shelf boost of half the gain each, hinged at the frequency.</item>
///         <item>Null: no filter, for the gain, pan and phase controls alone.</item>
///         <item>All Pass can have feedback around it (one sample later), which turns its phase sweep into peaks.</item>
///         <item>
///             Low and High Shelf 2: shelves whose Q may rise to <see cref="MaxShelf2Q" />, so they overshoot at the corner
///             as passive shelving networks do. Peaking 2: the peak narrows as its gain grows (proportional Q: Q times
///             the square root of the linear gain).
///         </item>
///         <item>
///             FF and FB Comb: the signal plus Depth times itself (feed-forward) or its output (feedback) Comb Time
///             earlier, giving notches or peaks at multiples of 1 / time.
///         </item>
///     </list>
///     A built-in LFO, with duty and quantise, can sweep the frequency between Frequency and Mod Frequency (log-spaced).
///     While anything that
///     shapes the filter is moving, coefficients are recomputed every <see cref="SubBlock" /> samples on a grid
///     counted from the start of the stream, so the result does not depend on the callback size. Then come a first-order
///     (6 dB/oct) low and high cut, the pans of each input side, phase invert, and level and balance.
/// </summary>
public sealed class FilterBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Frequency = 1;
    public const int Q = 2;
    public const int Gain = 3;
    public const int Level = 4;
    public const int Balance = 5;
    public const int Order = 6;
    public const int LowCut = 7;
    public const int HighCut = 8;
    public const int PanLeft = 9;
    public const int PanRight = 10;
    public const int PhaseInvert = 11;
    public const int CombTime = 12;
    public const int Depth = 13;
    public const int LfoOn = 14;
    public const int LfoType = 15;
    public const int LfoRate = 16;
    public const int LfoTempo = 17;
    public const int ModFrequency = 18;
    public const int Feedback = 19;
    public const int LfoDuty = 20;
    public const int LfoQuantize = 21;

    /// <summary>Type indices beyond the seven biquad responses (which match <see cref="BiquadType" />).</summary>
    public const int Null = 7;

    public const int AllPass = 8;
    public const int Tilt = 9;
    public const int FeedForwardComb = 10;
    public const int FeedbackComb = 11;
    public const int LowShelf2 = 12;
    public const int HighShelf2 = 13;
    public const int Peaking2 = 14;

    public const double MaxShelf2Q = 4;

    const int SubBlock = 32;
    const float MaxCombMs = 20f;
    const float MaxCombFeedback = 0.98f;

    readonly FractionalDelayLine combLeft = new();
    readonly FractionalDelayLine combRight = new();
    SvfCoefficients first;
    SvfCoefficients second;
    SvfState left;
    SvfState right;
    SvfState secondLeft;
    SvfState secondRight;
    FirstOrderCuts cuts;
    Lfo lfo;
    int phase;
    float allPassLeft;
    float allPassRight;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "filter",
        "Filter",
        "Filter",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                [
                    "Low Pass",
                    "High Pass",
                    "Band Pass",
                    "Notch",
                    "Peaking",
                    "Low Shelf",
                    "High Shelf",
                    "Null",
                    "All Pass",
                    "Tilt EQ",
                    "FF Comb",
                    "FB Comb",
                    "Low Shelf 2",
                    "High Shelf 2",
                    "Peaking 2"
                ]
            ),
            ParamDescriptor.Float("freq", "Frequency", 20f, 20_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("q", "Q", 0.1f, 18f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("gain", "Gain", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("order", "Order", ["2nd", "4th"]),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Float("panLeft", "Pan Left", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("panRight", "Pan Right", -100f, 100f, 100f, "%"),
            ParamDescriptor.Choice("phaseInvert", "Phase Invert", ChorusBlock.PhaseOptions),
            ParamDescriptor.Float("combTime", "Comb Time", 0.1f, MaxCombMs, 1f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", -100f, 100f, 50f, "%"),
            ParamDescriptor.Switch("lfoOn", "LFO Enable"),
            ParamDescriptor.Choice("lfoType", "LFO Type", Lfo.ShapeNames),
            ParamDescriptor.Float("lfoRate", "LFO Rate", 0.05f, 20f, 1f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("lfoTempo", "LFO Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("modFreq", "Mod Frequency", 20f, 20_000f, 4_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("feedback", "Feedback", -95f, 95f, 0f, "%"),
            ParamDescriptor.Float("lfoDuty", "LFO Duty", 0f, 100f, 50f, "%"),
            LfoConditioner.QuantizeParam()
        ],
        maxInstances: 4
    );

    /// <summary>The biquad response a type uses, or null for the types that are not one SVF section.</summary>
    static BiquadType? Response(int type) =>
        type switch {
            < Null => (BiquadType)type,
            AllPass => BiquadType.AllPass,
            LowShelf2 => BiquadType.LowShelf,
            HighShelf2 => BiquadType.HighShelf,
            Peaking2 => BiquadType.Peak,
            _ => null
        };

    protected override void OnPrepare(in ProcessSpec spec) {
        var capacity = (int)(MaxCombMs * 0.001 * spec.SampleRate) + 8;
        combLeft.Allocate(capacity);
        combRight.Allocate(capacity);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        secondLeft.Reset();
        secondRight.Reset();
        combLeft.Reset();
        combRight.Reset();
        cuts.Reset();
        lfo.Reset();
        phase = 0;
        allPassLeft = 0;
        allPassRight = 0;
        UpdateCoefficients(Params.Current(Frequency));
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            combLeft.Dispose();
            combRight.Dispose();
        }

        base.Dispose(disposing);
    }

    void UpdateCoefficients(float frequency) {
        var type = (int)Params.Current(Type);
        var q = Params.Current(Q);
        var gain = Params.Current(Gain);
        var rate = Spec.SampleRate;
        if (type == Tilt) {
            first = SvfCoefficients.Design(BiquadType.LowShelf, rate, frequency, 0.7071, -0.5 * gain);
            second = SvfCoefficients.Design(BiquadType.HighShelf, rate, frequency, 0.7071, 0.5 * gain);
        } else if (type is LowShelf2 or HighShelf2) {
            first = SvfCoefficients.Design(Response(type)!.Value, rate, frequency, q, gain, MaxShelf2Q);
            second = first;
        } else if (type == Peaking2) {
            first = SvfCoefficients.Design(BiquadType.Peak, rate, frequency, q * Math.Sqrt(Decibels.ToGain(Math.Abs(gain))), gain);
            second = first;
        } else if (Response(type) is { } response) {
            first = SvfCoefficients.Design(response, rate, frequency, q, gain);
            second = first;
        }
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        var lfoOn = Params.Current(LfoOn) >= 0.5f;
        var moving = lfoOn
            || Params.IsRamping(Type)
            || Params.IsRamping(Frequency)
            || Params.IsRamping(Q)
            || Params.IsRamping(Gain);
        var cascade = type == Tilt || ((int)Params.Current(Order) == 1 && type is <= (int)BiquadType.Notch or AllPass);
        var freeRate = Params.Advance(LfoRate, io.Length);
        var lfoRate = TempoDivisions.Rate((int)Params.Current(LfoTempo), context.Tempo, freeRate);
        var shape = (LfoShape)(int)Params.Current(LfoType);
        var duty = Params.Advance(LfoDuty, io.Length) / 100.0;
        var quantize = (int)Params.Current(LfoQuantize);
        var modFrequency = Params.Advance(ModFrequency, io.Length);
        var allPassFeedback = Params.Advance(Feedback, io.Length) / 100f;
        var feedback = type == AllPass && allPassFeedback != 0;
        if (!feedback) {
            (allPassLeft, allPassRight) = (0f, 0f);
        }

        if (type is FeedForwardComb or FeedbackComb) {
            Params.Advance(Frequency, io.Length);
            Params.Advance(Q, io.Length);
            Params.Advance(Gain, io.Length);
            Comb(io, type == FeedbackComb);
        } else if (type != Null) {
            for (var start = 0; start < io.Length;) {
                var length = moving ? Math.Min(SubBlock - phase, io.Length - start) : io.Length;
                var frequency = Params.Advance(Frequency, length);
                Params.Advance(Q, length);
                Params.Advance(Gain, length);
                if (!moving || phase == 0) {
                    if (lfoOn) {
                        // The LFO's 0..1 position moves log-wise from Frequency to Mod Frequency.
                        var value = lfo.Value(shape, 0, duty);
                        if (quantize > 0) {
                            value = LfoConditioner.Quantize(value, LfoConditioner.Levels(quantize));
                        }

                        var position = 0.5f * (1 + value);
                        frequency *= MathF.Pow(modFrequency / frequency, position);
                    }

                    UpdateCoefficients(frequency);
                }

                if (lfoOn) {
                    lfo.Advance(lfoRate * length, Spec.SampleRate);
                }

                var chunk = io.Slice(start, length);
                if (feedback) {
                    AllPassWithFeedback(chunk, cascade, allPassFeedback);
                    phase = moving ? (phase + length) % SubBlock : 0;
                    start += length;
                    continue;
                }

                left.Process(first, chunk.Left);
                right.Process(first, chunk.Right);
                if (cascade) {
                    secondLeft.Process(second, chunk.Left);
                    secondRight.Process(second, chunk.Right);
                }

                phase = moving ? (phase + length) % SubBlock : 0;
                start += length;
            }
        } else {
            Params.Advance(Frequency, io.Length);
            Params.Advance(Q, io.Length);
            Params.Advance(Gain, io.Length);
        }

        Finish(io);
    }

    /// <summary>The all-pass (one or two sections) with its output fed back to its input one sample later.</summary>
    void AllPassWithFeedback(in StereoBuffer chunk, bool cascade, float feedback) {
        for (var i = 0; i < chunk.Length; i++) {
            var l = left.Tick(first, chunk.Left[i] + feedback * allPassLeft);
            var r = right.Tick(first, chunk.Right[i] + feedback * allPassRight);
            if (cascade) {
                l = secondLeft.Tick(second, l);
                r = secondRight.Tick(second, r);
            }

            allPassLeft = MathF.Abs((float)l) < 1e-20f ? 0f : (float)l;
            allPassRight = MathF.Abs((float)r) < 1e-20f ? 0f : (float)r;
            chunk.Left[i] = allPassLeft;
            chunk.Right[i] = allPassRight;
        }

        left.FlushDenormals();
        right.FlushDenormals();
        secondLeft.FlushDenormals();
        secondRight.FlushDenormals();
    }

    /// <summary>Feed-forward or feedback comb at Comb Time with gain Depth.</summary>
    void Comb(in StereoBuffer io, bool feedback) {
        var delay = Params.Advance(CombTime, io.Length) * 0.001f * (float)Spec.SampleRate;
        var depth = Params.Advance(Depth, io.Length) / 100f;
        if (feedback) {
            depth = Math.Clamp(depth, -MaxCombFeedback, MaxCombFeedback);
        }

        for (var i = 0; i < io.Length; i++) {
            var x = io.Left[i];
            var y = x + depth * combLeft.Read(delay);
            combLeft.Write(feedback ? y : x);
            io.Left[i] = y;

            x = io.Right[i];
            y = x + depth * combRight.Read(delay);
            combRight.Write(feedback ? y : x);
            io.Right[i] = y;
        }
    }

    /// <summary>Low and high cut, the input sides' pans, phase invert, then level and balance.</summary>
    void Finish(in StereoBuffer io) {
        var n = io.Length;
        cuts.Design(Spec.SampleRate, Params.Advance(LowCut, n), Params.Advance(HighCut, n));
        var panLeft = Params.Advance(PanLeft, n) / 100f;
        var panRight = Params.Advance(PanRight, n) / 100f;
        var (polarityLeft, polarityRight) = ChorusBlock.Polarity((int)Params.Current(PhaseInvert));
        var panned = panLeft != -1f || panRight != 1f;
        var (leftToLeft, leftToRight) = (Math.Min(1f, 1f - panLeft), Math.Min(1f, 1f + panLeft));
        var (rightToLeft, rightToRight) = (Math.Min(1f, 1f - panRight), Math.Min(1f, 1f + panRight));
        for (var i = 0; i < n; i++) {
            var l = io.Left[i];
            var r = io.Right[i];
            cuts.Tick(ref l, ref r);
            if (panned) {
                (l, r) = (l * leftToLeft + r * rightToLeft, l * leftToRight + r * rightToRight);
            }

            io.Left[i] = polarityLeft * l;
            io.Right[i] = polarityRight * r;
        }

        cuts.FlushDenormals();
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }
}
