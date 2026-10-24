using Cathode.Dsp.IO;
using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     Deterministic, as-fast-as-possible rendering with no sound card. This is the foundation of the test suite
///     and of the <c>cathode-render</c> tool.
/// </summary>
public static class OfflineRenderer {
    /// <summary>Renders stereo input through <paramref name="processor" /> in blocks of <paramref name="blockSize" /> frames.</summary>
    public static (float[] Left, float[] Right) Render(
        IAudioProcessor processor,
        float[] left,
        float[] right,
        int blockSize
    ) =>
        Render(processor, left, right, _ => blockSize);

    /// <summary>
    ///     Renders with a block size chosen per callback (callback index -> frames). Variable sizes catch bugs at
    ///     block boundaries.
    /// </summary>
    public static (float[] Left, float[] Right) Render(
        IAudioProcessor processor,
        float[] left,
        float[] right,
        Func<int, int> blockSize
    ) {
        if (left.Length != right.Length) {
            throw new ArgumentException("Channels must have the same length.", nameof(right));
        }

        var outLeft = new float[left.Length];
        var outRight = new float[right.Length];
        var callback = 0;
        for (var offset = 0; offset < left.Length;) {
            var frames = Math.Clamp(blockSize(callback++), 1, left.Length - offset);
            processor.Process(
                new(
                    left.AsSpan(offset, frames),
                    right.AsSpan(offset, frames),
                    outLeft.AsSpan(offset, frames),
                    outRight.AsSpan(offset, frames)
                )
            );
            offset += frames;
        }

        return (outLeft, outRight);
    }

    /// <summary>
    ///     Renders through every output <paramref name="outputs" /> maps: Output 1's left and right, then Outputs 2-4's,
    ///     then the DI out's two channels (ten channels; an unmapped output stays silent).
    /// </summary>
    public static float[][] RenderOutputs(
        IAudioProcessor processor,
        float[] left,
        float[] right,
        int blockSize,
        OutputBus outputs
    ) {
        var channels = new float[2 * OutputBus.Count + 2][];
        for (var c = 0; c < channels.Length; c++) {
            channels[c] = new float[left.Length];
        }

        for (var offset = 0; offset < left.Length;) {
            var frames = Math.Min(blockSize, left.Length - offset);
            processor.Process(
                new(
                    left.AsSpan(offset, frames),
                    right.AsSpan(offset, frames),
                    channels[0].AsSpan(offset, frames),
                    channels[1].AsSpan(offset, frames)
                ) { Outputs = outputs }
            );
            for (var k = 1; k < OutputBus.Count; k++) {
                if (outputs.IsMapped(k)) {
                    outputs.Left(k)[..frames].CopyTo(channels[2 * k].AsSpan(offset));
                    outputs.Right(k)[..frames].CopyTo(channels[2 * k + 1].AsSpan(offset));
                }
            }

            if (outputs.HasDi) {
                outputs.DiLeft[..frames].CopyTo(channels[^2].AsSpan(offset));
                outputs.DiRight[..frames].CopyTo(channels[^1].AsSpan(offset));
            }

            offset += frames;
        }

        return channels;
    }

    /// <summary>Renders a WAV-style clip. Mono input is fed to both engine channels (a guitar DI is mono).</summary>
    public static AudioData Render(IAudioProcessor processor, AudioData input, int blockSize) {
        var left = input.Channels[0];
        var right = input.Channels.Length > 1 ? input.Channels[1] : left;
        var (outLeft, outRight) = Render(processor, left, right, blockSize);
        return new(input.SampleRate, [outLeft, outRight]);
    }
}
