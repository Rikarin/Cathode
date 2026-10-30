using Cathode.Dsp.Parameters;
using static Cathode.Dsp.Blocks.CompressorBlock;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The Compressor's static gain computer, its settings resolved for the type and the Controls mode: the
///     threshold and ratio the Compression knob sets (<see cref="SingleKnob" />), the Dynamics type's ratio
///     (<see cref="DynamicsRatio" />), the Tube type's wide knee, Auto Makeup, and the Compander's expander. The
///     block's audio path asks it for the gain to head for on every sample (<see cref="Target" />,
///     <see cref="Expansion" />), and the transfer curve the editor draws is <see cref="Output" />, so the curve and
///     the audio come from the same code. All levels are as the gain computer reads them: after Input Level's offset
///     and the sidechain filters.
/// </summary>
/// <param name="Type">The compressor type (<see cref="Pedal" /> … <see cref="Compander" />).</param>
/// <param name="Threshold">dB, after the Controls mode.</param>
/// <param name="Ratio">
///     After the Controls mode and the Dynamics type; <see cref="InfiniteRatio" /> or more is ∞:1.
/// </param>
/// <param name="Knee">dB, at least <see cref="TubeKneeDb" /> on the Tube type.</param>
/// <param name="Makeup">dB, Auto Makeup included.</param>
/// <param name="ExpanderThreshold">The Compander's expander threshold, dB.</param>
/// <param name="ExpanderSlope">The Compander's expander ratio less 1: how many dB more each dB under it drops.</param>
public readonly record struct CompressorCurve(
    int Type,
    float Threshold,
    float Ratio,
    float Knee,
    float Makeup,
    float ExpanderThreshold,
    float ExpanderSlope
) {
    /// <summary>The deepest the Compander's expander goes, dB.</summary>
    public const float ExpanderFloorDb = -80f;

    /// <summary>The smoothed parameters that shape the curve.</summary>
    static ReadOnlySpan<int> Shaping => [
        CompressorBlock.Threshold, CompressorBlock.Ratio, Compression, Dynamics, CompressorBlock.Knee,
        CompressorBlock.Makeup, CompressorBlock.ExpanderThreshold, ExpanderRatio
    ];

    /// <summary>
    ///     Whether the detector hears the gain cell's output (Feedback, JFET and Tube), so a steady level settles where
    ///     the gain is the target for the level it lets through.
    /// </summary>
    public bool Feedback => Type is CompressorBlock.Feedback or Jfet or Tube;

    /// <summary>
    ///     Audio thread: advances the smoothed parameters the curve depends on by <paramref name="frames" /> and
    ///     returns the curve for them (<see cref="Of" />).
    /// </summary>
    public static CompressorCurve Advance(ParamBank values, int frames) {
        foreach (var index in Shaping) {
            values.Advance(index, frames);
        }

        return Of(values);
    }

    /// <summary>
    ///     The curve for the block's current values (<paramref name="values" />, smoothed as the audio thread last
    ///     advanced them; their targets once the block is reset).
    /// </summary>
    public static CompressorCurve Of(ParamBank values) {
        var type = (int)values.Current(CompressorBlock.Type);
        var threshold = values.Current(CompressorBlock.Threshold);
        var ratio = values.Current(CompressorBlock.Ratio);
        if (values.Current(Controls) >= 0.5f) {
            (threshold, ratio) = SingleKnob(values.Current(Compression));
        }

        if (type == DynamicsProcessor) {
            ratio = DynamicsRatio(values.Current(Dynamics));
        }

        var knee = values.Current(CompressorBlock.Knee);
        if (type == Tube) {
            knee = Math.Max(knee, TubeKneeDb);
        }

        var makeup = values.Current(CompressorBlock.Makeup);
        if (values.Current(AutoMakeup) >= 0.5f) {
            makeup += AutoMakeupDb(type, threshold, ratio, knee, LineDb(values));
        }

        return new(
            type,
            threshold,
            ratio,
            knee,
            makeup,
            values.Current(CompressorBlock.ExpanderThreshold),
            values.Current(ExpanderRatio) - 1f
        );
    }

    /// <summary>What Input Level takes off the detector, dB: <see cref="LineLevelDb" /> on Line, else 0.</summary>
    public static float LineDb(ParamBank values) => values.Current(InputLevel) >= 0.5f ? LineLevelDb : 0f;

    /// <summary>
    ///     The compressor's gain for a steady <paramref name="levelDb" />, dB: 0 or less as it compresses, more for
    ///     the Sustainer and the Dynamics type's upward expansion (at most <see cref="MaxSustainDb" />). Makeup and the
    ///     Compander's expander are apart.
    /// </summary>
    public float Target(float levelDb) {
        if (Type == Sustainer) {
            return Math.Min(MaxSustainDb, -StaticCurve(-levelDb, -Threshold, Ratio, Knee) - levelDb); // >= 0
        }

        var target = StaticCurve(levelDb, Threshold, Ratio, Knee) - levelDb; // <= 0 (>= 0 when expanding)
        return Type == DynamicsProcessor && Ratio < 1f ? Math.Min(MaxSustainDb, target) : target;
    }

    /// <summary>
    ///     The Compander's expander gain for a steady <paramref name="levelDb" />, dB: below its threshold every dB
    ///     down drops by the expander ratio more, at most to <see cref="ExpanderFloorDb" />; 0 on the other types.
    /// </summary>
    public float Expansion(float levelDb) =>
        Type == Compander
            ? Math.Max(ExpanderFloorDb, Math.Min(0f, ExpanderSlope * (levelDb - ExpanderThreshold)))
            : 0f;

    /// <summary>
    ///     The transfer curve: the output level a steady <paramref name="inputDb" /> settles at, dB. On the feedback
    ///     types the detector hears the output, so the gain settles where it is the target for the level it lets
    ///     through (<see cref="Settle" />): their effective ratio is softer than the knob's.
    /// </summary>
    public float Output(float inputDb) {
        var gain = Feedback ? Settle(inputDb) : Target(inputDb) + Expansion(inputDb);
        return inputDb + gain + Makeup;
    }

    /// <summary>
    ///     The feedback types' steady gain for <paramref name="inputDb" />: the r with r = Target(input + r), found by
    ///     bisection between Target(input) and 0 (the target only falls as the level rises, so it lies between).
    /// </summary>
    float Settle(float inputDb) {
        var low = Math.Min(0f, Target(inputDb));
        var high = 0f;
        for (var i = 0; i < 40 && high - low > 1e-5f; i++) {
            var middle = 0.5f * (low + high);
            if (Target(inputDb + middle) < middle) {
                high = middle;
            } else {
                low = middle;
            }
        }

        return 0.5f * (low + high);
    }
}
