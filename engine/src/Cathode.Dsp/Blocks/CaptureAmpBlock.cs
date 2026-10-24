using Cathode.Dsp.Capture;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The capture tier of the Amp block: a neural capture (<see cref="ICaptureModel" />) with input trim, loudness
///     normalisation, a post-EQ (bass, middle, treble, presence) and output level. Mono in (left), mono out copied to
///     both channels. Without a model the gains are skipped and only the EQ runs.
/// </summary>
/// <remarks>
///     This base only reads AIDA-X models, which run in managed code. <c>NamAmpBlock</c> in Cathode.Dsp.Native replaces
///     it in the registry and adds NAM models through the native core.
/// </remarks>
public class CaptureAmpBlock(Func<string, ICaptureModel> load) : DspBlock(Info), IAssetConsumer {
    public const int Input = 0;
    public const int Output = 1;
    public const int Normalize = 2;
    public const int Bass = 3;
    public const int Middle = 4;
    public const int Treble = 5;
    public const int Presence = 6;

    /// <summary>Loudness that normalised models are brought to, matching the NAM plugin.</summary>
    public const double TargetLoudnessDb = -18;

    public const string ModelSlot = "model";

    /// <summary>EQ coefficients are recomputed this often while a tone knob moves.</summary>
    const int SubBlock = 32;

    /// <summary>The post-EQ bands, in parameter order from <see cref="Bass" />.</summary>
    static readonly (BiquadType Type, float Frequency, float Q)[] ToneBands = [
        (BiquadType.LowShelf, 120f, 0.7f),
        (BiquadType.Peak, 650f, 0.7f),
        (BiquadType.HighShelf, 2_500f, 0.7f),
        (BiquadType.Peak, 5_000f, 0.9f)
    ];

    readonly SvfState[] tone = new SvfState[ToneBands.Length];
    float inputGain = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "nam",
        "Amp (capture)",
        "Amp",
        [
            ParamDescriptor.Float("input", "Input", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("output", "Output", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("normalize", "Normalize", true),
            ParamDescriptor.Float("bass", "Bass", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("middle", "Middle", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("treble", "Treble", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("presence", "Presence", -12f, 12f, 0f, "dB")
        ],
        maxInstances: 2
    );

    /// <summary>The loaded capture, if any.</summary>
    public ICaptureModel? CaptureModel { get; private set; }

    public IReadOnlyList<string> AssetSlots { get; } = [ModelSlot];

    /// <summary>An amp that reads AIDA-X models only.</summary>
    public CaptureAmpBlock()
        : this(AidaModel.Load) { }

    /// <summary>Control thread, before <see cref="IDspBlock.Prepare" />.</summary>
    public void LoadAsset(string slot, string path) {
        if (slot != ModelSlot) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var loaded = load(path);
        CaptureModel?.Dispose();
        CaptureModel = loaded;
    }

    (float Input, float Output) TargetGains() {
        var normalisation = Params.Current(Normalize) >= 0.5f && CaptureModel?.Loudness is double loudness
            ? TargetLoudnessDb - loudness
            : 0;
        return (Decibels.ToGain(Params.Current(Input)),
            (float)(Decibels.ToGain(Params.Current(Output)) * Decibels.ToGain(normalisation)));
    }

    void ApplyTone(Span<float> samples) {
        for (var start = 0; start < samples.Length; start += SubBlock) {
            var chunk = samples.Slice(start, Math.Min(SubBlock, samples.Length - start));
            for (var b = 0; b < tone.Length; b++) {
                var moving = Params.IsRamping(Bass + b);
                var gain = Params.Advance(Bass + b, chunk.Length);
                if (gain == 0 && !moving) {
                    tone[b].Reset(); // a flat band is skipped, so a flat EQ is bit-transparent
                    continue;
                }

                var (type, frequency, q) = ToneBands[b];
                tone[b].Process(SvfCoefficients.Design(type, Spec.SampleRate, frequency, q, gain), chunk);
            }
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) => CaptureModel?.Reset(spec.SampleRate, spec.MaxBlockSize);

    protected override void OnReset() {
        (inputGain, outputGain) = TargetGains();
        for (var b = 0; b < tone.Length; b++) {
            tone[b].Reset();
        }
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        Params.Advance(Input, io.Length);
        Params.Advance(Output, io.Length);
        var (input, output) = TargetGains();

        if (CaptureModel is not null) {
            GainRamp.Apply(io.Left, inputGain, input);
            CaptureModel.Process(io.Left, io.Left);
        }

        ApplyTone(io.Left);

        if (CaptureModel is not null) {
            GainRamp.Apply(io.Left, outputGain, output);
        }

        io.Left.CopyTo(io.Right);
        inputGain = input;
        outputGain = output;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            CaptureModel?.Dispose();
            CaptureModel = null;
        }

        base.Dispose(disposing);
    }
}
