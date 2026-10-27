using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Cathode.Server.Realtime;

/// <summary>
///     Fans rig events and telemetry out to every connected WebSocket client. A slow client never slows the rig or
///     the other clients, and its telemetry never pushes out its events.
/// </summary>
/// <remarks>
///     <para>
///         The first message on every socket is <c>{"type":"connected"}</c>, sent once the client is registered. A
///         client waits for it, then reads <c>GET /api/v1/live</c>: from that point on it sees every change, with no
///         gap between its snapshot and the event stream.
///     </para>
///     <para>
///         Each client has three outboxes, sent in this order of priority: events (<see cref="Publish{T}" />, in order,
///         never dropped silently: a client that falls <see cref="EventCapacity" /> behind gets
///         <c>{"type":"resync"}</c> in their place and reads its state again, as after connecting); MIDI activity
///         (<see cref="PublishActivity{T}" />, the monitor's, the newest <see cref="ActivityCapacity" />); and frames
///         (<see cref="PublishFrame{T}" />: telemetry, scope, RTA), only the latest of each kind.
///     </para>
/// </remarks>
public sealed class EventHub {
    /// <summary>Events a client may fall behind by before it is told to resync.</summary>
    public static readonly int EventCapacity = 1024;

    /// <summary>MIDI monitor messages kept for a slow client; older ones are dropped.</summary>
    public static readonly int ActivityCapacity = 64;

    const string ConnectedMessage = """{"type":"connected"}""";
    const string ResyncMessage = """{"type":"resync"}""";

    readonly ConcurrentDictionary<Guid, Client> clients = new();

    public int ClientCount => clients.Count;

    /// <summary>A rig event: every client gets every one, in order (or a resync in their place).</summary>
    public void Publish<T>(T payload, JsonTypeInfo<T> typeInfo) {
        if (!clients.IsEmpty) {
            var json = JsonSerializer.Serialize(payload, typeInfo);
            foreach (var client in clients.Values) {
                client.Event(json);
            }
        }
    }

    /// <summary>MIDI monitor activity: lossy for a slow client, but never at the expense of events.</summary>
    public void PublishActivity<T>(T payload, JsonTypeInfo<T> typeInfo) {
        if (!clients.IsEmpty) {
            var json = JsonSerializer.Serialize(payload, typeInfo);
            foreach (var client in clients.Values) {
                client.Activity(json);
            }
        }
    }

    /// <summary>
    ///     A telemetry frame of <paramref name="kind" /> (<c>telemetry</c>, <c>scope</c>, <c>rta</c>): a client that
    ///     has not sent the previous one yet gets this one in its place.
    /// </summary>
    public void PublishFrame<T>(string kind, T payload, JsonTypeInfo<T> typeInfo) {
        if (!clients.IsEmpty) {
            var json = JsonSerializer.Serialize(payload, typeInfo);
            foreach (var client in clients.Values) {
                client.Frame(kind, json);
            }
        }
    }

    /// <summary>Serves one WebSocket until either side closes it.</summary>
    public async Task ServeAsync(WebSocket socket, CancellationToken cancellationToken) {
        var id = Guid.NewGuid();
        var client = new Client();
        client.Event(ConnectedMessage);
        clients[id] = client;
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receive = DrainIncomingAsync(socket, closed);
        try {
            while (true) {
                await client.Ready.WaitAsync(closed.Token);
                while (client.Next() is { } json) {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, closed.Token);
                }
            }
        } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { } finally {
            clients.TryRemove(id, out _);
            await receive;
            client.Ready.Dispose();
            try {
                if (socket.State == WebSocketState.CloseReceived) {
                    // Complete the client's close handshake; otherwise it sees an abnormal closure (1006).
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                } else if (socket.State == WebSocketState.Open) {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            } catch (WebSocketException) {
                // the client is already gone
            }
        }
    }

    /// <summary>Clients only listen; reading is how a close is noticed.</summary>
    static async Task DrainIncomingAsync(WebSocket socket, CancellationTokenSource closed) {
        var buffer = new byte[1_024];
        try {
            while (socket.State == WebSocketState.Open) {
                var result = await socket.ReceiveAsync(buffer, closed.Token);
                if (result.MessageType == WebSocketMessageType.Close) {
                    break;
                }
            }
        } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { } finally {
            await closed.CancelAsync();
        }
    }

    /// <summary>One client's outboxes. Writers never wait: the lock only guards a few queue operations.</summary>
    sealed class Client {
        readonly Lock gate = new();
        readonly Queue<string> events = new();
        readonly Queue<string> activity = new();

        /// <summary>The latest frame of each kind not yet sent, in the order the kinds first arrived.</summary>
        readonly List<KeyValuePair<string, string>> frames = [];

        /// <summary>Released when something is queued; the sender then drains everything.</summary>
        public SemaphoreSlim Ready { get; } = new(0, 1);

        public void Event(string json) {
            lock (gate) {
                if (events.Count >= EventCapacity) {
                    // Too far behind to catch up message by message: the client reads its state again instead.
                    events.Clear();
                    events.Enqueue(ResyncMessage);
                } else if (events.Count != 1 || events.Peek() != ResyncMessage) {
                    events.Enqueue(json); // after a resync marker, the client's fresh read covers what follows
                }
            }

            Signal();
        }

        public void Activity(string json) {
            lock (gate) {
                if (activity.Count >= ActivityCapacity) {
                    activity.Dequeue();
                }

                activity.Enqueue(json);
            }

            Signal();
        }

        public void Frame(string kind, string json) {
            lock (gate) {
                var index = frames.FindIndex(f => f.Key == kind);
                if (index >= 0) {
                    frames[index] = new(kind, json);
                } else {
                    frames.Add(new(kind, json));
                }
            }

            Signal();
        }

        /// <summary>The next message to send, events first, or null when every outbox is empty.</summary>
        public string? Next() {
            lock (gate) {
                if (events.TryDequeue(out var json) || activity.TryDequeue(out json)) {
                    return json;
                }

                if (frames.Count > 0) {
                    json = frames[0].Value;
                    frames.RemoveAt(0);
                    return json;
                }

                return null;
            }
        }

        void Signal() {
            try {
                if (Ready.CurrentCount == 0) {
                    Ready.Release();
                }
            } catch (SemaphoreFullException) {
                // already signalled
            } catch (ObjectDisposedException) {
                // the client has gone
            }
        }
    }
}
