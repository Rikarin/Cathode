using System.Diagnostics;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;

namespace Cathode.Host;

/// <summary>
/// Spike S3: CPU cost of a NAM model measured through the whole engine (Input -> NAM -> Output), offline at
/// full speed, with the same callback statistics the device path reports.
/// </summary>
internal static class NamBenchmark
{
    public static int Run(CommandLine options)
    {
        var path = options.Text("model") ?? throw new InvalidOperationException("--model <file.nam> is required.");
        var spec = new ProcessSpec(48_000, options.Int("buffer", 64));
        var seconds = options.Int("seconds", 10);
        var fastTanh = !options.Flag("exact-tanh");
        NamModel.SetFastTanh(fastTanh);

        var graph = Compile(Graphs.Nam(Path.GetFullPath(path)), spec);
        var amp = (NamAmpBlock)graph.Block(graph.SlotOf("amp1"));
        if (options.Text("size") is { } size)
        {
            amp.SetModelSize(double.Parse(size, System.Globalization.CultureInfo.InvariantCulture));
        }

        var engine = new CathodeEngine(spec);
        engine.Publish(graph);

        // The device path enables flush-to-zero on the audio thread; --ftz does the same here.
        if (options.Flag("ftz"))
        {
            Cathode.Audio.Native.NativeAudio.FlushDenormalsOnCurrentThread();
        }

        var random = new Random(1);
        var input = new float[(int)spec.SampleRate * seconds];
        if (!options.Flag("silent-input")) // upstream's benchmodel feeds zeros; real DI costs more
        {
            for (var i = 0; i < input.Length; i++)
            {
                input[i] = (float)(random.NextDouble() * 2 - 1) * 0.1f; // about -25 dBFS RMS, a typical DI
            }
        }

        var outLeft = new float[input.Length];
        var outRight = new float[input.Length];
        var callbacks = input.Length / spec.MaxBlockSize;

        void RenderAll()
        {
            for (var c = 0; c < callbacks; c++)
            {
                var offset = c * spec.MaxBlockSize;
                var block = input.AsSpan(offset, spec.MaxBlockSize);
                engine.Process(new AudioIO(block, block, outLeft.AsSpan(offset, spec.MaxBlockSize), outRight.AsSpan(offset, spec.MaxBlockSize)));
            }
        }

        RenderAll(); // warm-up
        engine.Stats.Reset();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        RenderAll();
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var info = amp.Model!;
        Console.WriteLine($"model:        {Path.GetFileName(path)}  {info.Architecture} (file {info.FileVersion})  NAM core {NamModel.CoreVersion}  size {options.Text("size") ?? "default"}  tanh {(fastTanh ? "fast" : "exact")}  input {(options.Flag("silent-input") ? "silence" : "noise")}  ftz {options.Flag("ftz")}");
        Console.WriteLine($"rendered:     {seconds} s at {spec.SampleRate / 1000:0.#} kHz / {spec.MaxBlockSize} frames in {elapsed.TotalMilliseconds:0.0} ms");
        Console.WriteLine($"cpu:          {elapsed.TotalSeconds / seconds * 100:0.00}% of one core  ({seconds / elapsed.TotalSeconds:0}x real time)");
        Console.WriteLine($"callback:     p50 <= {engine.Stats.Percentile(0.5):0}%  p99.9 <= {engine.Stats.Percentile(0.999):0}%  max {engine.Stats.MaxPercent:0.0}% of budget");
        Console.WriteLine($"allocations:  {allocated} bytes across {callbacks} callbacks");
        Console.WriteLine($"output:       peak {outLeft.Max(Math.Abs):0.000}, finite {outLeft.All(float.IsFinite)}");
        return allocated == 0 && outLeft.All(float.IsFinite) ? 0 : 1;
    }

    private static CompiledGraph Compile(GraphDefinition definition, ProcessSpec spec) =>
        new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks()).Compile(definition, spec, generation: 1);
}

/// <summary>Graphs used by the host's diagnostics.</summary>
internal static class Graphs
{
    /// <summary>Input -> NAM amp (normalised) -> Output.</summary>
    public static GraphDefinition Nam(string modelPath) => new()
    {
        Nodes =
        [
            new() { Id = "in1", Type = NodeTypes.Input, Row = 0, Column = 0 },
            new() { Id = "amp1", Type = "nam", Row = 0, Column = 1, Assets = { ["model"] = modelPath } },
            new() { Id = "out1", Type = NodeTypes.Output, Row = 0, Column = 2 },
        ],
        Cables = [new() { From = "in1", To = "amp1" }, new() { From = "amp1", To = "out1" }],
    };
}
