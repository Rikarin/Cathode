using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>
///     Audio I/O (docs/08-blocks-parity.md §2), offline with an <see cref="OutputBus" /> standing in for a multi-channel
///     device: Outputs 2-4, Copy Output 1 to Output 2, output modes, per-output EQ and Boost/Pad, Input 1 Gain, the DI
///     out, output peaks, the test tone, input calibration and latency alignment.
/// </summary>
public class AudioIoTests {
    const int Length = 9_600;
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(BlockRegistry.CreateDefault());

    [Fact]
    public void Outputs_2_to_4_play_on_their_mapped_pairs_and_unmapped_ones_stay_silent() {
        using var engine = Engine(TwoOutputs(-6.0206f));
        using var bus = new OutputBus([0, 2, 4, -1]);
        var input = Signals.Noise(Length, 1, 0.5f);

        var channels = OfflineRenderer.RenderOutputs(engine, input, input, 64, bus);

        Assert.Equal(input, channels[0]);
        Assert.Equal(input.Select(static s => s * 0.5f), channels[2], new Near(1e-5f));
        Assert.Equal(channels[2], channels[3]);
        Assert.All(channels[4], static s => Assert.Equal(0f, s)); // Output 3 is mapped but not on the grid
        Assert.All(channels[6], static s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Without_a_bus_or_a_mapping_only_output_1_is_rendered() {
        using var engine = Engine(TwoOutputs(0));
        using var bus = new OutputBus();
        var input = Signals.Noise(Length, 2, 0.5f);

        var channels = OfflineRenderer.RenderOutputs(engine, input, input, 64, bus);
        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.All(channels[2], static s => Assert.Equal(0f, s));
        Assert.Equal(input, left);
    }

    [Fact]
    public void Copy_output_1_to_2_plays_output_1_unless_the_grid_has_an_output_2() {
        using var copied = Engine(Chain("volpan", ("level", -6.0206f)));
        Set(copied, "copyOut2", 1);
        using var own = Engine(TwoOutputs(-12.0412f));
        Set(own, "copyOut2", 1);
        using var bus = new OutputBus([0, 2]);
        var input = Signals.Noise(Length, 3, 0.5f);

        var a = OfflineRenderer.RenderOutputs(copied, input, input, 64, bus);
        var b = OfflineRenderer.RenderOutputs(own, input, input, 64, bus);

        Assert.Equal(a[0], a[2]);
        Assert.Equal(input.Select(static s => s * 0.25f), b[2], new Near(1e-5f));
    }

    [Theory]
    [InlineData(RigControls.Stereo)]
    [InlineData(RigControls.SumLeftRight)]
    [InlineData(RigControls.CopyLeftToRight)]
    public void Output_modes_sum_or_copy_the_channels(int mode) {
        using var engine = Engine(Chain("volpan"));
        Set(engine, "out1Mode", mode);
        var left = Signals.Noise(Length, 4, 0.5f);
        var right = Signals.Noise(Length, 5, 0.5f);

        var (outLeft, outRight) = OfflineRenderer.Render(engine, left, right, 64);

        var (expectLeft, expectRight) = mode switch {
            RigControls.SumLeftRight => (Sum(left, right), Sum(left, right)),
            RigControls.CopyLeftToRight => (left, left),
            _ => (left, right)
        };
        Assert.Equal(expectLeft, outLeft, new Near(1e-6f));
        Assert.Equal(expectRight, outRight, new Near(1e-6f));
    }

    [Fact]
    public void Each_output_has_its_own_global_eq_and_boost_pad() {
        using var engine = Engine(TwoOutputs(0));
        engine.OutputEq(1).Params.SetTarget(engine.OutputEq(1).Descriptor.IndexOf("g1k"), 6f);
        Set(engine, "out1Level", -6.0206f);
        Set(engine, "out2Level", 6.0206f);
        using var bus = new OutputBus([0, 2]);
        var sine = Signals.Sine(1_000, 48_000, Length, 0.1f);

        var channels = OfflineRenderer.RenderOutputs(engine, sine, sine, 64, bus);

        var tail = 4_800;
        Assert.Equal(-6.02, Analysis.ToDb(Analysis.Rms(channels[0].AsSpan(tail)) / Analysis.Rms(sine.AsSpan(tail))), 1);
        // +6 dB of trim and about +6 dB of EQ at the band's centre
        Assert.InRange(Analysis.ToDb(Analysis.Rms(channels[2].AsSpan(tail)) / Analysis.Rms(sine.AsSpan(tail))), 11, 13);
    }

    [Fact]
    public void Input_1_gain_feeds_the_grid_and_the_di_out_takes_the_raw_input() {
        using var engine = Engine(Chain("volpan"));
        Set(engine, "in1Gain", 6.0206f);
        using var bus = new OutputBus(null, [4, 5]);
        var input = Signals.Noise(Length, 6, 0.25f);

        var channels = OfflineRenderer.RenderOutputs(engine, input, input, 64, bus);

        Assert.Equal(
            input.Skip(64).Select(static s => s * 2f),
            channels[0].Skip(64),
            new Near(1e-5f)
        ); // after the ramp in
        Assert.Equal(input, channels[^2]);
        Assert.Equal(input, channels[^1]);
    }

    [Fact]
    public void Output_peaks_follow_each_output_after_its_trim() {
        using var engine = Engine(TwoOutputs(0));
        Set(engine, "out2Level", -6.0206f);
        using var bus = new OutputBus([0, 2]);
        var peaks = new float[4];
        engine.ReadOutputPeaks(peaks);

        OfflineRenderer.RenderOutputs(engine, Signals.Step(Length, 0.9f), Signals.Step(Length, 0.9f), 64, bus);
        engine.ReadOutputPeaks(peaks); // the first callbacks ramp the trim in
        OfflineRenderer.RenderOutputs(engine, Signals.Step(Length, 0.9f), Signals.Step(Length, 0.9f), 64, bus);
        engine.ReadOutputPeaks(peaks);

        Assert.Equal(0.9f, peaks[0], 1e-4f);
        Assert.Equal(0.45f, peaks[1], 1e-3f);
        Assert.Equal(0f, peaks[2]);
    }

    [Fact]
    public void Multi_output_processing_does_not_allocate() {
        using var engine = Engine(TwoOutputs(-3));
        Set(engine, "copyOut2", 1);
        Set(engine, "out2Mode", RigControls.SumLeftRight);
        Set(engine, "out3Level", 3);
        using var bus = new OutputBus([0, 2, 4, 6], [8]);
        var input = Signals.Noise(Spec.MaxBlockSize, 7);
        var left = new float[Spec.MaxBlockSize];
        var right = new float[Spec.MaxBlockSize];
        var peaks = new float[4];

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 5_000; i++) {
                    engine.Process(new(input, input, left, right) { Outputs = bus });
                    if (i % 100 == 0) {
                        engine.ReadOutputPeaks(peaks);
                    }
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_test_tone_replaces_a_blocks_output_and_fades_back_out() {
        using var engine = new CathodeEngine(Spec);
        var compiler = new GraphCompiler(BlockRegistry.CreateDefault()) { TestTone = engine.TestTone };
        var compiled = compiler.Compile(Chain("volpan"), Spec, 3);
        engine.Publish(compiled);
        engine.TestTone.Start(3, compiled.SlotOf("block"), 1_000, 0.5f);

        var (playing, _) = OfflineRenderer.Render(engine, new float[Length], new float[Length], 64);
        engine.TestTone.Stop();
        var input = Signals.Noise(Length, 8, 0.1f);
        var (after, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(0.5 / Math.Sqrt(2), Analysis.Rms(playing.AsSpan(4_800)), 3);
        Assert.Equal(input.AsSpan(4_800).ToArray(), after.AsSpan(4_800).ToArray());
    }

    [Fact]
    public void Calibrating_the_input_keeps_the_amps_drive_when_the_interface_gain_changes() {
        // An interface turned down 6 dB, with its calibration updated to 2 V at full scale, drives a drive pedal and an
        // amp exactly as before: both see the same volts.
        var definition = Chain("drive", ("drive", 6f));
        definition.Nodes.Insert(2, new() { Id = "amp", Type = "circuitamp", Row = 0, Column = 2 });
        definition.Nodes[^1].Column = 3;
        definition.Cables = [
            new() { From = "in", To = "block" }, new() { From = "block", To = "amp" },
            new() { From = "amp", To = "out" }
        ];
        using var reference = Engine(definition);
        using var calibrated = Engine(definition);
        Set(calibrated, "in1Cal", 6.0206f);
        var input = Signals.Sine(220, 48_000, Length, 0.2f);
        var halved = input.Select(static s => s * 0.5f).ToArray();

        var (expected, _) = OfflineRenderer.Render(reference, input, input, 64);
        var (actual, _) = OfflineRenderer.Render(calibrated, halved, halved, 64);

        Assert.True(Analysis.NullResidualDb(expected, actual) < -60, "the calibrated amp sounds different");
    }

    [Fact]
    public void Outputs_are_aligned_to_the_slowest_and_the_latency_path_names_its_blocks() {
        var definition = TwoOutputs(0);
        definition.Oversampling = "linear";
        definition.Nodes.Single(static n => n.Id == "block").Type = "drive";
        definition.Nodes.Single(static n => n.Id == "block").Parameters = new() { ["mix"] = 0 };
        var graph = Compiler.Compile(definition, Spec, 1);
        using var engine = new CathodeEngine(Spec);
        engine.Publish(graph);
        using var bus = new OutputBus([0, 2]);
        var impulse = Signals.Impulse(Length, 1f, 100);

        var channels = OfflineRenderer.RenderOutputs(engine, impulse, impulse, 64, bus);

        Assert.True(graph.LatencySamples > 0);
        Assert.Equal(["in", "block", "out"], graph.LatencyPath.Select(static s => s.Block));
        Assert.Equal(graph.LatencySamples, graph.LatencyPath[1].Samples);
        Assert.Equal(100 + graph.LatencySamples, Analysis.PeakIndex(channels[0]));
        Assert.Equal(100 + graph.LatencySamples, Analysis.PeakIndex(channels[2]));
    }

    static CathodeEngine Engine(GraphDefinition definition) {
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(definition, Spec, 1));
        return engine;
    }

    static void Set(CathodeEngine engine, string id, float value) {
        var index = RigControls.Descriptor.IndexOf(id);
        engine.Rig.SetTarget(index, value);
        engine.Rig.SnapToTargets();
    }

    static float[] Sum(float[] a, float[] b) => [..a.Zip(b, static (x, y) => 0.5f * (x + y))];

    /// <summary>Input → one block → Output 1.</summary>
    static GraphDefinition Chain(string type, params (string Id, float Value)[] parameters) =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() {
                    Id = "block",
                    Type = type,
                    Row = 0,
                    Column = 1,
                    Parameters = parameters.ToDictionary(static p => p.Id, static p => p.Value)
                },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "block" }, new() { From = "block", To = "out" }]
        };

    /// <summary>Input → Volume → Output 1 on row 1, and Input → Volume (<paramref name="secondDb" />) → Output 2 on row 2.</summary>
    static GraphDefinition TwoOutputs(float secondDb) =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "block", Type = "volpan", Row = 0, Column = 1 },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 },
                new() {
                    Id = "vol2",
                    Type = "volpan",
                    Row = 1,
                    Column = 1,
                    Parameters = { ["level"] = secondDb }
                },
                new() { Id = "out2", Type = NodeTypes.Output2, Row = 1, Column = 2 }
            ],
            Cables = [
                new() { From = "in", To = "block" },
                new() { From = "block", To = "out" },
                new() { From = "in", To = "vol2" },
                new() { From = "vol2", To = "out2" }
            ]
        };

    sealed class Near(float tolerance) : IEqualityComparer<float> {
        public bool Equals(float x, float y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(float obj) => 0;
    }
}
