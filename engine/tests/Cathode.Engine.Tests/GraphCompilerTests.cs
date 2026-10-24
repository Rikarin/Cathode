using System.Text.Json;
using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Testing;

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
        cables.AddRange(previous.Select(p => (p, "out")));

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
