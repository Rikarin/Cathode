using System.Net.Sockets;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     Listens on a Unix domain socket for the Server (one client at a time), answers requests through
///     <see cref="EngineHostService" /> and pushes telemetry about 30 times a second.
/// </summary>
public sealed class EngineHostServer(EngineHostService service, string socketPath) {
    static readonly TimeSpan TelemetryInterval = TimeSpan.FromMilliseconds(33);

    public async Task RunAsync(CancellationToken cancellationToken) {
        if (File.Exists(socketPath)) {
            File.Delete(socketPath); // a stale socket from a previous run
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                using var client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                await ServeAsync(client, cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
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
        try {
            while (await reader.ReadAsync(stream, connection.Token).ConfigureAwait(false) is { } request) {
                var reply = service.Handle(request);
                await writeLock.WaitAsync(connection.Token).ConfigureAwait(false);
                try {
                    await writer.WriteAsync(stream, reply, connection.Token).ConfigureAwait(false);
                } finally {
                    writeLock.Release();
                }
            }
        } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or InvalidDataException) {
        } finally {
            await connection.CancelAsync().ConfigureAwait(false);
            try {
                await telemetry.ConfigureAwait(false);
            } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) {
            }
        }
    }

    async Task PushTelemetryAsync(Stream stream, IpcFrameWriter writer, SemaphoreSlim writeLock, CancellationToken cancellationToken) {
        using var timer = new PeriodicTimer(TelemetryInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
            var message = new IpcMessage { Type = IpcTypes.Telemetry, Telemetry = service.Telemetry() };
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await writer.WriteAsync(stream, message, cancellationToken).ConfigureAwait(false);
            } finally {
                writeLock.Release();
            }
        }
    }
}
