using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Dsp.IO;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Native;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Tuning;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Engine.Telemetry;
using Cathode.Ipc;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Cathode.EngineHost;

/// <summary>
///     Executes IPC requests against a <see cref="CathodeEngine" /> (docs/adr/0004-state-ownership.md): compiles and
///     publishes graphs, sets parameter targets, posts bypass commands, runs the tuner and gathers telemetry. Called
///     from control threads only; one lock serialises requests and telemetry.
/// </summary>
public sealed class EngineHostService : IDisposable {
    /// <summary>Longest block or parameter id the allocation-free path reads; longer ones take the slow path.</summary>
    const int MaxIdLength = 64;

    /// <summary>
    ///     CPU figures describe the last few seconds, not the process lifetime (which would keep start-up spikes
    ///     forever).
    /// </summary>
    static readonly TimeSpan StatsWindow = TimeSpan.FromSeconds(5);

    readonly GraphCompiler compiler;
    readonly Tuner tuner;
    readonly float[] tap = new float[4_096];
    readonly IDeviceMonitor? device;
    readonly Lock gate = new();
    readonly IpcMessage ok = new() { Type = IpcTypes.Ok };
    CompiledGraph? latest;
    MidiInput? midi;
    TunerReading? reading;
    long windowStarted = Stopwatch.GetTimestamp();
    long overloads; // cumulative across windows
    long overrunsSeen; // overruns already counted in the current window
    DeviceWindow deviceWindow = new(0, 0, 0);
    bool tunerOn;
    long exceptionsReported; // callback exceptions already logged
    long exceptionReportedAt; // Stopwatch timestamp of the last such log line

    static readonly int PitchIndex = RigControls.Descriptor.IndexOf("pitch");
    static readonly int PitchLow = RigControls.Descriptor.IndexOf("pitchLow");
    static readonly int PitchHigh = RigControls.Descriptor.IndexOf("pitchHigh");

    public CathodeEngine Engine { get; }

    public HostInfo Info { get; }

    /// <summary>
    ///     The audio device's status (<see cref="AudioDeviceStatus.Ok" /> without a device). Anything else means the
    ///     host must reopen the device; the serve command exits so its supervisor restarts it.
    /// </summary>
    public AudioDeviceStatus DeviceStatus => device?.Status ?? AudioDeviceStatus.Ok;

    /// <summary>
    ///     Where the host reports problems found while gathering telemetry (an exception on the audio thread); the serve
    ///     command points it at the console.
    /// </summary>
    public Action<string>? Log { get; set; }

    /// <summary>Sends MIDI out (footswitch LEDs); replaced in tests. Defaults to the native shim.</summary>
    public Func<string?, byte[], bool> NativeMidiSend { get; set; } = static (destination, bytes) =>
        NativeMidi.Send(destination, bytes);

    /// <summary>
    ///     The shared-memory telemetry the engine writes: a file named in <see cref="Info" /> when the host serves
    ///     another process, in memory otherwise.
    /// </summary>
    public TelemetryRegion TelemetryRegion { get; }

    public EngineHostService(
        ProcessSpec spec,
        string backend,
        BlockRegistry? registry = null,
        IDeviceMonitor? device = null,
        string? telemetryPath = null,
        double looperSeconds = Dsp.Looping.Looper.DefaultSeconds,
        int inputSources = 1
    ) {
        this.device = device;
        TelemetryRegion = telemetryPath is null
            ? TelemetryRegion.CreateInMemory(spec.SampleRate)
            : TelemetryRegion.CreateFile(telemetryPath, spec.SampleRate);
        Engine = new(spec) { Telemetry = TelemetryRegion };
        if (looperSeconds > 0) {
            Engine.EnableLooper(looperSeconds);
        }

        compiler = new(registry ?? BlockRegistry.CreateDefault().AddNativeBlocks()) {
            HardwareLoop = Engine.HardwareLoop, HardwareInputs = Engine.Inputs
        };
        tuner = new(spec.SampleRate);
        var version = typeof(EngineHostService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "dev";
        Info = new(version, backend, spec.SampleRate, spec.MaxBlockSize, TelemetryRegion.Path, inputSources | 1);
    }

    /// <summary>Starts reading MIDI input; <paramref name="forward" /> gets each batch (on the MIDI thread).</summary>
    public void StartMidi(Action<List<MidiEventMessage>> forward) {
        midi ??= new(Engine, forward);
        midi.Start();
    }

    public IpcMessage Handle(IpcMessage request) {
        lock (gate) {
            return request.Type switch {
                IpcTypes.Hello => new() { Type = IpcTypes.Ok, Id = request.Id, Host = Info },
                IpcTypes.LoadGraph => LoadGraph(request),
                IpcTypes.SetParams => SetParams(request),
                IpcTypes.SetBypass => SetBypass(request),
                IpcTypes.SetModifier => SetModifier(request),
                IpcTypes.SetTuner => SetTuner(request),
                IpcTypes.Status => new() { Type = IpcTypes.Ok, Id = request.Id, Telemetry = CollectTelemetry(new()) },
                IpcTypes.Tap => Tap(request),
                IpcTypes.Looper => Looper(request),
                IpcTypes.ExportLooper => ExportLooper(request),
                IpcTypes.SetMidi => SetMidi(request),
                IpcTypes.MidiOut => MidiOut(request),
                IpcTypes.Latency => new() { Type = IpcTypes.Ok, Id = request.Id, Latency = Latency() },
                IpcTypes.TestTone => TestTone(request),
                _ => IpcMessage.Fail(request.Id, $"Unknown request type '{request.Type}'.")
            };
        }
    }

    /// <summary>
    ///     Handles <c>setParams</c> and <c>setBypass</c> straight from a frame's UTF-8 JSON, without deserializing it: a
    ///     knob drag sends one per UI frame, and deserializing them was most of the host's steady allocation
    ///     (docs/adr/0001-process-model.md). Returns false for every other request, and for any frame it cannot read,
    ///     so the caller deserializes it and calls <see cref="Handle" />. A success reply is one reused message, valid
    ///     until the next call; failures allocate.
    /// </summary>
    public bool TryHandle(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out IpcMessage? reply) {
        reply = null;
        try {
            var reader = new Utf8JsonReader(frame);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) {
                return false;
            }

            var header = new FrameHeader { BlockLength = -1 };
            Span<char> block = stackalloc char[MaxIdLength];
            var parameters = default(Utf8JsonReader);
            if (!ReadHeader(ref reader, block, ref header, ref parameters)) {
                return false;
            }

            lock (gate) {
                reply = header.Type == IpcTypes.SetParams
                    ? SetParams(header.Id, header.Generation, ref parameters)
                    : SetBypass(header, block);
            }

            return true;
        } catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) {
            return false;
        }
    }

    /// <summary>What <see cref="TryHandle" /> reads from a frame besides the block id and the params.</summary>
    struct FrameHeader {
        public string? Type;
        public long Id;
        public int Generation;
        public bool Bypassed;
        public int BlockLength;
    }

    /// <summary>
    ///     Reads a frame's properties (reader inside its object). False when it is not a request
    ///     <see cref="TryHandle" /> answers.
    /// </summary>
    static bool ReadHeader(
        ref Utf8JsonReader reader,
        scoped Span<char> block,
        ref FrameHeader header,
        ref Utf8JsonReader parameters
    ) {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
            if (!ReadProperty(ref reader, block, ref header, ref parameters)) {
                return false;
            }
        }

        return header.Type is not null;
    }

    /// <summary>Reads one property of a frame (reader on its name); false when the frame is not handled here.</summary>
    static bool ReadProperty(
        ref Utf8JsonReader reader,
        scoped Span<char> block,
        ref FrameHeader header,
        ref Utf8JsonReader parameters
    ) {
        if (reader.ValueTextEquals("type"u8)) {
            reader.Read();
            header.Type = reader.ValueTextEquals(IpcTypes.SetParams) ? IpcTypes.SetParams
                : reader.ValueTextEquals(IpcTypes.SetBypass) ? IpcTypes.SetBypass
                : null;
            return header.Type is not null;
        }

        if (reader.ValueTextEquals("id"u8)) {
            reader.Read();
            header.Id = reader.GetInt64();
        } else if (reader.ValueTextEquals("generation"u8)) {
            reader.Read();
            header.Generation = reader.GetInt32();
        } else if (reader.ValueTextEquals("block"u8)) {
            reader.Read();
            header.BlockLength = ReadId(ref reader, block);
            return header.BlockLength >= 0 || reader.TokenType == JsonTokenType.Null;
        } else if (reader.ValueTextEquals("bypassed"u8)) {
            reader.Read();
            header.Bypassed = reader.TokenType == JsonTokenType.True;
        } else if (reader.ValueTextEquals("params"u8)) {
            parameters = reader; // a checkpoint: the array is applied once the whole frame has been read
            reader.Read();
            reader.Skip();
        } else {
            reader.Read();
            reader.Skip();
        }

        return true;
    }

    /// <summary>A <c>setBypass</c> frame; <paramref name="block" /> holds the block id when it had one.</summary>
    IpcMessage SetBypass(in FrameHeader header, ReadOnlySpan<char> block) {
        var id = header.Id;
        var blockLength = header.BlockLength;
        if (latest is null || header.Generation != latest.Generation) {
            return IpcMessage.Fail(id, $"Stale graph generation {header.Generation}.");
        }

        var slot = blockLength < 0 ? -1 : latest.SlotOf(block[..blockLength]);
        return slot < 0 ? IpcMessage.Fail(id, $"No block '{block[..Math.Max(blockLength, 0)]}'.")
            : Engine.Post(EngineCommand.SetBypass(latest.Generation, slot, header.Bypassed)) ? Ok(id)
            : IpcMessage.Fail(id, "The engine command queue is full.");
    }

    /// <summary>Meters, CPU, overloads and the latest tuner reading; also disposes graphs the audio thread retired.</summary>
    public HostTelemetry Telemetry() => Telemetry(new());

    /// <summary>
    ///     <see cref="Telemetry()" /> into an existing instance (and its tuner reading), so a periodic push allocates
    ///     nothing.
    /// </summary>
    public HostTelemetry Telemetry(HostTelemetry target) {
        lock (gate) {
            return CollectTelemetry(target);
        }
    }

    public void Dispose() {
        midi?.Dispose();
        lock (gate) {
            Engine.Dispose(); // the audio has stopped by now (the backend is disposed first)
            latest?.Dispose();
            latest = null;
        }

        // Only once the audio has stopped (the backend is disposed first), as the engine writes the region.
        Engine.Telemetry = null;
        TelemetryRegion.Dispose();
    }

    IpcMessage LoadGraph(IpcMessage request) {
        if (request.Graph is null) {
            return IpcMessage.Fail(request.Id, "loadGraph needs a graph.");
        }

        try {
            var graph = compiler.Compile(request.Graph, Engine.Spec, request.Generation);
            Engine.Publish(graph);
            latest = graph;
            DisposeRetired(); // not only on telemetry ticks, which a burst of preset changes outpaces
            return new() {
                Type = IpcTypes.Ok,
                Id = request.Id,
                SlotIds = [..graph.SlotIds],
                MeterIds = graph.MeterLayout,
                ModifierTargets = [..graph.Modifiers.Select(m => ModifierTargetOf(graph, m))]
            };
        } catch (GraphValidationException e) {
            return IpcMessage.Fail(request.Id, "The graph is invalid.", [..e.Errors]);
        }
    }

    static ModifierTarget ModifierTargetOf(CompiledGraph graph, ModifierBinding modifier) =>
        new(
            graph.SlotIds[modifier.Slot],
            modifier.ParamIndex == ModifierBinding.Engage
                ? ModNames.EngageParam
                : graph.Block(modifier.Slot).Descriptor.Params[modifier.ParamIndex].Id
        );

    IpcMessage SetParams(IpcMessage request) {
        var values = request.Params ?? [];
        // Rig and global parameters need no graph; anything else (or an empty update) is checked against it.
        var forGraph = values.Count == 0 || values.Any(static v => !IpcBlocks.IsEngineWide(v.Block));
        if (forGraph && (latest is null || request.Generation != latest.Generation)) {
            return IpcMessage.Fail(request.Id, $"Stale graph generation {request.Generation}.");
        }

        List<string>? errors = null;
        foreach (var value in values) {
            if (!SetParameter(value.Block, value.Param, value.Value)) {
                (errors ??= []).Add(NoParameter(value.Param, value.Block));
            }
        }

        RefreshBlocks();
        return errors is null
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "Some parameters were not set.", errors);
    }

    IpcMessage Tap(IpcMessage request) {
        Engine.Post(EngineCommand.TapTempo(request.Time ?? Engine.SampleTime));
        return IpcMessage.Ok(request.Id);
    }

    IpcMessage Looper(IpcMessage request) {
        if (Engine.Looper is null) {
            return IpcMessage.Fail(request.Id, "This engine has no looper.");
        }

        if (!Enum.TryParse<LooperAction>(request.Action, true, out var action)) {
            return IpcMessage.Fail(request.Id, $"Unknown looper action '{request.Action}'.");
        }

        return Engine.Post(EngineCommand.Looper(action))
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "The engine command queue is full.");
    }

    IpcMessage ExportLooper(IpcMessage request) {
        if (Engine.Looper is not { Length: > 0 } looper || request.Path is null) {
            return IpcMessage.Fail(request.Id, "The looper is empty.");
        }

        var (left, right) = looper.Snapshot();
        WavFile.Write(request.Path, new((int)Engine.Spec.SampleRate, [left, right]));
        return IpcMessage.Ok(request.Id);
    }

    IpcMessage SetMidi(IpcMessage request) {
        midi?.SetFollowClock(request.FollowClock == true);
        return IpcMessage.Ok(request.Id);
    }

    LatencyReport Latency() {
        var (input, output) = device?.Latency ?? (0, 0);
        return new(
            Engine.Spec.SampleRate,
            Engine.Spec.MaxBlockSize,
            input,
            output,
            latest?.LatencySamples ?? 0,
            [..latest?.LatencyPath ?? []]
        );
    }

    /// <summary>The cab resonance helper's sine in place of a block's output; never stored with the preset.</summary>
    IpcMessage TestTone(IpcMessage request) {
        var tone = Engine.TestTone;
        if (request.Enabled != true) {
            tone.Stop();
            return IpcMessage.Ok(request.Id);
        }

        var slot = request.Block is null || latest is null ? -1 : latest.SlotOf(request.Block);
        if (slot < 0) {
            return IpcMessage.Fail(request.Id, $"No block '{request.Block}'.");
        }

        var frequency = Math.Clamp(request.Frequency ?? 100, 20, 2_000);
        var level = Decibels.ToGain(Math.Clamp(request.Level ?? -20f, -60f, 0f));
        tone.Start(latest!.Generation, slot, frequency, level);
        return IpcMessage.Ok(request.Id);
    }

    IpcMessage MidiOut(IpcMessage request) {
        var bytes = (request.Bytes ?? []).Select(static b => (byte)b).ToArray();
        return bytes.Length is >= 1 and <= 3 && NativeMidiSend(request.Destination, bytes)
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "MIDI output is not available.");
    }

    /// <summary>
    ///     Applies the <c>params</c> array of a frame, positioned on its property name. Like
    ///     <see cref="SetParams(IpcMessage)" />, a stale generation rejects the whole update unless every value is a
    ///     rig or global parameter.
    /// </summary>
    IpcMessage SetParams(long id, int generation, ref Utf8JsonReader reader) {
        if ((latest is null || generation != latest.Generation) && TargetsGraph(reader)) {
            return IpcMessage.Fail(id, $"Stale graph generation {generation}.");
        }

        List<string>? errors = null;
        Span<char> block = stackalloc char[MaxIdLength];
        Span<char> param = stackalloc char[MaxIdLength];
        if (reader.Read() && reader.TokenType == JsonTokenType.StartArray) {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject) {
                if (SetParameter(ref reader, block, param) is { } error) {
                    (errors ??= []).Add(error);
                }
            }
        }

        RefreshBlocks();
        return errors is null ? Ok(id) : IpcMessage.Fail(id, "Some parameters were not set.", errors);
    }

    /// <summary>
    ///     Reads and applies one <c>{block, param, value}</c> object of a <c>params</c> array (reader on its start),
    ///     with <paramref name="block" /> and <paramref name="param" /> as scratch; null when set, else the error.
    /// </summary>
    string? SetParameter(ref Utf8JsonReader reader, scoped Span<char> block, scoped Span<char> param) {
        var blockLength = -1;
        var paramLength = -1;
        var value = 0f;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
            if (reader.ValueTextEquals("block"u8)) {
                reader.Read();
                blockLength = ReadId(ref reader, block);
            } else if (reader.ValueTextEquals("param"u8)) {
                reader.Read();
                paramLength = ReadId(ref reader, param);
            } else if (reader.ValueTextEquals("value"u8)) {
                reader.Read();
                value = reader.GetSingle();
            } else {
                reader.Read();
                reader.Skip();
            }
        }

        if (blockLength < 0 || paramLength < 0 || !SetParameter(block[..blockLength], param[..paramLength], value)) {
            return NoParameter(param[..Math.Max(paramLength, 0)], block[..Math.Max(blockLength, 0)]);
        }

        return null;
    }

    /// <summary>Whether a <c>params</c> array (reader on its property name) is empty or names a graph block.</summary>
    static bool TargetsGraph(Utf8JsonReader reader) {
        var empty = true;
        if (reader.Read() && reader.TokenType == JsonTokenType.StartArray) {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject) {
                empty = false;
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
                    var isBlock = reader.ValueTextEquals("block"u8);
                    reader.Read();
                    if (isBlock && !IsEngineWide(ref reader)) {
                        return true;
                    }

                    reader.Skip();
                }
            }
        }

        return empty;
    }

    /// <summary>Whether the string token under <paramref name="reader" /> is a rig or global EQ id (without allocating).</summary>
    static bool IsEngineWide(ref Utf8JsonReader reader) {
        Span<char> id = stackalloc char[MaxIdLength];
        var length = ReadId(ref reader, id);
        return length >= 0 && IpcBlocks.IsEngineWide(id[..length]);
    }

    /// <summary>Sets one target on the rig, an output's global EQ or a block of the current graph.</summary>
    bool SetParameter(ReadOnlySpan<char> block, ReadOnlySpan<char> param, float value) {
        ParamBank? bank;
        int index;
        var eq = IpcBlocks.GlobalEqOutput(block);
        if (block is IpcBlocks.Rig) {
            (bank, index) = (Engine.Rig, RigControls.Descriptor.IndexOf(param));
        } else if (eq >= 0) {
            (bank, index) = (Engine.OutputEq(eq).Params, Engine.OutputEq(eq).Descriptor.IndexOf(param));
        } else {
            var slot = latest?.SlotOf(block) ?? -1;
            (bank, index) = slot < 0
                ? (null, -1)
                : (latest!.Block(slot).Params, latest.Block(slot).Descriptor.IndexOf(param));
        }

        if (bank is null || index < 0) {
            return false;
        }

        bank.SetTarget(index, value);
        return true;
    }

    /// <summary>
    ///     Lets blocks with derived state (the Cab's mixed slot IRs) rebuild it for their new targets, here on the
    ///     control thread rather than on the audio thread.
    /// </summary>
    void RefreshBlocks() {
        if (latest is not { } graph) {
            return;
        }

        for (var slot = 0; slot < graph.SlotIds.Count; slot++) {
            if (graph.Block(slot) is IControlRefresh refresh) {
                refresh.Refresh();
            }
        }
    }

    static string NoParameter(ReadOnlySpan<char> param, ReadOnlySpan<char> block) =>
        $"No parameter '{param}' on block '{block}'.";

    /// <summary>Copies a string token into <paramref name="destination" />; -1 if it is not a string or too long.</summary>
    static int ReadId(ref Utf8JsonReader reader, scoped Span<char> destination) =>
        reader.TokenType == JsonTokenType.String
        && !reader.HasValueSequence
        && reader.ValueSpan.Length <= destination.Length
            ? reader.CopyString(destination)
            : -1;

    IpcMessage Ok(long id) {
        ok.Id = id;
        return ok;
    }

    IpcMessage SetBypass(IpcMessage request) {
        if (latest is null || request.Generation != latest.Generation) {
            return IpcMessage.Fail(request.Id, $"Stale graph generation {request.Generation}.");
        }

        var slot = request.Block is null ? -1 : latest.SlotOf(request.Block);
        if (slot < 0) {
            return IpcMessage.Fail(request.Id, $"No block '{request.Block}'.");
        }

        return Engine.Post(EngineCommand.SetBypass(latest.Generation, slot, request.Bypassed == true))
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "The engine command queue is full.");
    }

    IpcMessage SetModifier(IpcMessage request) {
        if (latest is null || request.Generation != latest.Generation) {
            return IpcMessage.Fail(request.Id, $"Stale graph generation {request.Generation}.");
        }

        var slot = request.Block is null ? -1 : latest.SlotOf(request.Block);
        var block = slot < 0 ? null : latest.Block(slot);
        var param = request.Param == ModNames.EngageParam ? ModifierBinding.Engage
            : block is null || request.Param is null ? -1
            : block.Descriptor.IndexOf(request.Param);
        var index = ModifierIndex(latest, slot, param);
        if (index < 0) {
            return IpcMessage.Fail(request.Id, $"No modifier on '{request.Block}.{request.Param}'.");
        }

        var baseValue = request.Params is [{ } value, ..] ? value.Value : 0f;
        return Engine.Post(EngineCommand.SetModifier(latest.Generation, index, request.Enabled == true, baseValue))
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "The engine command queue is full.");
    }

    /// <summary>
    ///     The last of the graph's modifiers on parameter <paramref name="param" /> of slot <paramref name="slot" />,
    ///     or -1.
    /// </summary>
    static int ModifierIndex(CompiledGraph graph, int slot, int param) {
        var index = -1;
        for (var i = 0; i < graph.Modifiers.Count && param != -1; i++) {
            if (graph.Modifiers[i].Slot == slot && graph.Modifiers[i].ParamIndex == param) {
                index = i;
            }
        }

        return index;
    }

    IpcMessage SetTuner(IpcMessage request) {
        tunerOn = request.Enabled == true;
        Engine.InputTapEnabled = tunerOn || latest?.UsesPitch == true;
        if (!tunerOn) {
            reading = null;
        }

        return IpcMessage.Ok(request.Id);
    }

    /// <summary>
    ///     Logs the first exception the engine threw on the audio thread with its stack trace, then, while they keep
    ///     coming, a count at most every 10 s: the callback swallows them (the output just goes silent), so this is the
    ///     only place a real-time bug shows up.
    /// </summary>
    void ReportCallbackExceptions(IDeviceMonitor monitor) {
        var count = monitor.CallbackExceptions;
        if (count == exceptionsReported) {
            return;
        }

        var exception = monitor.TakeCallbackException();
        var now = Stopwatch.GetTimestamp();
        if (exceptionsReported == 0 && exception is not null) {
            Log?.Invoke(
                $"error: the engine threw on the audio thread; that callback's output was silenced: {exception}"
            );
            exceptionReportedAt = now;
        } else if (Stopwatch.GetElapsedTime(exceptionReportedAt, now) >= TimeSpan.FromSeconds(10)) {
            Log?.Invoke(
                $"error: {count} exceptions on the audio thread so far "
                + $"(latest: {exception?.GetType().Name ?? "unknown"}: {exception?.Message})"
            );
            exceptionReportedAt = now;
        }

        exceptionsReported = count;
    }

    void DisposeRetired() {
        while (Engine.CollectRetired() is { } retired) {
            retired.Dispose();
        }
    }

    HostTelemetry CollectTelemetry(HostTelemetry target) {
        DisposeRetired();

        RefreshBlocks(); // picks up targets that modifiers moved on the audio thread

        if (device is not null) {
            TelemetryRegion.SetDriverOverloads(device.Overloads);
            ReportCallbackExceptions(device);
        }

        // The tap feeds the tuner, and the pitch follower when a modifier listens to pitch.
        var followPitch = latest?.UsesPitch == true;
        Engine.InputTapEnabled = tunerOn || followPitch;
        if (Engine.InputTapEnabled) {
            ReadPitch(followPitch);
        }

        var (input, left, right) = Engine.ReadMeters();
        Engine.ReadOutputPeaks(target.OutputPeaks);
        var stats = Engine.Stats;
        overloads += stats.Overruns - overrunsSeen;
        overrunsSeen = stats.Overruns;
        target.InputPeak = input;
        target.OutputPeakLeft = left;
        target.OutputPeakRight = right;
        target.CpuP999 = stats.Percentile(0.999);
        target.CpuMax = stats.MaxPercent;
        target.Overloads = overloads;
        WriteTuner(target);
        target.DriverOverloads = deviceWindow.Overloads;
        target.CallbackExceptions = exceptionsReported;
        target.DeviceStatus = DeviceStatus.Name();
        target.NativeP999 = deviceWindow.P999;
        target.NativeMax = deviceWindow.Max;
        target.Gen0 = GC.CollectionCount(0);
        target.Gen1 = GC.CollectionCount(1);
        target.Gen2 = GC.CollectionCount(2);
        target.GcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
        target.AllocatedBytes = GC.GetTotalAllocatedBytes();
        target.Tempo = Engine.Tempo;
        WriteLooper(target);
        if (Stopwatch.GetElapsedTime(windowStarted) >= StatsWindow) {
            stats.RequestReset();
            overrunsSeen = 0;
            windowStarted = Stopwatch.GetTimestamp();
            if (device is not null) {
                // Read the device once per window: its histogram snapshot allocates, and this process must stay quiet.
                deviceWindow = device.CloseWindow();
            }
        }

        return target;
    }

    /// <summary>Feeds the input tap to the tuner; the pitch follower's rig parameter tracks what it detects.</summary>
    void ReadPitch(bool followPitch) {
        int read;
        while ((read = Engine.InputTap.Read(tap)) > 0) {
            tuner.Push(tap.AsSpan(0, read));
        }

        var detected = tuner.Detect();
        reading = tunerOn ? detected : null;
        if (followPitch && detected is { } pitch) {
            var low = Engine.Rig.GetTarget(PitchLow);
            var high = Engine.Rig.GetTarget(PitchHigh);
            var normal = Math.Log(pitch.Frequency / low) / Math.Log(high / low);
            Engine.Rig.SetTarget(PitchIndex, (float)Math.Clamp(normal, 0, 1));
        }
    }

    void WriteTuner(HostTelemetry target) {
        if (reading is { } r) {
            var tunerReading = target.Tuner ??= new();
            tunerReading.Frequency = r.Frequency;
            tunerReading.Note = r.Note;
            tunerReading.Octave = r.Octave;
            tunerReading.Cents = r.Cents;
        } else {
            target.Tuner = null;
        }
    }

    void WriteLooper(HostTelemetry target) {
        if (Engine.Looper is { } looper) {
            var status = target.Looper ??= new();
            status.State = looper.State switch {
                LooperState.Recording => "recording",
                LooperState.Playing => "playing",
                LooperState.Overdubbing => "overdubbing",
                LooperState.Stopped => "stopped",
                LooperState.Armed => "armed",
                _ => "empty"
            };
            status.LengthSeconds = looper.Length / Engine.Spec.SampleRate;
            status.PositionSeconds = looper.Position / Engine.Spec.SampleRate;
            status.Reversed = looper.IsReversed;
            status.HalfSpeed = looper.IsHalfSpeed;
        } else {
            target.Looper = null;
        }
    }
}
