namespace Cathode.Ipc;

/// <summary>
///     A connection to an engine host, over IPC (<see cref="IpcEngineClient" />) or in the same process (tests,
///     development without audio).
/// </summary>
public interface IEngineLink : IAsyncDisposable {
    bool IsConnected { get; }

    /// <summary>
    ///     Sends a request and waits for its reply. Throws <see cref="IpcException" /> on an error reply.
    /// </summary>
    Task<IpcMessage> SendAsync(IpcMessage request, CancellationToken cancellationToken = default);

    event Action<HostTelemetry>? TelemetryReceived;

    /// <summary>MIDI input from the host's devices, timed on the engine's sample clock.</summary>
    event Action<IReadOnlyList<MidiEventMessage>>? MidiReceived;

    event Action? Disconnected;
}
