using Cathode.Audio;
using Cathode.Dsp;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     The engine host in the same process, driven by a <see cref="NullBackend" /> (real-time paced, no device). For
///     functional tests and development without audio. Never use it for timing or soak measurements: an in-process
///     engine shares the Server's GC (docs/adr/0001-process-model.md).
/// </summary>
public sealed class InProcessEngineLink : IEngineLink {
    readonly NullBackend backend;
    readonly Timer telemetry;

    public bool IsConnected => true;

    public EngineHostService Service { get; }

    public InProcessEngineLink(ProcessSpec? spec = null, BlockRegistry? registry = null) {
        var processSpec = spec ?? ProcessSpec.Default;
        // A short looper: tests and development need it to work, not to hold a minute of audio.
        Service = new(processSpec, "in-process (null)", registry, looperSeconds: 5);
        // Normal priority: a server's engine shares the process with the server itself (and in tests with other
        // servers); at the highest priority, engines that fall behind on a busy machine starve the server's threads.
        backend = new((int)processSpec.SampleRate, processSpec.MaxBlockSize, priority: ThreadPriority.Normal);
        backend.Start(Service.Engine);
        telemetry = new(
            _ => TelemetryReceived?.Invoke(Service.Telemetry()),
            null,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100)
        );
    }

    /// <summary>Tests and development: deliver MIDI as if a device had sent it (there is no device in process).</summary>
    public void InjectMidi(IReadOnlyList<MidiEventMessage> messages) => MidiReceived?.Invoke(messages);

    public Task<IpcMessage> SendAsync(IpcMessage request, CancellationToken cancellationToken = default) {
        var reply = Service.Handle(request);
        return reply.Type == IpcTypes.Error
            ? Task.FromException<IpcMessage>(new IpcException(reply.Error ?? "Engine error.", reply.Errors ?? []))
            : Task.FromResult(reply);
    }

    public async ValueTask DisposeAsync() {
        await telemetry.DisposeAsync().ConfigureAwait(false);
        backend.Dispose();
        Service.Dispose();
        Disconnected?.Invoke();
    }

    public event Action<HostTelemetry>? TelemetryReceived;

    public event Action<IReadOnlyList<MidiEventMessage>>? MidiReceived;

    public event Action? Disconnected;
}
