using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Cabinet simulation: up to two impulse responses (mic blend), each with its own level, plus low and high cut.
///     IRs are resampled to the engine rate, normalised to unit energy and truncated to <see cref="MaxIrLength" />.
///     Zero latency. Stereo IRs process left and right separately; mono IRs are used for both channels. Prepared IRs
///     are cached by file, so a preset change back to a cab already heard reads, resamples and allocates nothing for
///     it; the convolvers keep their state in native memory.
/// </summary>
public sealed class CabBlock() : DspBlock(Info), IAssetConsumer {
    public const int Level1 = 0;
    public const int Level2 = 1;
    public const int LowCut = 2;
    public const int HighCut = 3;
    public const int Level = 4;

    /// <summary>Longest IR kept, in samples at the engine rate (about 170 ms at 48 kHz).</summary>
    public const int MaxIrLength = 8_192;

    /// <summary>Low cut at or below this is off.</summary>
    public const float LowCutOff = 10f;

    /// <summary>High cut at or above this is off.</summary>
    public const float HighCutOff = 20_000f;

    static readonly IrShape Shape = new(MaxIrLength);

    readonly StereoConvolver[] slots = [new(Shape), new(Shape)];
    SvfState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    NativeBuffer scratchLeft = NativeBuffer.Empty;
    NativeBuffer scratchRight = NativeBuffer.Empty;
    float gain1 = 1f, gain2 = 1f, outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "cab",
        "Cab",
        "Cab",
        [
            ParamDescriptor.Float("level1", "IR 1 Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("level2", "IR 2 Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 500f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots { get; } = ["ir1", "ir2"];

    /// <summary>Control thread: load a WAV impulse response into <c>ir1</c> or <c>ir2</c>.</summary>
    public void LoadAsset(string slot, string path) {
        var index = slot switch {
            "ir1" => 0,
            "ir2" => 1,
            _ => throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot))
        };

        slots[index].Source = ImpulseResponses.Load(path);
    }

    /// <summary>The IR as it will be convolved (resampled, truncated, normalised), for tests and the editor's plot.</summary>
    public float[]? PreparedImpulseResponse(int slot, int channel = 0) => slots[slot].Prepared?[channel];

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var slot in slots) {
                slot.Dispose();
            }

            scratchLeft.Dispose();
            scratchRight.Dispose();
        }

        base.Dispose(disposing);
    }

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        foreach (var slot in slots) {
            slot.Build(spec.SampleRate);
        }

        scratchLeft.Dispose();
        scratchRight.Dispose();
        scratchLeft = new(spec.MaxBlockSize);
        scratchRight = new(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        foreach (var slot in slots) {
            slot.Reset();
        }

        lowCutLeft.Reset();
        lowCutRight.Reset();
        highCutLeft.Reset();
        highCutRight.Reset();
        gain1 = Decibels.ToGain(Params.Current(Level1));
        gain2 = Decibels.ToGain(Params.Current(Level2));
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var gain1 = Decibels.ToGain(Params.Advance(Level1, io.Length));
        var gain2 = Decibels.ToGain(Params.Advance(Level2, io.Length));
        var first = slots[0];
        var second = slots[1];

        if (second.IsLoaded) {
            var left = scratchLeft.Span[..io.Length];
            var right = scratchRight.Span[..io.Length];
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            second.Process(left, right);
            GainRamp.Apply(left, this.gain2, gain2);
            GainRamp.Apply(right, this.gain2, gain2);

            if (first.IsLoaded) {
                first.Process(io.Left, io.Right);
                GainRamp.Apply(io.Left, this.gain1, gain1);
                GainRamp.Apply(io.Right, this.gain1, gain1);
                Accumulate(left, io.Left);
                Accumulate(right, io.Right);
            } else {
                left.CopyTo(io.Left);
                right.CopyTo(io.Right);
            }
        } else if (first.IsLoaded) {
            first.Process(io.Left, io.Right);
            GainRamp.Apply(io.Left, this.gain1, gain1);
            GainRamp.Apply(io.Right, this.gain1, gain1);
        }

        this.gain1 = gain1;
        this.gain2 = gain2;

        var lowCut = Params.Advance(LowCut, io.Length);
        if (lowCut > LowCutOff) {
            var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, lowCut, 0.7071);
            lowCutLeft.Process(c, io.Left);
            lowCutRight.Process(c, io.Right);
        }

        var highCut = Params.Advance(HighCut, io.Length);
        if (highCut < HighCutOff) {
            var c = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, highCut, 0.7071);
            highCutLeft.Process(c, io.Left);
            highCutRight.Process(c, io.Right);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }
}
