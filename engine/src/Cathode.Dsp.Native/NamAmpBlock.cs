using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Native;

/// <summary>
///     The capture tier of the Amp block: a Neural Amp Modeler model with input trim, loudness normalisation and
///     output level. Mono in (left), mono out copied to both channels. Without a model it passes audio through.
/// </summary>
public sealed class NamAmpBlock : DspBlock, IAssetConsumer {
    public const int Input = 0;
    public const int Output = 1;
    public const int Normalize = 2;

    /// <summary>Loudness that normalised models are brought to, matching the NAM plugin.</summary>
    public const double TargetLoudnessDb = -18;

    public const string ModelSlot = "model";

    NamModel? model;
    float inputGain = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "nam",
        "Amp (NAM capture)",
        "Amp",
        [
            ParamDescriptor.Float("input", "Input", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("output", "Output", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("normalize", "Normalize", true)
        ],
        maxInstances: 2
    );

    public NamModelInfo? Model => this.model?.Info;

    public IReadOnlyList<string> AssetSlots { get; } = [ModelSlot];

    public NamAmpBlock()
        : base(Info) { }

    /// <summary>Control thread, before <see cref="IDspBlock.Prepare" />.</summary>
    public void LoadAsset(string slot, string path) {
        if (slot != ModelSlot) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var model = NamModel.Load(path);
        this.model?.Dispose();
        this.model = model;
    }

    /// <summary>Control thread, before the block is published: resize a slimmable model (0 = Lite, 1 = Full).</summary>
    public void SetModelSize(double size) => this.model?.SetSize(size);

    (float Input, float Output) TargetGains() {
        var normalisation = Params.Current(Normalize) >= 0.5f && this.model?.Info.Loudness is double loudness
            ? TargetLoudnessDb - loudness
            : 0;
        return (Decibels.ToGain(Params.Current(Input)),
            (float)(Decibels.ToGain(Params.Current(Output)) * Decibels.ToGain(normalisation)));
    }

    protected override void OnPrepare(in ProcessSpec spec) => this.model?.Reset(spec.SampleRate, spec.MaxBlockSize);

    protected override void OnReset() => (inputGain, outputGain) = TargetGains();

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        Params.Advance(Input, io.Length);
        Params.Advance(Output, io.Length);
        var (input, output) = TargetGains();

        if (this.model is not null) {
            GainRamp.Apply(io.Left, inputGain, input);
            this.model.Process(io.Left, io.Left);
            GainRamp.Apply(io.Left, outputGain, output);
        }

        io.Left.CopyTo(io.Right);
        inputGain = input;
        outputGain = output;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            this.model?.Dispose();
            this.model = null;
        }

        base.Dispose(disposing);
    }
}
