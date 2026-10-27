using Cathode.Engine;
using System.Diagnostics;

namespace Cathode.Host;

/// <summary>Offline rendering through the whole engine for the host's diagnostics, at full speed, no device.</summary>
static class OfflineRender {
    /// <summary>
    ///     Renders <paramref name="input" /> (on both input channels) in callbacks of <paramref name="blockSize" />
    ///     frames once to warm up, then once more measured: time, and bytes allocated on this thread.
    /// </summary>
    public static RenderResult Measure(CathodeEngine engine, float[] input, int blockSize) {
        var outLeft = new float[input.Length];
        var outRight = new float[input.Length];
        var callbacks = input.Length / blockSize;
        RenderAll(engine, input, outLeft, outRight, callbacks, blockSize); // warm-up
        engine.Stats.Reset();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        RenderAll(engine, input, outLeft, outRight, callbacks, blockSize);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new(outLeft, callbacks, elapsed, allocated);
    }

    /// <summary>Uniform noise of peak <paramref name="amplitude" />, the same on every run.</summary>
    public static float[] Noise(int length, float amplitude) {
        var random = new Random(1);
        var input = new float[length];
        for (var i = 0; i < input.Length; i++) {
            input[i] = (float)(random.NextDouble() * 2 - 1) * amplitude;
        }

        return input;
    }

    static void RenderAll(
        CathodeEngine engine,
        float[] input,
        float[] outLeft,
        float[] outRight,
        int callbacks,
        int blockSize
    ) {
        for (var c = 0; c < callbacks; c++) {
            var offset = c * blockSize;
            var block = input.AsSpan(offset, blockSize);
            engine.Process(new(block, block, outLeft.AsSpan(offset, blockSize), outRight.AsSpan(offset, blockSize)));
        }
    }
}

/// <summary>What <see cref="OfflineRender.Measure" /> rendered and measured.</summary>
/// <param name="OutLeft">The left output of the measured pass.</param>
/// <param name="Callbacks">Callbacks per pass.</param>
/// <param name="Elapsed">Wall time of the measured pass.</param>
/// <param name="Allocated">Bytes allocated on this thread during the measured pass.</param>
readonly record struct RenderResult(float[] OutLeft, int Callbacks, TimeSpan Elapsed, long Allocated);
