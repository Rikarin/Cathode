using System.Diagnostics;
using System.Reflection;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Dsp.Tuning;
using Cathode.Engine;
using Cathode.Dsp.IO;
using Cathode.Dsp.Parameters;
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
    CompiledGraph? latest;
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
        Cathode.Audio.Native.NativeMidi.Send(destination, bytes);

    /// <summary>Starts reading MIDI input; <paramref name="forward" /> gets each batch (on the MIDI thread).</summary>
    public void StartMidi(Action<List<MidiEventMessage>> forward) {
        midi ??= new MidiInput(Engine, forward);
        midi.Start();
    }

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
        double looperSeconds = Cathode.Engine.Rig.Looper.MaxSeconds
    ) {
        this.device = device;
        TelemetryRegion = telemetryPath is null
            ? TelemetryRegion.CreateInMemory(spec.SampleRate)
            : TelemetryRegion.CreateFile(telemetryPath, spec.SampleRate);
        Engine = new(spec) { Telemetry = TelemetryRegion };
        if (looperSeconds > 0) {
            Engine.EnableLooper(looperSeconds);
        }
        compiler = new(registry ?? BlockRegistry.CreateDefault().AddNativeBlocks());
        tuner = new(spec.SampleRate);
        var version = typeof(EngineHostService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "dev";
        Info = new(version, backend, spec.SampleRate, spec.MaxBlockSize, TelemetryRegion.Path);
    }

    public IpcMessage Handle(IpcMessage request) {
        lock (gate) {
            return request.Type switch {
                IpcTypes.Hello => new() { Type = IpcTypes.Ok, Id = request.Id, Host = Info },
                IpcTypes.LoadGraph => LoadGraph(request),
                IpcTypes.SetParams => SetParams(request),
                IpcTypes.SetBypass => SetBypass(request),
                IpcTypes.SetTuner => SetTuner(request),
                IpcTypes.Status => new() { Type = IpcTypes.Ok, Id = request.Id, Telemetry = CollectTelemetry() },
                IpcTypes.Tap => Tap(request),
                IpcTypes.Looper => Looper(request),
                IpcTypes.ExportLooper => ExportLooper(request),
                IpcTypes.SetMidi => SetMidi(request),
                IpcTypes.MidiOut => MidiOut(request),
                _ => IpcMessage.Fail(request.Id, $"Unknown request type '{request.Type}'.")
            };
        }
    }

    /// <summary>Meters, CPU, overloads and the latest tuner reading; also disposes graphs the audio thread retired.</summary>
    public HostTelemetry Telemetry() {
        lock (gate) {
            return CollectTelemetry();
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

        var errors = new List<string>();
        foreach (var value in values) {
            var (bank, index) = value.Block switch {
                IpcBlocks.Rig => (Engine.Rig, RigControls.Descriptor.IndexOf(value.Param)),
                IpcBlocks.Global => (Engine.GlobalEq.Params, Engine.GlobalEq.Descriptor.IndexOf(value.Param)),
                _ => Graph(value)
            };
            if (bank is null || index < 0) {
                errors.Add($"No parameter '{value.Param}' on block '{value.Block}'.");
                continue;
            }

            bank.SetTarget(index, value.Value);
        }

        return errors.Count == 0
            ? IpcMessage.Ok(request.Id)
            : IpcMessage.Fail(request.Id, "Some parameters were not set.", errors);
    }

    (ParamBank? Bank, int Index) Graph(ParamValue value) {
        var slot = latest!.SlotOf(value.Block);
        if (slot < 0) {
            return (null, -1);
        }

        var block = latest.Block(slot);
        return (block.Params, block.Descriptor.IndexOf(value.Param));
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

    HostTelemetry CollectTelemetry() {
        while (Engine.CollectRetired() is { } retired) {
            retired.Dispose();
        }

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
        var tunerReading = reading is { } r ? new HostTunerReading(r.Frequency, r.Note, r.Octave, r.Cents) : null;
        var stats = Engine.Stats;
        overloads += stats.Overruns - overrunsSeen;
        overrunsSeen = stats.Overruns;
        var result = new HostTelemetry(
            input,
            left,
            right,
            stats.Percentile(0.999),
            stats.MaxPercent,
            overloads,
            tunerReading,
            deviceWindow.Overloads,
            deviceWindow.P999,
            deviceWindow.Max,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalPauseDuration().TotalMilliseconds,
            GC.GetTotalAllocatedBytes(),
            Engine.Tempo,
            Engine.Looper is { } looper
                ? new(
                    looper.State.ToString().ToLowerInvariant(),
                    looper.Length / Engine.Spec.SampleRate,
                    looper.Position / Engine.Spec.SampleRate,
                    looper.IsReversed,
                    looper.IsHalfSpeed
                )
                : null
        );
        if (Stopwatch.GetElapsedTime(windowStarted) >= StatsWindow) {
            stats.RequestReset();
            overrunsSeen = 0;
            windowStarted = Stopwatch.GetTimestamp();
            if (device is not null) {
                // Read the device once per window: its histogram snapshot allocates, and this process must stay quiet.
                deviceWindow = device.CloseWindow();
            }
        }

        return result;
    }
}
