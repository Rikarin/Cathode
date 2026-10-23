using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Graph;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

public class CathodeEngineTests {
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(BlockRegistry.CreateDefault());

    [Fact]
    public void Outputs_silence_until_a_graph_is_published() {
        var engine = new CathodeEngine(Spec);

        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(128), Signals.Step(128), 64);

        Assert.All(left, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Parameter_targets_apply_without_the_command_queue() {
        var engine = new CathodeEngine(Spec);
        var graph = Compiler.Compile(VolumeGraph(0), Spec, 1);
        engine.Publish(graph);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        graph.SetParameter(graph.SlotOf("vol"), VolumePanBlock.Level, -6.0206f);
        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64);

        Assert.Equal(0.5f, left[^1], 1e-4f);
    }

    [Fact]
    public void Bypass_crossfades_to_dry_without_clicks() {
        var engine = new CathodeEngine(Spec);
        var graph = Compiler.Compile(VolumeGraph(Decibels.SilenceDb), Spec, 1);
        engine.Publish(graph);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        Assert.True(engine.Post(EngineCommand.SetBypass(1, graph.SlotOf("vol"), true)));
        var input = Signals.Step(4_800);
        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        var fadeSamples = (int)(CompiledGraph.BypassFadeMs * 0.001 * Spec.SampleRate);
        var maxStep = left.Zip(left.Skip(1), static (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep <= 1.5f / fadeSamples, $"max step {maxStep}");
        Assert.Equal(1f, left[fadeSamples + 1]);
        Assert.Equal(input[^200..], left[^200..]);
    }

    [Fact]
    public void Commands_for_a_replaced_graph_are_ignored() {
        var engine = new CathodeEngine(Spec);
        var first = Compiler.Compile(VolumeGraph(0), Spec, 1);
        var second = Compiler.Compile(VolumeGraph(Decibels.SilenceDb), Spec, 2);
        engine.Publish(first);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        engine.Publish(second);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        engine.Post(EngineCommand.SetBypass(1, 0, true)); // stale generation
        var (left, _) = OfflineRenderer.Render(engine, Signals.Step(1_024), Signals.Step(1_024), 64);

        Assert.False(second.IsBypassed(0));
        Assert.All(left, static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Swapped_out_graphs_are_handed_back_to_the_control_side() {
        var engine = new CathodeEngine(Spec);
        var first = Compiler.Compile(VolumeGraph(0), Spec, 1);
        engine.Publish(first);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        Assert.Null(engine.CollectRetired());

        engine.Publish(Compiler.Compile(VolumeGraph(0), Spec, 2));
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);

        Assert.Same(first, engine.CollectRetired());
        Assert.Equal(2, engine.Current!.Generation);
    }

    [Fact]
    public void Callbacks_larger_than_the_max_block_are_split() {
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(VolumeGraph(0), Spec, 1));
        var input = Signals.Noise(1_000, 2);

        var (left, _) = OfflineRenderer.Render(engine, input, input, 1_000);

        Assert.Equal(input, left);
        Assert.Equal(1_000, engine.SampleTime);
    }

    [Fact]
    public void Steady_state_processing_does_not_allocate() {
        var engine = new CathodeEngine(Spec);
        var graph = Compiler.Compile(GraphTemplates.Basic(), Spec, 1);
        engine.Publish(graph);
        var input = Signals.Noise(Spec.MaxBlockSize, 1);
        var outLeft = new float[Spec.MaxBlockSize];
        var outRight = new float[Spec.MaxBlockSize];
        var slot = graph.SlotOf("volpan1");
        var bypass = false;

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 5_000; i++) {
                    if (i % 500 == 0) {
                        bypass = !bypass;
                        engine.Post(EngineCommand.SetBypass(1, slot, bypass));
                        graph.SetParameter(slot, VolumePanBlock.Level, bypass ? -12 : 0);
                    }

                    engine.Process(new(input, input, outLeft, outRight));
                }
            }
        );

        Assert.Equal(0, allocated);
        Assert.Equal(10_000, engine.Stats.Count);
    }

    static GraphDefinition VolumeGraph(float levelDb) =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() {
                    Id = "vol",
                    Type = "volpan",
                    Row = 0,
                    Column = 1,
                    Parameters = { ["level"] = levelDb }
                },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "vol" }, new() { From = "vol", To = "out" }]
        };
}
