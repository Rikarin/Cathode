using System.Text.Json;
using Cathode.Dsp.IO;
using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine;
using Cathode.Engine.Graph;

// cathode-render --graph graph.json --in di.wav --out out.wav [--block 64] [--format float32|pcm24|pcm16]

var options = ParseOptions(args);
if (!options.TryGetValue("graph", out var graphPath)
    || !options.TryGetValue("in", out var inputPath)
    || !options.TryGetValue("out", out var outputPath)) {
    Console.Error.WriteLine(
        "Usage: cathode-render --graph <graph.json> --in <input.wav> --out <output.wav> [--block 64] [--format float32|pcm24|pcm16]"
    );
    return 2;
}

var blockSize = options.TryGetValue("block", out var b)
    ? int.Parse(b, System.Globalization.CultureInfo.InvariantCulture)
    : 64;
var format = options.TryGetValue("format", out var f)
    ? Enum.Parse<WavSampleFormat>(f, ignoreCase: true)
    : WavSampleFormat.Float32;

try {
    var definition = JsonSerializer.Deserialize(File.ReadAllText(graphPath), GraphJsonContext.Default.GraphDefinition)
        ?? throw new InvalidDataException("Empty graph file.");
    var input = WavFile.Read(inputPath);
    var spec = new ProcessSpec(input.SampleRate, blockSize);
    var engine = new CathodeEngine(spec);
    engine.Publish(
        new GraphCompiler(
            BlockRegistry.CreateDefault().AddNativeBlocks(),
            Path.GetDirectoryName(Path.GetFullPath(graphPath))
        ).Compile(definition, spec, generation: 1)
    );
    var output = OfflineRenderer.Render(engine, input, blockSize);
    WavFile.Write(outputPath, output, format);
    Console.WriteLine($"Rendered {input.Frames} frames at {input.SampleRate} Hz -> {outputPath}");
    return 0;
} catch (Exception e) when (e is GraphValidationException
                                or InvalidDataException
                                or IOException
                                or JsonException
                                or NotSupportedException) {
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static Dictionary<string, string> ParseOptions(string[] args) {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i + 1 < args.Length; i += 2) {
        if (args[i].StartsWith("--", StringComparison.Ordinal)) {
            result[args[i][2..]] = args[i + 1];
        }
    }

    return result;
}
