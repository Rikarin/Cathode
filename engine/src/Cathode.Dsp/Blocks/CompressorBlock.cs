using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

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
///             recovers, like a photocell.
///         </item>
///         <item>
///             <b>Sustainer</b>: upward compression on the Studio detector. Below the threshold, quiet signals are raised
///             by (1 - 1 / ratio) dB per dB, at most <see cref="MaxSustainDb" />, so notes sustain as they fade.
///         </item>
///     </list>
///     The detector can be fed through a sidechain low and high cut, and Auto Makeup adds back half the gain reduction a
///     0 dBFS signal would get. The mix follows the Axe-Fx law (50 % = -6 dB each) for parallel compression.
/// </summary>
public sealed class CompressorBlock() : DspBlock(Info) {
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

    public const int Pedal = 0;
    public const int Studio = 1;
    public const int Opto = 2;
    public const int Sustainer = 3;

    /// <summary>The most the Sustainer raises a quiet signal, in dB.</summary>
    public const float MaxSustainDb = 24f;

    const double RmsWindowMs = 5;

    double meanSquare;
    float mix = 1f;
    WetCuts sidechain;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "compressor",
        "Compressor",
        "Dynamics",
        [
            ParamDescriptor.Choice("type", "Type", ["Pedal", "Studio", "Opto", "Sustainer"]),
            ParamDescriptor.Float("threshold", "Threshold", -60f, 0f, -20f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, 20f, 4f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 100f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 10f, 2_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("knee", "Knee", 0f, 24f, 6f, "dB"),
            ParamDescriptor.Float("makeup", "Makeup", 0f, 30f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Switch("autoMakeup", "Auto Makeup"),
            WetCuts.LowCutParam("scLowCut", "Sidechain Low Cut"),
            WetCuts.HighCutParam("scHighCut", "Sidechain High Cut")
        ],
        maxInstances: 4
    );

    /// <summary>Current gain reduction in dB (0 or negative). For meters and tests.</summary>
    public float GainReductionDb { get; private set; }

    /// <summary>The static curve: output level for input level <paramref name="x" />, all in dB.</summary>
    public static float StaticCurve(float x, float threshold, float ratio, float knee) {
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

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    protected override void OnReset() {
        GainReductionDb = 0;
        meanSquare = 0;
        sidechain.Reset();
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var threshold = Params.Advance(Threshold, io.Length);
        var ratio = Params.Advance(Ratio, io.Length);
        var knee = Params.Advance(Knee, io.Length);
        var attack = Coefficient(Params.Advance(Attack, io.Length), rate);
        var releaseMs = Params.Advance(Release, io.Length);
        var release = Coefficient(releaseMs, rate);
        var makeup = Params.Advance(Makeup, io.Length);
        if (Params.Current(AutoMakeup) >= 0.5f) {
            makeup -= 0.5f * StaticCurve(0f, threshold, ratio, knee); // half the reduction at 0 dBFS
        }

        var upward = type == Sustainer;
        sidechain.Design(rate, Params.Advance(SidechainLowCut, io.Length), Params.Advance(SidechainHighCut, io.Length));
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var rmsCoefficient = Coefficient(RmsWindowMs, rate);

        var reduction = GainReductionDb;
        var meanSquare = this.meanSquare;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var detectLeft = left;
            var detectRight = right;
            sidechain.Tick(ref detectLeft, ref detectRight);

            float levelDb;
            if (type is Studio or Sustainer) {
                var square = 0.5 * ((double)detectLeft * detectLeft + (double)detectRight * detectRight);
                meanSquare = square + (meanSquare - square) * rmsCoefficient;
                levelDb = meanSquare > 1e-20 ? (float)(10 * Math.Log10(meanSquare)) : -200f;
            } else {
                var peak = Math.Max(MathF.Abs(detectLeft), MathF.Abs(detectRight));
                levelDb = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
            }

            var target = upward
                ? Math.Min(MaxSustainDb, -StaticCurve(-levelDb, -threshold, ratio, knee) - levelDb) // >= 0
                : StaticCurve(levelDb, threshold, ratio, knee) - levelDb; // <= 0
            if (target < reduction) {
                reduction = target + (reduction - target) * attack;
            } else {
                var coefficient = type == Opto
                    ? Coefficient(releaseMs * (1 - reduction / 6), rate) // opto: 6 dB of reduction doubles the release
                    : release;
                reduction = target + (reduction - target) * coefficient;
            }

            var gain = MathF.Pow(10f, (reduction + makeup) / 20f);
            currentMix += mixStep;
            io.Left[i] = left * (1 - currentMix) + left * gain * currentMix;
            io.Right[i] = right * (1 - currentMix) + right * gain * currentMix;
        }

        GainReductionDb = MathF.Abs(reduction) < 1e-6f ? 0 : reduction;
        sidechain.FlushDenormals();
        this.meanSquare = meanSquare < 1e-30 ? 0 : meanSquare;
        this.mix = mix;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
