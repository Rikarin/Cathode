using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Looping;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Engine.Telemetry;
using Cathode.Testing;

namespace Cathode.Engine.Tests;

/// <summary>
///     Engine support for the preset and scene features (docs/02-architecture.md §5.5, §5.9): spillover off, the grid
///     looper's hand-over, channel-scoped modifiers, block meters and the loudness meter.
/// </summary>
public class PresetFeatureEngineTests {
    static readonly ProcessSpec Spec = new(48_000, 64);
    static readonly GraphCompiler Compiler = new(BlockRegistry.CreateDefault());

    [Fact]
    public void With_spillover_off_a_preset_change_cuts_the_old_tail_after_the_swap_fade() {
        var engine = new CathodeEngine(Spec);
        engine.Rig.SetTarget(RigControls.Descriptor.IndexOf("spillover"), 0f);
        engine.Publish(Compiler.Compile(DelayGraph(), Spec, 1));
        var note = Signals.Sine(220, Spec.SampleRate, 9_600, 0.5f);
        OfflineRenderer.Render(engine, note, note, 64);

        engine.Publish(Compiler.Compile(Graph("volpan", "vol"), Spec, 2));
        var input = Signals.Concat(Signals.Sine(220, Spec.SampleRate, 4_800, 0.5f), new float[19_200]);
        var (left, _) = OfflineRenderer.Render(engine, input, input, 64);

        Assert.Equal(0, Analysis.Peak(left.AsSpan(6_000, 6_000)), 6); // no echo of the note played before the swap
        Assert.False(engine.IsSpilling);
        var maxStep = left.Zip(left.Skip(1), static (a, b) => Math.Abs(b - a)).Take(2_000).Max();
        Assert.True(maxStep < 0.1f, $"click at the swap: step {maxStep}");
    }

    [Fact]
    public void A_playing_grid_loop_carries_on_into_the_next_preset_when_it_has_a_looper_too() {
        var engine = new CathodeEngine(Spec);
        var first = Compiler.Compile(Graph("looper", "loop"), Spec, 1);
        engine.Publish(first);
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        var phrase = Signals.Sine(330, Spec.SampleRate, 24_000, 0.3f);
        OfflineRenderer.Render(engine, phrase, phrase, 64);
        engine.Post(EngineCommand.Looper(LooperAction.Record)); // second press: play
        OfflineRenderer.Render(engine, new float[4_800], new float[4_800], 64);
        var recorded = engine.Looper!.Length;
        Assert.Equal(LooperState.Playing, engine.Looper.State);

        var second = Compiler.Compile(Graph("looper", "loop"), Spec, 2);
        engine.Publish(second);
        var (left, _) = OfflineRenderer.Render(engine, new float[9_600], new float[9_600], 64);

        Assert.Equal(LooperState.Playing, second.Looper!.State);
        Assert.Equal(recorded, second.Looper.Length);
        Assert.Equal(LooperState.Empty, first.Looper!.State); // the old block got the new one's empty looper
        Assert.True(Analysis.Rms(left.AsSpan(4_800)) > 0.1, "the loop is heard after the change");
    }

    [Fact]
    public void A_loop_stops_when_the_next_preset_has_no_looper() {
        var engine = new CathodeEngine(Spec);
        var first = Compiler.Compile(Graph("looper", "loop"), Spec, 1);
        engine.Publish(first);
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, Signals.Step(9_600, 0.2f), Signals.Step(9_600, 0.2f), 64);
        engine.Post(EngineCommand.Looper(LooperAction.Record));
        OfflineRenderer.Render(engine, new float[640], new float[640], 64);

        engine.Publish(Compiler.Compile(Graph("volpan", "vol"), Spec, 2));
        OfflineRenderer.Render(engine, new float[640], new float[640], 64);

        Assert.NotEqual(LooperState.Playing, first.Looper!.State);
    }

    [Fact]
    public void A_disabled_modifier_leaves_its_parameter_until_it_is_turned_on() {
        var engine = new CathodeEngine(Spec);
        var definition = Graph("volpan", "vol");
        definition.Nodes[1].Parameters["level"] = 0f;
        definition.Modifiers.Add(
            new() {
                Block = "vol",
                Param = "level",
                Source = "manualA",
                Min = -6.0206f,
                Max = -6.0206f,
                Enabled = false
            }
        );
        var graph = Compiler.Compile(definition, Spec, 1);
        engine.Publish(graph);

        var (off, _) = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64);
        engine.Post(EngineCommand.SetModifier(1, 0, true));
        var (on, _) = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64);
        engine.Post(EngineCommand.SetModifier(1, 0, false, -12.0412f)); // off again, back to the channel's value
        var (back, _) = OfflineRenderer.Render(engine, Signals.Step(4_800), Signals.Step(4_800), 64);

        Assert.Equal(1f, off[^1], 1e-4f);
        Assert.Equal(0.5f, on[^1], 1e-3f);
        Assert.Equal(0.25f, back[^1], 1e-3f);
        Assert.False(graph.Modifiers[0].Enabled);
    }

    [Fact]
    public void Block_peaks_and_loudness_reach_the_telemetry_region() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var definition = Graph("volpan", "vol");
        definition.Nodes[1].Parameters["level"] = -6.0206f;
        var graph = Compiler.Compile(definition, Spec, 7);
        engine.Publish(graph);
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();

        var tone = Signals.Sine(997, Spec.SampleRate, 48_000, 0.8f);
        OfflineRenderer.Render(engine, tone, tone, 64);
        monitor.Poll();

        Assert.Equal(7, monitor.BlockGeneration);
        Assert.Equal(graph.MeterCount, monitor.BlockMeterCount);
        Assert.Equal(0.4, monitor.BlockMeters[graph.MeterIndex("vol")], 0.01);
        Assert.Equal(0.8, monitor.BlockMeters[graph.MeterIndex("in")], 0.01);
        // A 0.4 peak sine on both channels: 20 log10(0.4) - 3 + 3 = -7.96 LUFS.
        Assert.Equal(-7.96, monitor.LoudnessMomentary, 0.15);
        Assert.Equal(engine.Loudness.ShortTerm, monitor.LoudnessShortTerm);
    }

    [Fact]
    public void A_blocks_own_meters_reach_the_telemetry_region_beside_its_peak() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var definition = Graph("cab", "cab");
        var cab = CabBlock.Info.Params;
        definition.Nodes[1].Parameters[cab[CabBlock.SlotParam(0, CabBlock.Source)].Id] = CabBlock.SourceFlat;
        definition.Nodes[1].Parameters[cab[CabBlock.Preamp].Id] = MicPreampTypes.Fet1;
        definition.Nodes[1].Parameters[cab[CabBlock.MotorDrive].Id] = 100;
        definition.Nodes[1].Parameters[cab[CabBlock.MotorTime].Id] = 0.05f;
        var graph = Compiler.Compile(definition, Spec, 3);
        engine.Publish(graph);
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();

        var tone = Signals.Sine(997, Spec.SampleRate, 48_000, (float)MicPreampTypes.ClipOnset(MicPreampTypes.Fet1));
        OfflineRenderer.Render(engine, tone, tone, 64);
        monitor.Poll();

        var slot = graph.SlotOf("cab");
        Assert.Equal(["peak", "preampIn", "motorReduction"], graph.MeterIds(slot).ToArray());
        Assert.Equal(graph.MeterOffset(slot) + 1, graph.MeterIndex("cab", "preampIn"));
        Assert.Equal(graph.MeterCount, monitor.BlockMeterCount);
        var own = new float[2];
        ((IBlockMeters)graph.Block(slot)).ReadMeters(own);
        Assert.InRange(monitor.BlockMeters[graph.MeterIndex("cab")], 0.1f, 2f); // the standard peak meter
        Assert.InRange(own[0], -24f, 0f); // driven near the preamp's onset of clipping, less Motor Drive's reduction
        Assert.Equal(own[0], monitor.BlockMeters[graph.MeterIndex("cab", "preampIn")], 0.5);
        Assert.True(own[1] > 1f, $"motor reduction {own[1]}");
        Assert.Equal(own[1], monitor.BlockMeters[graph.MeterIndex("cab", "motorReduction")], 0.5);
    }

    [Fact]
    public void Each_modifiers_value_reaches_the_telemetry_region_for_its_generation() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var definition = Graph("volpan", "vol");
        definition.Modifiers.AddRange(
            [
                new() {
                    Block = "vol",
                    Param = "level",
                    Source = "manualA",
                    Min = -6f,
                    Max = -6f
                },
                new() {
                    Block = "vol",
                    Param = ModNames.EngageParam,
                    Source = "manualB",
                    Min = 1f,
                    Max = 1f
                },
                new() {
                    Block = "vol",
                    Param = "balance",
                    Source = "manualC",
                    Min = 25f,
                    Max = 25f,
                    Enabled = false
                }
            ]
        );
        var graph = Compiler.Compile(definition, Spec, 5);
        engine.Publish(graph);
        var monitor = new TelemetryMonitor(region);
        Assert.Equal(-1, monitor.ModifierGeneration);

        OfflineRenderer.Render(engine, new float[640], new float[640], 64);
        monitor.Poll();

        Assert.Equal(5, monitor.ModifierGeneration);
        Assert.Equal(3, monitor.ModifierValues.Length);
        Assert.Equal(-6f, monitor.ModifierValues[0], 1e-4f);
        Assert.True(float.IsNaN(monitor.ModifierValues[1]), "an engage modifier drives no value");
        Assert.True(float.IsNaN(monitor.ModifierValues[2]), "a disabled modifier drives no value");

        engine.Post(EngineCommand.SetModifier(5, 2, true));
        engine.Post(EngineCommand.SetModifier(5, 0, false));
        OfflineRenderer.Render(engine, new float[640], new float[640], 64);
        monitor.Poll();

        Assert.True(float.IsNaN(monitor.ModifierValues[0]), "turned off");
        Assert.Equal(25f, monitor.ModifierValues[2], 1e-4f);

        engine.Publish(Compiler.Compile(Graph("volpan", "vol"), Spec, 6));
        OfflineRenderer.Render(engine, new float[640], new float[640], 64);
        monitor.Poll();

        Assert.Equal(6, monitor.ModifierGeneration);
        Assert.Equal(0, monitor.ModifierValues.Length);
    }

    /// <summary>Input -> 300 ms delay (50 % mix, feedback 40 %) -> output.</summary>
    static GraphDefinition DelayGraph() {
        var graph = Graph("delay", "delay");
        graph.Nodes[1].Parameters = new() { ["time"] = 300, ["feedback"] = 40, ["mix"] = 50 };
        return graph;
    }

    static GraphDefinition Graph(string type, string id) =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = id, Type = type, Row = 0, Column = 1 },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = id }, new() { From = id, To = "out" }]
        };
}
