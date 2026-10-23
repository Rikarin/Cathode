using Cathode.Dsp.Convolution;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Applies a Tone Match correction: a minimum-phase filter that turns the rig's long-term spectrum into a reference
///     recording's. The Server learns the filter (<c>POST /api/v1/rig/blocks/{id}/tonematch</c>) and stores it as the
///     block's <c>match</c> asset; the block convolves it at the file's own gain. Without a match it passes the signal
///     through. Zero latency.
/// </summary>
public sealed class ToneMatchBlock() : DspBlock(Info), IAssetConsumer {
    public const int Mix = 0;
    public const int Level = 1;

    /// <summary>Longest correction filter kept; the Server designs 2048 taps (43 ms at 48 kHz).</summary>
    public const int FilterLength = 4_096;

    static readonly IrShape Shape = new(FilterLength, UnitEnergy: false);

    readonly StereoConvolver convolver = new(Shape);
    NativeBuffer wetLeft = NativeBuffer.Empty;
    NativeBuffer wetRight = NativeBuffer.Empty;
    float mix, outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "tonematch",
        "Tone Match",
        "EQ",
        [
            ParamDescriptor.Float("mix", "Amount", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 1
    );

    public IReadOnlyList<string> AssetSlots { get; } = ["match"];

    public bool IsMatched => convolver.IsLoaded;

    public void LoadAsset(string slot, string path) {
        if (slot != "match") {
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
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var mix = Params.Advance(Mix, io.Length) / 100f;
        if (convolver.IsLoaded) {
            var left = wetLeft.Span[..io.Length];
            var right = wetRight.Span[..io.Length];
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            convolver.Process(left, right);
            IrPlayerBlock.Blend(io.Left, left, this.mix, mix);
            IrPlayerBlock.Blend(io.Right, right, this.mix, mix);
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
}
