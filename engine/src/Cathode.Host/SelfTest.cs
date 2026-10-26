using Cathode.Dsp;
using Cathode.Engine;
using Cathode.Engine.Graph;
using System.Diagnostics;

namespace Cathode.Host;

/// <summary>Offline check of the real-time path: zero allocation and callback headroom, no device needed.</summary>
static class SelfTest {
    public static int Run() {
        const int seconds = 10;
        var spec = new ProcessSpec(48_000, 64);
        var graph = new GraphCompiler(BlockRegistry.CreateDefault()).Compile(
            GraphTemplates.Basic(),
            spec,
            1
        );
        var engine = new CathodeEngine(spec);
        engine.Publish(graph);

        var random = new Random(1);
        var input = new float[(int)spec.SampleRate * seconds];
        for (var i = 0; i < input.Length; i++) {
            input[i] = (float)(random.NextDouble() * 2 - 1) * 0.5f;
        }

        var outLeft = new float[input.Length];
        var outRight = new float[input.Length];
        var callbacks = input.Length / spec.MaxBlockSize;

        void RenderAll() {
            for (var c = 0; c < callbacks; c++) {
                var offset = c * spec.MaxBlockSize;
                var block = input.AsSpan(offset, spec.MaxBlockSize);
                engine.Process(
                    new(
                        block,
                        block,
                        outLeft.AsSpan(offset, spec.MaxBlockSize),
                        outRight.AsSpan(offset, spec.MaxBlockSize)
                    )
                );
            }
        }

        RenderAll(); // warm-up
        engine.Stats.Reset();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        RenderAll();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var realtime = seconds / elapsed.TotalSeconds;
        Console.WriteLine(
            $"graph:        {string.Join(" -> ", graph.SlotIds)} ({graph.Ops.Count} ops, {graph.BufferCount} buffer(s))"
        );
        Console.WriteLine(
            $"rendered:     {seconds} s at {spec.SampleRate / 1000:0.#} kHz / {spec.MaxBlockSize} frames in {elapsed.TotalMilliseconds:0.0} ms ({realtime:0}x real time)"
        );
        Console.WriteLine(
            $"callback:     p50 <= {engine.Stats.Percentile(0.5):0}%  p99.9 <= {engine.Stats.Percentile(0.999):0}%  max {engine.Stats.MaxPercent:0.0}% of budget"
        );
        Console.WriteLine($"allocations:  {allocated} bytes across {callbacks} callbacks");
        Console.WriteLine($"output peak:  {outLeft.Max(Math.Abs):0.000}");

        var ok = allocated == 0 && outLeft.All(float.IsFinite);
        Console.WriteLine(ok ? "self-test: PASS" : "self-test: FAIL");
        return ok ? 0 : 1;
    }
}
