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

    /// <summary>
    ///     Renders with several hardware input sources (<see cref="Dsp.Routing.HardwareInputs" />), as a multi-channel
    ///     device would deliver them: <paramref name="sources" />[s] is source s (Input 1 first, which is also the main
    ///     input), null where the device maps nothing. Every source must be as long as Input 1.
    /// </summary>
    public static (float[] Left, float[] Right) Render(
        IAudioProcessor processor,
        IReadOnlyList<(float[] Left, float[] Right)?> sources,
        int blockSize
    ) {
        var (left, right) = sources[0] ?? throw new ArgumentException("Input 1 is required.", nameof(sources));
        var count = Dsp.Routing.HardwareInputs.Count;
        var mask = 0;
        for (var s = 0; s < Math.Min(count, sources.Count); s++) {
            mask |= sources[s] is null ? 0 : 1 << s;
        }

        var outLeft = new float[left.Length];
        var outRight = new float[left.Length];
        var planar = new float[count * 2 * blockSize];
        for (var offset = 0; offset < left.Length; offset += blockSize) {
            var frames = Math.Min(blockSize, left.Length - offset);
            for (var s = 0; s < count; s++) {
                var source = s < sources.Count ? sources[s] : null;
                var l = planar.AsSpan(2 * s * frames, frames);
                var r = planar.AsSpan((2 * s + 1) * frames, frames);
                if (source is { } pair) {
                    pair.Left.AsSpan(offset, frames).CopyTo(l);
                    pair.Right.AsSpan(offset, frames).CopyTo(r);
                } else {
                    l.Clear();
                    r.Clear();
                }
            }

            processor.Process(
                new(
                    left.AsSpan(offset, frames),
                    right.AsSpan(offset, frames),
                    outLeft.AsSpan(offset, frames),
                    outRight.AsSpan(offset, frames),
                    [],
                    [],
                    [],
                    [],
                    planar.AsSpan(0, count * 2 * frames),
                    mask
                )
            );
        }

        return (outLeft, outRight);
    }

    /// <summary>Renders a WAV-style clip. Mono input is fed to both engine channels (a guitar DI is mono).</summary>
    public static AudioData Render(IAudioProcessor processor, AudioData input, int blockSize) {
        var left = input.Channels[0];
        var right = input.Channels.Length > 1 ? input.Channels[1] : left;
        var (outLeft, outRight) = Render(processor, left, right, blockSize);
        return new(input.SampleRate, [outLeft, outRight]);
    }
}
