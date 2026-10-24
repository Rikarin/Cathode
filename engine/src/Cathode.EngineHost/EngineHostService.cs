using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
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
    readonly List<HostBlockMeters> blockMeters = [];
    readonly List<IBlockMeters> meteredBlocks = [];
    CompiledGraph? latest;
    CompiledGraph? metered;
    MidiInput? midi;
    TunerReading? reading;
    long windowStarted = Stopwatch.GetTimestamp();
    long overloads; // cumulative across windows
    long overrunsSeen; // overruns already counted in the current window
    DeviceWindow deviceWindow = new(0, 0, 0);
    bool tunerOn;

    static readonly int PitchIndex = RigControls.Descriptor.IndexOf("pitch");
    static readonly int PitchLow = RigControls.Descriptor.IndexOf("pitchLow");
    static readonly int PitchHigh = RigControls.Descriptor.IndexOf("pitchHigh");

    public CathodeEngine Engine { get; }

    public HostInfo Info { get; }

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
        double looperSeconds = Dsp.Looping.Looper.DefaultSeconds
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
            HardwareLoop = Engine.HardwareLoop
        };
        tuner = new(spec.SampleRate);
        var version = typeof(EngineHostService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "dev";
        Info = new(version, backend, spec.SampleRate, spec.MaxBlockSize, TelemetryRegion.Path);
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
                IpcTypes.SetTuner => SetTuner(request),
                IpcTypes.Status => new() { Type = IpcTypes.Ok, Id = request.Id, Telemetry = CollectTelemetry(new()) },
                IpcTypes.Tap => Tap(request),
                IpcTypes.Looper => Looper(request),
                IpcTypes.ExportLooper => ExportLooper(request),
                IpcTypes.SetMidi => SetMidi(request),
                IpcTypes.MidiOut => MidiOut(request),
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

            string? type = null;
            long id = 0;
            var generation = 0;
            var bypassed = false;
            Span<char> block = stackalloc char[MaxIdLength];
            var blockLength = -1;
            var parameters = default(Utf8JsonReader);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
                if (reader.ValueTextEquals("type"u8)) {
                    reader.Read();
                    type = reader.ValueTextEquals(IpcTypes.SetParams) ? IpcTypes.SetParams
                        : reader.ValueTextEquals(IpcTypes.SetBypass) ? IpcTypes.SetBypass
                        : null;
                    if (type is null) {
                        return false;
                    }
                } else if (reader.ValueTextEquals("id"u8)) {
                    reader.Read();
                    id = reader.GetInt64();
                } else if (reader.ValueTextEquals("generation"u8)) {
                    reader.Read();
                    generation = reader.GetInt32();
                } else if (reader.ValueTextEquals("block"u8)) {
                    reader.Read();
                    blockLength = ReadId(ref reader, block);
                    if (blockLength < 0 && reader.TokenType != JsonTokenType.Null) {
                        return false;
                    }
                } else if (reader.ValueTextEquals("bypassed"u8)) {
                    reader.Read();
                    bypassed = reader.TokenType == JsonTokenType.True;
                } else if (reader.ValueTextEquals("params"u8)) {
                    parameters = reader; // a checkpoint: the array is applied once the whole frame has been read
                    reader.Read();
                    reader.Skip();
                } else {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (type is null) {
                return false;
            }

            lock (gate) {
                if (type == IpcTypes.SetParams) {
                    reply = SetParams(id, generation, ref parameters);
                } else if (latest is null || generation != latest.Generation) {
                    reply = IpcMessage.Fail(id, $"Stale graph generation {generation}.");
                } else {
                    var slot = blockLength < 0 ? -1 : latest.SlotOf(block[..blockLength]);
                    reply = slot < 0 ? IpcMessage.Fail(id, $"No block '{block[..Math.Max(blockLength, 0)]}'.")
                        : Engine.Post(EngineCommand.SetBypass(latest.Generation, slot, bypassed)) ? Ok(id)
                        : IpcMessage.Fail(id, "The engine command queue is full.");
                }
            }

            return true;
        } catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) {
            return false;
        }
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
            return IpcMessage.Ok(request.Id);
        } catch (GraphValidationException e) {
            return IpcMessage.Fail(request.Id, "The graph is invalid.", [..e.Errors]);
        }
    }

    IpcMessage SetParams(IpcMessage request) {
        var values = request.Params ?? [];
        // Rig and global parameters need no graph; anything else (or an empty update) is checked against it.
        var forGraph = values.Count == 0 || values.Any(static v => v.Block is not (IpcBlocks.Rig or IpcBlocks.Global));
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

        return Enum.TryParse<LooperAction>(request.Action, true, out var action)
            ? Engine.Post(EngineCommand.Looper(action))
                ? IpcMessage.Ok(request.Id)
                : IpcMessage.Fail(request.Id, "The engine command queue is full.")
            : IpcMessage.Fail(request.Id, $"Unknown looper action '{request.Action}'.");
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

    IpcMessage MidiOut(IpcMessage request) {
        var bytes = (request.Bytes ?? []).Select(static b => (byte)b).ToArray();
        return bytes.Length is >= 1 and <= 3 && NativeMidiSend(request.Destination, bytes)
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "MIDI output is not available.");
    }

    /// <summary>
    ///     Applies the <c>params</c> array of a frame, positioned on its property name. Like
    ///     <see cref="SetParams(IpcMessage)" />, a stale generation rejects the whole update unless every value is a rig
    ///     or global parameter.
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

                if (blockLength < 0
                    || paramLength < 0
                    || !SetParameter(block[..blockLength], param[..paramLength], value)) {
                    (errors ??= []).Add(
                        NoParameter(param[..Math.Max(paramLength, 0)], block[..Math.Max(blockLength, 0)])
                    );
                }
            }
        }

        RefreshBlocks();
        return errors is null ? Ok(id) : IpcMessage.Fail(id, "Some parameters were not set.", errors);
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
                    if (isBlock
                        && !reader.ValueTextEquals(IpcBlocks.Rig)
                        && !reader.ValueTextEquals(IpcBlocks.Global)) {
                        return true;
                    }

                    reader.Skip();
                }
            }
        }

        return empty;
    }

    /// <summary>Sets one target on the rig, the global EQ or a block of the current graph.</summary>
    bool SetParameter(ReadOnlySpan<char> block, ReadOnlySpan<char> param, float value) {
        ParamBank? bank;
        int index;
        if (block.SequenceEqual(IpcBlocks.Rig)) {
            (bank, index) = (Engine.Rig, RigControls.Descriptor.IndexOf(param));
        } else if (block.SequenceEqual(IpcBlocks.Global)) {
            (bank, index) = (Engine.GlobalEq.Params, Engine.GlobalEq.Descriptor.IndexOf(param));
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
    /// <summary>
    ///     The meters of the blocks that have their own (<see cref="IBlockMeters" />), or null. The list is rebuilt only
    ///     when the graph changes, so a telemetry push allocates nothing.
    /// </summary>
    List<HostBlockMeters>? CollectBlockMeters() {
        if (latest is not { } graph) {
            return null;
        }

        if (!ReferenceEquals(metered, graph)) {
            metered = graph;
            blockMeters.Clear();
            meteredBlocks.Clear();
            for (var slot = 0; slot < graph.SlotIds.Count; slot++) {
                if (graph.Block(slot) is IBlockMeters meters) {
                    meteredBlocks.Add(meters);
                    blockMeters.Add(
                        new() {
                            Block = graph.SlotIds[slot],
                            Ids = [..meters.MeterIds],
                            Values = new float[meters.MeterIds.Count]
                        }
                    );
                }
            }
        }

        for (var i = 0; i < meteredBlocks.Count; i++) {
            meteredBlocks[i].ReadMeters(blockMeters[i].Values);
        }

        return blockMeters.Count > 0 ? blockMeters : null;
    }

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

    IpcMessage SetTuner(IpcMessage request) {
        tunerOn = request.Enabled == true;
        Engine.InputTapEnabled = tunerOn || latest?.UsesPitch == true;
        if (!tunerOn) {
            reading = null;
        }

        return IpcMessage.Ok(request.Id);
    }

    HostTelemetry CollectTelemetry(HostTelemetry target) {
        while (Engine.CollectRetired() is { } retired) {
            retired.Dispose();
        }

        RefreshBlocks(); // picks up targets that modifiers moved on the audio thread

        if (device is not null) {
            TelemetryRegion.SetDriverOverloads(device.Overloads);
        }

        // The tap feeds the tuner, and the pitch follower when a modifier listens to pitch.
        var followPitch = latest?.UsesPitch == true;
        Engine.InputTapEnabled = tunerOn || followPitch;
        if (Engine.InputTapEnabled) {
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

        var (input, left, right) = Engine.ReadMeters();
        var stats = Engine.Stats;
        overloads += stats.Overruns - overrunsSeen;
        overrunsSeen = stats.Overruns;
        target.InputPeak = input;
        target.OutputPeakLeft = left;
        target.OutputPeakRight = right;
        target.CpuP999 = stats.Percentile(0.999);
        target.CpuMax = stats.MaxPercent;
        target.Overloads = overloads;
        if (reading is { } r) {
            var tunerReading = target.Tuner ??= new();
            tunerReading.Frequency = r.Frequency;
            tunerReading.Note = r.Note;
            tunerReading.Octave = r.Octave;
            tunerReading.Cents = r.Cents;
        } else {
            target.Tuner = null;
        }

        target.DriverOverloads = deviceWindow.Overloads;
        target.NativeP999 = deviceWindow.P999;
        target.NativeMax = deviceWindow.Max;
        target.Gen0 = GC.CollectionCount(0);
        target.Gen1 = GC.CollectionCount(1);
        target.Gen2 = GC.CollectionCount(2);
        target.GcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
        target.AllocatedBytes = GC.GetTotalAllocatedBytes();
        target.Tempo = Engine.Tempo;
        target.Blocks = CollectBlockMeters();
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
}
