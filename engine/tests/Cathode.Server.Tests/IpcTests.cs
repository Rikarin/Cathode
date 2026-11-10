using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Ipc;
using System.Net.Sockets;

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
        await using var stream = new MemoryStream();

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
        await using var stream = new MemoryStream();
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
        Assert.Contains(invalid.Errors!, static e => e.Contains("unknown type", StringComparison.Ordinal));
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

    [Fact]
    public async Task Client_forgets_a_request_that_times_out_is_cancelled_or_cannot_be_sent() {
        var token = TestContext.Current.CancellationToken;
        // A host that accepts the connection and never answers.
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        await using var client = new IpcEngineClient(socketPath) { ReplyTimeout = TimeSpan.FromMilliseconds(200) };
        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        using var host = await listener.AcceptAsync(token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var waiting = client.SendAsync(new() { Type = IpcTypes.Hello }, cancel.Token);
        var pendingWhileWaiting = client.PendingRequests;
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(new() { Type = IpcTypes.Hello }, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendAsync(new() { Type = IpcTypes.Hello }, cancelled.Token) // fails before it is written
        );

        Assert.Equal(1, pendingWhileWaiting);
        Assert.Equal(0, client.PendingRequests);
        Assert.True(client.IsConnected); // none of this is a lost link
    }

    [Fact]
    public async Task Midi_ports_and_midi_out_destinations_cross_the_socket() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        string? destination = "unset";
        service.NativeMidiSend = (port, _) => {
            destination = port;
            return true;
        };
        service.NativeMidiPorts = static () => [new("in-1", "Keys", true), new("out-1", "Pedalboard", false)];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath).RunAsync(stop.Token);
        await using var client = new IpcEngineClient(socketPath);

        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        var ports = await client.SendAsync(new() { Type = IpcTypes.MidiPorts }, token);
        await client.SendAsync(
            new() { Type = IpcTypes.MidiOut, Destination = "out-1", Bytes = [0xB0, 30, 127] },
            token
        );
        var sentTo = destination;
        await client.SendAsync(new() { Type = IpcTypes.MidiOut, Bytes = [0xC0, 3] }, token);

        Assert.Equal([new("in-1", "Keys", true), new("out-1", "Pedalboard", false)], ports.MidiPorts!);
        Assert.Equal("out-1", sentTo);
        Assert.Null(destination); // no destination: every output
        await Assert.ThrowsAsync<IpcException>(() => client.SendAsync(new() { Type = "status" }, token));

        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public void Midi_out_sends_only_one_complete_short_message() {
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        var sent = new List<byte[]>();
        service.NativeMidiSend = (_, bytes) => {
            sent.Add(bytes);
            return true;
        };

        IpcMessage Send(params int[] bytes) => service.Handle(new() { Type = IpcTypes.MidiOut, Bytes = [..bytes] });

        int[][] valid = [
            [0x90, 60, 100], [0x8F, 60, 0], [0xB0, 30, 127], [0xC5, 3], [0xD0, 64], [0xE0, 0, 64], // channel voice
            [0xF1, 0x31], [0xF2, 0, 8], [0xF3, 2], [0xF6], // system common
            [0xF8], [0xFA], [0xFB], [0xFC], [0xFE], [0xFF] // system real-time
        ];
        int[][] invalid = [
            [], [0x90, 60, 100, 0], // not 1 to 3 bytes
            [60, 100], [0x7F], // no status byte (running status)
            [0x190, 60, 100], [-1], // not a byte: must not be truncated into a status
            [0xB0, 30], [0xC0, 3, 0], [0x90], [0xF8, 0], [0xF2, 0], // the wrong length for the status
            [0xB0, 30, 128], [0xB0, 0x80, 1], [0xC0, 300], // data bytes above 127
            [0xF0, 1, 2], [0xF7], [0xF4], [0xF5], [0xF9], [0xFD] // SysEx and undefined statuses
        ];

        Assert.All(valid, bytes => Assert.Equal(IpcTypes.Ok, Send(bytes).Type));
        Assert.Equal(valid.Select(static bytes => bytes.Select(static b => (byte)b)), sent);
        Assert.All(invalid, bytes => Assert.Equal(IpcTypes.Error, Send(bytes).Type));
        Assert.Equal(valid.Length, sent.Count); // nothing invalid reached the native sender
    }

    [Fact]
    public async Task Midi_out_with_no_outputs_is_not_an_error_unless_one_was_named() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        service.NativeMidiSend = static (_, _) => false; // nothing to send to
        service.NativeMidiPorts = static () => [new("in-1", "Keys", true)];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath).RunAsync(stop.Token);
        await using var client = new IpcEngineClient(socketPath);

        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        var everyOutput = await client.SendAsync(new() { Type = IpcTypes.MidiOut, Bytes = [0xB0, 30, 127] }, token);

        Assert.Equal(IpcTypes.Ok, everyOutput.Type);
        var named = await Assert.ThrowsAsync<IpcException>(() =>
            client.SendAsync(new() { Type = IpcTypes.MidiOut, Destination = "out-1", Bytes = [0xB0, 30, 127] }, token)
        );
        Assert.Equal("MIDI output is not available.", named.Message);

        await stop.CancelAsync();
        await server;
    }
}
