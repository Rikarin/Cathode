using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Three-band compressor. Linkwitz–Riley 4th-order crossovers split the signal at <b>Low Freq</b> and
///     <b>High Freq</b>; the low band also runs through the second crossover's allpass, so the three bands sum flat when
///     nothing is compressed. Each band has its own feed-forward compressor (the soft-knee static curve of
///     <see cref="CompressorBlock" />, peak detection linked across channels, decoupled attack/release on the gain) and
///     output level. The mix blends against the uncompressed band sum, which has the same phase response, so parallel
///     compression does not comb.
/// </summary>
public sealed class MultibandCompressorBlock() : DspBlock(Info) {
    public const int LowFreq = 0;
    public const int HighFreq = 1;
    public const int Bands = 3;
    public const int Knee = 17;
    public const int Mix = 18;
    public const int Level = 19;
    public const int Balance = 20;

    const int ParamsPerBand = 5;
    const int SubBlock = 32;

    static readonly string[] BandNames = ["low", "mid", "high"];
    static readonly float[] DefaultAttack = [20f, 10f, 5f];
    static readonly float[] DefaultRelease = [200f, 150f, 100f];

    readonly float[] reduction = new float[Bands];
    readonly float[] bandGain = new float[Bands];
    Channel left;
    Channel right;
    float mix = 1f;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "mbcomp",
        "Multiband Compressor",
        "Dynamics",
        [
            ParamDescriptor.Float("lowFreq", "Low Freq", 40f, 1_000f, 200f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highFreq", "High Freq", 1_000f, 12_000f, 2_500f, "Hz", ParamTaper.Log),
            ..Enumerable.Range(0, Bands)
                .SelectMany(b => {
                        var id = BandNames[b];
                        var name = char.ToUpperInvariant(id[0]) + id[1..];
                        return new[] {
                            ParamDescriptor.Float($"{id}Threshold", $"{name} Threshold", -60f, 0f, -20f, "dB"),
                            ParamDescriptor.Float($"{id}Ratio", $"{name} Ratio", 1f, 20f, 3f, ":1", ParamTaper.Log),
                            ParamDescriptor.Float(
                                $"{id}Attack",
                                $"{name} Attack",
                                0.1f,
                                100f,
                                DefaultAttack[b],
                                "ms",
                                ParamTaper.Log
                            ),
                            ParamDescriptor.Float(
                                $"{id}Release",
                                $"{name} Release",
                                10f,
                                2_000f,
                                DefaultRelease[b],
                                "ms",
                                ParamTaper.Log
                            ),
                            ParamDescriptor.Float($"{id}Level", $"{name} Level", Decibels.SilenceDb, 20f, 0f, "dB")
                        };
                    }
                ),
            ParamDescriptor.Float("knee", "Knee", 0f, 24f, 6f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 2
    );

    public static int Threshold(int band) => 2 + band * ParamsPerBand;

    public static int Ratio(int band) => 3 + band * ParamsPerBand;

    public static int Attack(int band) => 4 + band * ParamsPerBand;

    public static int Release(int band) => 5 + band * ParamsPerBand;

    public static int BandLevel(int band) => 6 + band * ParamsPerBand;

    /// <summary>Current gain reduction of <paramref name="band" /> (0 = low) in dB, 0 or negative. For meters and tests.</summary>
    public float GainReductionDb(int band) => reduction[band];

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        Array.Clear(reduction);
        for (var b = 0; b < Bands; b++) {
            bandGain[b] = Decibels.ToGain(Params.Current(BandLevel(b)));
        }

        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var knee = Params.Advance(Knee, io.Length);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        Span<float> threshold = stackalloc float[Bands];
        Span<float> ratio = stackalloc float[Bands];
        Span<float> attack = stackalloc float[Bands];
        Span<float> release = stackalloc float[Bands];
        Span<float> fromGain = stackalloc float[Bands];
        Span<float> gainStep = stackalloc float[Bands];
        Span<float> reduction = stackalloc float[Bands];
        Span<double> band = stackalloc double[Bands * 2];
        this.reduction.CopyTo(reduction);

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var lowSplit = LinkwitzRileyCoefficients.Design(rate, Params.Advance(LowFreq, length));
            var highSplit = LinkwitzRileyCoefficients.Design(rate, Params.Advance(HighFreq, length));
            for (var b = 0; b < Bands; b++) {
                threshold[b] = Params.Advance(Threshold(b), length);
                ratio[b] = Params.Advance(Ratio(b), length);
                attack[b] = Coefficient(Params.Advance(Attack(b), length), rate);
                release[b] = Coefficient(Params.Advance(Release(b), length), rate);
                var to = Decibels.ToGain(Params.Advance(BandLevel(b), length));
                fromGain[b] = bandGain[b];
                gainStep[b] = (to - bandGain[b]) / length;
                bandGain[b] = to;
            }

            for (var j = 0; j < length; j++) {
                var i = start + j;
                left.Split(lowSplit, highSplit, io.Left[i], band[..Bands]);
                right.Split(lowSplit, highSplit, io.Right[i], band[Bands..]);

                double wetLeft = 0, wetRight = 0, dryLeft = 0, dryRight = 0;
                for (var b = 0; b < Bands; b++) {
                    var l = band[b];
                    var r = band[Bands + b];
                    var peak = (float)Math.Max(Math.Abs(l), Math.Abs(r));
                    var levelDb = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
                    var target = CompressorBlock.StaticCurve(levelDb, threshold[b], ratio[b], knee) - levelDb;
                    var coefficient = target < reduction[b] ? attack[b] : release[b];
                    reduction[b] = target + (reduction[b] - target) * coefficient;
                    var gain = MathF.Pow(10f, reduction[b] / 20f) * (fromGain[b] + gainStep[b] * (j + 1));
                    wetLeft += gain * l;
                    wetRight += gain * r;
                    dryLeft += l;
                    dryRight += r;
                }

                var m = this.mix + mixStep * (i + 1);
                io.Left[i] = (float)(dryLeft + (wetLeft - dryLeft) * m);
                io.Right[i] = (float)(dryRight + (wetRight - dryRight) * m);
            }
        }

        left.FlushDenormals();
        right.FlushDenormals();
        for (var b = 0; b < Bands; b++) {
            this.reduction[b] = reduction[b] > -1e-6f ? 0 : reduction[b]; // no subnormals after a long recovery
        }

        this.mix = mix;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>One channel of the three-way split.</summary>
    struct Channel {
        LinkwitzRileySplit low;
        LinkwitzRileySplit high;
        LinkwitzRileyAllpass lowAlign;

        public void Reset() {
            low.Reset();
            high.Reset();
            lowAlign.Reset();
        }

        public void Split(
            in LinkwitzRileyCoefficients lowSplit,
            in LinkwitzRileyCoefficients highSplit,
            double x,
            Span<double> bands
        ) {
            low.Tick(lowSplit, x, out var lowBand, out var rest);
            high.Tick(highSplit, rest, out var midBand, out var highBand);
            bands[0] = lowAlign.Tick(highSplit, lowBand);
            bands[1] = midBand;
            bands[2] = highBand;
        }

        public void FlushDenormals() {
            low.FlushDenormals();
            high.FlushDenormals();
            lowAlign.FlushDenormals();
        }
    }
}
