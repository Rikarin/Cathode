using Cathode.Dsp;
using Cathode.Dsp.Modulation;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Engine.Telemetry;
using Cathode.Ipc;

namespace Cathode.Engine.Tests;

/// <summary>
///     The rig's controllers in the shared-memory telemetry: the sequencer's stage for the editor's step marker, and
///     the LFOs' and ADSRs' values.
/// </summary>
public class ControllerTelemetryTests {
    const int Block = 64;
    static readonly ProcessSpec Spec = new(48_000, Block);
    static readonly GraphCompiler Compiler = new(BlockRegistry.CreateDefault());

    [Fact]
    public void The_sequencer_stage_steps_at_its_rate() {
        // 8 Hz is eight stages a second: a stage every 6000 frames, 93.75 callbacks of 64.
        var engine = Engine(("seqStages", 4f), ("seqRate", 8f));

        var runs = Runs(Stages(engine, 700));

        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3], runs.Select(static r => r.Stage));
        Assert.All(runs.SkipLast(1), static r => Assert.InRange(r.Callbacks, 93, 94));
    }

    [Fact]
    public void A_synced_sequencer_steps_once_per_division_of_the_tempo() {
        var eighth = TempoDivisions.Names.IndexOf("1/8");

        // An eighth at 240 BPM is 0.125 s (6000 frames); at 120 BPM it is twice that.
        var fast = Runs(Stages(Engine(("seqStages", 3f), ("seqTempo", eighth), ("tempo", 240f)), 700));
        var slow = Runs(Stages(Engine(("seqStages", 3f), ("seqTempo", eighth), ("tempo", 120f)), 700));

        Assert.Equal([0, 1, 2, 0, 1, 2, 0, 1], fast.Select(static r => r.Stage));
        Assert.All(fast.SkipLast(1), static r => Assert.InRange(r.Callbacks, 93, 94));
        Assert.Equal([0, 1, 2, 0], slow.Select(static r => r.Stage));
        Assert.All(slow.SkipLast(1), static r => Assert.InRange(r.Callbacks, 187, 188));
    }

    [Fact]
    public void The_controllers_run_only_while_a_graph_is_processed_and_the_audio_keeps_coming() {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        var engine = new CathodeEngine(Spec) { Telemetry = region };
        var monitor = new TelemetryMonitor(region);
        monitor.Poll();
        Assert.False(monitor.Controllers.Running); // nothing processed yet

        Run(engine, 4, 0f);
        monitor.Poll();
        Assert.False(monitor.Controllers.Running); // no graph: the controllers stand still

        engine.Publish(Compiler.Compile(Graph(), Spec, 1));
        Run(engine, 4, 0f);
        monitor.Poll();
        Assert.True(monitor.Controllers.Running);
        Assert.Equal(8, monitor.Controllers.SequencerStages); // the default

        // A large buffer calls back less often than the poller polls: a few empty polls are not a stop.
        monitor.Poll();
        monitor.Poll();
        monitor.Poll();
        Assert.True(monitor.Controllers.Running);
        monitor.Poll();
        Assert.False(monitor.Controllers.Running);
    }

    [Fact]
    public void The_lfos_and_adsrs_are_published_with_their_current_values() {
        var engine = Engine(("lfo1Rate", 5f), ("lfo2Rate", 0.05f));
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        engine.Telemetry = region;
        var monitor = new TelemetryMonitor(region);
        var lfo1 = new List<float>();
        for (var i = 0; i < 100; i++) {
            Run(engine, 10, 0.5f); // -6 dB, above the ADSRs' -40 dB threshold
            monitor.Poll();
            lfo1.Add(monitor.Controllers.Lfo1);
        }

        Assert.All(lfo1, static v => Assert.InRange(v, 0f, 1f));
        Assert.True(lfo1.Max() - lfo1.Min() > 0.9f, "LFO 1 sweeps its whole range");
        Assert.InRange(monitor.Controllers.Lfo2, 0.65f, 0.75f); // a sine a fifteenth of the way through its cycle
        Assert.Equal(0.5f, monitor.Controllers.Adsr1, 1e-3f); // attacked and decayed to its sustain
        Assert.Equal(0.5f, monitor.Controllers.Adsr2, 1e-3f);
    }

    static CathodeEngine Engine(params (string Id, float Value)[] settings) {
        var engine = new CathodeEngine(Spec);
        foreach (var (id, value) in settings) {
            engine.Rig.SetTarget(RigControls.Descriptor.IndexOf(id), value);
        }

        engine.Rig.SnapToTargets(); // no ramp from the defaults: the timing is exact from the first frame
        engine.Publish(Compiler.Compile(Graph(), Spec, 1));
        return engine;
    }

    /// <summary>The stage the telemetry reports after each of <paramref name="callbacks" /> callbacks.</summary>
    static List<int> Stages(CathodeEngine engine, int callbacks) {
        using var region = TelemetryRegion.CreateInMemory(Spec.SampleRate);
        engine.Telemetry = region;
        var monitor = new TelemetryMonitor(region);
        var stages = new List<int>(callbacks);
        for (var i = 0; i < callbacks; i++) {
            Run(engine, 1, 0f);
            monitor.Poll();
            Assert.True(monitor.Controllers.Running);
            stages.Add(monitor.Controllers.SequencerStage);
        }

        engine.Telemetry = null;
        return stages;
    }

    /// <summary>Consecutive equal stages as (stage, how many callbacks it lasted).</summary>
    static List<(int Stage, int Callbacks)> Runs(List<int> stages) {
        var runs = new List<(int Stage, int Callbacks)>();
        foreach (var stage in stages) {
            if (runs.Count > 0 && runs[^1].Stage == stage) {
                runs[^1] = (stage, runs[^1].Callbacks + 1);
            } else {
                runs.Add((stage, 1));
            }
        }

        return runs;
    }

    static void Run(CathodeEngine engine, int callbacks, float amplitude) {
        var input = Enumerable.Repeat(amplitude, Block).ToArray();
        var left = new float[Block];
        var right = new float[Block];
        for (var i = 0; i < callbacks; i++) {
            engine.Process(new(input, input, left, right));
        }
    }

    static GraphDefinition Graph() =>
        new() {
            Nodes = [
                new() { Id = "in", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "vol", Type = "volpan", Row = 0, Column = 1 },
                new() { Id = "out", Type = NodeTypes.Output, Row = 0, Column = 2 }
            ],
            Cables = [new() { From = "in", To = "vol" }, new() { From = "vol", To = "out" }]
        };
}
