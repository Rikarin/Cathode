using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
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
    public void Engaging_a_bypassed_block_tells_it_once() {
        var engine = new CathodeEngine(Spec);
        var graph = new GraphCompiler(TestRegistry.Create()).Compile(
            new() {
                Nodes = [
                    new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                    new() { Id = "fx", Type = "test-engage", Row = 0, Column = 1, Bypassed = true },
                    new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
                ],
                Cables = [new() { From = "in", To = "fx" }, new() { From = "fx", To = "out" }]
            },
            Spec,
            1
        );
        engine.Publish(graph);
        var block = (EngageCountingBlock)graph.Block(graph.SlotOf("fx"));

        engine.Post(EngineCommand.SetBypass(1, 0, true)); // already bypassed: no engage
        engine.Post(EngineCommand.SetBypass(1, 0, false));
        engine.Post(EngineCommand.SetBypass(1, 0, false)); // already engaged: no second call
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        Assert.Equal(1, block.Engaged);

        engine.Post(EngineCommand.SetBypass(1, 0, true));
        engine.Post(EngineCommand.SetBypass(1, 0, false));
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        Assert.Equal(2, block.Engaged);
    }

    [Fact]
    public void The_rig_effects_mix_reaches_blocks_with_global_mix_on() {
        var engine = new CathodeEngine(Spec);
        var graph = Compiler.Compile(
            new() {
                Nodes = [
                    new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                    new() {
                        Id = "rev",
                        Type = "reverb",
                        Row = 0,
                        Column = 1,
                        Parameters = { ["mix"] = 0f, ["globalMix"] = 1f }
                    },
                    new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
                ],
                Cables = [new() { From = "in", To = "rev" }, new() { From = "rev", To = "out" }]
            },
            Spec,
            1
        );
        engine.Publish(graph);
        var input = Signals.Sine(220, Spec.SampleRate, 9_600, 0.25f);

        var (dry, _) = OfflineRenderer.Render(engine, input, input, 64);
        engine.Rig.SetTarget(RigControls.Descriptor.IndexOf("effectsMix"), 40f);
        var (wet, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(input, dry);
        Assert.True(Analysis.Rms(wet.Zip(input, static (a, b) => a - b).ToArray()) > 1e-3, "no reverb heard");
    }

    [Fact]
    public void Midi_notes_are_held_until_released() {
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(VolumeGraph(0), Spec, 1));

        engine.Post(EngineCommand.Note(57, true));
        engine.Post(EngineCommand.Note(60, true));
        engine.Post(EngineCommand.Note(64, true));
        engine.Post(EngineCommand.Note(60, false));
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        Assert.Equal(HeldNotes.Of(57, 64), engine.HeldNotes);

        engine.Post(EngineCommand.AllNotesOff());
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        Assert.True(engine.HeldNotes.IsEmpty);
    }

    [Fact]
    public void Commands_for_a_replaced_graph_are_ignored() {
        var engine = new CathodeEngine(Spec);
        var first = Compiler.Compile(VolumeGraph(0), Spec, 1);
        var second = Compiler.Compile(VolumeGraph(Decibels.SilenceDb), Spec, 2);
        engine.Publish(first);
        OfflineRenderer.Render(engine, new float[64], new float[64], 64);
        engine.Publish(second);
        OfflineRenderer.Render(engine, new float[48_000 * 5], new float[48_000 * 5], 512); // let the first graph retire

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
        Assert.True(engine.IsSpilling); // the replaced graph rings out first
        OfflineRenderer.Render(engine, new float[48_000 * 5], new float[48_000 * 5], 512); // quiet for over 4.1 s

        Assert.False(engine.IsSpilling);
        Assert.Same(first, engine.CollectRetired());
        Assert.Equal(2, engine.Current!.Generation);
    }

    [Fact]
    public void A_preset_change_lets_the_old_delay_tail_ring_out_without_a_click() {
        var engine = new CathodeEngine(Spec);
        engine.Publish(Compiler.Compile(DelayGraph(), Spec, 1));
        var note = Signals.Sine(220, Spec.SampleRate, 9_600, 0.5f);
        OfflineRenderer.Render(engine, note, note, 64);

        // Swap to a plain volume block while the note keeps playing, then stop playing.
        engine.Publish(Compiler.Compile(VolumeGraph(0), Spec, 2));
        var input = Signals.Concat(Signals.Sine(220, Spec.SampleRate, 4_800, 0.5f), new float[19_200]);
        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        // The note played before the swap echoes 300 ms (14,400 samples) later, 4,800-14,400 samples after the swap. The
        // dry note stops at 4,800, so anything heard from 6,000 on is the old graph's tail: the spillover.
        Assert.True(Analysis.Peak(left.AsSpan(6_000, 6_000)) > 0.05, "delay tail after the swap");
        var maxStep = left.Zip(left.Skip(1), static (a, b) => Math.Abs(b - a)).Take(2_000).Max();
        Assert.True(maxStep < 0.1f, $"click at the swap: step {maxStep}");
    }

    [Theory]
    [InlineData("thru", 1.0, 0.0)]
    [InlineData("mute", 0.0, 0.0)]
    [InlineData("muteFxIn", 1.0, 0.05)]
    [InlineData("muteIn", 0.0, 0.05)]
    [InlineData("muteOut", 0.0, 0.0)]
    [InlineData("muteFxOut", 1.0, 0.0)]
    public void Bypass_modes_decide_what_passes_and_whether_tails_continue(string mode, double dry, double tail) {
        var engine = new CathodeEngine(Spec);
        var definition = DelayGraph();
        definition.Nodes.Single(n => n.Id == "delay").BypassMode = mode;
        var graph = Compiler.Compile(definition, Spec, 1);
        engine.Publish(graph);
        OfflineRenderer.Render(engine, Signals.Sine(220, Spec.SampleRate, 4_800, 0.5f), new float[4_800], 64);

        Assert.True(engine.Post(EngineCommand.SetBypass(1, graph.SlotOf("delay"), true)));
        var step = Signals.Step(19_200, 0.25f);
        var (left, _) = OfflineRenderer.Render(engine, step, step, 64);

        // Well after the fade, the dry step is either passed or not ...
        Assert.InRange(left[^1] / 0.25f, dry - 0.02, dry + 0.02);
        // ... and the echoes of the note played before the bypass either continue or stopped with it.
        var echo = left.AsSpan(9_000, 4_000).ToArray().Select(s => Math.Abs(s - (float)(dry * 0.25))).Max();
        Assert.True(tail > 0 ? echo > tail : echo < 0.01, $"{mode}: echo {echo:0.000}");
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

    /// <summary>Input -> 300 ms delay (50 % mix, feedback 40 %) -> output.</summary>
    static GraphDefinition DelayGraph() =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() {
                    Id = "delay",
                    Type = "delay",
                    Row = 0,
                    Column = 1,
                    Parameters = { ["time"] = 300, ["feedback"] = 40, ["mix"] = 50 }
                },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "delay" }, new() { From = "delay", To = "out" }]
        };

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
