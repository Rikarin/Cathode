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
    readonly EngineHostService service;
    readonly NullBackend backend;
    readonly Timer telemetry;

    public bool IsConnected => true;

    public EngineHostService Service => service;

    public InProcessEngineLink(ProcessSpec? spec = null, BlockRegistry? registry = null) {
        var processSpec = spec ?? ProcessSpec.Default;
        // A short looper: tests and development need it to work, not to hold a minute of audio.
        service = new(processSpec, "in-process (null)", registry, looperSeconds: 5);
        backend = new((int)processSpec.SampleRate, processSpec.MaxBlockSize);
        backend.Start(service.Engine);
        telemetry = new(
            _ => TelemetryReceived?.Invoke(service.Telemetry()),
            null,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100)
        );
    }

    public event Action<HostTelemetry>? TelemetryReceived;

    public event Action<IReadOnlyList<MidiEventMessage>>? MidiReceived;

    /// <summary>Tests and development: deliver MIDI as if a device had sent it (there is no device in process).</summary>
    public void InjectMidi(IReadOnlyList<MidiEventMessage> messages) => MidiReceived?.Invoke(messages);

    public event Action? Disconnected;

    public Task<IpcMessage> SendAsync(IpcMessage request, CancellationToken cancellationToken = default) {
        var reply = service.Handle(request);
        return reply.Type == IpcTypes.Error
            ? Task.FromException<IpcMessage>(new IpcException(reply.Error ?? "Engine error.", reply.Errors ?? []))
            : Task.FromResult(reply);
    }

    public async ValueTask DisposeAsync() {
        await telemetry.DisposeAsync().ConfigureAwait(false);
        backend.Dispose();
        service.Dispose();
        Disconnected?.Invoke();
    }
}
