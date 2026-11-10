using Cathode.Dsp.Amp;
using Cathode.Dsp.Capture;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The capture tier of the Amp block: a neural capture (<see cref="ICaptureModel" />) with input calibration and
///     trim, loudness normalisation, an EQ (bass, middle, treble, presence) after the capture or, with EQ Location on
///     Pre, before it, and output level. Stack Type chooses what Bass, Middle and Treble are: the three ±12 dB bands
///     (Active) or one of <see cref="ToneStackDesign.All" />, the passive stacks of the Tone Stack block, with the
///     knob's ±12 dB as the pot's travel (<see cref="PassiveGraphicEq.Position" />, 0 dB at noon) and the stack's loss
///     at noon made up. Presence is the 5 kHz band on every type, and Bright is a fixed treble shelf
///     (<see cref="BrightBand" />); both sit with the EQ. Mono in (left), mono out copied to both channels. Without a
///     model the gains are skipped and only the EQ runs. A model trained at
///     another rate than the engine's runs at its own rate inside a <see cref="CaptureResampler" />, whose delay the
///     block reports as its latency. A slimmable model (NAM A2) runs at the size the Size parameter names: Lite costs a
///     fraction of Full's CPU. Resizing is not real-time safe, so the size is applied when the block is prepared and
///     the parameter rebuilds the graph (<see cref="ParamDescriptor.Recompile" />).
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
    public const int Calibrate = 7;
    public const int Size = 8;

    public const int Location = 9;
    public const int Stack = 10;
    public const int Bright = 11;

    /// <summary>Loudness that normalised models are brought to, matching the NAM plugin.</summary>
    public const double TargetLoudnessDb = -18;

    /// <summary>
    ///     A full-scale sine's level in dBu when full scale is 1 V peak (Input 1's default calibration): 0.707 V RMS
    ///     against dBu's 0.775 V. NAM's <c>input_level_dbu</c> is measured with such a sine.
    /// </summary>
    public const double FullScaleDbuAt1V = -0.7918;

    public const string ModelSlot = "model";

    /// <summary>EQ coefficients are recomputed this often while a tone knob moves.</summary>
    const int SubBlock = 32;

    /// <summary>The EQ's bands, in parameter order from <see cref="Bass" />.</summary>
    static readonly (BiquadType Type, float Frequency, float Q)[] ToneBands = [
        (BiquadType.LowShelf, 120f, 0.7f),
        (BiquadType.Peak, 650f, 0.7f),
        (BiquadType.HighShelf, 2_500f, 0.7f),
        (BiquadType.Peak, 5_000f, 0.9f)
    ];

    /// <summary>
    ///     Bright: a +6 dB shelf above 3 kHz. Cathode's own voicing, not a part of any amp: a capture has no volume
    ///     pot for a bright cap to bridge.
    /// </summary>
    static readonly (BiquadType Type, float Frequency, float Q, float GainDb) BrightBand =
        (BiquadType.HighShelf, 3_000f, 0.7f, 6f);

    // The choices' values.
    public const int SizeFull = 0;
    public const int SizeLite = 1;
    public const int LocationPost = 0;
    public const int LocationPre = 1;

    /// <summary><see cref="Stack" />'s first choice: the ±12 dB bands. The passive stacks follow it.</summary>
    public const int StackActive = 0;

    readonly SvfState[] tone = new SvfState[ToneBands.Length];

    readonly ToneStackCircuit[] stacks =
        [..ToneStackDesign.All.Select(static d => new ToneStackCircuit(d, 48_000))];

    SvfState bright;
    bool brightOn;
    int activeStack = StackActive; // the Stack Type of the last callback
    float inputGain = 1f;
    float outputGain = 1f;
    float calibrationDb; // Input 1's, from the last callback (0 = 1 V at full scale, the default)
    bool prepared;
    bool tonePre; // the side of the capture the EQ ran on in the last callback
    CaptureResampler? resampler;

    /// <summary>
    ///     The rate a model is assumed to be trained at when its file does not say (as the NAM plugin
    ///     assumes).
    /// </summary>
    public const double DefaultModelRate = 48_000;

    public static BlockDescriptor Info { get; } = new(
        "nam",
        "Amp NAM",
        "Amp",
        [
            ParamDescriptor.Float("input", "Input", -24f, 24f, 0f, "dB"),
            ParamDescriptor.Float("output", "Output", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Switch("normalize", "Normalize", true),
            ParamDescriptor.Float("bass", "Bass", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("middle", "Middle", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("treble", "Treble", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Float("presence", "Presence", -12f, 12f, 0f, "dB"),
            ParamDescriptor.Switch("calibrate", "Calibrated Input", true),
            ParamDescriptor.Choice("size", "Size", ["Full", "Lite"]) with { Recompile = true },
            ParamDescriptor.Choice("location", "EQ Location", ["Post", "Pre"]),
            ParamDescriptor.Choice("stack", "Stack Type", ["Active", ..ToneStackDesign.All.Select(static d => d.Name)]),
            ParamDescriptor.Switch("bright", "Bright")
        ],
        maxInstances: 2
    );

    /// <summary>The loaded capture, if any.</summary>
    public ICaptureModel? CaptureModel { get; private set; }

    public IReadOnlyList<string> AssetSlots { get; } = [ModelSlot];

    /// <summary>The resampling around the model, or null when it runs at the engine rate.</summary>
    public CaptureResampler? Resampler => resampler;

    public override int LatencySamples => resampler?.Latency ?? 0;

    /// <summary>An amp that reads AIDA-X models only.</summary>
    public CaptureAmpBlock()
        : this(AidaModel.Load) { }

    /// <summary>
    ///     Control thread, normally before <see cref="IDspBlock.Prepare" />; on a block that is already prepared the
    ///     new model is reset for the current spec, so it never runs unprepared.
    /// </summary>
    public void LoadAsset(string slot, string path) {
        if (slot != ModelSlot) {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        var loaded = load(path);
        CaptureResampler? resampling = null;
        if (prepared) {
            try {
                resampling = PrepareModel(loaded, Spec);
            } catch {
                loaded.Dispose();
                throw;
            }
        }

        CaptureModel?.Dispose();
        CaptureModel = loaded;
        resampler = resampling;
    }

    /// <summary>
    ///     Control thread, before <paramref name="model" /> is reset: bring a model that comes in several sizes to
    ///     Lite or Full. Nothing here: AIDA-X models have one size.
    /// </summary>
    protected virtual void ApplySize(ICaptureModel model, bool lite) { }

    /// <summary>
    ///     Sizes <paramref name="model" /> as the Size parameter says and resets it for <paramref name="spec" />: at
    ///     its training rate behind a resampler when that differs from the engine rate (returned), else at the engine
    ///     rate (null).
    /// </summary>
    CaptureResampler? PrepareModel(ICaptureModel model, in ProcessSpec spec) {
        // The target, not the current value: Prepare runs before the parameters snap to the values the graph was
        // compiled with.
        ApplySize(model, (int)Params.GetTarget(Size) == SizeLite);
        var trained = model.ExpectedSampleRate > 0 ? model.ExpectedSampleRate : DefaultModelRate;
        if (!CaptureResampler.IsNeeded(spec.SampleRate, trained)) {
            model.Reset(spec.SampleRate, spec.MaxBlockSize);
            return null;
        }

        var resampling = new CaptureResampler(spec.SampleRate, trained, spec.MaxBlockSize);
        model.Reset(resampling.ModelRate, resampling.MaxModelFrames);
        return resampling;
    }

    /// <summary>
    ///     The gain in dB that drives the capture as the real amp was driven: a model that records the dBu that
    ///     reached full scale when it was made gets the difference to Input 1's full scale, as the NAM plugin's
    ///     Calibrate Input does. 0 for a model without that level, or with Calibrated Input off.
    /// </summary>
    public double CalibrationGainDb(double inputCalibrationDb) =>
        Params.Current(Calibrate) >= 0.5f && CaptureModel?.InputLevelDbu is double recorded
            ? inputCalibrationDb + FullScaleDbuAt1V - recorded
            : 0;

    (float Input, float Output) TargetGains() {
        var normalisation = Params.Current(Normalize) >= 0.5f && CaptureModel?.Loudness is double loudness
            ? TargetLoudnessDb - loudness
            : 0;
        return ((float)(Decibels.ToGain(Params.Current(Input)) * Decibels.ToGain(CalibrationGainDb(calibrationDb))),
            (float)(Decibels.ToGain(Params.Current(Output)) * Decibels.ToGain(normalisation)));
    }

    void ApplyTone(Span<float> samples) {
        var type = Math.Clamp((int)Params.Current(Stack), StackActive, stacks.Length);
        if (type != activeStack) {
            ResetTone(); // a stack chosen again starts discharged, and the bands it replaces start clean
            activeStack = type;
        }

        var on = Params.Current(Bright) >= 0.5f;
        if (on != brightOn) {
            bright.Reset();
            brightOn = on;
        }

        var passive = type == StackActive ? null : stacks[type - 1];
        Span<double> knobs = stackalloc double[3];
        for (var start = 0; start < samples.Length; start += SubBlock) {
            var chunk = samples.Slice(start, Math.Min(SubBlock, samples.Length - start));
            for (var b = 0; b < tone.Length; b++) {
                var moving = Params.IsRamping(Bass + b);
                var gain = Params.Advance(Bass + b, chunk.Length);
                if (passive is not null && b < knobs.Length) {
                    knobs[b] = PassiveGraphicEq.Position(gain); // the pot's rotation: -12 dB is 0, +12 dB is full
                    continue;
                }

                if (gain == 0 && !moving) {
                    tone[b].Reset(); // a flat band is skipped, so a flat EQ is bit-transparent
                    continue;
                }

                var (band, frequency, q) = ToneBands[b];
                tone[b].Process(SvfCoefficients.Design(band, Spec.SampleRate, frequency, q, gain), chunk);
            }

            if (passive is not null) {
                RunStack(passive, ToneStackBlock.MakeupOf(type - 1), knobs, chunk);
            }

            if (on) {
                var (band, frequency, q, gain) = BrightBand;
                bright.Process(SvfCoefficients.Design(band, Spec.SampleRate, frequency, q, gain), chunk);
            }
        }
    }

    static void RunStack(ToneStackCircuit stack, double makeup, ReadOnlySpan<double> knobs, Span<float> samples) {
        stack.Set(knobs[0], knobs[1], knobs[2]);
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)Flush.Value(stack.Tick(samples[i]) * makeup);
        }

        stack.FlushDenormals();
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        foreach (var stack in stacks) {
            stack.SetSampleRate(spec.SampleRate);
        }

        resampler = CaptureModel is null ? null : PrepareModel(CaptureModel, spec);
        prepared = true;
    }

    protected override void OnReset() {
        resampler?.Reset();
        (inputGain, outputGain) = TargetGains();
        tonePre = Params.Current(Location) >= 0.5f;
        activeStack = Math.Clamp((int)Params.Current(Stack), StackActive, stacks.Length);
        brightOn = Params.Current(Bright) >= 0.5f;
        ResetTone();
    }

    void ResetTone() {
        for (var b = 0; b < tone.Length; b++) {
            tone[b].Reset();
        }

        foreach (var stack in stacks) {
            stack.Reset();
        }

        bright.Reset();
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        Params.Advance(Input, io.Length);
        Params.Advance(Output, io.Length);
        calibrationDb = context.InputCalibrationDb;
        var (input, output) = TargetGains();
        var pre = Params.Current(Location) >= 0.5f;
        if (pre != tonePre) {
            ResetTone(); // the bands held the signal of the capture's other side
            tonePre = pre;
        }

        if (CaptureModel is not null) {
            GainRamp.Apply(io.Left, inputGain, input);
            if (pre) {
                ApplyTone(io.Left); // before the capture the EQ changes what it distorts, as an amp's tone stack does
            }

            if (resampler is not null) {
                resampler.Process(io.Left, CaptureModel);
            } else {
                CaptureModel.Process(io.Left, io.Left);
            }
        }

        if (!pre || CaptureModel is null) {
            ApplyTone(io.Left);
        }

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
