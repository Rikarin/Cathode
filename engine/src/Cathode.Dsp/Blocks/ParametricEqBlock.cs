using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Five-band parametric EQ. Bands 2-4 are peaks; bands 1 and 5 are each one of:
///     <list type="bullet">
///         <item><b>Shelf</b>: a second-order low (band 1) or high (band 5) shelf, its Q limited to 1.</item>
///         <item><b>Peak</b>: a peaking band.</item>
///         <item>
///             <b>Blocking</b>: a high-pass (band 1) or low-pass (band 5) that ignores its gain. At 12 dB/oct its Q sets
///             the resonance; the other slopes (6 to 36 dB/oct) are Butterworth.
///         </item>
///         <item><b>Shelving 2</b>: a gentle first-order shelf, centred on the band's frequency; Q does nothing.</item>
///         <item>
///             <b>Peaking 2</b>: a proportional-Q peak, broad at small gains and narrowing to the band's Q at ±12 dB, as
///             on an analogue console.
///         </item>
///     </list>
///     <b>Solo</b> listens to one band's region alone: a peak's band-pass at its frequency and Q, band 1's low-pass or
///     band 5's high-pass at its frequency. Coefficients follow smoothed parameters every <see cref="SubBlock" /> samples.
/// </summary>
public sealed class ParametricEqBlock : DspBlock {
    public const int Bands = 5;
    public const int LowType = 15;
    public const int HighType = 16;
    public const int Level = 17;
    public const int Balance = 18;
    public const int LowSlope = 19;
    public const int HighSlope = 20;
    public const int Solo = 21;

    // Values of LowType and HighType.
    public const int Shelf = 0;
    public const int Peak = 1;
    public const int Blocking = 2;
    public const int Shelf2 = 3;
    public const int Peak2 = 4;

    const int SubBlock = 32;
    static readonly float[] DefaultFrequencies = [100f, 300f, 1_000f, 3_000f, 8_000f];

    readonly EqBand[] bands = [new(), new(), new(), new(), new()];
    readonly FirstOrderShelf[] shelves = new FirstOrderShelf[2];
    readonly CutState[] cuts = new CutState[4]; // band 1 left, right; band 5 left, right
    SvfState soloLeft, soloRight;
    int solo;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "peq",
        "Parametric EQ",
        "EQ",
        [
            ..Enumerable.Range(1, Bands)
                .SelectMany(static b => new[] {
                        ParamDescriptor.Float(
                            $"freq{b}",
                            $"Frequency {b}",
                            20f,
                            20_000f,
                            DefaultFrequencies[b - 1],
                            "Hz",
                            ParamTaper.Log
                        ),
                        ParamDescriptor.Float($"gain{b}", $"Gain {b}", -24f, 24f, 0f, "dB"),
                        ParamDescriptor.Float($"q{b}", $"Q {b}", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log)
                    }
                ),
            ParamDescriptor.Choice(
                "lowtype",
                "Band 1 Type",
                ["Low Shelf", "Peak", "Blocking", "Shelving 2", "Peaking 2"]
            ),
            ParamDescriptor.Choice(
                "hightype",
                "Band 5 Type",
                ["High Shelf", "Peak", "Blocking", "Shelving 2", "Peaking 2"]
            ),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Choice("lowslope", "Band 1 Slope", CutFilter.SteepSlopes, CutFilter.Slope12),
            ParamDescriptor.Choice("highslope", "Band 5 Slope", CutFilter.SteepSlopes, CutFilter.Slope12),
            ParamDescriptor.Choice("solo", "Solo", ["Off", "Band 1", "Band 2", "Band 3", "Band 4", "Band 5"])
        ],
        maxInstances: 4
    );

    public ParametricEqBlock()
        : base(Info) { }

    public static int Frequency(int band) => (band - 1) * 3;

    public static int Gain(int band) => (band - 1) * 3 + 1;

    public static int Q(int band) => (band - 1) * 3 + 2;

    /// <summary>Solo: <paramref name="chunk" /> through band <paramref name="band" />'s region alone.</summary>
    void Listen(in StereoBuffer chunk, int band, float frequency, float q) {
        var type = band switch {
            1 => BiquadType.LowPass,
            Bands => BiquadType.HighPass,
            _ => BiquadType.BandPass
        };
        var c = SvfCoefficients.Design(type, Spec.SampleRate, frequency, type == BiquadType.BandPass ? q : 0.7071);
        soloLeft.Process(c, chunk.Left);
        soloRight.Process(c, chunk.Right);
    }

    protected override void OnReset() {
        foreach (var band in bands) {
            band.Reset();
        }

        Array.Clear(shelves);
        Array.Clear(cuts);
        soloLeft.Reset();
        soloRight.Reset();
        solo = (int)Params.Current(Solo);
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var solo = (int)Params.Current(Solo);
        if (solo != this.solo) {
            this.solo = solo;
            soloLeft.Reset();
            soloRight.Reset();
        }

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var chunk = io.Slice(start, length);
            for (var b = 1; b <= Bands; b++) {
                var moving = Params.IsRamping(Frequency(b)) || Params.IsRamping(Gain(b)) || Params.IsRamping(Q(b));
                var frequency = Params.Advance(Frequency(b), length);
                var gain = Params.Advance(Gain(b), length);
                var q = Params.Advance(Q(b), length);
                if (solo != 0) {
                    if (b == solo) {
                        Listen(chunk, b, frequency, q);
                    }

                    continue;
                }

                var outer = b is 1 or Bands;
                var typeParam = b == 1 ? LowType : HighType;
                var kind = !outer ? Peak : (int)Params.Current(typeParam);
                switch (kind) {
                    case Shelf2:
                        shelves[b == 1 ? 0 : 1].Process(chunk, b == 1, Spec.SampleRate, frequency, gain);
                        continue;
                    case Blocking when (int)Params.Current(b == 1 ? LowSlope : HighSlope) != CutFilter.Slope12: {
                        var slope = (int)Params.Current(b == 1 ? LowSlope : HighSlope);
                        var c = CutFilter.Design(b == 1, Spec.SampleRate, frequency, slope);
                        var first = b == 1 ? 0 : 2;
                        cuts[first].Process(c, chunk.Left);
                        cuts[first + 1].Process(c, chunk.Right);
                        continue;
                    }
                }

                var type = kind switch {
                    Shelf => b == 1 ? BiquadType.LowShelf : BiquadType.HighShelf,
                    Blocking => b == 1 ? BiquadType.HighPass : BiquadType.LowPass,
                    _ => BiquadType.Peak
                };
                if (kind == Peak2) {
                    q = GraphicEqualizer.VariableQ(q, gain);
                }

                bands[b - 1].Process(chunk, type, Spec.SampleRate, frequency, q, gain, moving);
            }
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>
    ///     A first-order low or high shelf on both channels (TPT one-pole): <c>x + (G - 1) LP(x)</c> or
    ///     <c>x + (G - 1) HP(x)</c>, the pole placed so the transition is centred on the band's frequency.
    /// </summary>
    struct FirstOrderShelf {
        const double DenormalThreshold = 1e-30;

        double left;
        double right;

        public void Process(in StereoBuffer io, bool low, double sampleRate, float frequency, float gainDb) {
            if (gainDb == 0) {
                left = 0;
                right = 0;
                return;
            }

            var gain = Math.Pow(10, gainDb / 20.0);
            // Low shelf: zero at f sqrt(G), pole at f / sqrt(G); high shelf the mirror image.
            var corner = low ? frequency / Math.Sqrt(gain) : frequency * Math.Sqrt(gain);
            var g = Math.Tan(Math.PI * Math.Clamp(corner, 1.0, sampleRate * 0.49) / sampleRate);
            var pole = g / (1 + g);
            Run(io.Left, ref left, low, pole, gain - 1);
            Run(io.Right, ref right, low, pole, gain - 1);
        }

        static void Run(Span<float> samples, ref double state, bool low, double pole, double amount) {
            for (var i = 0; i < samples.Length; i++) {
                double x = samples[i];
                var v = (x - state) * pole;
                var lp = v + state;
                state = lp + v;
                samples[i] = (float)(x + amount * (low ? lp : x - lp));
            }

            if (Math.Abs(state) < DenormalThreshold) {
                state = 0;
            }
        }
    }
}
