using Cathode.Ipc;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace Cathode.Server.Tests;

/// <summary>
///     A stand-in for cathode-host on a Unix domain socket: answers every request with <c>ok</c> (<c>hello</c> with a
///     host description), records what it receives, and can drop the connection instead of answering, as a host that
///     dies mid-request does.
/// </summary>
[SuppressMessage("Skala", "SK3502", Justification = "False positive: DisposeAsync disposes all three.")]
sealed class FakeHost : IAsyncDisposable {
    readonly CancellationTokenSource stop = new();
    readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    readonly Task accepting;
    Socket? current;
    int connections;

    public FakeHost(string socketPath) {
        SocketPath = socketPath;
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        accepting = AcceptAsync();
    }

    public string SocketPath { get; }

    /// <summary>Every request received, in order, across connections.</summary>
    public ConcurrentQueue<IpcMessage> Received { get; } = new();

    /// <summary>How many times the Server has connected.</summary>
    public int Connections => Volatile.Read(ref connections);

    /// <summary>When it returns true for a request, the connection is closed instead of answering it (once).</summary>
    public Func<IpcMessage, bool>? DieOn { get; set; }

    /// <summary>Closes the current connection, as a host that exits does.</summary>
    public void Disconnect() => current?.Dispose();

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync();
        listener.Dispose();
        current?.Dispose();
        await accepting.ContinueWith(static _ => { }, TaskScheduler.Default);
        stop.Dispose();
        File.Delete(SocketPath);
    }

    async Task AcceptAsync() {
        while (!stop.IsCancellationRequested) {
            using var client = await listener.AcceptAsync(stop.Token);
            current = client;
            Interlocked.Increment(ref connections);
            await using var stream = new NetworkStream(client, false);
            try {
                while (await IpcFraming.ReadAsync(stream, stop.Token) is { } request) {
                    Received.Enqueue(request);
                    if (DieOn is { } die && die(request)) {
                        DieOn = null;
                        break;
                    }

                    var reply = new IpcMessage { Type = IpcTypes.Ok, Id = request.Id };
                    if (request.Type == IpcTypes.Hello) {
                        reply.Host = new("test", "fake", 48_000, 128);
                    }

                    await IpcFraming.WriteAsync(stream, reply, stop.Token);
                }
            } catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) {
                // the Server dropped the link, or Disconnect closed it
            }
        }
    }
}
