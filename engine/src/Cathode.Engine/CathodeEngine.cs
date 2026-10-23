using System.Diagnostics;
using Cathode.Dsp;
using Cathode.Engine.Graph;

namespace Cathode.Engine;

/// <summary>
///     The real-time processing loop. The control side publishes graphs and posts commands; the audio thread calls
///     <see cref="Process" />, which picks up the latest graph, drains commands and renders.
/// </summary>
/// <remarks>
///     Graph hand-over is RCU-style: <see cref="Publish" /> stores a pending graph, the audio thread swaps it in at the
///     next callback boundary and parks the old one for <see cref="CollectRetired" />. Nothing is disposed on the
///     audio thread. (Crossfaded preset changes and spillover tails arrive in Phase 5.)
/// </remarks>
public sealed class CathodeEngine : IAudioProcessor {
    readonly SpscRing<EngineCommand> commands;
    readonly Lock postLock = new();
    CompiledGraph? pending;
    CompiledGraph? current;
    CompiledGraph? retired;
    long sampleTime;
    bool inputTapEnabled;
    float inputPeak;
    float outputPeakLeft;
    float outputPeakRight;
    int meterReset;

    public ProcessSpec Spec { get; }

    public CallbackStats Stats { get; } = new();

    public double Tempo { get; set; } = 120;

    /// <summary>The graph the audio thread is currently running (may lag a just-published graph by one callback).</summary>
    public CompiledGraph? Current => Volatile.Read(ref current);

    public long SampleTime => Volatile.Read(ref sampleTime);

    /// <summary>
    ///     Input samples (left channel) for analysis on another thread, e.g. the tuner. Written by the audio thread only
    ///     while <see cref="InputTapEnabled" /> is set.
    /// </summary>
    public SampleRing InputTap { get; } = new(16_384);

    /// <summary>Any thread: start or stop feeding <see cref="InputTap" />.</summary>
    public bool InputTapEnabled {
        get => Volatile.Read(ref inputTapEnabled);
        set => Volatile.Write(ref inputTapEnabled, value);
    }

    public CathodeEngine(ProcessSpec spec, int commandCapacity = 1024) {
        Spec = spec;
        commands = new(commandCapacity);
    }

    /// <summary>Control side: hand a prepared graph to the audio thread.</summary>
    public void Publish(CompiledGraph graph) {
        if (graph.Spec != Spec) {
            throw new ArgumentException("Graph was compiled for a different process spec.", nameof(graph));
        }

        // A graph that was still pending was never seen by the audio thread, so it is safe to dispose here.
        Interlocked.Exchange(ref pending, graph)?.Dispose();
    }

    /// <summary>Control side: take ownership of the graph the audio thread most recently replaced, if any.</summary>
    public CompiledGraph? CollectRetired() => Interlocked.Exchange(ref retired, null);

    /// <summary>Control side, any thread: queue a discrete event. Returns false when the queue is full.</summary>
    public bool Post(in EngineCommand command) {
        lock (postLock) {
            return commands.TryEnqueue(command);
        }
    }

    /// <summary>
    ///     Control side: peak levels since the previous call (input left, output left, output right). The audio thread
    ///     starts a new measurement period at its next callback.
    /// </summary>
    public (float Input, float OutputLeft, float OutputRight) ReadMeters() {
        var result = (Volatile.Read(ref inputPeak), Volatile.Read(ref outputPeakLeft),
            Volatile.Read(ref outputPeakRight));
        Volatile.Write(ref meterReset, 1);
        return result;
    }

    /// <summary>Audio thread.</summary>
    public void Process(in AudioIO io) {
        var start = Stopwatch.GetTimestamp();

        var pending = Interlocked.Exchange(ref this.pending, null);
        if (pending is not null) {
            var previous = current;
            Volatile.Write(ref current, pending);
            if (previous is not null) {
                // If the control side has not collected the last retired graph it is simply dropped to the GC.
                Interlocked.Exchange(ref retired, previous);
            }
        }

        if (inputTapEnabled) {
            InputTap.Write(io.InputLeft);
        }

        var graph = current;
        while (commands.TryDequeue(out var command)) {
            if (graph is not null && command.Generation == graph.Generation) {
                Apply(graph, command);
            }
        }

        if (graph is null) {
            io.OutputLeft.Clear();
            io.OutputRight.Clear();
        } else {
            var maxBlock = Spec.MaxBlockSize;
            for (var offset = 0; offset < io.Frames; offset += maxBlock) {
                var length = Math.Min(maxBlock, io.Frames - offset);
                graph.Process(io.Slice(offset, length), new(Spec.SampleRate, Tempo, sampleTime));
                Volatile.Write(ref sampleTime, sampleTime + length);
            }
        }

        UpdateMeters(io);
        Stats.Record(start, io.Frames, Spec.SampleRate);
    }

    void UpdateMeters(in AudioIO io) {
        if (Volatile.Read(ref meterReset) != 0) {
            inputPeak = 0;
            outputPeakLeft = 0;
            outputPeakRight = 0;
            Volatile.Write(ref meterReset, 0);
        }

        Volatile.Write(ref inputPeak, Math.Max(inputPeak, Peak(io.InputLeft)));
        Volatile.Write(ref outputPeakLeft, Math.Max(outputPeakLeft, Peak(io.OutputLeft)));
        Volatile.Write(ref outputPeakRight, Math.Max(outputPeakRight, Peak(io.OutputRight)));
    }

    static float Peak(ReadOnlySpan<float> samples) {
        var peak = 0f;
        foreach (var sample in samples) {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    static void Apply(CompiledGraph graph, in EngineCommand command) {
        switch (command.Kind) {
            case EngineCommandKind.SetBypass:
                graph.SetBypass(command.Slot, command.Value != 0f);
                break;
        }
    }
}
