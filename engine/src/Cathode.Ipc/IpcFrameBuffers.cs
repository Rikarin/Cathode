using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cathode.Ipc;

/// <summary>
///     Reusable framing for one connection on the host side, where every allocation counts towards the next GC
///     (docs/adr/0001-process-model.md). Writes serialize straight into a reused buffer; reads reuse their header and
///     payload buffers, and both pool their async state, so a frame that waits on the socket allocates nothing either.
///     Not thread-safe: use one writer under the connection's write lock and one reader per read loop. Same wire
///     format as <see cref="IpcFraming" />.
/// </summary>
public sealed class IpcFrameWriter : IDisposable {
    readonly ArrayBufferWriter<byte> buffer = new(4_096);
    readonly Utf8JsonWriter json;

    public IpcFrameWriter() {
        json = new(buffer);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    public async ValueTask WriteAsync(
        Stream stream,
        IpcMessage message,
        CancellationToken cancellationToken = default
    ) {
        buffer.ResetWrittenCount();
        buffer.Advance(4); // room for the length prefix
        json.Reset(buffer);
        JsonSerializer.Serialize(json, message, IpcJsonContext.Default.IpcMessage);
        json.Flush();
        var frame = buffer.WrittenMemory;
        BinaryPrimitives.WriteInt32LittleEndian(MemoryMarshal.AsMemory(frame).Span, frame.Length - 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => json.Dispose();
}

/// <summary>
///     The reading half of <see cref="IpcFrameWriter" />: header and payload buffers are reused across frames.
/// </summary>
public sealed class IpcFrameReader {
    readonly byte[] header = new byte[4];
    byte[] payload = new byte[4_096];
    int length;

    /// <summary>
    ///     The UTF-8 JSON of the frame last read by <see cref="ReadFrameAsync" />; valid until the next read.
    /// </summary>
    public ReadOnlySpan<byte> Frame => payload.AsSpan(0, length);

    /// <summary>Returns null when the peer closed the connection cleanly.</summary>
    public async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false) ? Deserialize() : null;

    /// <summary>
    ///     Reads the next frame into <see cref="Frame" /> without deserializing it. Returns false when the peer closed
    ///     the connection cleanly.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<bool> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default) {
        if (!await IpcFraming.ReadExactlyOrEndAsync(stream, header, header.Length, cancellationToken)
                .ConfigureAwait(false)) {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > IpcFraming.MaxFrameBytes) {
            throw new InvalidDataException($"IPC frame of {length} bytes is out of range.");
        }

        if (payload.Length < length) {
            payload = new byte[Math.Max(length, payload.Length * 2)];
        }

        if (!await IpcFraming.ReadExactlyOrEndAsync(stream, payload, length, cancellationToken).ConfigureAwait(false)) {
            throw new EndOfStreamException("IPC connection closed in the middle of a frame.");
        }

        this.length = length;
        return true;
    }

    /// <summary>Deserializes <see cref="Frame" />.</summary>
    public IpcMessage Deserialize() =>
        JsonSerializer.Deserialize(Frame, IpcJsonContext.Default.IpcMessage)
        ?? throw new InvalidDataException("Empty IPC message.");
}
