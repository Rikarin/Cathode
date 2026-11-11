using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.IO;
using Cathode.Dsp.Native;
using Cathode.Engine.Graph;
using Cathode.Ipc;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>NAM tests share the process-global fast-tanh switch, so they never run in parallel.</summary>
[CollectionDefinition(nameof(NamSerialGroup), DisableParallelization = true)]
public sealed class NamSerialGroup;

[Collection(nameof(NamSerialGroup))]
public class NamAmpBlockTests {
    const int Block = 64;
    static readonly ProcessSpec Spec = new(48_000, Block);

    /// <summary>Model file, reference render, the Size parameter on Lite.</summary>
    public static TheoryData<string, string, bool> GoldenModels =>
        new() {
            { "wavenet_a1_standard", "wavenet_a1_standard", false },
            { "A2", "A2", false },
            { "A2", "A2-lite", true },
            { "lstm", "lstm", false }
        };

    [Theory]
    [MemberData(nameof(GoldenModels))]
    public void Matches_the_upstream_reference_render(string model, string referenceName, bool lite) {
        RequireNative();
        using var exact = new ExactTanh(); // upstream render uses exact tanh
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden($"{referenceName}.ref.wav")).Channels[0];

        using var block = CreateBlock(model, false, lite);
        var output = Render(block, input, static _ => Block);

        var relativeDb = Analysis.NullResidualDb(output, reference) - Analysis.ToDb(Analysis.Rms(reference));
        Assert.True(relativeDb < -80, $"{referenceName}: residual {relativeDb:0.0} dB relative to the reference");
    }

    [Fact]
    public void Size_survives_a_second_prepare_and_goes_back_to_full() {
        RequireNative();
        using var exact = new ExactTanh();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var full = WavFile.Read(NamAssets.Golden("A2.ref.wav")).Channels[0];
        var liteReference = WavFile.Read(NamAssets.Golden("A2-lite.ref.wav")).Channels[0];
        using var block = CreateBlock("A2", false, true);

        block.Prepare(new(48_000, 128)); // the engine prepares a block again when the device's buffer changes
        var lite = Render(block, input, static _ => Block);
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Size, Cathode.Dsp.Blocks.CaptureAmpBlock.SizeFull);
        block.Prepare(Spec);
        var back = Render(block, input, static _ => Block);

        Assert.True(Analysis.NullResidualDb(lite, liteReference) - Analysis.ToDb(Analysis.Rms(liteReference)) < -80);
        Assert.True(Analysis.NullResidualDb(back, full) - Analysis.ToDb(Analysis.Rms(full)) < -80);
    }

    [Fact]
    public void Size_does_nothing_to_a_model_that_has_one_size() {
        RequireNative();
        var input = Signals.Noise(4_800, 5, 0.2f);
        using var full = CreateBlock("wavenet_a1_standard", false);
        using var lite = CreateBlock("wavenet_a1_standard", false, true);

        Assert.False(lite.Model!.Slimmable);
        Assert.Equal(Render(full, input, static _ => Block), Render(lite, input, static _ => Block));
    }

    [Fact]
    public void The_file_reader_and_the_core_agree_on_which_example_models_have_sizes() {
        // The Server tells the editor whether a capture has sizes from the file alone (NamFile); the core is the
        // authority. Upstream's examples hold both slimmable kinds (a container, a WaveNet) and five that are neither.
        RequireNative();
        var models = Directory.GetFiles(NamAssets.ModelsDirectory, "*.nam");
        var slimmable = 0;

        foreach (var path in models) {
            using var model = NamModel.Load(path);
            Assert.True(
                model.Info.Slimmable == Cathode.Dsp.Capture.NamFile.IsSlimmable(path),
                Path.GetFileName(path)
            );
            slimmable += model.Info.Slimmable ? 1 : 0;
        }

        Assert.InRange(slimmable, 3, models.Length - 1); // A2 and upstream's two slimmable examples at least
    }

    [Fact]
    public void Golden_comparison_detects_a_tenth_of_a_decibel() {
        // Guards against a golden test that passes vacuously (an earlier stimulus was digital silence).
        RequireNative();
        using var exact = new ExactTanh();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("wavenet_a1_standard.ref.wav")).Channels[0];

        using var block = CreateBlock("wavenet_a1_standard", false);
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Input, 0.1f);
        block.Reset();
        var output = Render(block, input, static _ => Block);

        var relativeDb = Analysis.NullResidualDb(output, reference) - Analysis.ToDb(Analysis.Rms(reference));
        Assert.True(relativeDb > -60, $"a 0.1 dB input change only produced {relativeDb:0.0} dB of residual");
    }

    [Fact]
    public void Fast_tanh_stays_close_to_exact_tanh() {
        RequireNative();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("wavenet_a1_standard.ref.wav")).Channels[0];
        var before = NamModel.FastTanh;
        try {
            NamModel.SetFastTanh(true);
            using var block = CreateBlock("wavenet_a1_standard", false);
            var output = Render(block, input, static _ => Block);

            Assert.True(Analysis.Esr(reference, output) < 1e-3, $"ESR {Analysis.Esr(reference, output):E2}");
        } finally {
            NamModel.SetFastTanh(before);
        }
    }

    [Fact]
    public void A_model_the_engine_loads_runs_with_fast_tanh_without_anyone_switching_it_on() {
        // Nothing here calls SetFastTanh, as nothing in the serve path does: the core's own default is exact tanh,
        // which costs a WaveNet more than twice the CPU (ADR-0003). The tests that need exact tanh put the switch
        // back, so this sees what a fresh engine sees.
        RequireNative();
        Assert.True(NamModel.FastTanh);
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var exact = WavFile.Read(NamAssets.Golden("wavenet_a1_standard.ref.wav")).Channels[0];
        using var block = (NamAmpBlock)BlockRegistry.CreateDefault().AddNativeBlocks().Create("nam");
        block.LoadAsset(
            Cathode.Dsp.Blocks.CaptureAmpBlock.ModelSlot,
            Path.Combine(NamAssets.ModelsDirectory, "wavenet_a1_standard.nam")
        );
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Normalize, 0);
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Calibrate, 0);
        block.Prepare(Spec);

        var output = Render(block, input, static _ => Block);

        // The approximation leaves a residual far above the -80 dB the exact render nulls to, and stays inaudible.
        var relativeDb = Analysis.NullResidualDb(output, exact) - Analysis.ToDb(Analysis.Rms(exact));
        Assert.True(relativeDb > -70, $"the render nulls against exact tanh to {relativeDb:0.0} dB: fast tanh is off");
        Assert.True(Analysis.Esr(exact, output) < 1e-3, $"ESR {Analysis.Esr(exact, output):E2}");
    }

    [Fact]
    public void Output_does_not_depend_on_block_size() {
        RequireNative();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var random = new Random(9);
        using var fixedSize = CreateBlock("wavenet_a1_standard", false);
        using var variableSize = CreateBlock("wavenet_a1_standard", false);

        var a = Render(fixedSize, input, static _ => Block);
        var b = Render(variableSize, input, _ => random.Next(1, Block + 1));

        Assert.True(Analysis.NullResidualDb(a, b) < -120);
    }

    [Fact]
    public void Process_does_not_allocate() {
        RequireNative();
        using var block = CreateBlock("A2", true);
        var left = Signals.Noise(Block, 1, 0.3f);
        var right = new float[Block];
        var context = new BlockContext(Spec.SampleRate, 120, 0);

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 500; i++) {
                    block.Process(new(left, right), context);
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_model_processed_before_reset_or_after_dispose_passes_the_input_through() {
        RequireNative();
        var input = Signals.Noise(Block, 4, 0.3f);
        var output = new float[Block];

        // Before the fix a zero block size never advanced the native loop and hung the calling thread.
        var model = NamModel.Load(Path.Combine(NamAssets.ModelsDirectory, "A2.nam"));
        model.Process(input, output);
        Assert.Equal(input, output);

        model.Dispose();
        Array.Clear(output);
        model.Process(input, output);
        Assert.Equal(input, output);
    }

    [Fact]
    public void A_model_loaded_into_a_prepared_block_is_reset_for_its_spec() {
        RequireNative();
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        using var loadedFirst = CreateBlock("wavenet_a1_standard", false);
        using var loadedLater = new NamAmpBlock();
        loadedLater.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Normalize, 0);
        loadedLater.Prepare(Spec);
        loadedLater.LoadAsset(
            Cathode.Dsp.Blocks.CaptureAmpBlock.ModelSlot,
            Path.Combine(NamAssets.ModelsDirectory, "wavenet_a1_standard.nam")
        );
        loadedLater.Reset();

        var expected = Render(loadedFirst, input, static _ => Block);
        var actual = Render(loadedLater, input, static _ => Block);

        Assert.True(Analysis.NullResidualDb(expected, actual) < -120);
    }

    [Fact]
    public void A_model_that_records_its_input_level_is_driven_at_input_1s_calibration() {
        RequireNative();
        var input = Signals.Noise(4_800, 3, 0.2f);
        using var calibrated = CreateBlock("lstm", false);
        calibrated.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Calibrate, 1);
        calibrated.Reset();
        using var raw = CreateBlock("lstm", false);

        // lstm.nam was made at +18.3 dBu; Input 1's default full scale is 1 V peak, -0.79 dBu: 19.09 dB less drive.
        Assert.Equal(18.3, calibrated.Model!.InputLevel!.Value, 4);
        var gain = (float)Decibels.ToGain(Cathode.Dsp.Blocks.CaptureAmpBlock.FullScaleDbuAt1V - 18.3);
        var expected = Render(raw, [..input.Select(x => x * gain)], static _ => Block);
        var actual = Render(calibrated, input, static _ => Block);

        for (var i = 0; i < input.Length; i++) {
            Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-5, $"sample {i}: {actual[i]} vs {expected[i]}");
        }
    }

    [Fact]
    public void Normalize_brings_the_model_to_the_target_loudness() {
        RequireNative();
        var input = Signals.Noise(4_800, 2, 0.2f);
        using var plain = CreateBlock("A2", false);
        using var normalized = CreateBlock("A2", true);

        var gain = Analysis.Rms(Render(normalized, input, static _ => Block))
            / Analysis.Rms(Render(plain, input, static _ => Block));

        var expected = Cathode.Dsp.Blocks.CaptureAmpBlock.TargetLoudnessDb - normalized.Model!.Loudness!.Value;
        Assert.Equal(expected, Analysis.ToDb(gain), 0.01);
    }

    [Fact]
    public void Graph_loads_the_model_as_an_asset_and_renders_it() {
        RequireNative();
        using var exact = new ExactTanh();
        var definition = new GraphDefinition {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() {
                    Id = "amp1",
                    Type = "nam",
                    Row = 0,
                    Column = 1,
                    // Against the core's own render, which knows no calibration.
                    Parameters = { ["normalize"] = 0, ["calibrate"] = 0 },
                    Assets = { ["model"] = "lstm.nam" }
                },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "amp1" }, new() { From = "amp1", To = "out" }]
        };
        var engine = new CathodeEngine(Spec);
        engine.Publish(
            new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks(), NamAssets.ModelsDirectory).Compile(
                definition,
                Spec,
                1
            )
        );
        var input = WavFile.Read(NamAssets.Golden("input.wav")).Channels[0];
        var reference = WavFile.Read(NamAssets.Golden("lstm.ref.wav")).Channels[0];

        var (left, right) = OfflineRenderer.Render(engine, input, input, Block);

        Assert.True(Analysis.NullResidualDb(left, reference) - Analysis.ToDb(Analysis.Rms(reference)) < -80);
        Assert.Equal(left, right);
    }

    [Fact]
    public void Unloadable_models_are_reported_as_graph_errors() {
        RequireNative();
        var definition = new GraphDefinition {
            Nodes = [
                new() {
                    Id = "missing",
                    Type = "nam",
                    Row = 0,
                    Column = 0,
                    Assets = { ["model"] = "does-not-exist.nam" }
                },
                new() {
                    Id = "garbage",
                    Type = "nam",
                    Row = 1,
                    Column = 0,
                    Assets = { ["model"] = NamAssets.Golden("README.md") }
                },
                new() {
                    Id = "noslot",
                    Type = "volpan",
                    Row = 2,
                    Column = 0,
                    Assets = { ["model"] = "lstm.nam" }
                }
            ]
        };

        var error = Assert.Throws<GraphValidationException>(() =>
            new GraphCompiler(BlockRegistry.CreateDefault().AddNativeBlocks(), NamAssets.ModelsDirectory).Compile(
                definition,
                Spec
            )
        );

        Assert.Contains(error.Errors, static e => e.Contains("'missing'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("'garbage'", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("no asset slot", StringComparison.Ordinal));
    }

    /// <summary>Exact tanh, as upstream's renders use, until disposed; then the switch is as it was.</summary>
    sealed class ExactTanh : IDisposable {
        readonly bool before = NamModel.FastTanh;

        public ExactTanh() {
            NamModel.SetFastTanh(false);
        }

        public void Dispose() => NamModel.SetFastTanh(before);
    }

    static void RequireNative() =>
        Assert.SkipUnless(
            NamModel.IsLibraryPresent && Directory.Exists(NamAssets.ModelsDirectory),
            "cth_nam not built (run native/build.sh)"
        );

    static NamAmpBlock CreateBlock(string model, bool normalize, bool lite = false) {
        var block = new NamAmpBlock();
        block.LoadAsset(
            Cathode.Dsp.Blocks.CaptureAmpBlock.ModelSlot,
            Path.Combine(NamAssets.ModelsDirectory, model + ".nam")
        );
        block.Params.SetTarget(
            Cathode.Dsp.Blocks.CaptureAmpBlock.Size,
            lite ? Cathode.Dsp.Blocks.CaptureAmpBlock.SizeLite : Cathode.Dsp.Blocks.CaptureAmpBlock.SizeFull
        );
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Normalize, normalize ? 1 : 0);
        block.Params.SetTarget(Cathode.Dsp.Blocks.CaptureAmpBlock.Calibrate, 0); // the references know no calibration
        block.Prepare(Spec);
        return block;
    }

    static float[] Render(IDspBlock block, float[] input, Func<int, int> blockSize) {
        var left = (float[])input.Clone();
        var right = new float[input.Length];
        var callback = 0;
        for (var offset = 0; offset < input.Length;) {
            var frames = Math.Min(blockSize(callback++), input.Length - offset);
            block.Process(
                new(left.AsSpan(offset, frames), right.AsSpan(offset, frames)),
                new(Spec.SampleRate, 120, offset)
            );
            offset += frames;
        }

        return left;
    }
}

/// <summary>
///     Locates the NAM golden files (committed) and the upstream example models (fetched by the native build).
/// </summary>
static class NamAssets {
    static readonly string EngineRoot = FindEngineRoot();

    public static string ModelsDirectory => Path.Combine(EngineRoot, "artifacts", "native", "nam-models");

    public static string Golden(string file) => Path.Combine(EngineRoot, "tests", "assets", "nam", file);

    static string FindEngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder (Cathode.slnx).");
    }
}
