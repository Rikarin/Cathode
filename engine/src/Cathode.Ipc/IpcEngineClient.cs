using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace Cathode.Ipc;

/// <summary>
///     Server side of the IPC link: connects to the Host's Unix domain socket (retrying until it appears), correlates
///     replies with requests, and raises <see cref="TelemetryReceived" /> for pushed telemetry.
/// </summary>
[SuppressMessage(
    "Skala",
    "SK3502",
    Justification = "False positive: DisposeAsync disposes both, and IAsyncDisposable comes through IEngineLink."
)]
public sealed class IpcEngineClient(string socketPath) : IEngineLink {
    readonly ConcurrentDictionary<long, TaskCompletionSource<IpcMessage>> pending = new();
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly CancellationTokenSource stop = new();
    Socket? socket;
    NetworkStream? stream;
    Task? readLoop;
    long nextId;

    public bool IsConnected => stream is not null;

    /// <summary>Connects, retrying every 100 ms until <paramref name="timeout" />.</summary>
    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default) {
        var deadline = DateTime.UtcNow + timeout;
        while (true) {
            var candidate = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try {
                await candidate.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken)
                    .ConfigureAwait(false);
                socket = candidate;
                var connected = new NetworkStream(candidate, false);
                stream = connected;
                readLoop = Task.Run(() => ReadLoopAsync(connected, stop.Token), CancellationToken.None);
                return;
            } catch (SocketException) when (DateTime.UtcNow < deadline) {
                candidate.Dispose();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            } catch {
                candidate.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IpcMessage> SendAsync(IpcMessage request, CancellationToken cancellationToken = default) {
        var connected = stream ?? throw new InvalidOperationException("Not connected to the engine host.");
        request.Id = Interlocked.Increment(ref nextId);
        var reply = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[request.Id] = reply;
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await IpcFraming.WriteAsync(connected, request, cancellationToken).ConfigureAwait(false);
        } finally {
            writeLock.Release();
        }

        var message = await reply.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return message.Type == IpcTypes.Error
            ? throw new IpcException(message.Error ?? "Engine error.", message.Errors ?? [])
            : message;
    }

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync().ConfigureAwait(false);
        socket?.Dispose();
        if (readLoop is not null) {
            try {
                await readLoop.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // The read loop ends by cancellation, which is what stop just asked of it.
            }
        }

        stream?.Dispose();
        stop.Dispose();
        writeLock.Dispose();
    }

    async Task ReadLoopAsync(NetworkStream connected, CancellationToken cancellationToken) {
        try {
            while (await IpcFraming.ReadAsync(connected, cancellationToken).ConfigureAwait(false) is { } message) {
                if (message.Type == IpcTypes.Telemetry && message.Telemetry is { } telemetry) {
                    Raise(TelemetryReceived, telemetry);
                } else if (message.Type == IpcTypes.Midi && message.Midi is { } midi) {
                    Raise(MidiReceived, midi);
                } else if (pending.TryRemove(message.Id, out var reply)) {
                    reply.TrySetResult(message);
                }
            }
        } catch (Exception e) when (e is IOException
                                        or SocketException
                                        or ObjectDisposedException
                                        or OperationCanceledException) { } finally {
            stream = null;
            foreach (var reply in pending.Values) {
                reply.TrySetException(new IOException("Engine host disconnected."));
            }

            pending.Clear();
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    ///     Raises a pushed message's event. A handler's failure is the handler's: it must not end the read loop, which
    ///     would count it as a lost link (every pending reply failed, a reconnect and a full resync).
    /// </summary>
    void Raise<T>(Action<T>? handlers, T value) {
        if (handlers is null) {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>()) {
            try {
                handler(value);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                HandlerFailed?.Invoke(e);
            }
        }
    }

    public event Action<HostTelemetry>? TelemetryReceived;

    /// <summary>A telemetry or MIDI handler threw; the link carries on.</summary>
    public event Action<Exception>? HandlerFailed;

    public event Action<IReadOnlyList<MidiEventMessage>>? MidiReceived;

    public event Action? Disconnected;
}

public sealed class IpcException : Exception {
    public IReadOnlyList<string> Errors { get; }

    public IpcException() : this("Engine error.", []) { }

    public IpcException(string message) : this(message, []) { }

    public IpcException(string message, Exception innerException) : base(message, innerException) {
        Errors = [];
    }

    public IpcException(string message, IReadOnlyList<string> errors) : base(message) {
        Errors = errors;
    }
}
