using System.Threading.Channels;
using Cathode.Contracts;
using Cathode.Engine.Graph;
using Cathode.Engine.Telemetry;
using Cathode.EngineHost;
using Cathode.Ipc;
using Cathode.Server.Realtime;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Engine;

/// <summary>
///     Keeps a link to the engine host: connects (and reconnects), sends work through one ordered queue, raises
///     <see cref="Connected" /> so the rig can resync, and forwards telemetry to WebSocket clients.
/// </summary>
/// <remarks>
///     Meters and CPU go to clients at about 30 Hz from the host's shared-memory region (<see cref="TelemetryRegion" />),
///     with the tuner from the slower IPC telemetry. A host without a region falls back to IPC telemetry alone.
/// </remarks>
public sealed partial class EngineConnection(
    IOptions<ServerOptions> options,
    EventHub hub,
    ILogger<EngineConnection> logger)
    : BackgroundService, IEngineSync {
    static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);

    readonly Channel<Work> queue = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    IEngineLink? link;
    HostInfo? info;
    HostTelemetry? telemetry;
    TelemetryMonitor? monitor;
    string? lastError;

    /// <summary>The live link, for diagnostics and tests (an <see cref="InProcessEngineLink" /> in in-process mode).</summary>
    public IEngineLink? Link => link;

    public bool IsConnected => info is not null && link?.IsConnected == true;

    /// <summary>True while meters come from the host's shared-memory region rather than IPC telemetry.</summary>
    public bool HasSharedTelemetry => monitor is not null;

    /// <summary>Raised after every (re)connection, once the host has answered <c>hello</c>.</summary>
    public event Func<Task>? Connected;

    public EngineStatus Status() {
        var t = telemetry;
        var i = info;
        return new(
            IsConnected,
            options.Value.Engine.Mode,
            i?.Backend,
            i?.SampleRate ?? 0,
            i?.BufferFrames ?? 0,
            t?.CpuP999 ?? 0,
            t?.CpuMax ?? 0,
            t?.Overloads ?? 0,
            lastError,
            t?.DriverOverloads ?? 0,
            t?.NativeP999 ?? 0,
            t?.NativeMax ?? 0,
            t?.Gen0 ?? 0,
            t?.Gen1 ?? 0,
            t?.Gen2 ?? 0,
            t?.GcPauseMs ?? 0,
            t?.AllocatedBytes ?? 0
        );
    }

    public Task LoadGraphAsync(GraphDefinition graph, int generation) {
        if (!IsConnected) {
            return Task.CompletedTask;
        }

        var reply = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(new() { Type = IpcTypes.LoadGraph, Generation = generation, Graph = graph }, reply));
        return reply.Task;
    }

    public void SetParams(int generation, IReadOnlyList<ParamValue> values) {
        if (IsConnected && values.Count > 0) {
            queue.Writer.TryWrite(
                new(new() { Type = IpcTypes.SetParams, Generation = generation, Params = [..values] }, null)
            );
        }
    }

    public void SetBypass(int generation, string block, bool bypassed) {
        if (IsConnected) {
            queue.Writer.TryWrite(
                new(
                    new() { Type = IpcTypes.SetBypass, Generation = generation, Block = block, Bypassed = bypassed },
                    null
                )
            );
        }
    }

    public void SetTuner(bool enabled) {
        if (IsConnected) {
            queue.Writer.TryWrite(new(new() { Type = IpcTypes.SetTuner, Enabled = enabled }, null));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await RunOnceAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception e) {
                // Whatever went wrong with this link (a timeout, a protocol error, a host that died mid-request), drop it
                // and reconnect. An exception escaping here would stop the background service and, with the default
                // host options, the whole server.
                lastError = e.Message;
                LinkLost(logger, e.Message);
            }

            await Task.Delay(1_000, stoppingToken).ContinueWith(static _ => { }, TaskScheduler.Default);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        await base.StopAsync(cancellationToken);
        if (link is not null) {
            await link.DisposeAsync();
        }
    }

    async Task RunOnceAsync(CancellationToken stoppingToken) {
        var engine = options.Value.Engine;
        IEngineLink current;
        if (string.Equals(engine.Mode, "inprocess", StringComparison.OrdinalIgnoreCase)) {
            current = new InProcessEngineLink();
        } else {
            var client = new IpcEngineClient(engine.SocketPath);
            await client.ConnectAsync(TimeSpan.FromDays(365), stoppingToken); // keeps retrying until the host is up
            current = client;
        }

        using var lost = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        current.Disconnected += () => lost.Cancel();
        current.TelemetryReceived += OnTelemetry;
        TelemetryRegion? mapped = null;
        Task meters = Task.CompletedTask;
        try {
            DiscardQueued("The engine host restarted.");
            link = current;
            info = (await current.SendAsync(new() { Type = IpcTypes.Hello }, stoppingToken)).Host;
            lastError = null;
            if (info is not null) {
                EngineConnected(logger, info.Backend, info.SampleRate, info.BufferFrames);
            }

            // In process the region is the host service's own; across processes, map the file the host named.
            var region = current is InProcessEngineLink local
                ? local.Service.TelemetryRegion
                : mapped = OpenRegion(info?.TelemetryPath);
            if (region is not null) {
                monitor = new(region);
                meters = PublishMetersAsync(monitor, lost.Token);
            }

            // The pump must run before the handlers: a resync enqueues a loadGraph and waits for its reply.
            var pump = PumpAsync(current, lost.Token);
            if (Connected is { } handlers) {
                foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>()) {
                    try {
                        await handler();
                    } catch (Exception e) when (e is IpcException or IOException or Rig.RigException) {
                        lastError = e.Message;
                    }
                }
            }

            await pump;
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            // the link dropped; the outer loop reconnects
        } finally {
            current.TelemetryReceived -= OnTelemetry;
            monitor = null;
            await lost.CancelAsync();
            await meters.ContinueWith(static _ => { }, TaskScheduler.Default);
            mapped?.Dispose();
            info = null;
            link = null;
            await current.DisposeAsync();
        }
    }

    TelemetryRegion? OpenRegion(string? path) {
        if (path is null) {
            return null;
        }

        try {
            return TelemetryRegion.OpenFile(path);
        } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) {
            // Meters fall back to IPC telemetry; nothing else depends on the region.
            TelemetryUnavailable(logger, path, e.Message);
            return null;
        }
    }

    async Task PublishMetersAsync(TelemetryMonitor source, CancellationToken cancellationToken) {
        using var timer = new PeriodicTimer(MeterInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken)) {
            if (source.Poll() is not { } reading) {
                continue;
            }

            hub.Publish(
                new(
                    "telemetry",
                    reading.InputPeak,
                    reading.OutputPeakLeft,
                    reading.OutputPeakRight,
                    reading.CpuP999,
                    reading.Overruns,
                    Tuner(telemetry)
                ),
                ContractsJsonContext.Default.Telemetry
            );
        }
    }

    static TunerInfo? Tuner(HostTelemetry? t) =>
        t?.Tuner is { } r ? new TunerInfo(r.Frequency, r.Note, r.Octave, r.Cents) : null;

    async Task PumpAsync(IEngineLink current, CancellationToken cancellationToken) {
        await foreach (var work in queue.Reader.ReadAllAsync(cancellationToken)) {
            await SendAsync(current, work, cancellationToken);
        }
    }

    async Task SendAsync(IEngineLink current, Work work, CancellationToken cancellationToken) {
        try {
            var reply = await current.SendAsync(work.Message, cancellationToken);
            work.Reply?.TrySetResult(reply);
        } catch (IpcException e) {
            lastError = e.Errors.Count > 0 ? string.Join("; ", e.Errors) : e.Message;
            if (work.Reply is null) {
                EngineRejected(logger, work.Message.Type, lastError);
            }

            work.Reply?.TrySetException(e);
        }
    }

    void DiscardQueued(string reason) {
        while (queue.Reader.TryRead(out var work)) {
            work.Reply?.TrySetException(new IOException(reason));
        }
    }

    void OnTelemetry(HostTelemetry t) {
        telemetry = t;
        if (monitor is null) {
            hub.Publish(
                new("telemetry", t.InputPeak, t.OutputPeakLeft, t.OutputPeakRight, t.CpuP999, t.Overloads, Tuner(t)),
                ContractsJsonContext.Default.Telemetry
            );
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine connected: {Backend}, {Rate} Hz, {Frames} frames")]
    static partial void EngineConnected(ILogger logger, string backend, double rate, int frames);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine link lost: {Message}")]
    static partial void LinkLost(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shared telemetry '{Path}' unavailable, using IPC: {Message}")]
    static partial void TelemetryUnavailable(ILogger logger, string path, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Engine rejected {Type}: {Error}")]
    static partial void EngineRejected(ILogger logger, string type, string error);

    sealed record Work(IpcMessage Message, TaskCompletionSource<IpcMessage>? Reply);
}
