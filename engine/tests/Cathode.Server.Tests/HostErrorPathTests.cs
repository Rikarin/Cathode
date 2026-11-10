using Cathode.Dsp;
using Cathode.Dsp.Looping;
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
///     What the host does with requests that go wrong: a frame that is not a request, a length prefix out of range, a
///     client that leaves in the middle of a frame, a handler that throws, a block that cannot be prepared, telemetry
///     that fails, a looper export that cannot be written or races the recording. Each is answered or logged, and the
///     host serves the next request.
/// </summary>
public sealed class HostErrorPathTests : IDisposable {
    /// <summary>
    ///     What one export of the loop may allocate: the file stream and its buffer, the path strings and the reply
    ///     (4,624 bytes measured on .NET 10, macOS), with room. A copy of the loop would be eight bytes a frame.
    /// </summary>
    const long ExportBudget = 32_000;

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

    [Fact]
    public async Task Every_frame_that_is_not_a_request_is_answered_on_the_same_connection() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath).RunAsync(stop.Token);
        using var socket = await ConnectAsync(token);
        await using var stream = new NetworkStream(socket, false);
        string[] frames = [
            "", // a zero-length frame
            "null",
            "42",
            "[]",
            "{}",
            "{",
            """{"type":null,"id":1}""",
            """{"type":7,"id":1}""",
            """{"type":"hello","id":"one"}""",
            """{"type":"hello","id":1.5}""",
            """{"type":"loadGraph","id":1,"graph":7}""",
            """{"type":"loadGraph","id":1,"graph":{"nodes":[{"id":5}]}}""",
            """{"type":"setBypass","id":1,"generation":0,"block":{}}""",
            """{"type":"setParams","id":1,"generation":0,"params":{"block":"rig"}}""",
            """{"type":"setParams","id":1,"generation":0,"params":[{"block":"rig","param":"tempo","value":"fast"}]}""",
            """{"type":"midiOut","id":1,"bytes":"144"}""",
            """{"type":"exportLooper","id":1,"path":5}""",
            new('[', 200), // deeper than the JSON reader goes
            "{\"type\":\"hello\",\"id\":1}\u0000trailing"
        ];

        foreach (var frame in frames) {
            await SendAsync(stream, frame, token);
            var reply = await ReplyAsync(stream, token);
            Assert.True(reply.Type == IpcTypes.Error, $"'{frame}' was answered with '{reply.Type}'");
        }

        await SendAsync(stream, """{"type":"hello","id":2}""", token);
        var hello = await ReplyAsync(stream, token);
        Assert.Equal((IpcTypes.Ok, 2L), (hello.Type, hello.Id));

        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public async Task Random_and_damaged_frames_each_get_one_reply() {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath).RunAsync(stop.Token);
        using var socket = await ConnectAsync(token);
        await using var stream = new NetworkStream(socket, false);
        var random = new Random(241);
        string[] requests = [
            """{"type":"setParams","id":1,"generation":0,"params":[{"block":"rig","param":"tempo","value":100}]}""",
            """{"type":"setBypass","id":1,"generation":1,"block":"filter1","bypassed":true}""",
            """{"type":"midiPorts","id":1}""",
            """{"type":"latency","id":1}"""
        ];

        for (var i = 0; i < 400; i++) {
            byte[] payload;
            if (i % 2 == 0) {
                payload = new byte[random.Next(0, 200)];
                random.NextBytes(payload);
            } else {
                // A real request with a few bytes changed: mostly still JSON, often no longer a request.
                payload = Encoding.UTF8.GetBytes(requests[random.Next(requests.Length)]);
                for (var change = random.Next(1, 4); change > 0; change--) {
                    payload[random.Next(payload.Length)] = (byte)random.Next(256);
                }
            }

            await SendAsync(stream, payload, token);
            var reply = await ReplyAsync(stream, token);
            Assert.True(reply.Type is IpcTypes.Ok or IpcTypes.Error, $"frame {i} was answered with '{reply.Type}'");
        }

        await SendAsync(stream, """{"type":"hello","id":2}""", token);
        Assert.Equal(IpcTypes.Ok, (await ReplyAsync(stream, token)).Type);

        await stop.CancelAsync();
        await server;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(IpcFraming.MaxFrameBytes + 1)] // just over the limit
    [InlineData(17 * 1024 * 1024)]
    [InlineData(int.MaxValue)]
    public async Task A_length_prefix_out_of_range_ends_that_connection_and_the_next_is_served(int length) {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath, log.Enqueue).RunAsync(stop.Token);

        using (var socket = await ConnectAsync(token)) {
            await using var stream = new NetworkStream(socket, false);
            var prefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
            await stream.WriteAsync(prefix, token);
            Assert.True(await ClosedAsync(stream, token)); // nothing after such a prefix can be trusted
        }

        Assert.Equal(IpcTypes.Ok, await HelloOnANewConnectionAsync(token));
        Assert.Contains(log, static line => line.Contains("out of range", StringComparison.Ordinal));

        await stop.CancelAsync();
        await server;
    }

    [Theory]
    [InlineData(2, 0)] // inside the length prefix
    [InlineData(4, 0)] // a prefix promising 100 bytes, then nothing
    [InlineData(4, 10)] // inside the payload
    public async Task A_client_that_goes_away_in_the_middle_of_a_frame_does_not_stop_the_host(int prefix, int body) {
        var token = TestContext.Current.CancellationToken;
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var server = new EngineHostServer(service, socketPath, log.Enqueue).RunAsync(stop.Token);

        using (var socket = await ConnectAsync(token)) {
            await using var stream = new NetworkStream(socket, false);
            var frame = new byte[4 + body];
            BinaryPrimitives.WriteInt32LittleEndian(frame, 100);
            "{\"type\":\"hello\",\"id\":1}"u8[..body].CopyTo(frame.AsSpan(4));
            await stream.WriteAsync(frame.AsMemory(0, prefix + body), token);
            socket.Shutdown(SocketShutdown.Both);
        }

        Assert.Equal(IpcTypes.Ok, await HelloOnANewConnectionAsync(token));

        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public void Exporting_the_looper_to_a_path_that_cannot_be_written_is_an_error_and_the_next_export_works() {
        using var service = new EngineHostService(ProcessSpec.Default, "test") { Log = log.Enqueue };
        var folder = Directory.CreateTempSubdirectory("cth-export-");
        try {
            var empty = service.Handle(
                new() { Type = IpcTypes.ExportLooper, Id = 1, Path = Path.Combine(folder.FullName, "empty.wav") }
            );
            service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = GraphTemplates.Basic() });
            service.Handle(new() { Type = IpcTypes.Looper, Action = "record" });
            Pump(service, 100);
            service.Handle(new() { Type = IpcTypes.Looper, Action = "stop" });
            Pump(service, 1);

            var missingFolder = service.Handle(
                new() {
                    Type = IpcTypes.ExportLooper, Id = 2, Path = Path.Combine(folder.FullName, "no", "such", "loop.wav")
                }
            );
            var aFolder = service.Handle(new() { Type = IpcTypes.ExportLooper, Id = 3, Path = folder.FullName });
            var written = Path.Combine(folder.FullName, "loop.wav");
            var ok = service.Handle(new() { Type = IpcTypes.ExportLooper, Id = 4, Path = written });

            Assert.Equal(IpcTypes.Error, empty.Type);
            Assert.Equal((IpcTypes.Error, 2L), (missingFolder.Type, missingFolder.Id));
            Assert.Equal((IpcTypes.Error, 3L), (aFolder.Type, aFolder.Id));
            Assert.True(ok.Type == IpcTypes.Ok, ok.Error);
            Assert.True(new FileInfo(written).Length > 100 * service.Engine.Spec.MaxBlockSize);
        } finally {
            folder.Delete(true);
        }
    }

    /// <summary>
    ///     An export has a defined answer in every state: refused while the loop is being written (recording or
    ///     overdubbing), with no file left behind, and written once it plays. The host must not collect during a
    ///     session, so the export copies the loop to the file in chunks: its managed allocation stays that of opening a
    ///     file, however long the loop (in two arrays this one would take a megabyte).
    /// </summary>
    [Fact]
    public void An_export_is_refused_while_the_loop_is_written_and_allocates_little_once_it_plays() {
        using var service = new EngineHostService(ProcessSpec.Default, "test");
        service.Log = log.Enqueue;
        var folder = Directory.CreateTempSubdirectory("cth-export-");
        try {
            var path = Path.Combine(folder.FullName, "loop.wav");
            var export = new IpcMessage { Type = IpcTypes.ExportLooper, Id = 1, Path = path };
            service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = GraphTemplates.Basic() });

            service.Handle(new() { Type = IpcTypes.Looper, Action = "record" });
            Pump(service, 2_000);
            Assert.Equal(LooperState.Recording, service.Engine.Looper!.State);
            var recording = service.Handle(export);

            service.Handle(new() { Type = IpcTypes.Looper, Action = "overdub" });
            Pump(service, 10);
            Assert.Equal(LooperState.Overdubbing, service.Engine.Looper.State);
            var overdubbing = service.Handle(export);
            var leftBehind = File.Exists(path);

            service.Handle(new() { Type = IpcTypes.Looper, Action = "play" });
            Pump(service, 1);
            Assert.Equal(LooperState.Playing, service.Engine.Looper.State);
            var playing = IpcMessage.Fail(0, "not run");
            var allocated = Testing.AllocationProbe.Measure(() => playing = service.Handle(export));

            Assert.Equal((IpcTypes.Error, 1L), (recording.Type, recording.Id));
            Assert.Contains("recording or overdubbing", recording.Error, StringComparison.Ordinal);
            Assert.Equal(IpcTypes.Error, overdubbing.Type);
            Assert.Equal(recording.Error, overdubbing.Error);
            Assert.False(leftBehind, "a refused export left a file");
            Assert.True(playing.Type == IpcTypes.Ok, playing.Error);
            var frames = service.Engine.Looper.Length;
            Assert.InRange(frames, 1_990 * service.Engine.Spec.MaxBlockSize, 2_010 * service.Engine.Spec.MaxBlockSize);
            Assert.True(new FileInfo(path).Length >= 8L * frames); // two channels of 32-bit float
            Assert.True(allocated < ExportBudget, $"exporting {frames} frames allocated {allocated} bytes");
        } finally {
            folder.Delete(true);
        }
    }

    [Fact]
    public void Exporting_while_the_looper_records_is_answered_and_the_host_keeps_running() {
        // The audio thread keeps appending to the loop while the control thread asks for it. Every export that finds
        // the looper still recording afterwards was refused: the state leaves Recording only once.
        using var service = new EngineHostService(ProcessSpec.Default, "test") { Log = log.Enqueue };
        var folder = Directory.CreateTempSubdirectory("cth-export-");
        var running = true;
        var audio = new Thread(() => {
                var frames = service.Engine.Spec.MaxBlockSize;
                var input = new float[frames];
                var left = new float[frames];
                var right = new float[frames];
                Array.Fill(input, 0.25f);
                while (Volatile.Read(ref running)) {
                    service.Engine.Process(new(input, input, left, right));
                    Thread.SpinWait(2_000); // well under real time's 1.3 ms a block, yet the 60 s loop lasts seconds
                }
            }
        ) { IsBackground = true, Name = "test-audio" };
        try {
            service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = GraphTemplates.Basic() });
            audio.Start();
            service.Handle(new() { Type = IpcTypes.Looper, Action = "record" });
            var looper = service.Engine.Looper!;
            Assert.True(SpinWait.SpinUntil(() => looper.Length > 48_000, TimeSpan.FromSeconds(10)));
            var path = Path.Combine(folder.FullName, "recording.wav");

            var replies = new List<(IpcMessage Reply, bool StillRecording)>();
            for (var i = 0; i < 5 && looper.State == LooperState.Recording; i++) {
                var reply = service.Handle(new() { Type = IpcTypes.ExportLooper, Id = 10 + i, Path = path });
                replies.Add((reply, looper.State == LooperState.Recording));
            }

            var hello = service.Handle(new() { Type = IpcTypes.Hello, Id = 2 });

            Assert.NotEmpty(replies); // the looper was recording when the first export started
            Assert.All(
                replies,
                static r => Assert.True(
                    r.StillRecording ? r.Reply.Type == IpcTypes.Error : r.Reply.Type is IpcTypes.Ok or IpcTypes.Error,
                    $"{r.Reply.Type}: {r.Reply.Error}"
                )
            );
            Assert.Equal(IpcTypes.Ok, hello.Type);
        } finally {
            Volatile.Write(ref running, false);
            audio.Join();
            folder.Delete(true);
        }
    }

    /// <summary>Runs <paramref name="callbacks" /> audio callbacks with a constant input.</summary>
    static void Pump(EngineHostService service, int callbacks) {
        var frames = service.Engine.Spec.MaxBlockSize;
        var input = new float[frames];
        Array.Fill(input, 0.25f);
        for (var i = 0; i < callbacks; i++) {
            service.Engine.Process(new(input, input, new float[frames], new float[frames]));
        }
    }

    /// <summary>Whether the host closes the connection (pushes may still arrive first).</summary>
    static async Task<bool> ClosedAsync(Stream stream, CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            while (await IpcFraming.ReadAsync(stream, timeout.Token) is { } message) {
                Assert.True(message.Type is IpcTypes.Telemetry or IpcTypes.Midi, $"got a '{message.Type}'");
            }

            return true;
        } catch (IOException) {
            return true; // closed with a push half written, or reset
        }
    }

    async Task<string> HelloOnANewConnectionAsync(CancellationToken token) {
        using var socket = await ConnectAsync(token);
        await using var stream = new NetworkStream(socket, false);
        await SendAsync(stream, """{"type":"hello","id":1}""", token);
        return (await ReplyAsync(stream, token)).Type;
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
    static Task SendAsync(Stream stream, string payload, CancellationToken token) =>
        SendAsync(stream, Encoding.UTF8.GetBytes(payload), token);

    static async Task SendAsync(Stream stream, byte[] bytes, CancellationToken token) {
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
