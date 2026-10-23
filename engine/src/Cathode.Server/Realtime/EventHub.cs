using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Cathode.Server.Realtime;

/// <summary>
///     Fans rig events and telemetry out to every connected WebSocket client. Each client has a small bounded queue
///     that drops the oldest message, so a slow client never slows the rig or the other clients.
/// </summary>
/// <remarks>
///     The first message on every socket is <c>{"type":"connected"}</c>, sent once the client is registered. A client
///     waits for it, then reads <c>GET /api/v1/live</c>: from that point on it sees every change, with no gap between
///     its snapshot and the event stream.
/// </remarks>
public sealed class EventHub {
    const string ConnectedMessage = """{"type":"connected"}""";

    readonly ConcurrentDictionary<Guid, Channel<string>> clients = new();

    public int ClientCount => clients.Count;

    public void Publish<T>(T payload, JsonTypeInfo<T> typeInfo) {
        if (clients.IsEmpty) {
            return;
        }

        var json = JsonSerializer.Serialize(payload, typeInfo);
        foreach (var client in clients.Values) {
            client.Writer.TryWrite(json);
        }
    }

    /// <summary>Serves one WebSocket until either side closes it.</summary>
    public async Task ServeAsync(WebSocket socket, CancellationToken cancellationToken) {
        var id = Guid.NewGuid();
        var outbox = Channel.CreateBounded<string>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
        );
        clients[id] = outbox;
        outbox.Writer.TryWrite(ConnectedMessage);
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receive = DrainIncomingAsync(socket, closed);
        try {
            await foreach (var json in outbox.Reader.ReadAllAsync(closed.Token)) {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, closed.Token);
            }
        } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { } finally {
            clients.TryRemove(id, out _);
            await receive;
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
}
