using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Single-band filter block. It uses a trapezoidal state-variable filter (RBJ-equivalent responses) because its
///     parameters are smoothed and modulated; a direct-form biquad produced +60 dBFS transients under fast sweeps.
///     While frequency, Q or gain are smoothing, coefficients are recomputed every <see cref="SubBlock" /> samples.
/// </summary>
public sealed class FilterBlock : DspBlock {
    public const int Type = 0;
    public const int Frequency = 1;
    public const int Q = 2;
    public const int Gain = 3;
    public const int Level = 4;

    const int SubBlock = 32;

    public static BlockDescriptor Info { get; } = new(
        "filter",
        "Filter",
        "Filter",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Low Pass", "High Pass", "Band Pass", "Notch", "Peaking", "Low Shelf", "High Shelf"]
            ),
            ParamDescriptor.Float("freq", "Frequency", 20f, 20_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("q", "Q", 0.1f, 18f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("gain", "Gain", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 4
    );

    SvfCoefficients _coefficients;
    SvfState _left;
    SvfState _right;
    float _gain = 1f;

    public FilterBlock()
        : base(Info) { }

    protected override void OnReset() {
        _left.Reset();
        _right.Reset();
        UpdateCoefficients();
        _gain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        if (Params.IsRamping(Type) || Params.IsRamping(Frequency) || Params.IsRamping(Q) || Params.IsRamping(Gain)) {
            for (var start = 0; start < io.Length; start += SubBlock) {
                var length = Math.Min(SubBlock, io.Length - start);
                Params.Advance(Frequency, length);
                Params.Advance(Q, length);
                Params.Advance(Gain, length);
                UpdateCoefficients();
                var chunk = io.Slice(start, length);
                _left.Process(_coefficients, chunk.Left);
                _right.Process(_coefficients, chunk.Right);
            }
        } else {
            UpdateCoefficients();
            _left.Process(_coefficients, io.Left);
            _right.Process(_coefficients, io.Right);
        }

        var targetGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _gain, targetGain);
        GainRamp.Apply(io.Right, _gain, targetGain);
        _gain = targetGain;
    }

    void UpdateCoefficients() =>
        _coefficients = SvfCoefficients.Design(
            (BiquadType)(int)Params.Current(Type),
            Spec.SampleRate,
            Params.Current(Frequency),
            Params.Current(Q),
            Params.Current(Gain)
        );
}
