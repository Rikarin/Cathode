using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Ipc;

namespace Cathode.Server.Tests;

public sealed class IpcTests : IDisposable {
    readonly string socketPath = Path.Combine(
        Path.GetTempPath(),
        $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock"
    ); // Unix socket paths are limited to ~104 bytes on macOS

    public void Dispose() {
        File.Delete(socketPath);
    }

    [Fact]
    public async Task Framing_round_trips_every_field() {
        var message = new IpcMessage {
            Type = IpcTypes.SetParams,
            Id = 42,
            Generation = 3,
            Params = [new("amp1", "input", -3.5f)],
            Graph = GraphTemplates.Basic()
        };
        using var stream = new MemoryStream();

        await IpcFraming.WriteAsync(stream, message, TestContext.Current.CancellationToken);
        stream.Position = 0;
        var read = await IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(42, read.Id);
        Assert.Equal(new("amp1", "input", -3.5f), read.Params![0]);
        Assert.Equal(GraphTemplates.Basic().Nodes.Count, read.Graph!.Nodes.Count);
        Assert.Null(await IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken)); // clean end
    }

    [Fact]
    public async Task Reusable_frame_buffers_match_the_wire_format_both_ways() {
        var token = TestContext.Current.CancellationToken;
        using var writer = new IpcFrameWriter();
        var reader = new IpcFrameReader();
        using var stream = new MemoryStream();
        var big = new IpcMessage { Type = IpcTypes.LoadGraph, Id = 1, Graph = GraphTemplates.Basic() };
        var small = new IpcMessage { Type = IpcTypes.SetParams, Id = 2, Params = [new("a", "b", 1)] };

        await writer.WriteAsync(stream, big, token);
        await writer.WriteAsync(stream, small, token); // a shorter frame after a longer one reuses the buffer
        await IpcFraming.WriteAsync(stream, big, token);
        stream.Position = 0;

        Assert.Equal(
            GraphTemplates.Basic().Nodes.Count,
            (await IpcFraming.ReadAsync(stream, token))!.Graph!.Nodes.Count
        );
        Assert.Equal(new("a", "b", 1), (await reader.ReadAsync(stream, token))!.Params![0]);
        Assert.Equal(1, (await reader.ReadAsync(stream, token))!.Id);
        Assert.Null(await reader.ReadAsync(stream, token));
    }

    [Fact]
    public async Task Reusable_writer_allocates_a_fraction_of_plain_framing() {
        // The host pushes telemetry 30 times a second; its allocations decide how long the GC budget lasts.
        var token = TestContext.Current.CancellationToken;
        using var writer = new IpcFrameWriter();
        var stream = new MemoryStream(1 << 22);
        var message = new IpcMessage {
            Type = IpcTypes.Telemetry,
            Telemetry = new() {
                InputPeak = 0.1f,
                OutputPeakLeft = 0.2f,
                OutputPeakRight = 0.3f,
                CpuP999 = 4,
                CpuMax = 5
            }
        };

        async Task<double> PerFrame(Func<Task> write) {
            for (var i = 0; i < 100; i++) {
                await write(); // warm up
            }

            stream.Position = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1_000; i++) {
                await write();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / 1_000.0;
        }

        var plain = await PerFrame(() => IpcFraming.WriteAsync(stream, message, token));
        var reused = await PerFrame(() => writer.WriteAsync(stream, message, token).AsTask());

        Assert.True(reused < plain / 3, $"reusable writer {reused:0} bytes per frame vs plain {plain:0}");
    }

    [Fact]
    public void Service_loads_graphs_and_applies_parameters() {
        using var service = new EngineHostService(ProcessSpec.Default, "test");

        var loaded = service.Handle(
            new() { Type = IpcTypes.LoadGraph, Id = 1, Generation = 7, Graph = GraphTemplates.Basic() }
        );
        var set = service.Handle(
            new() { Type = IpcTypes.SetParams, Id = 2, Generation = 7, Params = [new("volpan1", "level", -12f)] }
        );

        Assert.Equal(IpcTypes.Ok, loaded.Type);
        Assert.Equal(IpcTypes.Ok, set.Type);
    }

    [Fact]
    public void Service_reports_invalid_graphs_stale_generations_and_unknown_parameters() {
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        var broken = new GraphDefinition { Nodes = [new() { Id = "x", Type = "no-such-block" }] };

        var invalid = service.Handle(new() { Type = IpcTypes.LoadGraph, Id = 1, Generation = 1, Graph = broken });
        service.Handle(new() { Type = IpcTypes.LoadGraph, Id = 2, Generation = 2, Graph = GraphTemplates.Basic() });
        var stale = service.Handle(new() { Type = IpcTypes.SetParams, Id = 3, Generation = 1, Params = [] });
        var unknown = service.Handle(
            new() { Type = IpcTypes.SetParams, Id = 4, Generation = 2, Params = [new("volpan1", "nope", 1)] }
        );

        Assert.Equal(IpcTypes.Error, invalid.Type);
        Assert.Contains(invalid.Errors!, e => e.Contains("unknown type", StringComparison.Ordinal));
        Assert.Equal(IpcTypes.Error, stale.Type);
        Assert.Equal(IpcTypes.Error, unknown.Type);
    }

    [Fact]
    public async Task Client_and_server_talk_over_a_unix_socket_with_telemetry() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath).RunAsync(stop.Token);
        await using var client = new IpcEngineClient(socketPath);
        var telemetry = new TaskCompletionSource<HostTelemetry>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.TelemetryReceived += t => telemetry.TrySetResult(t);

        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        var hello = await client.SendAsync(new() { Type = IpcTypes.Hello }, token);
        await client.SendAsync(
            new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = GraphTemplates.Basic() },
            token
        );
        var error = await Assert.ThrowsAsync<IpcException>(() => client.SendAsync(
                new() { Type = IpcTypes.SetBypass, Generation = 1, Block = "ghost" },
                token
            )
        );
        var pushed = await telemetry.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

        Assert.Equal("test", hello.Host!.Backend);
        Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
        Assert.True(pushed.CpuP999 >= 0);

        await stop.CancelAsync();
        await server;
    }
}
