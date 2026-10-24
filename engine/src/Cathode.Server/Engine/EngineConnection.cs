using System.Threading.Channels;
using Cathode.Contracts;
using Cathode.Engine.Graph;
using Cathode.Engine.Telemetry;
using Cathode.EngineHost;
using Cathode.Ipc;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
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
    ILogger<EngineConnection> logger
)
    : BackgroundService, IEngineSync {
    /// <summary>Scope window: 2048 samples (43 ms at 48 kHz), sent as 512 points.</summary>
    const int ScopeSamples = 2048;

    const int ScopeDecimation = 4;
    static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);

    readonly Channel<Work> queue = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    HostInfo? info;
    TelemetryMonitor? monitor;

    /// <summary>The block id of each slot of the graph last loaded, for block meters.</summary>
    SlotMap slots = new(-1, []);
    string? lastError;
    volatile bool scopeEnabled;

    /// <summary>The live link, for diagnostics and tests (an <see cref="InProcessEngineLink" /> in in-process mode).</summary>
    public IEngineLink? Link { get; private set; }

    public bool IsConnected => info is not null && Link?.IsConnected == true;

    /// <summary>Whether WebSocket clients get scope frames. Off by default: they are the largest messages by far.</summary>
    public bool ScopeEnabled {
        get => scopeEnabled;
        set => scopeEnabled = value;
    }

    /// <summary>True while meters come from the host's shared-memory region rather than IPC telemetry.</summary>
    public bool HasSharedTelemetry => monitor is not null;

    /// <summary>The engine's tempo and looper as of the last IPC telemetry.</summary>
    public HostTelemetry? LastTelemetry { get; private set; }

    public EngineStatus Status() {
        var t = LastTelemetry;
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

    public async Task LoadGraphAsync(GraphDefinition graph, int generation) {
        if (!IsConnected) {
            return;
        }

        var reply = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(new() { Type = IpcTypes.LoadGraph, Generation = generation, Graph = graph }, reply));
        var answer = await reply.Task;
        if (answer.SlotIds is { } ids) {
            Volatile.Write(ref slots, new(generation, [..ids]));
        }
    }

    public void SetModifier(int generation, string block, string param, bool enabled, float baseValue) {
        if (IsConnected) {
            queue.Writer.TryWrite(
                new(
                    new() {
                        Type = IpcTypes.SetModifier,
                        Generation = generation,
                        Block = block,
                        Param = param,
                        Enabled = enabled,
                        Params = [new(block, param, baseValue)]
                    },
                    null
                )
            );
        }
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

    public void Tap(long? sampleTime = null) => Enqueue(new() { Type = IpcTypes.Tap, Time = sampleTime });

    public void Looper(string action) => Enqueue(new() { Type = IpcTypes.Looper, Action = action });

    public Task ExportLooperAsync(string path) {
        if (!IsConnected) {
            return Task.FromException(new IOException("The engine is not connected."));
        }

        var reply = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(new() { Type = IpcTypes.ExportLooper, Path = path }, reply));
        return reply.Task;
    }

    public void SetMidi(bool followClock) => Enqueue(new() { Type = IpcTypes.SetMidi, FollowClock = followClock });

    public void MidiOut(string? destination, IReadOnlyList<byte> bytes) =>
        Enqueue(
            new() { Type = IpcTypes.MidiOut, Destination = destination, Bytes = [..bytes.Select(static b => (int)b)] }
        );

    public void SetTuner(bool enabled) {
        if (IsConnected) {
            queue.Writer.TryWrite(new(new() { Type = IpcTypes.SetTuner, Enabled = enabled }, null));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        await base.StopAsync(cancellationToken);
        if (Link is not null) {
            await Link.DisposeAsync();
        }
    }

    void Enqueue(IpcMessage message) {
        if (IsConnected) {
            queue.Writer.TryWrite(new(message, null));
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
        current.MidiReceived += OnMidi;
        TelemetryRegion? mapped = null;
        var meters = Task.CompletedTask;
        try {
            DiscardQueued("The engine host restarted.");
            Link = current;
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
                    } catch (Exception e) when (e is IpcException or IOException or RigException) {
                        lastError = e.Message;
                    }
                }
            }

            await pump;
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            // the link dropped; the outer loop reconnects
        } finally {
            current.TelemetryReceived -= OnTelemetry;
            current.MidiReceived -= OnMidi;
            monitor = null;
            await lost.CancelAsync();
            await meters.ContinueWith(static _ => { }, TaskScheduler.Default);
            mapped?.Dispose();
            info = null;
            Link = null;
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
        var window = new float[ScopeSamples];
        while (await timer.WaitForNextTickAsync(cancellationToken)) {
            if (scopeEnabled && source.Region.ReadScope(window)) {
                var points = new float[ScopeSamples / ScopeDecimation];
                for (var i = 0; i < points.Length; i++) {
                    points[i] = window[i * ScopeDecimation];
                }

                hub.Publish(
                    new("scope", source.Region.SampleRate / ScopeDecimation, points),
                    ContractsJsonContext.Default.ScopeFrame
                );
            }

            PublishRta(source.Region);
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
                    Tuner(LastTelemetry),
                    LastTelemetry?.Tempo ?? 120,
                    Looper(LastTelemetry),
                    BlockMeters(source),
                    source.LoudnessMomentary,
                    source.LoudnessShortTerm
                ),
                ContractsJsonContext.Default.Telemetry
            );
        }
    }

    /// <summary>Each block's peak by id, when the meters belong to the graph whose slot ids the host reported.</summary>
    Dictionary<string, float>? BlockMeters(TelemetryMonitor source) {
        var (generation, ids) = Volatile.Read(ref slots);
        if (source.BlockGeneration != generation || generation < 0) {
            return null;
        }

        var peaks = source.BlockPeaks;
        var meters = new Dictionary<string, float>(Math.Min(peaks.Length, ids.Length));
        for (var i = 0; i < peaks.Length && i < ids.Length; i++) {
            if (!ids[i].StartsWith("shunt", StringComparison.Ordinal)) {
                meters[ids[i]] = peaks[i];
            }
        }

        return meters;
    }

    static LooperInfo? Looper(HostTelemetry? t) =>
        t?.Looper is { } l ? new(l.State, l.LengthSeconds, l.PositionSeconds, l.Reversed, l.HalfSpeed) : null;

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

    void OnMidi(IReadOnlyList<MidiEventMessage> messages) => MidiReceived?.Invoke(messages);

    void OnTelemetry(HostTelemetry t) {
        LastTelemetry = t;
        TelemetryUpdated?.Invoke(t);
        if (monitor is null) {
            hub.Publish(
                new(
                    "telemetry",
                    t.InputPeak,
                    t.OutputPeakLeft,
                    t.OutputPeakRight,
                    t.CpuP999,
                    t.Overloads,
                    Tuner(t),
                    t.Tempo,
                    Looper(t)
                ),
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

    /// <summary>Raised after every (re)connection, once the host has answered <c>hello</c>.</summary>
    public event Func<Task>? Connected;

    /// <summary>MIDI from the host's devices (or injected in process).</summary>
    public event Action<IReadOnlyList<MidiEventMessage>>? MidiReceived;

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

    /// <summary>Raised for every IPC telemetry frame (about ten a second).</summary>
    public event Action<HostTelemetry>? TelemetryUpdated;

    sealed record Work(IpcMessage Message, TaskCompletionSource<IpcMessage>? Reply);

    sealed record SlotMap(int Generation, string[] Ids);
}
