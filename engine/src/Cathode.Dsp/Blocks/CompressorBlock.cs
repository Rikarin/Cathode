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
///     </list>
///     The mix follows the Axe-Fx law (50 % = -6 dB each) for parallel compression.
/// </summary>
public sealed class CompressorBlock : DspBlock {
    public const int Type = 0;
    public const int Threshold = 1;
    public const int Ratio = 2;
    public const int Attack = 3;
    public const int Release = 4;
    public const int Knee = 5;
    public const int Makeup = 6;
    public const int Mix = 7;
    public const int Level = 8;

    private const double RmsWindowMs = 5;

    public static BlockDescriptor Info { get; } = new(
        "compressor",
        "Compressor",
        "Dynamics",
        [
            ParamDescriptor.Choice("type", "Type", ["Pedal", "Studio", "Opto"]),
            ParamDescriptor.Float("threshold", "Threshold", -60f, 0f, -20f, "dB"),
            ParamDescriptor.Float("ratio", "Ratio", 1f, 20f, 4f, ":1", ParamTaper.Log),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 100f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 10f, 2_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("knee", "Knee", 0f, 24f, 6f, "dB"),
            ParamDescriptor.Float("makeup", "Makeup", 0f, 30f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
        ],
        maxInstances: 4
    );

    private float _gainReductionDb;
    private double _meanSquare;
    private float _mix = 1f;
    private float _outputGain = 1f;

    public CompressorBlock()
        : base(Info) { }

    /// <summary>Current gain reduction in dB (0 or negative). For meters and tests.</summary>
    public float GainReductionDb => _gainReductionDb;

    protected override void OnReset() {
        _gainReductionDb = 0;
        _meanSquare = 0;
        _mix = Params.Current(Mix) / 100f;
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

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
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var rmsCoefficient = Coefficient(RmsWindowMs, rate);

        var reduction = _gainReductionDb;
        var meanSquare = _meanSquare;
        var mixStep = (mix - _mix) / io.Length;
        var currentMix = _mix;
        for (var i = 0; i < io.Length; i++) {
            var left = io.Left[i];
            var right = io.Right[i];

            float levelDb;
            if (type == 1) {
                var square = 0.5 * ((double)left * left + (double)right * right);
                meanSquare = square + (meanSquare - square) * rmsCoefficient;
                levelDb = meanSquare > 1e-20 ? (float)(10 * Math.Log10(meanSquare)) : -200f;
            } else {
                var peak = Math.Max(MathF.Abs(left), MathF.Abs(right));
                levelDb = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
            }

            var target = StaticCurve(levelDb, threshold, ratio, knee) - levelDb; // <= 0
            if (target < reduction) {
                reduction = target + (reduction - target) * attack;
            } else {
                var coefficient = type == 2
                    ? Coefficient(releaseMs * (1 - reduction / 6), rate) // opto: 6 dB of reduction doubles the release
                    : release;
                reduction = target + (reduction - target) * coefficient;
            }

            var gain = MathF.Pow(10f, (reduction + makeup) / 20f);
            currentMix += mixStep;
            io.Left[i] = left * (1 - currentMix) + left * gain * currentMix;
            io.Right[i] = right * (1 - currentMix) + right * gain * currentMix;
        }

        _gainReductionDb = reduction > -1e-6f ? 0 : reduction;
        _meanSquare = meanSquare < 1e-30 ? 0 : meanSquare;
        _mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    private static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));
}
