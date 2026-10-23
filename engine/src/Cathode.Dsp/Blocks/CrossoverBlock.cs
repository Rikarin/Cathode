using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Two-way Linkwitz–Riley 4th-order crossover. A block is processed in place, so the two bands are recombined here
///     with their own level and balance; at unity they sum to an allpass, flat in magnitude. Cascade two blocks for
///     three ways.
///     <list type="bullet">
///         <item><b>Stereo</b>: each channel is split, and each band's balance attenuates the opposite side.</item>
///         <item><b>Low Left / High Right</b>: the mono sum is split, the low band goes left and the high band right.</item>
///     </list>
/// </summary>
public sealed class CrossoverBlock() : DspBlock(Info) {
    public const int Frequency = 0;
    public const int LowLevel = 1;
    public const int HighLevel = 2;
    public const int LowPan = 3;
    public const int HighPan = 4;
    public const int Mode = 5;
    public const int Level = 6;

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
            ParamDescriptor.Float("lowLevel", "Low Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("highLevel", "High Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("lowPan", "Low Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("highPan", "High Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("mode", "Mode", ["Stereo", "Low Left / High Right"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2,
        channels: 2
    );

    /// <summary>Balance law: centre is unity on both sides, panning attenuates the opposite side linearly.</summary>
    static (float Left, float Right) Balance(float pan) => (pan > 0 ? 1 - pan : 1, pan < 0 ? 1 + pan : 1);

    Gains Target(int length) {
        var output = Decibels.ToGain(Params.Advance(Level, length));
        var low = Decibels.ToGain(Params.Advance(LowLevel, length)) * output;
        var high = Decibels.ToGain(Params.Advance(HighLevel, length)) * output;
        var lowPan = Params.Advance(LowPan, length) / 100f;
        var highPan = Params.Advance(HighPan, length) / 100f;
        if (Params.Current(Mode) >= 0.5f) {
            return new(low, 0, 0, high);
        }

        var (lowLeft, lowRight) = Balance(lowPan);
        var (highLeft, highRight) = Balance(highPan);
        return new(low * lowLeft, low * lowRight, high * highLeft, high * highRight);
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
                    l[i] = (float)(g.LowLeft * low);
                    r[i] = (float)(g.HighRight * high);
                } else {
                    left.Tick(c, l[i], out var lowLeft, out var highLeft);
                    right.Tick(c, r[i], out var lowRight, out var highRight);
                    l[i] = (float)(g.LowLeft * lowLeft + g.HighLeft * highLeft);
                    r[i] = (float)(g.LowRight * lowRight + g.HighRight * highRight);
                }
            }

            gains = to;
        }

        left.FlushDenormals();
        right.FlushDenormals();
    }

    /// <summary>Per-band, per-side output gains (band level x balance x output level).</summary>
    readonly record struct Gains(float LowLeft, float LowRight, float HighLeft, float HighRight) {
        public static Gains Lerp(in Gains a, in Gains b, float t) => new(
            a.LowLeft + (b.LowLeft - a.LowLeft) * t,
            a.LowRight + (b.LowRight - a.LowRight) * t,
            a.HighLeft + (b.HighLeft - a.HighLeft) * t,
            a.HighRight + (b.HighRight - a.HighRight) * t
        );
    }
}
