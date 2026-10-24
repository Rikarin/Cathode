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
///     <para>
///         <b>Crossover Slope</b> 12 dB/oct uses second-order Linkwitz–Riley splits instead (two one-poles squared; the
///         high band is inverted so the bands still sum to an allpass). <b>Detector</b> picks peak or RMS (5 ms) level,
///         <b>Auto Attack/Release</b> scales each band's times by its crest factor (<see cref="CrestFactor" />), and each
///         band can be muted (mute two to solo the third).
///     </para>
/// </summary>
public sealed class MultibandCompressorBlock() : DspBlock(Info) {
    public const int LowFreq = 0;
    public const int HighFreq = 1;
    public const int Bands = 3;
    public const int Knee = 17;
    public const int Mix = 18;
    public const int Level = 19;
    public const int Balance = 20;
    public const int Detector = 21;
    public const int Slope = 22;
    public const int AutoTime = 23;
    public const int LowMute = 24;

    public const int Slope24 = 0;
    public const int Slope12 = 1;

    const int ParamsPerBand = 5;
    const int SubBlock = 32;
    const double RmsWindowMs = 5;

    static readonly string[] BandNames = ["low", "mid", "high"];
    static readonly float[] DefaultAttack = [20f, 10f, 5f];
    static readonly float[] DefaultRelease = [200f, 150f, 100f];

    readonly float[] reduction = new float[Bands];
    readonly float[] bandGain = new float[Bands];
    readonly double[] meanSquare = new double[Bands];
    readonly CrestFactor[] crest = new CrestFactor[Bands];
    Channel left;
    Channel right;
    SecondOrderChannel gentleLeft;
    SecondOrderChannel gentleRight;
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
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("detector", "Detector", ["Peak", "RMS"]),
            ParamDescriptor.Choice("slope", "Crossover Slope", ["24 dB/oct", "12 dB/oct"]),
            ParamDescriptor.Switch("autoTime", "Auto Attack/Release"),
            ParamDescriptor.Switch("lowMute", "Low Mute"),
            ParamDescriptor.Switch("midMute", "Mid Mute"),
            ParamDescriptor.Switch("highMute", "High Mute")
        ],
        maxInstances: 2
    );

    public static int Threshold(int band) => 2 + band * ParamsPerBand;

    public static int Ratio(int band) => 3 + band * ParamsPerBand;

    public static int Attack(int band) => 4 + band * ParamsPerBand;

    public static int Release(int band) => 5 + band * ParamsPerBand;

    public static int BandLevel(int band) => 6 + band * ParamsPerBand;

    public static int Mute(int band) => LowMute + band;

    /// <summary>Current gain reduction of <paramref name="band" /> (0 = low) in dB, 0 or negative. For meters and tests.</summary>
    public float GainReductionDb(int band) => reduction[band];

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        gentleLeft.Reset();
        gentleRight.Reset();
        Array.Clear(reduction);
        Array.Clear(meanSquare);
        for (var b = 0; b < Bands; b++) {
            crest[b].Reset();
        }

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
        Span<float> attackMs = stackalloc float[Bands];
        Span<float> releaseMs = stackalloc float[Bands];
        Span<float> mute = stackalloc float[Bands];
        this.reduction.CopyTo(reduction);
        var rms = Params.Current(Detector) >= 0.5f;
        var gentle = Params.Current(Slope) >= 0.5f;
        var auto = Params.Current(AutoTime) >= 0.5f;
        var rmsCoefficient = Coefficient(RmsWindowMs, rate);
        var crestCoefficient = CrestFactor.Coefficient(rate);
        for (var b = 0; b < Bands; b++) {
            mute[b] = Params.Current(Mute(b)) >= 0.5f ? 0f : 1f;
        }

        if (gentle) {
            left.Reset(); // the other topology's state is stale when it comes back
            right.Reset();
        } else {
            gentleLeft.Reset();
            gentleRight.Reset();
        }

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var lowFrequency = Params.Advance(LowFreq, length);
            var highFrequency = Params.Advance(HighFreq, length);
            var lowSplit = LinkwitzRileyCoefficients.Design(rate, lowFrequency);
            var highSplit = LinkwitzRileyCoefficients.Design(rate, highFrequency);
            var lowPole = SecondOrderChannel.Pole(rate, lowFrequency);
            var highPole = SecondOrderChannel.Pole(rate, highFrequency);
            for (var b = 0; b < Bands; b++) {
                threshold[b] = Params.Advance(Threshold(b), length);
                ratio[b] = Params.Advance(Ratio(b), length);
                attackMs[b] = Params.Advance(Attack(b), length);
                releaseMs[b] = Params.Advance(Release(b), length);
                attack[b] = Coefficient(attackMs[b], rate);
                release[b] = Coefficient(releaseMs[b], rate);
                var to = Decibels.ToGain(Params.Advance(BandLevel(b), length));
                fromGain[b] = bandGain[b];
                gainStep[b] = (to - bandGain[b]) / length;
                bandGain[b] = to;
            }

            for (var j = 0; j < length; j++) {
                var i = start + j;
                if (gentle) {
                    gentleLeft.Split(lowPole, highPole, io.Left[i], band[..Bands]);
                    gentleRight.Split(lowPole, highPole, io.Right[i], band[Bands..]);
                } else {
                    left.Split(lowSplit, highSplit, io.Left[i], band[..Bands]);
                    right.Split(lowSplit, highSplit, io.Right[i], band[Bands..]);
                }

                double wetLeft = 0, wetRight = 0, dryLeft = 0, dryRight = 0;
                for (var b = 0; b < Bands; b++) {
                    var l = band[b];
                    var r = band[Bands + b];
                    var peak = (float)Math.Max(Math.Abs(l), Math.Abs(r));
                    float levelDb;
                    if (rms) {
                        var square = 0.5 * (l * l + r * r);
                        meanSquare[b] = square + (meanSquare[b] - square) * rmsCoefficient;
                        levelDb = meanSquare[b] > 1e-20 ? (float)(10 * Math.Log10(meanSquare[b])) : -200f;
                    } else {
                        levelDb = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
                    }

                    var target = CompressorBlock.StaticCurve(levelDb, threshold[b], ratio[b], knee) - levelDb;
                    float coefficient;
                    if (auto) {
                        var scale = crest[b].Tick(peak, crestCoefficient);
                        coefficient = Coefficient((target < reduction[b] ? attackMs[b] : releaseMs[b]) * scale, rate);
                    } else {
                        coefficient = target < reduction[b] ? attack[b] : release[b];
                    }

                    reduction[b] = target + (reduction[b] - target) * coefficient;
                    var gain = MathF.Pow(10f, reduction[b] / 20f) * (fromGain[b] + gainStep[b] * (j + 1)) * mute[b];
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
        gentleLeft.FlushDenormals();
        gentleRight.FlushDenormals();
        for (var b = 0; b < Bands; b++) {
            crest[b].FlushDenormals();
            if (meanSquare[b] < 1e-30) {
                meanSquare[b] = 0;
            }

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

    /// <summary>
    ///     One channel of the three-way split with second-order Linkwitz–Riley crossovers: each band a TPT one-pole squared
    ///     (low-pass for the low side, high-pass for the high side, inverted), so a split sums to the first-order allpass
    ///     <c>(1 - s) / (1 + s)</c>; the low band also runs through the second split's allpass.
    /// </summary>
    struct SecondOrderChannel {
        const double DenormalThreshold = 1e-30;

        double lowA, lowB, lowC, highA, highB, highC, align;

        /// <summary>The one-pole coefficient g / (1 + g) for a crossover at <paramref name="frequency" />.</summary>
        public static double Pole(double sampleRate, double frequency) {
            var g = Math.Tan(Math.PI * Math.Clamp(frequency, 1.0, sampleRate * 0.49) / sampleRate);
            return g / (1 + g);
        }

        public void Reset() => (lowA, lowB, lowC, highA, highB, highC, align) = (0, 0, 0, 0, 0, 0, 0);

        public void Split(double lowPole, double highPole, double x, Span<double> bands) {
            var (lowBand, rest) = Split(ref lowA, ref lowB, ref lowC, lowPole, x);
            bands[0] = 2 * LowPass(ref align, highPole, lowBand) - lowBand;
            (bands[1], bands[2]) = Split(ref highA, ref highB, ref highC, highPole, rest);
        }

        public void FlushDenormals() {
            Flush(ref lowA);
            Flush(ref lowB);
            Flush(ref lowC);
            Flush(ref highA);
            Flush(ref highB);
            Flush(ref highC);
            Flush(ref align);
        }

        static void Flush(ref double state) {
            if (Math.Abs(state) < DenormalThreshold) {
                state = 0;
            }
        }

        static double LowPass(ref double state, double pole, double x) {
            var v = (x - state) * pole;
            var low = v + state;
            state = low + v;
            return low;
        }

        /// <summary>
        ///     One LR2 split: the first one-pole gives the low and high halves, each squared by a second one-pole; the high
        ///     band is inverted so the two sum to an allpass.
        /// </summary>
        static (double Low, double High) Split(
            ref double first,
            ref double low,
            ref double high,
            double pole,
            double x
        ) {
            var lp = LowPass(ref first, pole, x);
            var hp = x - lp;
            return (LowPass(ref low, pole, lp), -(hp - LowPass(ref high, pole, hp)));
        }
    }
}
