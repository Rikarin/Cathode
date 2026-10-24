using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Graph;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>Multiplexer, Feedback Send/Return and FX Loop: the routing blocks the graph compiler wires up itself.</summary>
public class RoutingTests {
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(TestRegistry.Create());

    [Theory]
    [InlineData(0f, 0.5f)]
    [InlineData(1f, 0.25f)]
    [InlineData(6f, 0.75f)] // All
    public void Multiplexer_passes_only_the_selected_row(float input, float expected) {
        var output = Run(Mux(input), Signals.Step(4_800));

        Assert.All(output[^100..], s => Assert.Equal(expected, s, 1e-4f));
    }

    [Fact]
    public void Multiplexer_crossfades_when_the_selection_changes() {
        var definition = Mux(0f);
        var engine = new CathodeEngine(Spec);
        using var graph = Compiler.Compile(definition, Spec, 1);
        engine.Publish(graph);
        var first = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64).Left;
        graph.SetParameter(graph.SlotOf("mux"), 0, 1f);
        var second = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64).Left;

        Assert.Equal(0.5f, first[^1], 1e-4f);
        Assert.Equal(0.25f, second[^1], 1e-4f);
        var steepest = second.Zip(second.Skip(1), static (a, b) => MathF.Abs(b - a)).Max();
        Assert.True(steepest < 0.25f / 400, $"jumped by {steepest}"); // a 10 ms (480-sample) fade
    }

    [Fact]
    public void Mixer_sums_the_rows_with_their_own_gain_and_balance() {
        // Row 1 (0.5) at 50 % gain, centred; row 2 (0.25) at full gain, balanced hard right.
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("a", "volpan", 0, 1, ("level", -6.0206f)),
                Node("b", "volpan", 1, 1, ("level", -12.0412f)),
                Node("mixer", "mixer", 0, 2, ("gain1", 50f), ("balance2", 100f)),
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "a"),
            ("in", "b"),
            ("a", "mixer"),
            ("b", "mixer"),
            ("mixer", "out")
        );
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(definition, Spec, 1));

        var (left, right) = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64);

        Assert.Equal(0.25f, left[^1], 1e-4f);
        Assert.Equal(0.5f, right[^1], 1e-4f);
    }

    [Fact]
    public void Compiler_links_an_amp_to_the_next_cab_downstream() {
        // Row 0: amp, volume, cab. Row 1: a second amp beside the cab, fed from the volume, with no cab after it.
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("amp", "circuitamp", 0, 1, ("speaker", CircuitAmpBlock.SpeakerAuto)),
                Node("other", "circuitamp", 1, 3),
                Node("vol", "volpan", 0, 2),
                Node("cab", "cab", 0, 3),
                Node("out", NodeTypes.Output, 0, 4)
            ],
            ("in", "amp"),
            ("amp", "vol"),
            ("vol", "other"),
            ("vol", "cab"),
            ("cab", "out"),
            ("other", "out")
        );
        using var graph = Compiler.Compile(definition, Spec, 1);

        var amp = (CircuitAmpBlock)graph.Block(graph.SlotOf("amp"));
        var other = (CircuitAmpBlock)graph.Block(graph.SlotOf("other"));
        Assert.Same(graph.Block(graph.SlotOf("cab")), amp.FollowedCab);
        Assert.Null(other.FollowedCab);
    }

    [Fact]
    public void Feedback_return_repeats_the_send_one_block_later() {
        var output = Run(Feedback(-6.0206f), Signals.Impulse(512));

        Assert.Equal(1f, output[0], 1e-5f);
        Assert.Equal(0.5f, output[64], 2e-3f); // through the +18 dBFS soft limit, almost linear here
        Assert.Equal(0.25f, output[128], 2e-3f);
        Assert.Equal(0f, output[32], 1e-6f);
    }

    [Fact]
    public void Feedback_loop_does_not_depend_on_the_callback_size() {
        var definition = Feedback(-3f);
        var input = Signals.Noise(9_600, 3);

        var fixedSize = Run(definition, input);
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(definition, Spec, 1));
        var varied = OfflineRenderer.Render(engine, input, input, 17).Left;

        Assert.True(Analysis.NullResidualDb(fixedSize, varied) < -140);
    }

    [Fact]
    public void Feedback_loop_with_gain_above_one_saturates_instead_of_running_away() {
        var output = Run(Feedback(6f), Signals.Impulse(48_000));

        Assert.True(Analysis.AllFinite(output));
        Assert.True(Analysis.Peak(output) < 16f);
    }

    [Fact]
    public void A_preset_has_at_most_one_feedback_send_per_pair() {
        var definition = Feedback(0f);
        definition.Nodes.Add(Node("send1", "fbsend", 2, 2)); // pair 1, like "send"

        var error = Assert.Throws<GraphValidationException>(() => Compiler.Compile(definition, Spec));

        Assert.Contains(
            error.Errors,
            static e => e.Contains("at most one Feedback Send per pair", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Two_feedback_pairs_are_matched_by_the_number_their_ids_end_in() {
        // Pair 1 at -6 dB on rows 1-2, pair 2 at -12 dB on rows 3-4: each return repeats only its own send.
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("fbreturn1", "fbreturn", 0, 1),
                Node("fbsend1", "fbsend", 1, 2, ("level", -6.0206f)),
                Node("shunt1", NodeTypes.Shunt, 0, 2),
                Node("fbreturn2", "fbreturn", 2, 1),
                Node("fbsend2", "fbsend", 3, 2, ("level", -12.0412f)),
                Node("shunt2", NodeTypes.Shunt, 2, 2),
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "fbreturn1"),
            ("fbreturn1", "fbsend1"),
            ("fbreturn1", "shunt1"),
            ("shunt1", "out"),
            ("in", "fbreturn2"),
            ("fbreturn2", "fbsend2"),
            ("fbreturn2", "shunt2"),
            ("shunt2", "out")
        );

        var output = Run(definition, Signals.Impulse(512));

        // Both rows pass the impulse, then each repeats at its own level once per 64-sample block.
        Assert.Equal(2f, output[0], 1e-5f);
        Assert.Equal(0.5f + 0.25f, output[64], 2e-3f);
        Assert.Equal(0.25f + 0.0625f, output[128], 2e-3f);
        Assert.Equal(1, GraphCompiler.FeedbackPair("fbsend"));
        Assert.Equal(2, GraphCompiler.FeedbackPair("fbreturn2"));
    }

    [Fact]
    public void Fx_loop_latency_is_compensated_and_its_return_comes_from_the_device() {
        const int callback = 128;
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("loop", "fxloop", 0, 1, ("latency", callback * 1000f / 48_000)),
                Node("dry", "volpan", 1, 1),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "loop"),
            ("in", "dry"),
            ("loop", "out"),
            ("dry", "out")
        );
        var engine = new CathodeEngine(Spec);
        using var graph = new GraphCompiler(TestRegistry.Create()) { HardwareLoop = engine.HardwareLoop }
            .Compile(definition, Spec, 1);
        engine.Publish(graph);

        // A loopback cable: what one callback sends comes back on the next one, a round trip of one callback.
        var input = Signals.Impulse(callback * 8);
        var output = new float[input.Length];
        var returned = new float[callback];
        var send = new float[callback];
        var right = new float[callback];
        for (var offset = 0; offset < input.Length; offset += callback) {
            var slice = input.AsSpan(offset, callback);
            engine.Process(new(slice, slice, output.AsSpan(offset, callback), right, returned, returned, send, send));
            send.CopyTo(returned);
        }

        Assert.Equal(callback, graph.LatencySamples);
        Assert.Equal(2f, output[callback], 1e-5f); // loop and the compensated dry path arrive together
        Assert.Equal(0f, output[0], 1e-6f);
    }

    static GraphDefinition Mux(float input) =>
        Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("a", "volpan", 0, 1, ("level", -6.0206f)),
                Node("b", "volpan", 1, 1, ("level", -12.0412f)),
                Node("mux", "mux", 0, 2, ("input", input)),
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "a"),
            ("in", "b"),
            ("a", "mux"),
            ("b", "mux"),
            ("mux", "out")
        );

    /// <summary>in → return → (out, send): the send feeds the return, so each repeat is <paramref name="levelDb" /> down.</summary>
    static GraphDefinition Feedback(float levelDb) =>
        Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("return", "fbreturn", 0, 1),
                Node("send", "fbsend", 1, 2, ("level", levelDb)),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "return"),
            ("return", "send"),
            ("return", "out")
        );

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
