using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Engine.Graph;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>NAM tests share the process-global fast-tanh switch, so they never run in parallel.</summary>
[CollectionDefinition(nameof(NamSerialGroup), DisableParallelization = true)]
public sealed class NamSerialGroup;

[Collection(nameof(NamSerialGroup))]
public class NamAmpBlockTests
{
    private const int Block = 64;
    private static readonly ProcessSpec Spec = new(48_000, Block);

    /// <summary>Model file, reference render, slimmable size (null = model default).</summary>
    public static TheoryData<string, string, double?> GoldenModels => new()
    {
        { "wavenet_a1_standard", "wavenet_a1_standard", null },
        { "A2", "A2", null },
        { "A2", "A2-lite", 0.0 },
        { "lstm", "lstm", null },
    };

    [Theory]
    [MemberData(nameof(GoldenModels))]
    public void Matches_the_upstream_reference_render(string model, string referenceName, double? size)
    {
        RequireNative();
        NamModel.SetFastTanh(false); // upstream render uses exact tanh
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden($"{referenceName}.ref.wav")).Channels[0];

        using var block = CreateBlock(model, normalize: false, size);
        var output = Render(block, input, _ => Block);

        var relativeDb = Analysis.NullResidualDb(output, reference) - Analysis.ToDb(Analysis.Rms(reference));
        Assert.True(relativeDb < -80, $"{referenceName}: residual {relativeDb:0.0} dB relative to the reference");
    }

    [Fact]
    public void Golden_comparison_detects_a_tenth_of_a_decibel()
    {
        // Guards against a golden test that passes vacuously (an earlier stimulus was digital silence).
        RequireNative();
        NamModel.SetFastTanh(false);
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("wavenet_a1_standard.ref.wav")).Channels[0];

        using var block = CreateBlock("wavenet_a1_standard", normalize: false);
        block.Params.SetTarget(NamAmpBlock.Input, 0.1f);
        block.Reset();
        var output = Render(block, input, _ => Block);

        var relativeDb = Analysis.NullResidualDb(output, reference) - Analysis.ToDb(Analysis.Rms(reference));
        Assert.True(relativeDb > -60, $"a 0.1 dB input change only produced {relativeDb:0.0} dB of residual");
    }

    [Fact]
    public void Fast_tanh_stays_close_to_exact_tanh()
    {
        RequireNative();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("wavenet_a1_standard.ref.wav")).Channels[0];
        try
        {
            NamModel.SetFastTanh(true);
            using var block = CreateBlock("wavenet_a1_standard", normalize: false);
            var output = Render(block, input, _ => Block);

            Assert.True(Analysis.Esr(reference, output) < 1e-3, $"ESR {Analysis.Esr(reference, output):E2}");
        }
        finally
        {
            NamModel.SetFastTanh(false);
        }
    }

    [Fact]
    public void Output_does_not_depend_on_block_size()
    {
        RequireNative();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var random = new Random(9);
        using var fixedSize = CreateBlock("wavenet_a1_standard", normalize: false);
        using var variableSize = CreateBlock("wavenet_a1_standard", normalize: false);

        var a = Render(fixedSize, input, _ => Block);
        var b = Render(variableSize, input, _ => random.Next(1, Block + 1));

        Assert.True(Analysis.NullResidualDb(a, b) < -120);
    }

    [Fact]
    public void Process_does_not_allocate()
    {
        RequireNative();
        using var block = CreateBlock("A2", normalize: true);
        var left = Signals.Noise(Block, seed: 1, amplitude: 0.3f);
        var right = new float[Block];
        var context = new BlockContext(Spec.SampleRate, 120, 0);

        var allocated = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                block.Process(new StereoBuffer(left, right), context);
            }
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Normalize_brings_the_model_to_the_target_loudness()
    {
        RequireNative();
        var input = Signals.Noise(4_800, seed: 2, amplitude: 0.2f);
        using var plain = CreateBlock("A2", normalize: false);
        using var normalized = CreateBlock("A2", normalize: true);

        var gain = Analysis.Rms(Render(normalized, input, _ => Block)) / Analysis.Rms(Render(plain, input, _ => Block));

        var expected = NamAmpBlock.TargetLoudnessDb - normalized.Model!.Loudness!.Value;
        Assert.Equal(expected, Analysis.ToDb(gain), 0.01);
    }

    [Fact]
    public void Graph_loads_the_model_as_an_asset_and_renders_it()
    {
        RequireNative();
        NamModel.SetFastTanh(false);
        var definition = new GraphDefinition
        {
            Nodes =
            [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "amp1", Type = "nam", Row = 0, Column = 1, Parameters = { ["normalize"] = 0 }, Assets = { ["model"] = "lstm.nam" } },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 },
            ],
            Cables = [new() { From = "in", To = "amp1" }, new() { From = "amp1", To = "out" }],
        };
        var engine = new CathodeEngine(Spec);
        engine.Publish(new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks(), NamAssets.ModelsDirectory).Compile(definition, Spec, generation: 1));
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("lstm.ref.wav")).Channels[0];

        var (left, right) = OfflineRenderer.Render(engine, input, input, Block);

        Assert.True(Analysis.NullResidualDb(left, reference) - Analysis.ToDb(Analysis.Rms(reference)) < -80);
        Assert.Equal(left, right);
    }

    [Fact]
    public void Unloadable_models_are_reported_as_graph_errors()
    {
        RequireNative();
        var definition = new GraphDefinition
        {
            Nodes =
            [
                new() { Id = "missing", Type = "nam", Row = 0, Column = 0, Assets = { ["model"] = "does-not-exist.nam" } },
                new() { Id = "garbage", Type = "nam", Row = 1, Column = 0, Assets = { ["model"] = NamAssets.Golden("README.md") } },
                new() { Id = "noslot", Type = "volpan", Row = 2, Column = 0, Assets = { ["model"] = "lstm.nam" } },
            ],
        };

        var error = Assert.Throws<GraphValidationException>(() =>
            new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks(), NamAssets.ModelsDirectory).Compile(definition, Spec));

        Assert.Contains(error.Errors, static e => e.Contains("'missing'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("'garbage'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("no asset slot", StringComparison.Ordinal));
    }

    private static void RequireNative() =>
        Assert.SkipUnless(NamModel.IsLibraryPresent && Directory.Exists(NamAssets.ModelsDirectory), "cth_nam not built (run native/build.sh)");

    private static NamAmpBlock CreateBlock(string model, bool normalize, double? size = null)
    {
        var block = new NamAmpBlock();
        block.LoadAsset(NamAmpBlock.ModelSlot, Path.Combine(NamAssets.ModelsDirectory, model + ".nam"));
        if (size is double s)
        {
            block.SetModelSize(s);
        }

        block.Params.SetTarget(NamAmpBlock.Normalize, normalize ? 1 : 0);
        block.Prepare(Spec);
        return block;
    }

    private static float[] Render(IDspBlock block, float[] input, Func<int, int> blockSize)
    {
        var left = (float[])input.Clone();
        var right = new float[input.Length];
        var callback = 0;
        for (var offset = 0; offset < input.Length;)
        {
            var frames = Math.Min(blockSize(callback++), input.Length - offset);
            block.Process(new StereoBuffer(left.AsSpan(offset, frames), right.AsSpan(offset, frames)), new BlockContext(Spec.SampleRate, 120, offset));
            offset += frames;
        }

        return left;
    }
}

/// <summary>Locates the NAM golden files (committed) and the upstream example models (fetched by the native build).</summary>
internal static class NamAssets
{
    private static readonly string EngineRoot = FindEngineRoot();

    public static string ModelsDirectory => Path.Combine(EngineRoot, "artifacts", "native", "nam-models");

    public static string Golden(string file) => Path.Combine(EngineRoot, "tests", "assets", "nam", file);

    private static string FindEngineRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder (Cathode.slnx).");
    }
}
