using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.EngineHost;
using Cathode.Ipc;
using Cathode.Testing;

namespace Cathode.Server.Tests;

/// <summary>
///     The host must not collect during a session (docs/adr/0001-process-model.md), so what it does at a steady rate
///     (knob drags, scene bypasses, telemetry pushes) must not allocate, and a preset load must stay small.
/// </summary>
/// <remarks>
///     Allocation is counted per thread, so the measured code runs synchronously on the test's thread: the framing's
///     awaits on a <see cref="MemoryStream" /> complete at once, and <see cref="Run(ValueTask)" /> fails the test if
///     one ever did not (a continuation on another thread would hide its allocations, or count someone else's). Each
///     steady-rate figure is the least of up to three measurements (<see cref="AllocationProbe" />): the runtime now
///     and then does one-off work on the measuring thread (a tier-1 rejit, on-stack replacement, a cold type load),
///     which shows up in one measurement, while an allocation in the code under test shows up in all three.
/// </remarks>
public sealed class HostAllocationTests : IDisposable {
    static readonly string EngineRoot = FindEngineRoot();

    readonly EngineHostService service = new(ProcessSpec.Default, "test");
    readonly IpcFrameWriter writer = new();
    readonly IpcFrameReader reader = new();
    readonly MemoryStream output = new(1 << 20);

    public void Dispose() {
        writer.Dispose();
        service.Dispose();
    }

    [Fact]
    public void Parameter_and_bypass_commands_and_telemetry_allocate_nothing() {
        var token = TestContext.Current.CancellationToken;
        RoundTrip(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = Rig() }, 1);
        var knob = new IpcMessage {
            Type = IpcTypes.SetParams, Generation = 1, Params = [new("drive1", "drive", 3.5f)]
        };
        var scene = new IpcMessage {
            Type = IpcTypes.SetParams,
            Generation = 1,
            Params = [..Enumerable.Range(0, 8).Select(static i => new ParamValue("drive1", "drive", i))]
        };
        var bypass = new IpcMessage { Type = IpcTypes.SetBypass, Generation = 1, Block = "delay1", Bypassed = true };
        var telemetry = new HostTelemetry();
        var push = new IpcMessage { Type = IpcTypes.Telemetry, Telemetry = telemetry };

        void Pushes() {
            for (var i = 0; i < 1_000; i++) {
                output.Position = 0;
                service.Telemetry(telemetry);
                Run(writer.WriteAsync(output, push, token));
            }
        }

        Assert.Equal(0, RoundTrip(knob, 1_000));
        Assert.Equal(0, RoundTrip(scene, 1_000));
        // No audio thread drains the 1,024-command queue: a bypass's 100 warm-ups and up to three measurements fit.
        Assert.Equal(0, RoundTrip(bypass, 300));
        Assert.Equal(0, AllocationProbe.Measure(Pushes));
    }

    [Fact]
    public void Multi_output_callbacks_and_their_telemetry_allocate_nothing() {
        // Outputs 1-4 and the DI out mapped, a grid with an Output 2, Copy Output 1 to Output 2 and the output modes.
        var token = TestContext.Current.CancellationToken;
        var graph = Rig();
        graph.Nodes.Add(new() { Id = "out2", Type = NodeTypes.Output2, Row = 1, Column = 7 });
        graph.Nodes.Add(new() { Id = "vol2", Type = "volpan", Row = 1, Column = 6 });
        graph.Cables.Add(new() { From = "reverb1", To = "vol2" });
        graph.Cables.Add(new() { From = "vol2", To = "out2" });
        RoundTrip(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = graph }, 1);
        var rig = new IpcMessage {
            Type = IpcTypes.SetParams,
            Generation = 1,
            Params = [
                new(IpcBlocks.Rig, "copyOut2", 1), new(IpcBlocks.Rig, "out3Mode", 1),
                new(IpcBlocks.Rig, "out1Level", -3),
                new(IpcBlocks.GlobalEq(1), "g1k", 3)
            ]
        };
        RoundTrip(rig, 1);
        using var bus = new OutputBus([0, 2, 4, 6], [8, 9]);
        var frames = service.Engine.Spec.MaxBlockSize;
        var input = new float[frames];
        var left = new float[frames];
        var right = new float[frames];
        var telemetry = new HostTelemetry();
        var push = new IpcMessage { Type = IpcTypes.Telemetry, Telemetry = telemetry };

        void Callbacks() {
            for (var i = 0; i < 2_000; i++) {
                service.Engine.Process(new(input, input, left, right) { Outputs = bus });
                if (i % 20 == 0) {
                    output.Position = 0;
                    service.Telemetry(telemetry);
                    Run(writer.WriteAsync(output, push, token));
                }
            }
        }

        Assert.Equal(0, AllocationProbe.Measure(Callbacks));
        Assert.True(service.Engine.Current!.HasOutput(1));
    }

    [Fact]
    public void Midi_clock_and_active_sensing_allocate_nothing_on_the_midi_thread() {
        Assert.SkipUnless(Cathode.Audio.Native.NativeAudio.IsLibraryPresent, "cth_audio not built");
        using var midi = new MidiInput(service.Engine, static _ => { });
        midi.SetFollowClock(true);
        // One poll's worth at 120 BPM and then some: clock ticks 1 ms apart and an active-sensing byte.
        var start = Cathode.Audio.Native.NativeMidi.NowNanoseconds();
        var poll = new List<Cathode.Audio.Native.MidiMessage>(64);
        for (var i = 0; i < 48; i++) {
            poll.Add(new(start + (ulong)i * 1_000_000, 0xF8, 0, 0, 1));
        }

        poll.Add(new(start, 0xFE, 0, 0, 1));
        Assert.Null(midi.Translate(poll)); // nothing for the Server, so no batch

        Assert.Equal(
            0,
            AllocationProbe.Measure(() => {
                    for (var i = 0; i < 1_000; i++) {
                        midi.Translate(poll);
                        _ = Cathode.Audio.Native.NativeMidi.PortCount(); // the port scan's check
                    }
                }
            )
        );

        // A message the Server maps still gets through.
        var batch = midi.Translate([new(start, 0xB0, 11, 64, 3)]);
        Assert.Equal(0xB0, Assert.Single(batch!).Status);
    }

    [Fact]
    public void Reloading_a_preset_stays_small() {
        // The cab's IR is cached and the convolvers live in native memory; before that a load was about 160 KB. The
        // Drive block builds every component-level network its catalogue can switch to up front, for both channels, so
        // that switching its type on the audio thread never allocates; as the catalogue grew to 61 circuits and the
        // other parity passes added parameters, a load crept to 169,480 bytes. Most of the Drive's networks were
        // overhead, now gone: an adaptor reads its netlist's topology from arrays the netlist builds once and shares
        // (the catalogue's netlists are static), a small adaptor solves its MNA in stack scratch instead of holding the
        // buffers, and a linear root (the tone networks) carries no Newton solver state. That took the Drive from about
        // 130 KB to 74 KB and a load to 112,664 bytes, under a limit that leaves about 2 KB.
        var load = new IpcMessage { Type = IpcTypes.LoadGraph, Generation = 1, Graph = Rig() };

        var allocated = RoundTrip(load, 3);

        Assert.True(allocated < 115_000, $"a preset load allocated {allocated} bytes");
    }

    [Fact]
    public void Fast_path_applies_parameters_like_the_deserializing_path() {
        service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 4, Graph = GraphTemplates.Basic() });
        var frame = """{"params":[{"value":-12.5,"param":"level","block":"volpan1"},"""u8
            + """{"block":"volpan1","param":"nope","value":1}],"""u8
            + "\n"u8
            + """ "generation":4,"id":9,"type":"setParams","graph":null}"""u8;

        Assert.True(service.TryHandle(frame, out var reply));
        Assert.Equal(IpcTypes.Error, reply.Type);
        Assert.Equal(9, reply.Id);
        Assert.Equal(["No parameter 'nope' on block 'volpan1'."], reply.Errors!);
        var graph = RunOneCallback();
        var block = graph.Block(graph.SlotOf("volpan1"));
        Assert.Equal(-12.5f, block.Params.GetTarget(block.Descriptor.IndexOf("level")));

        Assert.True(service.TryHandle("""{"type":"setParams","id":10,"generation":4,"params":[]}"""u8, out reply));
        Assert.Equal((IpcTypes.Ok, 10L), (reply.Type, reply.Id));
        Assert.True(service.TryHandle("""{"type":"setParams","id":11,"generation":3,"params":[]}"""u8, out reply));
        Assert.Contains("Stale", reply.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Fast_path_sets_rig_parameters_without_a_current_generation() {
        var frame =
            """{"type":"setParams","id":5,"generation":99,"params":[{"block":"rig","param":"tempo","value":90}]}"""u8;

        Assert.True(service.TryHandle(frame, out var reply));

        Assert.Equal(IpcTypes.Ok, reply.Type);
        Assert.Equal(90f, service.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf("tempo")));
        Assert.True(
            service.TryHandle(
                """{"type":"setParams","generation":99,"params":[{"block":"rig","param":"tempo","value":80},"""u8
                + """{"block":"amp1","param":"x","value":1}]}"""u8,
                out reply
            )
        );
        Assert.Contains("Stale", reply.Error, StringComparison.Ordinal); // a graph value rejects the whole update
        Assert.Equal(90f, service.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf("tempo")));
    }

    [Fact]
    public void Fast_path_posts_bypass_and_leaves_other_requests_to_the_slow_path() {
        service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = GraphTemplates.Basic() });
        var graph = RunOneCallback();

        // An escaped id reads the same as a plain one.
        Assert.True(
            service.TryHandle(
                """{"type":"setBypass","generation":1,"block":"filter1","bypassed":true}"""u8,
                out var reply
            )
        );
        Assert.Equal(IpcTypes.Ok, reply.Type);
        RunOneCallback();
        Assert.True(graph.IsBypassed(graph.SlotOf("filter1")));
        Assert.True(service.TryHandle("""{"type":"setBypass","generation":1,"block":"ghost"}"""u8, out reply));
        Assert.Contains("ghost", reply.Error, StringComparison.Ordinal);

        Assert.False(service.TryHandle("""{"type":"hello","id":1}"""u8, out _));
        Assert.False(service.TryHandle("""{"type":"loadGraph","generation":2,"graph":{"nodes":[]}}"""u8, out _));
        Assert.False(service.TryHandle("""{"type":"setParams","generation":"one"}"""u8, out _));
        Assert.False(service.TryHandle("not json"u8, out _));
    }

    /// <summary>Runs one audio callback, which swaps in a published graph and applies queued commands.</summary>
    CompiledGraph RunOneCallback() {
        var frames = service.Engine.Spec.MaxBlockSize;
        var input = new float[frames];
        service.Engine.Process(new(input, input, new float[frames], new float[frames]));
        return service.Engine.Current!;
    }

    /// <summary>
    ///     Frames <paramref name="request" /> once, then reads, handles and answers it as the server does, and returns
    ///     the bytes allocated per round trip: as <see cref="AllocationProbe" /> measures, the least of up to three
    ///     batches of <paramref name="count" />, so a one-off of the runtime's is not counted while what the request
    ///     itself allocates (a preset load's graph) is, in every batch.
    /// </summary>
    long RoundTrip(IpcMessage request, int count) {
        var token = TestContext.Current.CancellationToken;
        var framed = new MemoryStream();
        IpcFraming.WriteAsync(framed, request, token).GetAwaiter().GetResult();
        var input = new MemoryStream(framed.ToArray(), false);
        var failures = 0;

        void Once() {
            input.Position = 0;
            output.Position = 0;
            Run(reader.ReadFrameAsync(input, token));
            if (!service.TryHandle(reader.Frame, out var reply)) {
                reply = service.Handle(reader.Deserialize());
            }

            failures += reply.Type == IpcTypes.Ok ? 0 : 1; // asserted outside the measurement: Assert allocates
            Run(writer.WriteAsync(output, reply, token));
        }

        void Batch() {
            for (var i = 0; i < count; i++) {
                Once();
            }
        }

        for (var i = 0; i < 100; i++) {
            Once(); // warm up: tiered JIT and the reused buffers
        }

        var allocated = AllocationProbe.Measure(Batch, static () => { });
        Assert.Equal(0, failures);
        return allocated / count;
    }

    /// <summary>
    ///     Finishes framing that has already completed, as it does over a <see cref="MemoryStream" />, so the measured
    ///     code never leaves the measuring thread; throws if it has not.
    /// </summary>
    static void Run(ValueTask task) {
        if (!task.IsCompletedSuccessfully) {
            throw new InvalidOperationException(
                "The framing went asynchronous: the measurement would leave the thread."
            );
        }

        task.GetAwaiter().GetResult();
    }

    static void Run<T>(ValueTask<T> task) {
        if (!task.IsCompletedSuccessfully) {
            throw new InvalidOperationException(
                "The framing went asynchronous: the measurement would leave the thread."
            );
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>The soak's high-gain rig without the NAM amp, whose native library a test run may not have.</summary>
    static GraphDefinition Rig() {
        string[] types = ["gate", "drive", "cab", "delay", "reverb"];
        var graph = new GraphDefinition {
            Nodes = [
                new() { Id = "in1", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "out1", Type = NodeTypes.Output, Row = 0, Column = types.Length + 1 }
            ],
            Cables = [new() { From = types[^1] + "1", To = "out1" }]
        };
        var previous = "in1";
        for (var i = 0; i < types.Length; i++) {
            var node = new NodeDefinition { Id = types[i] + "1", Type = types[i], Row = 0, Column = i + 1 };
            if (types[i] == "cab") {
                node.Assets["slot1"] = Path.Combine(EngineRoot, "examples", "synthetic-cab.wav");
                node.Parameters["m1source"] = CabBlock.SourceIr;
            }

            graph.Nodes.Add(node);
            graph.Cables.Add(new() { From = previous, To = node.Id });
            previous = node.Id;
        }

        return graph;
    }

    static string FindEngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cathode.slnx not found above the test binaries.");
    }
}
