using Cathode.Ipc;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Cathode.EngineHost;

/// <summary>
///     Listens on a Unix domain socket for the Server (one client at a time), answers requests through
///     <see cref="EngineHostService" /> and pushes telemetry ten times a second. Meters and CPU at display rate come
///     from the shared-memory region instead (<see cref="EngineHostService.TelemetryRegion" />); the pushed messages
///     carry the tuner, GC counts and device windows. Parameter and bypass commands, replies and telemetry pushes
///     allocate nothing, so the process stays collection-free (docs/adr/0001-process-model.md).
/// </summary>
public sealed class EngineHostServer(EngineHostService service, string socketPath, Action<string>? log = null) {
    static readonly TimeSpan TelemetryInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>MIDI waiting for the Server; when none is connected the oldest batches are dropped.</summary>
    readonly Channel<List<MidiEventMessage>> midi =
        Channel.CreateBounded<List<MidiEventMessage>>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
        );

    public async Task RunAsync(CancellationToken cancellationToken) {
        if (File.Exists(socketPath)) {
            File.Delete(socketPath); // a stale socket from a previous run
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        service.StartMidi(batch => midi.Writer.TryWrite(batch));
        try {
            while (!cancellationToken.IsCancellationRequested) {
                using var client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                log?.Invoke("client connected");
                await ServeAsync(client, cancellationToken).ConfigureAwait(false);
                log?.Invoke("client disconnected");
            }
        } catch (OperationCanceledException) {
            // The host is shutting down: stop accepting clients and remove the socket.
        } finally {
            File.Delete(socketPath);
        }
    }

    async Task ServeAsync(Socket client, CancellationToken cancellationToken) {
        await using var stream = new NetworkStream(client, false);
        using var writeLock = new SemaphoreSlim(1, 1);
        using var writer = new IpcFrameWriter(); // shared by replies and telemetry, always under writeLock
        var reader = new IpcFrameReader();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var telemetry = PushTelemetryAsync(stream, writer, writeLock, connection.Token);
        var midiPush = PushMidiAsync(stream, writer, writeLock, connection.Token);
        try {
            while (await reader.ReadFrameAsync(stream, connection.Token).ConfigureAwait(false)) {
                if (!service.TryHandle(reader.Frame, out var reply)) {
                    reply = service.Handle(reader.Deserialize());
                }

                await writeLock.WaitAsync(connection.Token).ConfigureAwait(false);
                try {
                    await writer.WriteAsync(stream, reply, connection.Token).ConfigureAwait(false);
                } finally {
                    writeLock.Release();
                }
            }
        } catch (Exception e) when (e is IOException
                                        or SocketException
                                        or OperationCanceledException
                                        or InvalidDataException) {
            log?.Invoke($"connection ended: {e.GetType().Name}: {e.Message}");
        } finally {
            await connection.CancelAsync().ConfigureAwait(false);
            foreach (var task in new[] { telemetry, midiPush }) {
                try {
                    await task.ConfigureAwait(false);
                } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
            }
        }
    }

    /// <summary>
    ///     Forwards MIDI as soon as it arrives: a footswitch should not wait for the next telemetry tick.
    /// </summary>
    async Task PushMidiAsync(
        Stream stream,
        IpcFrameWriter writer,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken
    ) {
        await foreach (var batch in midi.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await writer.WriteAsync(stream, new() { Type = IpcTypes.Midi, Midi = batch }, cancellationToken)
                    .ConfigureAwait(false);
            } finally {
                writeLock.Release();
            }
        }
    }

    async Task PushTelemetryAsync(
        Stream stream,
        IpcFrameWriter writer,
        SemaphoreSlim writeLock,
        CancellationToken cancellationToken
    ) {
        using var timer = new PeriodicTimer(TelemetryInterval);
        var telemetry = new HostTelemetry();
        var message = new IpcMessage { Type = IpcTypes.Telemetry, Telemetry = telemetry };
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
            service.Telemetry(telemetry);
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await writer.WriteAsync(stream, message, cancellationToken).ConfigureAwait(false);
            } finally {
                writeLock.Release();
            }
        }
    }
}
