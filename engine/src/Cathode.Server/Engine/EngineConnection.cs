using Cathode.Contracts;
using Cathode.Dsp.Routing;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Engine.Telemetry;
using Cathode.EngineHost;
using Cathode.Ipc;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

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

    /// <summary>
    ///     Work for the pump. Not single-reader: a request whose link dropped while it was being queued drains the
    ///     queue itself (<see cref="Submit" />), as the pump may already have stopped.
    /// </summary>
    readonly Channel<Work> queue = Channel.CreateUnbounded<Work>();

    HostInfo? info;
    TelemetryMonitor? monitor;

    /// <summary>The block and meter id of each block meter of the graph last loaded.</summary>
    MeterMap meterMap = new(-1, [], []);

    /// <summary>The block and parameter each modifier of the graph last loaded drives.</summary>
    ModifierMap modifierMap = new(-1, []);

    string? lastError;
    volatile bool scopeEnabled;

    /// <summary>True once the <see cref="Connected" /> handlers (the rig's resync) have run on the current link.</summary>
    volatile bool synced;

    /// <summary>
    ///     Whether the current link's host has been sent a graph. Until it has, graph-scoped commands are dropped: a fresh
    ///     host would reject them as stale, and the resync that follows carries the rig's whole state anyway. Read and
    ///     written only by the pump.
    /// </summary>
    bool graphSent;

    /// <summary>The live link, for diagnostics and tests (an <see cref="InProcessEngineLink" /> in in-process mode).</summary>
    public IEngineLink? Link { get; private set; }

    public bool IsConnected => info is not null && Link?.IsConnected == true;

    /// <summary>Connected, and the rig has been resent to the host since it (re)connected.</summary>
    public bool IsSynced => synced && IsConnected;

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
        // Reported as connected once the host has the rig: a client that edits it the moment it sees "connected" must not
        // reach a host that still has no graph.
        return new(
            IsSynced,
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
            t?.AllocatedBytes ?? 0,
            InputNames(i?.InputSources ?? 1),
            t?.CallbackExceptions ?? 0,
            t?.DeviceStatus ?? "ok"
        );
    }

    public async Task LoadGraphAsync(GraphDefinition graph, int generation) {
        if (!IsConnected) {
            return;
        }

        IpcMessage answer;
        try {
            answer = await Submit(new() { Type = IpcTypes.LoadGraph, Generation = generation, Graph = graph });
        } catch (IOException) {
            // The link dropped (or the host stalled) before the host answered: as if no engine were connected. The
            // rig is the authority; the resync after the next connection sends it this graph.
            return;
        }

        if (answer.SlotIds is { } ids) {
            Volatile.Write(ref meterMap, MeterMap.Of(generation, ids, answer.MeterIds));
            Volatile.Write(ref modifierMap, new(generation, answer.ModifierTargets ?? []));
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

        return Submit(new() { Type = IpcTypes.ExportLooper, Path = path });
    }

    /// <summary>The device's and the graph's latency (zeros while no engine is connected).</summary>
    public async Task<LatencyInfo> LatencyAsync() {
        var reply = await RequestAsync(new() { Type = IpcTypes.Latency });
        if (reply?.Latency is not { } l) {
            return new(false, info?.SampleRate ?? 0, info?.BufferFrames ?? 0, 0, 0, 0, [], 0, 0);
        }

        var total = l.InputLatencyFrames + l.OutputLatencyFrames + l.GraphLatencyFrames;
        return new(
            true,
            l.SampleRate,
            l.BufferFrames,
            l.InputLatencyFrames,
            l.OutputLatencyFrames,
            l.GraphLatencyFrames,
            [..l.Path.Select(static s => new LatencyStepInfo(s.Block, s.Type, s.Samples))],
            total,
            l.SampleRate > 0 ? total / l.SampleRate * 1_000 : 0
        );
    }

    /// <summary>Starts, retunes or stops the test tone in place of <paramref name="block" />'s output.</summary>
    public async Task TestToneAsync(string block, TestToneRequest request) {
        if (await RequestAsync(
                    new() {
                        Type = IpcTypes.TestTone,
                        Block = block,
                        Enabled = request.Enabled,
                        Frequency = request.Frequency,
                        Level = request.Level
                    }
                )
                is null
            && request.Enabled) {
            throw new RigException(503, "The engine is not connected.");
        }
    }

    /// <summary>A request with a reply, or null when no engine is connected.</summary>
    async Task<IpcMessage?> RequestAsync(IpcMessage message) {
        if (!IsConnected) {
            return null;
        }

        try {
            return await Submit(message);
        } catch (IpcException e) {
            throw new RigException(422, e.Message, e.Errors);
        } catch (IOException) {
            return null; // the link dropped before the host answered
        }
    }

    /// <summary>
    ///     Queues a request and returns its reply. It always completes: with the host's answer, an
    ///     <see cref="IpcException" /> when the host rejects it, or an <see cref="IOException" /> when the link drops
    ///     (or the host stalls) before it answers.
    /// </summary>
    Task<IpcMessage> Submit(IpcMessage message) {
        var reply = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(message, reply));
        if (!IsConnected) {
            // The link dropped between the caller's check and the write, and the pump may have gone with it: nothing
            // else would answer this request until the host comes back.
            DiscardQueued("The engine host disconnected.");
        }

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
            synced = false;
            graphSent = false;
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

            synced = true;
            await pump;
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            // the link dropped; the outer loop reconnects
        } finally {
            current.TelemetryReceived -= OnTelemetry;
            current.MidiReceived -= OnMidi;
            monitor = null;
            synced = false;
            await lost.CancelAsync();
            await meters.ContinueWith(static _ => { }, TaskScheduler.Default);
            mapped?.Dispose();
            info = null;
            Link = null;
            await current.DisposeAsync();
            // Fail what the pump never sent, now rather than at the next connection: a caller waiting for a reply
            // (a structural edit, holding the rig's lock) must not wait for a host that may never come back.
            DiscardQueued("The engine host disconnected.");
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

                hub.PublishFrame(
                    "scope",
                    new("scope", source.Region.SampleRate / ScopeDecimation, points),
                    ContractsJsonContext.Default.ScopeFrame
                );
            }

            PublishRta(source.Region);
            if (source.Poll() is not { } reading) {
                continue;
            }

            hub.PublishFrame(
                "telemetry",
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
                    source.LoudnessShortTerm,
                    OutputPeaks(LastTelemetry),
                    Modulation(source)
                ),
                ContractsJsonContext.Default.Telemetry
            );
        }
    }

    /// <summary>
    ///     Every block meter by block id and meter id, when the meters belong to the graph whose meters the host
    ///     reported: the one place block meters are mapped to ids.
    /// </summary>
    Dictionary<string, Dictionary<string, float>>? BlockMeters(TelemetryMonitor source) {
        var map = Volatile.Read(ref meterMap);
        if (source.BlockGeneration != map.Generation || map.Generation < 0) {
            return null;
        }

        var values = source.BlockMeters;
        var blocks = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        for (var i = 0; i < values.Length && i < map.Blocks.Length; i++) {
            if (map.Blocks[i] is not { } block) {
                continue;
            }

            if (!blocks.TryGetValue(block, out var meters)) {
                blocks[block] = meters = new(1, StringComparer.Ordinal);
            }

            meters[map.Meters[i]] = values[i];
        }

        return blocks;
    }

    /// <summary>
    ///     The value of every parameter a modifier drives, by block id and parameter id, when the values belong to the
    ///     graph whose modifiers the host reported; null when none is driving one (disabled, engage or not yet run).
    /// </summary>
    Dictionary<string, Dictionary<string, float>>? Modulation(TelemetryMonitor source) {
        var map = Volatile.Read(ref modifierMap);
        if (source.ModifierGeneration != map.Generation || map.Generation < 0) {
            return null;
        }

        var values = source.ModifierValues;
        Dictionary<string, Dictionary<string, float>>? blocks = null;
        for (var i = 0; i < values.Length && i < map.Targets.Count; i++) {
            var target = map.Targets[i];
            if (!float.IsFinite(values[i]) || target.Param == ModNames.EngageParam) {
                continue;
            }

            blocks ??= new(StringComparer.Ordinal);
            if (!blocks.TryGetValue(target.Block, out var parameters)) {
                blocks[target.Block] = parameters = new(1, StringComparer.Ordinal);
            }

            parameters[target.Param] = values[i];
        }

        return blocks;
    }

    /// <summary>The names of the hardware input sources in <paramref name="mask" /> (bit 0 = Input 1).</summary>
    static string[] InputNames(int mask) => [
        ..Enumerable.Range(0, HardwareInputs.Count)
            .Where(s => (mask & (1 << s)) != 0)
            .Select(static s => HardwareInputs.Names[s])
    ];

    static LooperInfo? Looper(HostTelemetry? t) =>
        t?.Looper is { } l ? new(l.State, l.LengthSeconds, l.PositionSeconds, l.Reversed, l.HalfSpeed) : null;

    /// <summary>Outputs 1-4's peaks (a copy: an in-process host refills its telemetry in place).</summary>
    static float[]? OutputPeaks(HostTelemetry? t) => t is null ? null : [..t.OutputPeaks];

    static TunerInfo? Tuner(HostTelemetry? t) =>
        t?.Tuner is { } r ? new TunerInfo(r.Frequency, r.Note, r.Octave, r.Cents) : null;

    async Task PumpAsync(IEngineLink current, CancellationToken cancellationToken) {
        await foreach (var work in queue.Reader.ReadAllAsync(cancellationToken)) {
            await SendAsync(current, work, cancellationToken);
        }
    }

    async Task SendAsync(IEngineLink current, Work work, CancellationToken cancellationToken) {
        try {
            if (!graphSent && WithoutGraph(work.Message) is var message && message != work.Message) {
                if (message is null) {
                    return; // only graph-scoped work, queued between the hello and the resync's loadGraph
                }

                work = work with { Message = message };
            }

            var reply = await current.SendAsync(work.Message, cancellationToken);
            graphSent |= work.Message.Type == IpcTypes.LoadGraph;
            work.Reply?.TrySetResult(reply);
        } catch (IpcException e) {
            lastError = e.Errors.Count > 0 ? string.Join("; ", e.Errors) : e.Message;
            if (work.Reply is null) {
                EngineRejected(logger, work.Message.Type, lastError);
            }

            work.Reply?.TrySetException(e);
        } catch (Exception e) {
            // The link dropped (IOException, the lost token) or the host stalled past the client's timeout: the request
            // fails, and the exception ends the pump so the link is dropped and reconnected.
            work.Reply?.TrySetException(new IOException("The engine host did not answer: " + e.Message, e));
            throw;
        }
    }

    /// <summary>
    ///     <paramref name="message" /> without what needs a graph: null for a bypass or modifier, the engine-wide values of
    ///     a parameter update (null when none), the message itself for anything else.
    /// </summary>
    static IpcMessage? WithoutGraph(IpcMessage message) {
        switch (message.Type) {
            case IpcTypes.SetBypass or IpcTypes.SetModifier:
                return null;
            case IpcTypes.SetParams when message.Params is { } values:
                if (values.All(static v => IpcBlocks.IsEngineWide(v.Block))) {
                    return message;
                }

                List<ParamValue> wide = [..values.Where(static v => IpcBlocks.IsEngineWide(v.Block))];
                return wide.Count == 0
                    ? null
                    : new() { Type = message.Type, Id = message.Id, Generation = message.Generation, Params = wide };
            default:
                return message;
        }
    }

    void DiscardQueued(string reason) {
        while (queue.Reader.TryRead(out var work)) {
            work.Reply?.TrySetException(new IOException(reason));
        }
    }

    /// <summary>
    ///     Hands MIDI to every listener. A listener that fails (MIDI learn on an unwritable data folder) is logged; it
    ///     must not reach the link's read loop, which would take the failure for a lost host.
    /// </summary>
    void OnMidi(IReadOnlyList<MidiEventMessage> messages) {
        if (MidiReceived is not { } handlers) {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<IReadOnlyList<MidiEventMessage>>>()) {
            try {
                handler(messages);
            } catch (Exception e) when (e is not OutOfMemoryException) {
                MidiHandlerFailed(logger, e.Message);
            }
        }
    }

    void OnTelemetry(HostTelemetry t) {
        LastTelemetry = t;
        TelemetryUpdated?.Invoke(t);
        if (monitor is null) {
            hub.PublishFrame(
                "telemetry",
                new(
                    "telemetry",
                    t.InputPeak,
                    t.OutputPeakLeft,
                    t.OutputPeakRight,
                    t.CpuP999,
                    t.Overloads,
                    Tuner(t),
                    t.Tempo,
                    Looper(t),
                    OutputPeaks: OutputPeaks(t)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling MIDI failed: {Error}")]
    static partial void MidiHandlerFailed(ILogger logger, string error);

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

    /// <summary>Which block and parameter each modifier of a graph drives (the telemetry region's order).</summary>
    sealed record ModifierMap(int Generation, List<ModifierTarget> Targets);

    /// <summary>
    ///     Which block and meter each of a graph's block meters is (the telemetry region's order: slot by slot, each
    ///     slot's meters as the host listed them). Shunts have a null block: they have no meter.
    /// </summary>
    sealed record MeterMap(int Generation, string?[] Blocks, string[] Meters) {
        const string Peak = "peak";

        /// <summary>Each <c>peak</c> in <paramref name="meterIds" /> starts the next slot's meters.</summary>
        public static MeterMap Of(int generation, List<string> slotIds, string[]? meterIds) {
            // A host that does not list its meters has only the peak per slot.
            var meters = meterIds ?? [..slotIds.Select(static _ => Peak)];
            var blocks = new string?[meters.Length];
            var slot = -1;
            for (var i = 0; i < meters.Length; i++) {
                if (meters[i] == Peak) {
                    slot++;
                }

                var id = slot >= 0 && slot < slotIds.Count ? slotIds[slot] : null;
                blocks[i] = id is null || id.StartsWith("shunt", StringComparison.Ordinal) ? null : id;
            }

            return new(generation, blocks, meters);
        }
    }
}
