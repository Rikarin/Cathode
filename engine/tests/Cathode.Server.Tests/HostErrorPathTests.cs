using Cathode.Dsp;
using Cathode.Dsp.Parameters;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Ipc;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Cathode.Server.Tests;

/// <summary>
///     What the host does with requests that go wrong: a frame that is not a request, a handler that throws, a block
///     that cannot be prepared, telemetry that fails. Each is answered or logged, and the host serves the next request.
/// </summary>
public sealed class HostErrorPathTests : IDisposable {
    readonly string socketPath = Path.Combine(
        Path.GetTempPath(),
        $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock"
    ); // Unix socket paths are limited to ~104 bytes on macOS

    readonly ConcurrentQueue<string> log = new();

    public void Dispose() {
        File.Delete(socketPath);
    }

    [Fact]
    public async Task A_frame_that_is_not_a_request_gets_an_error_and_the_host_answers_the_next() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath, log.Enqueue).RunAsync(stop.Token);
        using var socket = await ConnectAsync(token);
        await using var stream = new NetworkStream(socket, false);

        await SendAsync(stream, "not json", token);
        var garbage = await ReplyAsync(stream, token);
        await SendAsync(stream, """{"type":"hello","id":7,"generation":"one"}""", token);
        var wrongType = await ReplyAsync(stream, token);
        await SendAsync(stream, """{"type":"setParams","id":8,"generation":1,"params":[{"block":5}]}""", token);
        var wrongId = await ReplyAsync(stream, token);
        await SendAsync(stream, """{"type":"hello","id":9}""", token);
        var hello = await ReplyAsync(stream, token);

        Assert.Equal((IpcTypes.Error, 0L), (garbage.Type, garbage.Id));
        Assert.Equal((IpcTypes.Error, 7L), (wrongType.Type, wrongType.Id));
        Assert.StartsWith("Malformed request", wrongType.Error, StringComparison.Ordinal);
        Assert.Equal((IpcTypes.Error, 8L), (wrongId.Type, wrongId.Id));
        Assert.Equal((IpcTypes.Ok, 9L), (hello.Type, hello.Id));
        Assert.Equal("test", hello.Host!.Backend);

        await stop.CancelAsync();
        await server; // would rethrow whatever the bad frames had thrown
    }

    [Fact]
    public async Task A_handler_that_throws_answers_with_an_error_and_keeps_the_link() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test") { Log = log.Enqueue };
        var failure = new Exception[] { new IOException("disk full"), new UnauthorizedAccessException("not yours") };
        var calls = 0;
        service.NativeMidiSend = (_, _) => throw failure[calls++];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath, log.Enqueue).RunAsync(stop.Token);
        await using var client = new IpcEngineClient(socketPath);
        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        var send = new IpcMessage { Type = IpcTypes.MidiOut, Bytes = [0xB0, 30, 127] };

        var io = await Assert.ThrowsAsync<IpcException>(() => client.SendAsync(send, token));
        var access = await Assert.ThrowsAsync<IpcException>(() => client.SendAsync(send, token));
        var hello = await client.SendAsync(new() { Type = IpcTypes.Hello }, token);

        Assert.Contains("disk full", io.Message, StringComparison.Ordinal);
        Assert.Contains("not yours", access.Message, StringComparison.Ordinal);
        Assert.Equal(IpcTypes.Ok, hello.Type);
        Assert.True(client.IsConnected);
        Assert.DoesNotContain(log, static line => line.StartsWith("connection ended", StringComparison.Ordinal));
        Assert.Contains(log, static line => line.Contains("'midiOut' failed", StringComparison.Ordinal));

        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public void A_block_that_fails_to_prepare_fails_the_load_and_disposes_the_blocks_built_before_it() {
        var blocks = new List<FaultyBlock>();
        var registry = BlockRegistry.CreateDefault();
        registry.Register(
            FaultyBlock.Info,
            () => {
                var block = new FaultyBlock { FailPrepare = blocks.Count == 1 }; // the second one
                blocks.Add(block);
                return block;
            }
        );
        using var service = new EngineHostService(ProcessSpec.Default, "test", registry);

        var failed = service.Handle(
            new() { Type = IpcTypes.LoadGraph, Id = 1, Generation = 1, Graph = Faulty("faulty1", "faulty2") }
        );
        var next = service.Handle(
            new() { Type = IpcTypes.LoadGraph, Id = 2, Generation = 2, Graph = GraphTemplates.Basic() }
        );

        Assert.Equal((IpcTypes.Error, 1L), (failed.Type, failed.Id));
        Assert.Contains("cannot prepare", failed.Error, StringComparison.Ordinal);
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, static block => Assert.Equal(1, block.Disposals));
        Assert.Equal(IpcTypes.Ok, next.Type);
    }

    [Fact]
    public async Task Telemetry_that_throws_is_logged_once_and_resumes() {
        var token = TestContext.Current.CancellationToken;
        var block = new FaultyBlock();
        var registry = BlockRegistry.CreateDefault();
        registry.Register(FaultyBlock.Info, () => block);
        using var service = new EngineHostService(ProcessSpec.Default, "test", registry);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath, log.Enqueue).RunAsync(stop.Token);
        await using var client = new IpcEngineClient(socketPath);
        var pushes = 0;
        client.TelemetryReceived += _ => Interlocked.Increment(ref pushes);
        await client.ConnectAsync(TimeSpan.FromSeconds(5), token);
        await client.SendAsync(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = Faulty("faulty1") }, token);

        block.FailRefresh = true;
        await WaitAsync(() => block.FailedRefreshes >= 3, token); // three ticks' worth
        var hello = await client.SendAsync(new() { Type = IpcTypes.Hello }, token);
        block.FailRefresh = false;
        var before = Volatile.Read(ref pushes);
        await WaitAsync(() => Volatile.Read(ref pushes) > before + 1, token);

        Assert.Equal(IpcTypes.Ok, hello.Type);
        Assert.Single(log, static line => line.Contains("gathering telemetry failed", StringComparison.Ordinal));

        await stop.CancelAsync();
        await server;
    }

    static async Task WaitAsync(Func<bool> condition, CancellationToken token) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition()) {
            Assert.True(DateTime.UtcNow < deadline, "The host did not get there within 10 s.");
            await Task.Delay(20, token);
        }
    }

    /// <summary>Input, the named faulty blocks in a row, Output.</summary>
    static GraphDefinition Faulty(params string[] ids) {
        var graph = new GraphDefinition {
            Nodes = [
                new() { Id = "in1", Type = NodeTypes.Input, Row = 0, Column = 0 },
                new() { Id = "out1", Type = NodeTypes.Output, Row = 0, Column = ids.Length + 1 }
            ]
        };
        var previous = "in1";
        for (var i = 0; i < ids.Length; i++) {
            graph.Nodes.Add(new() { Id = ids[i], Type = FaultyBlock.Info.TypeId, Row = 0, Column = i + 1 });
            graph.Cables.Add(new() { From = previous, To = ids[i] });
            previous = ids[i];
        }

        graph.Cables.Add(new() { From = previous, To = "out1" });
        return graph;
    }

    async Task<Socket> ConnectAsync(CancellationToken token) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true) {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return socket;
            } catch (SocketException) when (DateTime.UtcNow < deadline) {
                socket.Dispose();
                await Task.Delay(50, token);
            }
        }
    }

    /// <summary>Writes <paramref name="payload" /> as one frame, whatever it holds.</summary>
    static async Task SendAsync(Stream stream, string payload, CancellationToken token) {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var frame = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length);
        bytes.CopyTo(frame, 4);
        await stream.WriteAsync(frame, token);
    }

    /// <summary>The next message that is a reply (telemetry and MIDI pushes share the stream).</summary>
    static async Task<IpcMessage> ReplyAsync(Stream stream, CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true) {
            var message = await IpcFraming.ReadAsync(stream, timeout.Token);
            Assert.NotNull(message); // the host must not close the connection
            if (message.Type is not (IpcTypes.Telemetry or IpcTypes.Midi)) {
                return message;
            }
        }
    }

    /// <summary>A block that passes audio through and fails where it is told to.</summary>
    sealed class FaultyBlock() : DspBlock(Info), IControlRefresh {
        int failedRefreshes;

        public static BlockDescriptor Info { get; } =
            new("test-faulty", "Faulty", "Test", [ParamDescriptor.Float("unused", "Unused", 0, 1, 0)]);

        public bool FailPrepare { get; init; }

        public volatile bool FailRefresh;

        public int FailedRefreshes => Volatile.Read(ref failedRefreshes);

        public int Disposals { get; private set; }

        public void Refresh() {
            if (FailRefresh) {
                Interlocked.Increment(ref failedRefreshes);
                throw new NotSupportedException("cannot refresh");
            }
        }

        protected override void OnPrepare(in ProcessSpec spec) {
            if (FailPrepare) {
                throw new NotSupportedException("cannot prepare");
            }
        }

        protected override void OnReset() { }

        protected override void OnProcess(in StereoBuffer io, in BlockContext context) { }

        protected override void Dispose(bool disposing) {
            Disposals++;
            base.Dispose(disposing);
        }
    }
}
