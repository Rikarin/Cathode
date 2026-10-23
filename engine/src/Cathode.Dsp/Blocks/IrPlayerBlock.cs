using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Plays an impulse response that is not a cabinet: an acoustic body, a room, a microphone or speaker correction, or
///     an effect captured as an IR. Longer IRs than the Cab block (up to <see cref="MaxIrLength" />), kept at the file's
///     own gain, with a dry/wet mix and low and high cut on the wet signal. Zero latency.
/// </summary>
public sealed class IrPlayerBlock() : DspBlock(Info), IAssetConsumer {
    public const int Mix = 0;
    public const int LowCut = 1;
    public const int HighCut = 2;
    public const int Level = 3;

    /// <summary>Longest IR kept, in samples at the engine rate (about 680 ms at 48 kHz).</summary>
    public const int MaxIrLength = 32_768;

    static readonly IrShape Shape = new(MaxIrLength, UnitEnergy: false);

    readonly StereoConvolver convolver = new(Shape);
    SvfState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    NativeBuffer wetLeft = NativeBuffer.Empty;
    NativeBuffer wetRight = NativeBuffer.Empty;
    float mix, outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "irplayer",
        "IR Player",
        "Cab",
        [
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("lowcut", "Low Cut", CabBlock.LowCutOff, 500f, CabBlock.LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, CabBlock.HighCutOff, CabBlock.HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots { get; } = ["ir"];

    /// <summary>The IR as it will be convolved, for tests and the editor's plot.</summary>
    public float[]? PreparedImpulseResponse(int channel = 0) => convolver.Prepared?[channel];

    public void LoadAsset(string slot, string path) {
        if (slot != "ir") {
            throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot));
        }

        convolver.Source = ImpulseResponses.Load(path);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        convolver.Build(spec.SampleRate);
        wetLeft.Dispose();
        wetRight.Dispose();
        wetLeft = new(spec.MaxBlockSize);
        wetRight = new(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        convolver.Reset();
        lowCutLeft.Reset();
        lowCutRight.Reset();
        highCutLeft.Reset();
        highCutRight.Reset();
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var lowCut = Params.Advance(LowCut, io.Length);
        var highCut = Params.Advance(HighCut, io.Length);
        if (convolver.IsLoaded) {
            var left = wetLeft.Span[..io.Length];
            var right = wetRight.Span[..io.Length];
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            convolver.Process(left, right);
            if (lowCut > CabBlock.LowCutOff) {
                var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, lowCut, 0.7071);
                lowCutLeft.Process(c, left);
                lowCutRight.Process(c, right);
            }

            if (highCut < CabBlock.HighCutOff) {
                var c = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, highCut, 0.7071);
                highCutLeft.Process(c, left);
                highCutRight.Process(c, right);
            }

            Blend(io.Left, left, this.mix, mix);
            Blend(io.Right, right, this.mix, mix);
        }

        this.mix = mix;
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            convolver.Dispose();
            wetLeft.Dispose();
            wetRight.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary><c>dry = dry + (wet - dry) * mix</c>, with the mix ramped across the block.</summary>
    internal static void Blend(Span<float> dry, ReadOnlySpan<float> wet, float from, float to) {
        var step = (to - from) / dry.Length;
        var mix = from;
        for (var i = 0; i < dry.Length; i++) {
            mix += step;
            dry[i] += (wet[i] - dry[i]) * mix;
        }
    }
}
