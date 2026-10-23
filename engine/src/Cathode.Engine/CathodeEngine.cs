using System.Diagnostics;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.Engine.Telemetry;

namespace Cathode.Engine;

/// <summary>
///     The real-time processing loop. The control side publishes graphs and posts commands; the audio thread calls
///     <see cref="Process" />, which picks up the latest graph, drains commands and renders.
/// </summary>
/// <remarks>
///     <para>
///         Graph hand-over is RCU-style: <see cref="Publish" /> stores a pending graph and the audio thread swaps it in at
///         the next callback boundary. Nothing is disposed on the audio thread: replaced graphs go to a small queue that
///         <see cref="CollectRetired" /> drains.
///     </para>
///     <para>
///         Swaps are gapless with <b>spillover</b> (docs/02-architecture.md §5.5): the new graph fades in over
///         <see cref="SwapFadeMs" /> while the old one keeps running on a fading input, so its delay and reverb tails ring
///         out underneath. The old graph retires once its output has stayed below -90 dB for 4.1 s (longer than any delay
///         repeat), or after <see cref="TailCapSeconds" />.
///     </para>
/// </remarks>
public sealed class CathodeEngine : IAudioProcessor, IDisposable {
    public const double SwapFadeMs = 20;
    public const double TailCapSeconds = 12;
    const float TailSilence = 3.2e-5f; // -90 dBFS
    /// <summary>
    ///     How long a spilling graph must stay silent before it retires. A delay is silent between repeats, so this has
    ///     to outlast the longest delay time (DelayBlock.MaxTimeMs, 4 s), or a tail would be cut between two echoes.
    /// </summary>
    const double TailQuietMs = 4_100;
    const int RetiredSlots = 4;

    readonly SpscRing<EngineCommand> commands;
    readonly CompiledGraph?[] retiredGraphs = new CompiledGraph?[RetiredSlots];
    readonly NativeBuffer tailInLeft;
    readonly NativeBuffer tailInRight;
    readonly NativeBuffer tailOutLeft;
    readonly NativeBuffer tailOutRight;
    readonly float swapStep;
    readonly long tailCap;
    readonly long tailQuiet;
    CompiledGraph? tail;
    float tailInput; // gain into the spilling graph: 1 at the swap, falling to 0
    float swapGain = 1f; // gain on the new graph: 0 at the swap, rising to 1
    long tailAge;
    long tailQuietFor;
    readonly Lock postLock = new();
    CompiledGraph? pending;
    CompiledGraph? current;
    long sampleTime;
    bool inputTapEnabled;
    float inputPeak;
    float outputPeakLeft;
    float outputPeakRight;
    int meterReset;
    TelemetryRegion? telemetry;

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

    /// <summary>
    ///     Shared-memory telemetry the audio thread writes after every callback. Set it before audio starts, or from
    ///     any thread while running; the engine never owns or disposes it.
    /// </summary>
    public TelemetryRegion? Telemetry {
        get => Volatile.Read(ref telemetry);
        set => Volatile.Write(ref telemetry, value);
    }

    public CathodeEngine(ProcessSpec spec, int commandCapacity = 1024) {
        Spec = spec;
        commands = new(commandCapacity);
        tailInLeft = new(spec.MaxBlockSize);
        tailInRight = new(spec.MaxBlockSize);
        tailOutLeft = new(spec.MaxBlockSize);
        tailOutRight = new(spec.MaxBlockSize);
        swapStep = (float)(1.0 / Math.Max(1.0, SwapFadeMs * 0.001 * spec.SampleRate));
        tailCap = (long)(TailCapSeconds * spec.SampleRate);
        tailQuiet = (long)(TailQuietMs * 0.001 * spec.SampleRate);
    }

    /// <summary>True while a replaced graph is still ringing out under the current one.</summary>
    public bool IsSpilling => Volatile.Read(ref tail) is not null;

    /// <summary>Control side: hand a prepared graph to the audio thread.</summary>
    public void Publish(CompiledGraph graph) {
        if (graph.Spec != Spec) {
            throw new ArgumentException("Graph was compiled for a different process spec.", nameof(graph));
        }

        // A graph that was still pending was never seen by the audio thread, so it is safe to dispose here.
        Interlocked.Exchange(ref pending, graph)?.Dispose();
    }

    /// <summary>
    ///     Control side: take ownership of one graph the audio thread has finished with, or null. Call it until it returns
    ///     null; the caller disposes what it gets.
    /// </summary>
    public CompiledGraph? CollectRetired() {
        for (var i = 0; i < RetiredSlots; i++) {
            if (Interlocked.Exchange(ref retiredGraphs[i], null) is { } graph) {
                return graph;
            }
        }

        return null;
    }

    /// <summary>Control side, after the audio has stopped: frees the engine's buffers and any graphs it still holds.</summary>
    public void Dispose() {
        while (CollectRetired() is { } graph) {
            graph.Dispose();
        }

        Interlocked.Exchange(ref tail, null)?.Dispose();
        tailInLeft.Dispose();
        tailInRight.Dispose();
        tailOutLeft.Dispose();
        tailOutRight.Dispose();
    }

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
                // The old graph spills over under the new one; a graph still spilling from an earlier swap retires now.
                Retire(Interlocked.Exchange(ref tail, previous));
                tailInput = 1f;
                tailAge = 0;
                tailQuietFor = 0;
                swapGain = 0f;
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
                var slice = io.Slice(offset, length);
                var context = new BlockContext(Spec.SampleRate, Tempo, sampleTime);
                graph.Process(slice, context);
                if (swapGain < 1f) {
                    swapGain = Ramp(slice.OutputLeft, slice.OutputRight, swapGain, 1f);
                }

                if (tail is not null) {
                    Spill(slice, context);
                }

                Volatile.Write(ref sampleTime, sampleTime + length);
            }
        }

        var input = Peak(io.InputLeft);
        var left = Peak(io.OutputLeft);
        var right = Peak(io.OutputRight);
        UpdateMeters(input, left, right);
        var percent = Stats.Record(start, io.Frames, Spec.SampleRate);
        if (Volatile.Read(ref telemetry) is { } region) {
            region.WriteScope(io.OutputLeft);
            region.RecordCallback(io.Frames, percent, input, left, right);
        }
    }

    /// <summary>Runs the replaced graph on a fading copy of the input and adds its output (the tails).</summary>
    void Spill(in AudioIO io, in BlockContext context) {
        var length = io.Frames;
        var inLeft = tailInLeft.Span[..length];
        var inRight = tailInRight.Span[..length];
        var outLeft = tailOutLeft.Span[..length];
        var outRight = tailOutRight.Span[..length];
        if (tailInput > 0f) {
            io.InputLeft.CopyTo(inLeft);
            io.InputRight.CopyTo(inRight);
            tailInput = Ramp(inLeft, inRight, tailInput, 0f);
        } else {
            inLeft.Clear();
            inRight.Clear();
        }

        tail!.Process(new(inLeft, inRight, outLeft, outRight), context);
        var peak = 0f;
        for (var i = 0; i < length; i++) {
            io.OutputLeft[i] += outLeft[i];
            io.OutputRight[i] += outRight[i];
            peak = Math.Max(peak, Math.Max(Math.Abs(outLeft[i]), Math.Abs(outRight[i])));
        }

        tailAge += length;
        tailQuietFor = tailInput <= 0f && peak < TailSilence ? tailQuietFor + length : 0;
        if (tailQuietFor >= tailQuiet || tailAge >= tailCap) {
            Retire(Interlocked.Exchange(ref tail, null));
        }
    }

    /// <summary>Applies a linear gain ramp from <paramref name="gain" /> towards <paramref name="target" />.</summary>
    float Ramp(Span<float> left, Span<float> right, float gain, float target) {
        for (var i = 0; i < left.Length; i++) {
            gain = target > gain ? Math.Min(target, gain + swapStep) : Math.Max(target, gain - swapStep);
            left[i] *= gain;
            right[i] *= gain;
        }

        return gain;
    }

    /// <summary>Audio thread: hand a graph to the control side; if every slot is still full it is left to the GC.</summary>
    void Retire(CompiledGraph? graph) {
        if (graph is null) {
            return;
        }

        for (var i = 0; i < RetiredSlots; i++) {
            if (Interlocked.CompareExchange(ref retiredGraphs[i], graph, null) is null) {
                return;
            }
        }
    }

    void UpdateMeters(float input, float left, float right) {
        if (Volatile.Read(ref meterReset) != 0) {
            inputPeak = 0;
            outputPeakLeft = 0;
            outputPeakRight = 0;
            Volatile.Write(ref meterReset, 0);
        }

        Volatile.Write(ref inputPeak, Math.Max(inputPeak, input));
        Volatile.Write(ref outputPeakLeft, Math.Max(outputPeakLeft, left));
        Volatile.Write(ref outputPeakRight, Math.Max(outputPeakRight, right));
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
