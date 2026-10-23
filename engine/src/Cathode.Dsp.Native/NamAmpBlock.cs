using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Native;

/// <summary>
/// The capture tier of the Amp block: a Neural Amp Modeler model with input trim, loudness normalisation and
/// output level. Mono in (left), mono out copied to both channels. Without a model it passes audio through.
/// </summary>
public sealed class NamAmpBlock : DspBlock, IAssetConsumer
{
    public const int Input = 0;
    public const int Output = 1;
    public const int Normalize = 2;

    /// <summary>Loudness that normalised models are brought to, matching the NAM plugin.</summary>
    public const double TargetLoudnessDb = -18;

    public const string ModelSlot = "model";

    public static BlockDescriptor Info { get; } = new(
        "nam",
        "Amp (NAM capture)",
        "Amp",
        [
            ParamDescriptor.Float("input", "Input", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("output", "Output", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("normalize", "Normalize", defaultValue: true),
        ],
        maxInstances: 2);

    private NamModel? _model;
    private float _inputGain = 1f;
    private float _outputGain = 1f;

    public NamAmpBlock()
        : base(Info)
    {
    }

    public NamModelInfo? Model => _model?.Info;

    public IReadOnlyList<string> AssetSlots { get; } = [ModelSlot];

    /// <summary>Control thread, before <see cref="IDspBlock.Prepare"/>.</summary>
    public void LoadAsset(string slot, string path)
    {
        if (slot != ModelSlot)
        {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var model = NamModel.Load(path);
        _model?.Dispose();
        _model = model;
    }

    /// <summary>Control thread, before the block is published: resize a slimmable model (0 = Lite, 1 = Full).</summary>
    public void SetModelSize(double size) => _model?.SetSize(size);

    protected override void OnPrepare(in ProcessSpec spec) => _model?.Reset(spec.SampleRate, spec.MaxBlockSize);

    protected override void OnReset() => (_inputGain, _outputGain) = TargetGains();

    protected override void OnProcess(in StereoBuffer io, in BlockContext context)
    {
        Params.Advance(Input, io.Length);
        Params.Advance(Output, io.Length);
        var (input, output) = TargetGains();

        if (_model is not null)
        {
            Ramp(io.Left, _inputGain, input);
            _model.Process(io.Left, io.Left);
            Ramp(io.Left, _outputGain, output);
        }

        io.Left.CopyTo(io.Right);
        _inputGain = input;
        _outputGain = output;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model?.Dispose();
            _model = null;
        }

        base.Dispose(disposing);
    }

    private (float Input, float Output) TargetGains()
    {
        var normalisation = Params.Current(Normalize) >= 0.5f && _model?.Info.Loudness is double loudness
            ? TargetLoudnessDb - loudness
            : 0;
        return (Decibels.ToGain(Params.Current(Input)), (float)(Decibels.ToGain(Params.Current(Output)) * Decibels.ToGain(normalisation)));
    }

    private static void Ramp(Span<float> samples, float from, float to)
    {
        if (from == 1f && to == 1f)
        {
            return;
        }

        var step = (to - from) / samples.Length;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= from + step * (i + 1);
        }
    }
}
