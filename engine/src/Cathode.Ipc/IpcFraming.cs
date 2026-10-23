using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cathode.Ipc;

/// <summary>Frames are a 4-byte little-endian length followed by that many bytes of UTF-8 JSON.</summary>
public static class IpcFraming {
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    public static async Task WriteAsync(
        Stream stream,
        IpcMessage message,
        CancellationToken cancellationToken = default
    ) {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, IpcJsonContext.Default.IpcMessage);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns null when the peer closed the connection cleanly.</summary>
    public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default) {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, header.Length, cancellationToken).ConfigureAwait(false)) {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxFrameBytes) {
            throw new InvalidDataException($"IPC frame of {length} bytes is out of range.");
        }

        var payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, payload, payload.Length, cancellationToken).ConfigureAwait(false)) {
            throw new EndOfStreamException("IPC connection closed in the middle of a frame.");
        }

        return JsonSerializer.Deserialize(payload, IpcJsonContext.Default.IpcMessage)
            ?? throw new InvalidDataException("Empty IPC message.");
    }

    /// <summary>Fills the first <paramref name="count" /> bytes; false if the stream ended before the first byte.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    internal static async ValueTask<bool> ReadExactlyOrEndAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken
    ) {
        var read = 0;
        while (read < count) {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0) {
                return read == 0
                    ? false
                    : throw new EndOfStreamException("IPC connection closed in the middle of a frame.");
            }

            read += n;
        }

        return true;
    }
}
