using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Engine.Telemetry;
using Cathode.Ipc;
using Cathode.Testing;
using System.Text.Json;

namespace Cathode.Engine.Tests;

public class GraphCompilerTests {
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(TestRegistry.Create());

    [Fact]
    public void Series_chain_processes_in_place_with_a_single_buffer() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0), Node("vol", "volpan", 0, 1, ("level", -6.0206f)),
                Node("shunt", NodeTypes.Shunt, 0, 2), Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "vol"),
            ("vol", "shunt"),
            ("shunt", "out")
        );

        using var graph = Compiler.Compile(definition, Spec);
        var output = Run(definition, Signals.Step(256));

        Assert.Equal(1, graph.BufferCount);
        Assert.All(output, static s => Assert.Equal(0.5f, s, 1e-4f));
    }

    [Fact]
    public void Parallel_rows_are_summed_where_they_join() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("a", "volpan", 0, 1, ("level", -6.0206f)),
                Node("b", "volpan", 1, 1, ("level", -12.0412f)),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "a"),
            ("in", "b"),
            ("a", "out"),
            ("b", "out")
        );

        var output = Run(definition, Signals.Step(256));

        Assert.All(output, static s => Assert.Equal(0.75f, s, 1e-4f));
    }

    [Fact]
    public void The_output_mixes_each_row_at_its_own_level() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0, ("level", -6.0206f)),
                Node("a", "volpan", 0, 1),
                Node("b", "volpan", 1, 1),
                Node("out", NodeTypes.Output, 0, 2, ("level2", -6.0206f), ("level", 6.0206f))
            ],
            ("in", "a"),
            ("in", "b"),
            ("a", "out"),
            ("b", "out")
        );

        var output = Run(definition, Signals.Step(4_800));

        // Input at half; row 1 at unity and row 2 at half sum to 0.75, then the main level doubles it.
        Assert.Equal(1.5f, output[^1], 1e-3f);
    }

    [Fact]
    public void An_output_on_one_row_scales_it_in_place() {
        var definition = Graph(
            [Node("in", NodeTypes.Input, 2, 0), Node("out", NodeTypes.Output, 2, 1, ("level3", -6.0206f))],
            ("in", "out")
        );

        using var graph = Compiler.Compile(definition, Spec);
        var output = Run(definition, Signals.Step(4_800));

        Assert.Equal(1, graph.BufferCount);
        Assert.Contains(graph.Ops, static op => op.Kind == GraphOpKind.ScaleRow && op.Row == 2);
        Assert.Equal(0.5f, output[^1], 1e-3f);
    }

    [Theory]
    [InlineData(0f, false)] // Block: its own quiet input stays under the threshold
    [InlineData(3f, true)] // Row 2: the loud row ducks it
    public void A_compressor_hears_the_row_its_sidechain_selects(float sidechain, bool ducked) {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("quiet", "volpan", 0, 1, ("level", -40f)),
                Node(
                    "comp",
                    "compressor",
                    0,
                    2,
                    ("threshold", -30f),
                    ("ratio", 20f),
                    ("knee", 0f),
                    ("scSource", sidechain)
                ),
                Node("loud", "volpan", 1, 1),
                Node("mute", "volpan", 1, 2, ("level", -80f)),
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "quiet"),
            ("quiet", "comp"),
            ("comp", "out"),
            ("in", "loud"),
            ("loud", "mute"),
            ("mute", "out")
        );

        var output = Run(definition, Signals.Sine(500, Spec.SampleRate, 9_600));

        var peak = Analysis.Peak(output.AsSpan(4_800));
        if (ducked) {
            Assert.True(peak < 0.002f, $"peak {peak}");
        } else {
            Assert.Equal(0.01f, peak, 1e-3f);
        }
    }

    [Theory]
    [InlineData("mux", SideInputs.Cables | SideInputs.Hardware)]
    [InlineData("mixer", SideInputs.Cables)]
    [InlineData("output", SideInputs.Cables)]
    [InlineData("output3", SideInputs.Cables)]
    [InlineData("pitch", SideInputs.Cables | SideInputs.Hardware)]
    [InlineData("vocoder", SideInputs.Cables | SideInputs.Hardware)]
    [InlineData("compressor", SideInputs.Taps | SideInputs.Hardware)]
    [InlineData("gate", SideInputs.Taps | SideInputs.Hardware)]
    [InlineData("input", SideInputs.Hardware)]
    [InlineData("volpan", SideInputs.None)]
    public void Blocks_declare_the_signals_they_take_from_elsewhere(string type, SideInputs expected) {
        using var block = TestRegistry.Create().Create(type);

        Assert.Equal(expected, block is ISideInputBlock side ? side.SideInputs : SideInputs.None);
    }

    [Fact]
    public void Side_inputs_reach_their_blocks_through_one_op_as_cables_or_taps() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("a", "volpan", 0, 1),
                Node("b", "volpan", 1, 1),
                Node("mux", "mux", 0, 2),
                Node("comp", "compressor", 0, 3),
                Node("out", NodeTypes.Output, 0, 4)
            ],
            ("in", "a"),
            ("in", "b"),
            ("a", "mux"),
            ("b", "mux"),
            ("mux", "comp"),
            ("comp", "out")
        );

        using var graph = Compiler.Compile(definition, Spec);
        var mux = graph.SlotOf("mux");
        var comp = graph.SlotOf("comp");

        Assert.Equal(SideInputs.Cables | SideInputs.Hardware, ((ISideInputBlock)graph.Block(mux)).SideInputs);
        Assert.Equal(SideInputs.Taps | SideInputs.Hardware, ((ISideInputBlock)graph.Block(comp)).SideInputs);
        // The multiplexer gets its two cabled rows, each added into its own buffer.
        var cables = graph.Ops.Where(op => op.Kind == GraphOpKind.SideInput && op.Index == mux).ToArray();
        Assert.Equal([0, 1], cables.Select(static op => op.Row).Order());
        Assert.All(cables, static op => Assert.True(op.Buffer >= 0 && op.Source >= 0));
        // The compressor is offered taps only: the grid input, then the last block on each row to its left.
        var taps = graph.Ops.Where(op => op.Kind == GraphOpKind.SideInput && op.Index == comp).ToArray();
        Assert.Equal([-1, 0, 1], taps.Select(static op => op.Row));
        Assert.All(taps, static op => Assert.Equal(-1, op.Buffer));
        Assert.Equal(-1, taps[0].Source);
    }

    [Fact]
    public void Buffers_are_reused_down_a_long_parallel_grid() {
        var nodes = new List<NodeDefinition> { Node("in", NodeTypes.Input, 0, 0) };
        var cables = new List<(string, string)>();
        var previous = new[] { "in" };
        for (var column = 1; column < 13; column++) {
            var current = new[] { $"a{column}", $"b{column}" };
            nodes.Add(Node(current[0], "volpan", 0, column));
            nodes.Add(Node(current[1], "volpan", 1, column));
            foreach (var p in previous) {
                cables.AddRange(current.Select(c => (p, c)));
            }

            previous = current;
        }

        nodes.Add(Node("out", NodeTypes.Output, 0, 13));
        cables.AddRange(previous.Select(static p => (p, "out")));

        using var graph = Compiler.Compile(Graph([..nodes], [..cables]), Spec);

        Assert.True(graph.BufferCount <= 4, $"used {graph.BufferCount} buffers");
    }

    [Fact]
    public void Parallel_paths_are_latency_compensated() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("slow", "test-latency", 0, 1),
                Node("fast", "volpan", 1, 1),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "slow"),
            ("in", "fast"),
            ("slow", "out"),
            ("fast", "out")
        );

        using var graph = Compiler.Compile(definition, Spec);
        var output = Run(definition, Signals.Impulse(256));

        Assert.Equal(10, graph.LatencySamples);
        Assert.Equal(10, Analysis.PeakIndex(output));
        Assert.Equal(2f, output[10], 1e-6f); // both paths arrive together instead of as two echoes
        Assert.Equal(2.0, Analysis.Rms(output) * Math.Sqrt(output.Length), 1e-5);
    }

    [Theory]
    [InlineData(BypassModes.Thru)]
    [InlineData(BypassModes.MuteFxIn)]
    [InlineData(BypassModes.MuteFxOut)]
    public void A_bypassed_latent_block_keeps_its_latency_so_parallel_paths_stay_aligned(string mode) {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("slow", "test-latency", 0, 1),
                Node("fast", "volpan", 1, 1),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "slow"),
            ("in", "fast"),
            ("slow", "out"),
            ("fast", "out")
        );
        var slow = definition.Nodes.Single(static n => n.Id == "slow");
        slow.Bypassed = true;
        slow.BypassMode = mode;

        // Before the fix a THRU-bypassed latent block passed its input on at once while the parallel path stayed
        // delayed by its latency: two impulses 10 samples apart instead of one of twice the height.
        var output = Run(definition, Signals.Impulse(256));

        Assert.Equal(10, Analysis.PeakIndex(output));
        Assert.Equal(2f, output[10], 1e-6f);
        Assert.Equal(2.0, Analysis.Rms(output) * Math.Sqrt(output.Length), 1e-5);
    }

    [Fact]
    public void Bypassing_a_latent_block_while_running_keeps_the_alignment() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("slow", "test-latency", 0, 1),
                Node("fast", "volpan", 1, 1),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "slow"),
            ("in", "fast"),
            ("slow", "out"),
            ("fast", "out")
        );
        var engine = new CathodeEngine(Spec);
        var graph = Compiler.Compile(definition, Spec, 1);
        engine.Publish(graph);
        var sine = Signals.Sine(1_000, Spec.SampleRate, 4_800, 0.25f);
        var engaged = OfflineRenderer.Render(engine, sine, sine, Spec.MaxBlockSize).Left;

        Assert.True(engine.Post(EngineCommand.SetBypass(1, graph.SlotOf("slow"), true)));
        var bypassed = OfflineRenderer.Render(engine, sine, sine, Spec.MaxBlockSize).Left;

        // The two paths sum in phase whether the block runs or not: the same level after the fade, no comb filter,
        // and no step at the fade (both sides of the crossfade are aligned).
        var tail = 2_400;
        Assert.Equal(Analysis.Rms(engaged.AsSpan(tail)), Analysis.Rms(bypassed.AsSpan(tail)), 1e-4);
        var maxStep = bypassed.Zip(bypassed.Skip(1), static (a, b) => Math.Abs(b - a)).Take(1_500).Max();
        Assert.True(maxStep < 0.1f, $"step {maxStep}");
    }

    [Fact]
    public void Nodes_that_cannot_reach_an_output_are_pruned_but_keep_their_slot() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0), Node("used", "volpan", 0, 1), Node("dangling", "filter", 1, 1),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "used"),
            ("in", "dangling"),
            ("used", "out")
        );

        using var graph = Compiler.Compile(definition, Spec);

        Assert.Equal(1, graph.Ops.Count(op => op.Kind == GraphOpKind.Process && op.Index == graph.SlotOf("used")));
        Assert.True(graph.SlotOf("dangling") >= 0);
        Assert.DoesNotContain(graph.Ops, op => op.Kind == GraphOpKind.Process && op.Index == graph.SlotOf("dangling"));
    }

    [Fact]
    public void A_graph_whose_meters_or_modifiers_overflow_the_telemetry_is_refused() {
        var count = TelemetryLayout.Modifiers + 1;
        var definition = new GraphDefinition { Rows = count / 14 + 1, Columns = 14 };
        for (var i = 0; i < count; i++) {
            definition.Nodes.Add(Node($"v{i}", "volpan", i / 14, i % 14));
            definition.Modifiers.Add(new() { Block = $"v{i}", Param = "level", Source = "manualA" });
        }

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec));

        Assert.Contains(error.Errors, e => e.Contains($"{count} block meters", StringComparison.Ordinal));
        Assert.Contains(error.Errors, e => e.Contains($"{count} modifiers", StringComparison.Ordinal));
    }

    [Fact]
    public void A_full_grid_of_any_block_fits_the_telemetry() {
        // The server accepts at most a 6 x 14 grid: every block's peak and own meters must then fit.
        var registry = TestRegistry.Create();
        var most = 0;
        foreach (var descriptor in registry.Descriptors) {
            using var block = registry.Create(descriptor.TypeId);
            most = Math.Max(most, block is IBlockMeters own ? own.MeterIds.Count : 0);
        }

        Assert.True(6 * 14 * (1 + most) <= TelemetryLayout.BlockMeters, $"{most} own meters per block");
    }

    [Fact]
    public void Invalid_grids_report_every_problem() {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("x", "volpan", 0, 0),
                Node("y", "no-such-block", 1, 3),
                Node("z", "volpan", 9, 1, ("nope", 1)),
                Node("out", NodeTypes.Output, 0, 5)
            ],
            ("in", "out"),
            ("in", "ghost")
        );

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec));

        Assert.Contains(error.Errors, static e => e.Contains("shares cell", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("unknown type", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("outside", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("adjacent columns", StringComparison.Ordinal));
        Assert.Contains(error.Errors, static e => e.Contains("unknown node", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_parameter_ids_are_rejected() {
        var definition = Graph([Node("in", NodeTypes.Input, 0, 0), Node("v", "volpan", 0, 1, ("nope", 1))]);

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec));

        Assert.Contains(error.Errors, static e => e.Contains("no parameter 'nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void Graph_json_round_trips() {
        var json = JsonSerializer.Serialize(
            GraphTemplates.Basic(),
            GraphJsonContext.Default.GraphDefinition
        );
        var restored = JsonSerializer.Deserialize(json, GraphJsonContext.Default.GraphDefinition)!;

        Assert.Equal(
            json,
            JsonSerializer.Serialize(restored, GraphJsonContext.Default.GraphDefinition)
        );
        Assert.Contains("\"params\"", json, StringComparison.Ordinal);
    }

    static NodeDefinition Node(
        string id,
        string type,
        int row,
        int column,
        params (string Id, float Value)[] parameters
    ) {
        var node = new NodeDefinition { Id = id, Type = type, Row = row, Column = column };
        foreach (var (paramId, value) in parameters) {
            node.Parameters[paramId] = value;
        }

        return node;
    }

    static GraphDefinition Graph(NodeDefinition[] nodes, params (string From, string To)[] cables) =>
        new() {
            Nodes = [..nodes], Cables = [..cables.Select(static c => new CableDefinition { From = c.From, To = c.To })]
        };

    static float[] Run(GraphDefinition definition, float[] input) {
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(definition, Spec, 1));
        return OfflineRenderer.Render(engine, input, input, Spec.MaxBlockSize).Left;
    }
}
