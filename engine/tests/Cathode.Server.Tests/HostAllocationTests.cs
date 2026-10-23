using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Ipc;

namespace Cathode.Server.Tests;

/// <summary>
///     The host must not collect during a session (docs/adr/0001-process-model.md), so what it does at a steady rate
///     (knob drags, scene bypasses, telemetry pushes) must not allocate, and a preset load must stay small.
/// </summary>
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
    public async Task Parameter_and_bypass_commands_and_telemetry_allocate_nothing() {
        var token = TestContext.Current.CancellationToken;
        await RoundTripAsync(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = Rig() }, 1);
        var knob = new IpcMessage {
            Type = IpcTypes.SetParams, Generation = 1, Params = [new("drive1", "drive", 3.5f)]
        };
        var scene = new IpcMessage {
            Type = IpcTypes.SetParams,
            Generation = 1,
            Params = [..Enumerable.Range(0, 8).Select(i => new ParamValue("drive1", "drive", i))]
        };
        var bypass = new IpcMessage { Type = IpcTypes.SetBypass, Generation = 1, Block = "delay1", Bypassed = true };
        var telemetry = new HostTelemetry();
        var push = new IpcMessage { Type = IpcTypes.Telemetry, Telemetry = telemetry };

        async Task<long> PushesAsync() {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1_000; i++) {
                output.Position = 0;
                service.Telemetry(telemetry);
                await writer.WriteAsync(output, push, token);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        await PushesAsync(); // warm up

        Assert.Equal(0, await RoundTripAsync(knob, 1_000));
        Assert.Equal(0, await RoundTripAsync(scene, 1_000));
        Assert.Equal(0, await RoundTripAsync(bypass, 500)); // no audio thread drains the 1,024-command queue
        Assert.Equal(0, await PushesAsync());
    }

    [Fact]
    public async Task Reloading_a_preset_stays_small() {
        // The cab's IR is cached and the convolvers live in native memory; before that a load was about 160 KB.
        var load = new IpcMessage { Type = IpcTypes.LoadGraph, Generation = 1, Graph = Rig() };

        var allocated = await RoundTripAsync(load, 3);

        Assert.True(allocated < 48_000, $"a preset load allocated {allocated} bytes");
    }

    [Fact]
    public void Fast_path_applies_parameters_like_the_deserializing_path() {
        service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 4, Graph = GraphTemplates.Basic() });
        var frame = """
            {"params":[{"value":-12.5,"param":"level","block":"volpan1"},{"block":"volpan1","param":"nope","value":1}],
             "generation":4,"id":9,"type":"setParams","graph":null}
            """u8;

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
        var frame = """{"type":"setParams","id":5,"generation":99,"params":[{"block":"rig","param":"tempo","value":90}]}"""u8;

        Assert.True(service.TryHandle(frame, out var reply));

        Assert.Equal(IpcTypes.Ok, reply.Type);
        Assert.Equal(90f, service.Engine.Rig.GetTarget(Cathode.Engine.Rig.RigControls.Descriptor.IndexOf("tempo")));
        Assert.True(
            service.TryHandle(
                """{"type":"setParams","generation":99,"params":[{"block":"rig","param":"tempo","value":80},{"block":"amp1","param":"x","value":1}]}"""u8,
                out reply
            )
        );
        Assert.Contains("Stale", reply.Error, StringComparison.Ordinal); // a graph value rejects the whole update
        Assert.Equal(90f, service.Engine.Rig.GetTarget(Cathode.Engine.Rig.RigControls.Descriptor.IndexOf("tempo")));
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

    /// <summary>Frames <paramref name="request" /> once, then reads, handles and answers it as the server does.</summary>
    async Task<long> RoundTripAsync(IpcMessage request, int count) {
        var token = TestContext.Current.CancellationToken;
        var framed = new MemoryStream();
        await IpcFraming.WriteAsync(framed, request, token);
        var input = new MemoryStream(framed.ToArray(), false);
        var failures = 0;

        async Task RunAsync() {
            input.Position = 0;
            output.Position = 0;
            await reader.ReadFrameAsync(input, token);
            if (!service.TryHandle(reader.Frame, out var reply)) {
                reply = service.Handle(reader.Deserialize());
            }

            failures += reply.Type == IpcTypes.Ok ? 0 : 1; // asserted outside the measurement: Assert allocates
            await writer.WriteAsync(output, reply, token);
        }

        for (var i = 0; i < 100; i++) {
            await RunAsync(); // warm up: tiered JIT and the pooled async state machines
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++) {
            await RunAsync();
        }

        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
        Assert.Equal(0, failures);
        return allocated;
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
                node.Assets["ir1"] = Path.Combine(EngineRoot, "examples", "synthetic-cab.wav");
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
