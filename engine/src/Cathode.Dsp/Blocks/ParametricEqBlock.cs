using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Five-band parametric EQ. Band 1 can be a low shelf, a peak or a blocking (high-pass) filter, band 5 a high
///     shelf, a peak or a blocking (low-pass) filter; bands 2-4 are peaks. A blocking band ignores its gain.
///     Coefficients follow smoothed parameters every <see cref="SubBlock" /> samples.
/// </summary>
public sealed class ParametricEqBlock : DspBlock {
    public const int Bands = 5;
    public const int LowType = 15;
    public const int HighType = 16;
    public const int Level = 17;
    public const int Balance = 18;

    const int SubBlock = 32;
    static readonly float[] DefaultFrequencies = [100f, 300f, 1_000f, 3_000f, 8_000f];

    readonly EqBand[] bands = [new(), new(), new(), new(), new()];
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "peq",
        "Parametric EQ",
        "EQ",
        [
            ..Enumerable.Range(1, Bands)
                .SelectMany(b => new[] {
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
            ParamDescriptor.Choice("lowtype", "Band 1 Type", ["Low Shelf", "Peak", "Blocking"]),
            ParamDescriptor.Choice("hightype", "Band 5 Type", ["High Shelf", "Peak", "Blocking"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 4
    );

    public ParametricEqBlock()
        : base(Info) { }

    public static int Frequency(int band) => (band - 1) * 3;

    public static int Gain(int band) => (band - 1) * 3 + 1;

    public static int Q(int band) => (band - 1) * 3 + 2;

    protected override void OnReset() {
        foreach (var band in bands) {
            band.Reset();
        }

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var chunk = io.Slice(start, length);
            for (var b = 1; b <= Bands; b++) {
                var moving = Params.IsRamping(Frequency(b)) || Params.IsRamping(Gain(b)) || Params.IsRamping(Q(b));
                var frequency = Params.Advance(Frequency(b), length);
                var gain = Params.Advance(Gain(b), length);
                var q = Params.Advance(Q(b), length);
                var type = b switch {
                    1 when Params.Current(LowType) == 0 => BiquadType.LowShelf,
                    1 when Params.Current(LowType) == 2 => BiquadType.HighPass,
                    Bands when Params.Current(HighType) == 0 => BiquadType.HighShelf,
                    Bands when Params.Current(HighType) == 2 => BiquadType.LowPass,
                    _ => BiquadType.Peak
                };
                bands[b - 1].Process(chunk, type, Spec.SampleRate, frequency, q, gain, moving);
            }
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
