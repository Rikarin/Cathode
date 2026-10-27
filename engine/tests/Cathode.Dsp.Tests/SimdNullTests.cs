using Cathode.Audio;
using Cathode.Dsp.IO;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Ipc;
using Cathode.Testing;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Cathode.Dsp.Tests;

/// <summary>
///     SIMD against scalar (docs/03-testing-strategy.md §3, null tests): the code with vector paths, the convolver's
///     complex multiply-accumulate (Cab, IR Player), the AIDA-X model's dot products (Amp NAM), the pitch tracker's
///     difference function (Pitch) and the circuit solvers' paired LU rows (Circuit Amp, Drive), each falls back to
///     scalar code when the CPU has no vector unit. There is no switch for that inside a process (the JIT folds
///     <c>Vector.IsHardwareAccelerated</c> to a constant), so each graph is rendered by cathode-render twice in
///     separate processes, once as the machine runs it and once with <c>DOTNET_EnableHWIntrinsic=0</c>, which turns
///     every hardware intrinsic off and so takes every scalar path, and the two renders must null. The first must also
///     equal, bit for bit, the same graph rendered in this process (§3, determinism across two processes).
/// </summary>
public sealed class SimdNullTests(ITestOutputHelper output) : IDisposable {
    const int Rate = 48_000;

    /// <summary>
    ///     Null residual below the render's own level. Summing in another order changes the last bits of a float;
    ///     feedback (the solvers' Newton iterations, the model's recurrent state) carries those differences on but
    ///     must not grow them. On an x64 machine the convolver, the model and the tracker null about 130 to 145 dB
    ///     down; the solvers' paired rows do the same operations element by element, so they null exactly.
    /// </summary>
    const double MaxResidualDb = -100;

    readonly string folder = Directory.CreateTempSubdirectory("cathode-simd-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    public static TheoryData<string, string> Graphs =>
        new() {
            {
                "cab", """{ "id": "b1", "type": "cab", "assets": { "slot1": "ir.wav" }, "params": { "m1source": 0 } }"""
            },
            { "irplayer", """{ "id": "b1", "type": "irplayer", "assets": { "ir": "ir.wav" } }""" },
            { "nam", """{ "id": "b1", "type": "nam", "assets": { "model": "amp.aidax" } }""" },
            {
                "pitch", """{ "id": "b1", "type": "pitch", "params": { "type": 2 } }"""
            }, // Diatonic: follows the tracker
            { "circuitamp", """{ "id": "b1", "type": "circuitamp", "params": { "gain": 7 } }""" },
            { "drive", """{ "id": "b1", "type": "drive", "params": { "drive": 7 } }""" }
        };

    [Theory]
    [MemberData(nameof(Graphs))]
    public void Scalar_and_vector_paths_render_the_same(string name, string node) {
        Assert.SkipUnless(
            Vector.IsHardwareAccelerated,
            "this machine has no vector unit: both renders would be scalar"
        );
        WriteAssets();
        var graph = Path.Combine(folder, $"{name}.json");
        File.WriteAllText(
            graph,
            $$"""
              { "nodes": [
                  { "id": "in1", "type": "input", "row": 0, "column": 0 },
                  {{node.Replace("{ \"id\"", "{ \"row\": 0, \"column\": 1, \"id\"", StringComparison.Ordinal)}},
                  { "id": "out1", "type": "output", "row": 0, "column": 2 } ],
                "cables": [ { "from": "in1", "to": "b1" }, { "from": "b1", "to": "out1" } ] }
              """
        );

        var vector = Render(graph, $"{name}-vector.wav", false);
        var scalar = Render(graph, $"{name}-scalar.wav", true);

        // Determinism across processes: this process renders the same graph bit for bit as the child did.
        var here = RenderHere(graph);
        for (var c = 0; c < vector.Channels.Length; c++) {
            Assert.True(
                here.Channels[c].AsSpan().SequenceEqual(vector.Channels[c]),
                $"{name}: another process differs"
            );
        }

        for (var c = 0; c < vector.Channels.Length; c++) {
            var level = Analysis.ToDb(Analysis.Rms(vector.Channels[c]));
            var residual = Analysis.NullResidualDb(vector.Channels[c], scalar.Channels[c]) - level;
            output.WriteLine($"{name} channel {c}: {level:0.0} dBFS, scalar null {residual:0.0} dB below it");
            Assert.True(level > -60, $"{name} renders {level:0.0} dBFS: too quiet to show a difference");
            Assert.True(residual < MaxResidualDb, $"{name} channel {c}: scalar path {residual:0.0} dB from SIMD");
        }
    }

    /// <summary>The graph rendered in this process, as cathode-render renders it (64-frame blocks).</summary>
    AudioData RenderHere(string graph) {
        var definition = JsonSerializer.Deserialize(File.ReadAllText(graph), GraphJsonContext.Default.GraphDefinition)!;
        var input = WavFile.Read(Path.Combine(folder, "in.wav"));
        var spec = new ProcessSpec(input.SampleRate, 64);
        var engine = new CathodeEngine(spec);
        using var compiled = new GraphCompiler(BlockRegistry.CreateDefault(), folder).Compile(definition, spec, 1);
        engine.Publish(compiled);
        return OfflineRenderer.Render(engine, input, 64);
    }

    AudioData Render(string graph, string file, bool scalar) {
        var tool = Path.Combine(
            AppContext.BaseDirectory.Replace(
                Path.Combine("tests", "Cathode.Dsp.Tests"),
                Path.Combine("tools", "Cathode.Render"),
                StringComparison.Ordinal
            ),
            "cathode-render.dll"
        );
        Assert.True(File.Exists(tool), $"cathode-render not built at {tool}");
        var path = Path.Combine(folder, file);
        var start = new ProcessStartInfo(
            "dotnet",
            ["exec", tool, "--graph", graph, "--in", Path.Combine(folder, "in.wav"), "--out", path]
        ) { RedirectStandardOutput = true, RedirectStandardError = true };
        // Set either way, so the parent's environment (a CI job's own setting) cannot decide it.
        start.Environment["DOTNET_EnableHWIntrinsic"] = scalar ? "0" : "1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        process.WaitForExitAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        var text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"cathode-render failed ({process.ExitCode}):\n{text}");
        // The child says which paths it took: proof the switch took effect.
        Assert.Contains($"simd: {(scalar ? "off" : "on")}", text, StringComparison.Ordinal);
        return WavFile.Read(path);
    }

    void WriteAssets() {
        // A guitar-ish input: a decaying A2 with its harmonics (one pitch, for the tracker) over a little noise, two
        // seconds.
        var input = new float[2 * Rate];
        var noise = Signals.Noise(input.Length, 3, 0.01f);
        double[] notes = [110, 220, 330, 440, 550, 660];
        for (var i = 0; i < input.Length; i++) {
            var t = (double)i / Rate;
            var sum = 0.0;
            foreach (var f in notes) {
                sum += Math.Sin(2 * Math.PI * f * t) * Math.Exp(-1.5 * t);
            }

            input[i] = (float)(0.08 * sum) + noise[i];
        }

        WavFile.Write(Path.Combine(folder, "in.wav"), new(Rate, [input, input]));

        // An impulse response long enough for several partitions: decaying noise, 8192 samples.
        var ir = Signals.Noise(8_192, 5);
        for (var i = 0; i < ir.Length; i++) {
            ir[i] *= (float)Math.Exp(-i / 1_200.0);
        }

        ir[0] = 1;
        WavFile.Write(Path.Combine(folder, "ir.wav"), new(Rate, [ir]));
        File.WriteAllText(
            Path.Combine(folder, "amp.aidax"),
            CaptureAmpBlockTests.SyntheticModel.Create("lstm", 16, 1, true, 2).Json,
            Encoding.UTF8
        );
    }
}
