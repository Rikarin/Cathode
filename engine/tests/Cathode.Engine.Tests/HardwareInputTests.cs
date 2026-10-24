using Cathode.Audio;
using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Routing;
using Cathode.Engine.Graph;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>
///     Multi-channel input (docs/08-blocks-parity.md §2): the device's sources reach the Input cell's Source, the
///     Multiplexer, sidechains and the Vocoder's and Pitch detector's analysis sources. Rendered offline with the sources
///     a multi-input interface would deliver.
/// </summary>
public class HardwareInputTests {
    const int Frames = 9_600;
    static readonly ProcessSpec Spec = new(48_000, 64);

    [Fact]
    public void By_default_the_input_cell_reads_input_1_and_ignores_the_other_sources() {
        var output = Render(Chain(), Sources(0.5f, 0.25f));

        Assert.All(output[^100..], static s => Assert.Equal(0.5f, s, 1e-6f));
    }

    [Theory]
    [InlineData(1f, 0.25f)] // Input 2
    [InlineData(3f, 0f)] // Input 4: mapped to nothing, so silent
    [InlineData(5f, 0.125f)] // USB 7/8
    public void The_input_cell_takes_the_source_it_selects(float source, float expected) {
        var sources = Sources(0.5f, 0.25f);
        sources[5] = (Signals.Step(Frames, 0.125f), Signals.Step(Frames, 0.125f));

        var output = Render(Chain(("source", source)), sources);

        Assert.All(output[^100..], s => Assert.Equal(expected, s, 1e-6f));
    }

    [Fact]
    public void A_source_keeps_its_left_and_right_apart() {
        var sources = Sources(0.5f, 0f);
        sources[1] = (Signals.Step(Frames, 0.25f), Signals.Step(Frames, -0.25f));
        var engine = Engine(Chain(("source", 1f)));

        var (left, right) = OfflineRenderer.Render(engine, sources, 64);

        Assert.Equal(0.25f, left[^1], 1e-6f);
        Assert.Equal(-0.25f, right[^1], 1e-6f);
    }

    [Fact]
    public void Changing_the_input_source_crossfades() {
        var engine = Engine(Chain(), out var graph);
        var sources = Sources(0.5f, 0.25f);
        var first = OfflineRenderer.Render(engine, sources, 64).Left;
        graph.SetParameter(graph.SlotOf("in"), InputBlock.Source, 1f);
        var second = OfflineRenderer.Render(engine, sources, 64).Left;

        Assert.Equal(0.5f, first[^1], 1e-6f);
        Assert.Equal(0.25f, second[^1], 1e-6f);
        var steepest = second.Zip(second.Skip(1), static (a, b) => MathF.Abs(b - a)).Max();
        Assert.True(steepest < 0.25f / 400, $"jumped by {steepest}"); // a 10 ms (480-sample) fade
    }

    [Fact]
    public void The_input_source_does_not_depend_on_the_callback_size() {
        var sources = Sources(0.5f, 0.25f);
        sources[1] = (Signals.Sine(330, Spec.SampleRate, Frames), Signals.Sine(330, Spec.SampleRate, Frames));
        var small = OfflineRenderer.Render(Engine(Chain(("source", 1f))), sources, 16).Left;
        var large = OfflineRenderer.Render(Engine(Chain(("source", 1f))), sources, 64).Left;

        Assert.Equal(large, small);
    }

    [Theory]
    [InlineData(0f, 0.5f)] // Row 1 (the grid input through the volpan)
    [InlineData(8f, 0.25f)] // Input 2
    public void The_multiplexer_can_select_a_hardware_input(float input, float expected) {
        var definition = Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("a", "volpan", 0, 1),
                Node("mux", "mux", 0, 2, ("input", input)),
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "a"),
            ("a", "mux"),
            ("mux", "out")
        );

        var output = Render(definition, Sources(0.5f, 0.25f));

        Assert.All(output[^100..], s => Assert.Equal(expected, s, 1e-5f));
    }

    [Theory]
    [InlineData(0f, false)] // Block: its own quiet input stays under the threshold
    [InlineData(9f, true)] // Input 2: a loud source on the second input ducks it
    [InlineData(10f, false)] // Input 3: unmapped, so the detector hears silence
    public void A_compressor_sidechain_can_listen_to_a_hardware_input(float sidechain, bool ducked) {
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
                Node("out", NodeTypes.Output, 0, 3)
            ],
            ("in", "quiet"),
            ("quiet", "comp"),
            ("comp", "out")
        );
        var sources = new (float[] Left, float[] Right)?[2];
        var sine = Signals.Sine(500, Spec.SampleRate, Frames);
        sources[0] = (sine, sine);
        sources[1] = (Signals.Step(Frames, 0.9f), Signals.Step(Frames, 0.9f));

        var output = Render(definition, sources);

        var peak = Analysis.Peak(output.AsSpan(4_800));
        if (ducked) {
            Assert.True(peak < 0.002f, $"peak {peak}");
        } else {
            Assert.Equal(0.01f, peak, 1e-3f);
        }
    }

    [Theory]
    [InlineData(0f, true)] // Block: its loud input holds it open
    [InlineData(9f, false)] // Input 2, silent: the gate shuts the loud guitar
    public void A_gate_sidechain_can_listen_to_a_hardware_input(float sidechain, bool open) {
        var definition = Chain("gate", ("threshold", -40f), ("scSource", sidechain));
        var sources = new (float[] Left, float[] Right)?[2];
        var sine = Signals.Sine(500, Spec.SampleRate, Frames, 0.5f);
        sources[0] = (sine, sine);
        sources[1] = (new float[Frames], new float[Frames]);

        var peak = Analysis.Peak(Render(definition, sources).AsSpan(Frames / 2));

        Assert.True(open ? peak > 0.4 : peak < 0.01, $"peak {peak}");
    }

    [Theory]
    [InlineData(8f, true)] // Input 2 carries the voice
    [InlineData(9f, false)] // Input 3 is silent
    public void The_vocoder_can_analyse_a_hardware_input(float analysisSource, bool heard) {
        var definition = Chain("vocoder", ("analysisSource", analysisSource), ("mix", 100f));
        var sources = new (float[] Left, float[] Right)?[3];
        var carrier = Signals.Sine(110, Spec.SampleRate, Frames, 0.5f);
        var voice = Signals.Noise(Frames, 7, 0.5f);
        sources[0] = (carrier, carrier);
        sources[1] = (voice, voice);

        var rms = Analysis.Rms(Render(definition, sources).AsSpan(Frames / 2));

        Assert.True(heard ? rms > 1e-3 : rms < 1e-5, $"rms {rms}");
    }

    [Fact]
    public void The_pitch_detector_can_track_a_hardware_input() {
        // Ring-mod-free check: with the detector on a silent Input 3 the block still runs and stays bounded, and on
        // Input 2 (the same signal as the grid input) it matches tracking the block input.
        var sine = Signals.Sine(220, Spec.SampleRate, Frames, 0.5f);
        (float[] Left, float[] Right)?[] sources = [(sine, sine), (sine, sine)];
        var own = Render(Chain("pitch", ("detectorSource", 0f)), sources);
        var input2 = Render(Chain("pitch", ("detectorSource", 8f)), sources);
        var silent = Render(Chain("pitch", ("detectorSource", 9f)), sources);

        Assert.True(Analysis.AllFinite(silent));
        Assert.True(Analysis.NullResidualDb(own, input2) < -60, "Input 2 should track like the block input");
    }

    [Fact]
    public void A_replaced_graph_stops_hearing_the_hardware_inputs() {
        var inputs = new HardwareInputs();
        var sources = new float[HardwareInputs.Count * 2 * 64];
        sources.AsSpan(2 * 64, 128).Fill(1f); // Input 2
        inputs.BeginCallback(new float[64], new float[64], sources, 0b11, 64);
        var port = new InputPort(inputs, 48_000);
        var left = new float[64];
        var right = new float[64];

        port.Read(1, left, right);
        Assert.Equal(1f, left[^1]);
        port.FadeOut();
        var fading = 0;
        for (; fading < 100; fading++) {
            port.Read(1, left, right);
            port.Advance(64);
            if (left[^1] == 0f) {
                break;
            }
        }

        Assert.InRange(fading * 64, 900, 1_000); // the 20 ms swap fade (960 samples)
        port.Read(1, left, right);
        Assert.All(left, static s => Assert.Equal(0f, s));
        inputs.Dispose();
    }

    [Fact]
    public void Input_maps_parse_one_channel_list_per_source() {
        var map = InputMap.Parse("1;3,4;;5");

        Assert.Equal([[0], [2, 3], [], [4]], map.Sources);
        Assert.Equal(0b1011, map.Mask);
        Assert.Equal(5, map.Channels);
        Assert.True(map.HasExtraSources);
        Assert.Equal([0], InputMap.Parse(null).Sources.Single());
        Assert.False(InputMap.Parse("").HasExtraSources);
        Assert.Equal([0], InputMap.Parse(";3").Sources[0]); // an empty Input 1 stays on channel 1
    }

    [Fact]
    public void Hardware_input_choices_follow_the_rows_on_every_block() {
        Assert.Equal("Input 1", MultiplexerBlock.Info.Params[0].Options![MultiplexerBlock.FirstInput]);
        Assert.Equal("Input 1", SidechainSource.Options[SidechainSource.FirstInput]);
        Assert.Equal(HardwareInputs.Names, InputBlock.Info.Params[InputBlock.Source].Options);
        Assert.Equal("USB 7/8", PitchBlock.Info.Params[PitchBlock.DetectorSource].Options![^1]);
        Assert.Equal("USB 7/8", VocoderBlock.Info.Params[VocoderBlock.AnalysisSource].Options![^1]);
    }

    /// <summary>Input 1 and Input 2 as constant steps, the other sources unmapped.</summary>
    static (float[] Left, float[] Right)?[] Sources(float input1, float input2) {
        var sources = new (float[] Left, float[] Right)?[HardwareInputs.Count];
        sources[0] = (Signals.Step(Frames, input1), Signals.Step(Frames, input1));
        sources[1] = (Signals.Step(Frames, input2), Signals.Step(Frames, input2));
        return sources;
    }

    static CathodeEngine Engine(GraphDefinition definition) => Engine(definition, out _);

    static CathodeEngine Engine(GraphDefinition definition, out CompiledGraph graph) {
        var engine = new CathodeEngine(Spec);
        graph = new GraphCompiler(BlockRegistry.CreateDefault()) { HardwareInputs = engine.Inputs }
            .Compile(definition, Spec, 1);
        engine.Publish(graph);
        return engine;
    }

    static float[] Render(GraphDefinition definition, IReadOnlyList<(float[] Left, float[] Right)?> sources) =>
        OfflineRenderer.Render(Engine(definition), sources, Spec.MaxBlockSize).Left;

    /// <summary>in → out, with parameters on the Input cell.</summary>
    static GraphDefinition Chain(params (string Id, float Value)[] input) =>
        Graph(
            [Node("in", NodeTypes.Input, 0, 0, input), Node("out", NodeTypes.Output, 0, 1)],
            ("in", "out")
        );

    /// <summary>in → block → out.</summary>
    static GraphDefinition Chain(string type, params (string Id, float Value)[] parameters) =>
        Graph(
            [
                Node("in", NodeTypes.Input, 0, 0),
                Node("block", type, 0, 1, parameters),
                Node("out", NodeTypes.Output, 0, 2)
            ],
            ("in", "block"),
            ("block", "out")
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
}
