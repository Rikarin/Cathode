using Cathode.Contracts;
using Cathode.Server.Realtime;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>The WebSocket fan-out with a client that stalls, as a tablet on slow Wi-Fi does.</summary>
public sealed class EventHubTests {
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rig_events_are_never_dropped_behind_telemetry_frames() {
        var hub = new EventHub();
        var socket = new StalledSocket();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var serving = hub.ServeAsync(socket, stop.Token);

        // While the client is stalled: a burst of 30 Hz frames, MIDI activity and a few rig events among them.
        for (var i = 0; i < 1_000; i++) {
            hub.PublishFrame("telemetry", Frame(i), ContractsJsonContext.Default.Telemetry);
            hub.PublishActivity(new MidiActivity("midi", 0xB0, 1, i % 128), ContractsJsonContext.Default.MidiActivity);
            if (i % 100 == 0) {
                hub.Publish(new RigEvent("sceneChanged", i, Scene: i / 100 % 8), ContractsJsonContext.Default.RigEvent);
            }
        }

        socket.Resume();
        await ServerFixture.Eventually(() => socket.Has("telemetry"));
        await stop.CancelAsync();
        await serving;

        var types = socket.Sent.Select(static m => m["type"]!.GetValue<string>()).ToList();
        Assert.Equal("connected", types[0]);
        // Every event, in order, before anything lossy.
        Assert.Equal(
            Enumerable.Range(0, 10).Select(static i => (long)i * 100),
            socket.Sent.Skip(1).Take(10).Select(static m => m["revision"]!.GetValue<long>())
        );
        // The newest MIDI activity, then only the latest telemetry frame.
        Assert.Equal(EventHub.ActivityCapacity, types.Count(static t => t == "midi"));
        var telemetry = Assert.Single(socket.Sent, static m => m["type"]!.GetValue<string>() == "telemetry");
        Assert.Equal(999, telemetry["cpuP999"]!.GetValue<double>());
        Assert.Equal("telemetry", types[^1]);
    }

    [Fact]
    public async Task A_client_too_far_behind_is_told_to_resync() {
        var hub = new EventHub();
        var socket = new StalledSocket();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var serving = hub.ServeAsync(socket, stop.Token);

        for (var i = 0; i < EventHub.EventCapacity + 10; i++) {
            hub.Publish(new RigEvent("paramChanged", i), ContractsJsonContext.Default.RigEvent);
        }

        socket.Resume();
        await ServerFixture.Eventually(() => socket.Has("resync"));
        hub.Publish(new RigEvent("sceneChanged", 5_000), ContractsJsonContext.Default.RigEvent);
        await ServerFixture.Eventually(() => socket.Has("sceneChanged"));
        await stop.CancelAsync();
        await serving;

        // The marker in place of the backlog, then the stream carries on.
        Assert.Equal(
            ["connected", "resync", "sceneChanged"],
            socket.Sent.Select(static m => m["type"]!.GetValue<string>())
        );
    }

    static Telemetry Frame(int i) => new("telemetry", 0, 0, 0, i, 0, null, 120, null);

    /// <summary>A WebSocket whose sends block until <see cref="Resume" />, and that records what was sent.</summary>
    sealed class StalledSocket : WebSocket {
        readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly List<JsonNode> sent = [];

        public IReadOnlyList<JsonNode> Sent {
            get {
                lock (sent) {
                    return [..sent];
                }
            }
        }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public void Resume() => resumed.TrySetResult();

        /// <summary>Whether a message of <paramref name="type" /> has been sent.</summary>
        public bool Has(string type) => Sent.Any(m => m["type"]!.GetValue<string>() == type);

        public override void Abort() { }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        ) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        ) =>
            Task.CompletedTask;

        public override void Dispose() { }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        ) {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken
        ) {
            var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer))!;
            await resumed.Task.WaitAsync(cancellationToken);
            lock (sent) {
                sent.Add(message);
            }
        }
    }
}
