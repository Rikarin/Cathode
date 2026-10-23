namespace Cathode.Dsp.Tests;

/// <summary>Drives a single block directly, without the engine.</summary>
static class BlockRender {
    public static (float[] Left, float[] Right) Stereo(
        IDspBlock block,
        float[] left,
        float[] right,
        Func<int, int> blockSize,
        long startSample = 0
    ) {
        var outLeft = (float[])left.Clone();
        var outRight = (float[])right.Clone();
        var callback = 0;
        for (var offset = 0; offset < left.Length;) {
            var frames = Math.Clamp(blockSize(callback++), 1, left.Length - offset);
            var io = new StereoBuffer(outLeft.AsSpan(offset, frames), outRight.AsSpan(offset, frames));
            block.Process(io, new(48_000, 120, startSample + offset));
            offset += frames;
        }

        return (outLeft, outRight);
    }

    public static float[] Mono(IDspBlock block, float[] input, int blockSize) =>
        Stereo(block, input, input, _ => blockSize).Left;
}
