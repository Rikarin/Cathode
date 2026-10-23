using System.Diagnostics;
using System.Reflection;
using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Dsp.Tuning;
using Cathode.Engine;
using Cathode.Engine.Graph;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     Executes IPC requests against a <see cref="CathodeEngine" /> (docs/adr/0004-state-ownership.md): compiles and
///     publishes graphs, sets parameter targets, posts bypass commands, runs the tuner and gathers telemetry. Called
///     from control threads only; one lock serialises requests and telemetry.
/// </summary>
public sealed class EngineHostService : IDisposable {
    readonly GraphCompiler compiler;
    readonly Tuner tuner;
    readonly float[] tap = new float[4_096];
    /// <summary>CPU figures describe the last few seconds, not the process lifetime (which would keep start-up spikes forever).</summary>
    static readonly TimeSpan StatsWindow = TimeSpan.FromSeconds(5);

    readonly Lock gate = new();
    CompiledGraph? latest;
    TunerReading? reading;
    long windowStarted = Stopwatch.GetTimestamp();
    long overloads; // cumulative across windows
    long overrunsSeen; // overruns already counted in the current window

    public CathodeEngine Engine { get; }

    public HostInfo Info { get; }

    public EngineHostService(ProcessSpec spec, string backend, BlockRegistry? registry = null) {
        Engine = new(spec);
        compiler = new(registry ?? BlockRegistry.CreateDefault().AddNativeBlocks());
        tuner = new(spec.SampleRate);
        var version = typeof(EngineHostService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        Info = new(version, backend, spec.SampleRate, spec.MaxBlockSize);
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
        lock (gate) {
            Engine.CollectRetired()?.Dispose();
            latest?.Dispose();
            latest = null;
        }
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
            return IpcMessage.Fail(request.Id, "The graph is invalid.", [.. e.Errors]);
        }
    }

    IpcMessage SetParams(IpcMessage request) {
        if (latest is null || request.Generation != latest.Generation) {
            return IpcMessage.Fail(request.Id, $"Stale graph generation {request.Generation}.");
        }

        var errors = new List<string>();
        foreach (var value in request.Params ?? []) {
            var slot = latest.SlotOf(value.Block);
            var index = slot < 0 ? -1 : latest.Block(slot).Descriptor.IndexOf(value.Param);
            if (index < 0) {
                errors.Add($"No parameter '{value.Param}' on block '{value.Block}'.");
                continue;
            }

            latest.SetParameter(slot, index, value.Value);
        }

        return errors.Count == 0 ? IpcMessage.Ok(request.Id) : IpcMessage.Fail(request.Id, "Some parameters were not set.", errors);
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
        Engine.InputTapEnabled = request.Enabled == true;
        if (request.Enabled != true) {
            reading = null;
        }

        return IpcMessage.Ok(request.Id);
    }

    HostTelemetry CollectTelemetry() {
        Engine.CollectRetired()?.Dispose();

        if (Engine.InputTapEnabled) {
            int read;
            while ((read = Engine.InputTap.Read(tap)) > 0) {
                tuner.Push(tap.AsSpan(0, read));
            }

            reading = tuner.Detect();
        }

        var (input, left, right) = Engine.ReadMeters();
        var tunerReading = reading is { } r ? new HostTunerReading(r.Frequency, r.Note, r.Octave, r.Cents) : null;
        var stats = Engine.Stats;
        overloads += stats.Overruns - overrunsSeen;
        overrunsSeen = stats.Overruns;
        var result = new HostTelemetry(input, left, right, stats.Percentile(0.999), stats.MaxPercent, overloads, tunerReading);
        if (Stopwatch.GetElapsedTime(windowStarted) >= StatsWindow) {
            stats.RequestReset();
            overrunsSeen = 0;
            windowStarted = Stopwatch.GetTimestamp();
        }

        return result;
    }
}
