using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Two-way Linkwitz–Riley 4th-order crossover. A block is processed in place, so the two bands are recombined here
///     with their own levels and pans; at unity they sum to an allpass, flat in magnitude. Cascade two blocks for
///     three ways.
///     <list type="bullet">
///         <item>
///             <b>Stereo</b>: each channel is split; each of the four bands (the left and right input's low and high) has
///             its own level, and its own pan with the balance law (the far side is turned down). The pans default to hard
///             left for the left input's bands and hard right for the right's, which leaves the image as it is.
///         </item>
///         <item><b>Low Left / High Right</b>: the mono sum is split, the low band goes left and the high band right.</item>
///     </list>
/// </summary>
public sealed class CrossoverBlock() : DspBlock(Info) {
    public const int Frequency = 0;
    public const int LowLevelLeft = 1;
    public const int LowLevelRight = 2;
    public const int HighLevelLeft = 3;
    public const int HighLevelRight = 4;
    public const int LowPanLeft = 5;
    public const int LowPanRight = 6;
    public const int HighPanLeft = 7;
    public const int HighPanRight = 8;
    public const int Mode = 9;
    public const int Level = 10;
    public const int Balance = 11;

    const int SubBlock = 32;

    LinkwitzRileySplit left;
    LinkwitzRileySplit right;
    Gains gains;

    public static BlockDescriptor Info { get; } = new(
        "crossover",
        "Crossover",
        "Utility",
        [
            ParamDescriptor.Float("frequency", "Frequency", 40f, 16_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("lowLevelL", "Low Level L", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("lowLevelR", "Low Level R", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("highLevelL", "High Level L", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("highLevelR", "High Level R", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("lowPanL", "Low Pan L", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("lowPanR", "Low Pan R", -100f, 100f, 100f, "%"),
            ParamDescriptor.Float("highPanL", "High Pan L", -100f, 100f, -100f, "%"),
            ParamDescriptor.Float("highPanR", "High Pan R", -100f, 100f, 100f, "%"),
            ParamDescriptor.Choice("mode", "Mode", ["Stereo", "Low Left / High Right"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 2,
        channels: 2
    );

    /// <summary>Balance law: a band panned to <paramref name="pan" /> (-1..1) keeps unity on the near side.</summary>
    static (float Left, float Right) PanGains(float pan) => (Math.Min(1f, 1f - pan), Math.Min(1f, 1f + pan));

    Gains Target(int length) {
        var (outLeft, outRight) = OutputStage.Gains(Params.Advance(Level, length), Params.Advance(Balance, length));
        var lowL = Decibels.ToGain(Params.Advance(LowLevelLeft, length));
        var lowR = Decibels.ToGain(Params.Advance(LowLevelRight, length));
        var highL = Decibels.ToGain(Params.Advance(HighLevelLeft, length));
        var highR = Decibels.ToGain(Params.Advance(HighLevelRight, length));
        var (lowLToLeft, lowLToRight) = PanGains(Params.Advance(LowPanLeft, length) / 100f);
        var (lowRToLeft, lowRToRight) = PanGains(Params.Advance(LowPanRight, length) / 100f);
        var (highLToLeft, highLToRight) = PanGains(Params.Advance(HighPanLeft, length) / 100f);
        var (highRToLeft, highRToRight) = PanGains(Params.Advance(HighPanRight, length) / 100f);
        if (Params.Current(Mode) >= 0.5f) {
            return new(lowL * outLeft, 0, 0, 0, 0, 0, 0, highR * outRight);
        }

        return new(
            lowL * lowLToLeft * outLeft,
            lowL * lowLToRight * outRight,
            lowR * lowRToLeft * outLeft,
            lowR * lowRToRight * outRight,
            highL * highLToLeft * outLeft,
            highL * highLToRight * outRight,
            highR * highRToLeft * outLeft,
            highR * highRToRight * outRight
        );
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        gains = Target(0);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var split = Params.Current(Mode) >= 0.5f;
        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var c = LinkwitzRileyCoefficients.Design(Spec.SampleRate, Params.Advance(Frequency, length));
            var from = gains;
            var to = Target(length);
            var l = io.Left.Slice(start, length);
            var r = io.Right.Slice(start, length);
            for (var i = 0; i < length; i++) {
                var t = (i + 1f) / length;
                var g = Gains.Lerp(from, to, t);
                if (split) {
                    left.Tick(c, 0.5 * (l[i] + r[i]), out var low, out var high);
                    l[i] = (float)(g.LowLToLeft * low);
                    r[i] = (float)(g.HighRToRight * high);
                } else {
                    left.Tick(c, l[i], out var lowLeft, out var highLeft);
                    right.Tick(c, r[i], out var lowRight, out var highRight);
                    l[i] = (float)(g.LowLToLeft * lowLeft + g.LowRToLeft * lowRight + g.HighLToLeft * highLeft
                        + g.HighRToLeft * highRight);
                    r[i] = (float)(g.LowLToRight * lowLeft + g.LowRToRight * lowRight + g.HighLToRight * highLeft
                        + g.HighRToRight * highRight);
                }
            }

            gains = to;
        }

        left.FlushDenormals();
        right.FlushDenormals();
    }

    /// <summary>Each input band's gain into each output side (band level x pan x output level).</summary>
    readonly record struct Gains(
        float LowLToLeft,
        float LowLToRight,
        float LowRToLeft,
        float LowRToRight,
        float HighLToLeft,
        float HighLToRight,
        float HighRToLeft,
        float HighRToRight
    ) {
        public static Gains Lerp(in Gains a, in Gains b, float t) => new(
            a.LowLToLeft + (b.LowLToLeft - a.LowLToLeft) * t,
            a.LowLToRight + (b.LowLToRight - a.LowLToRight) * t,
            a.LowRToLeft + (b.LowRToLeft - a.LowRToLeft) * t,
            a.LowRToRight + (b.LowRToRight - a.LowRToRight) * t,
            a.HighLToLeft + (b.HighLToLeft - a.HighLToLeft) * t,
            a.HighLToRight + (b.HighLToRight - a.HighLToRight) * t,
            a.HighRToLeft + (b.HighRToLeft - a.HighRToLeft) * t,
            a.HighRToRight + (b.HighRToRight - a.HighRToRight) * t
        );
    }
}
