using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;
using System.Globalization;
using System.Text.Json;

namespace Cathode.Host;

/// <summary>
///     Spike S3: CPU cost of a NAM model measured through the whole engine (Input -> NAM -> Output), offline at
///     full speed, with the same callback statistics the device path reports.
/// </summary>
static class NamBenchmark {
    public static int Run(CommandLine options) {
        var graphPath = options.Text("graph");
        var path = options.Text("model");
        if (graphPath is null && path is null) {
            throw new InvalidOperationException("--model <file.nam> or --graph <graph.json> is required.");
        }

        var spec = new ProcessSpec(48_000, options.Int("buffer", 64));
        var seconds = options.Int("seconds", 10);
        var fastTanh = !options.Flag("exact-tanh");
        NamModel.SetFastTanh(fastTanh);

        var graph = graphPath is not null
            ? CompileFile(graphPath, spec)
            : Compile(GraphTemplates.Nam(Path.GetFullPath(path!)), spec);
        var amp = graph.SlotOf("amp1") is >= 0 and var ampSlot ? graph.Block(ampSlot) as NamAmpBlock : null;
        if (options.Text("size") is { } size && amp is not null) {
            amp.SetModelSize(double.Parse(size, CultureInfo.InvariantCulture));
        }

        var engine = new CathodeEngine(spec);
        engine.Publish(graph);

        // The device path enables flush-to-zero on the audio thread; --ftz does the same here.
        if (options.Flag("ftz")) {
            NativeAudio.FlushDenormalsOnCurrentThread();
        }

        var length = (int)spec.SampleRate * seconds;
        var input = options.Flag("silent-input")
            ? new float[length] // upstream's benchmodel feeds zeros; real DI costs more
            : OfflineRender.Noise(length, 0.1f); // about -25 dBFS RMS, a typical DI
        var (outLeft, callbacks, elapsed, allocated) = OfflineRender.Measure(engine, input, spec.MaxBlockSize);

        Console.WriteLine(
            graphPath is not null
                ? DescribeGraph(graphPath, graph, fastTanh)
                : DescribeModel(options, path, amp, fastTanh)
        );
        Console.WriteLine(
            $"rendered:     {seconds} s at {spec.SampleRate / 1000:0.#} kHz / {spec.MaxBlockSize} frames in "
            + $"{elapsed.TotalMilliseconds:0.0} ms"
        );
        Console.WriteLine(
            $"cpu:          {elapsed.TotalSeconds / seconds * 100:0.00}% of one core  "
            + $"({seconds / elapsed.TotalSeconds:0}x real time)"
        );
        Console.WriteLine(
            $"callback:     p50 <= {engine.Stats.Percentile(0.5):0}%  p99.9 <= {engine.Stats.Percentile(0.999):0}%  "
            + $"max {engine.Stats.MaxPercent:0.0}% of budget"
        );
        Console.WriteLine($"allocations:  {allocated} bytes across {callbacks} callbacks");
        Console.WriteLine($"output:       peak {outLeft.Max(Math.Abs):0.000}, finite {outLeft.All(float.IsFinite)}");
        return allocated == 0 && outLeft.All(float.IsFinite) ? 0 : 1;
    }

    static string DescribeGraph(string graphPath, CompiledGraph graph, bool fastTanh) =>
        $"graph:        {Path.GetFileName(graphPath)}  {graph.SlotIds.Count} blocks "
        + $"({string.Join(", ", graph.SlotIds)})  tanh {(fastTanh ? "fast" : "exact")}";

    static string DescribeModel(CommandLine options, string? path, NamAmpBlock? amp, bool fastTanh) =>
        $"model:        {Path.GetFileName(path)}  {amp?.Model?.Architecture} (file {amp?.Model?.FileVersion})  "
        + $"NAM core {NamModel.CoreVersion}  size {options.Text("size") ?? "default"}  "
        + $"tanh {(fastTanh ? "fast" : "exact")}  input {(options.Flag("silent-input") ? "silence" : "noise")}  "
        + $"ftz {options.Flag("ftz")}";

    /// <summary>A graph from a JSON file; relative asset paths resolve against the file's folder.</summary>
    static CompiledGraph CompileFile(string graphPath, ProcessSpec spec) =>
        new GraphCompiler(
            BlockRegistry.CreateDefault().AddNativeBlocks(),
            Path.GetDirectoryName(Path.GetFullPath(graphPath))
        ).Compile(
            JsonSerializer.Deserialize(File.ReadAllText(graphPath), GraphJsonContext.Default.GraphDefinition)!,
            spec,
            1
        );

    static CompiledGraph Compile(GraphDefinition definition, ProcessSpec spec) =>
        new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks()).Compile(definition, spec, 1);
}
