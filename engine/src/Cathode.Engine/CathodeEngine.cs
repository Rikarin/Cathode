using System.Diagnostics;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
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
///     <para>
///         Around the graph sits the rig (Cathode.Engine.Rig), which outlives preset changes: per sub-block the
///         controllers
///         advance and drive the graph's modifiers; after the graph come the scene level, the looper, the global EQ and
///         the metronome.
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
    const double TapWindowSeconds = 2.5;
    const int TapHistory = 4;

    readonly SpscRing<EngineCommand> commands;
    readonly CompiledGraph?[] retiredGraphs = new CompiledGraph?[RetiredSlots];
    readonly NativeBuffer tailInLeft;
    readonly NativeBuffer tailInRight;
    readonly NativeBuffer tailOutLeft;
    readonly NativeBuffer tailOutRight;
    readonly float swapStep;
    readonly long tailCap;
    readonly long tailQuiet;
    readonly Modulation modulation;
    readonly Metronome metronome;
    readonly long[] taps = new long[TapHistory];
    readonly int tempoIndex = RigControls.Descriptor.IndexOf("tempo");
    readonly int metronomeIndex = RigControls.Descriptor.IndexOf("metronome");
    readonly int metroLevelIndex = RigControls.Descriptor.IndexOf("metroLevel");
    readonly int effectsMixIndex = RigControls.Descriptor.IndexOf("effectsMix");
    readonly int gateOffsetIndex = RigControls.Descriptor.IndexOf("gateOffset");
    readonly int spilloverIndex = RigControls.Descriptor.IndexOf("spillover");
    readonly LoudnessMeter loudness;
    int tapCount;
    float levelGain = 1f;
    Looper? looper;
    CompiledGraph? tail;
    float tailInput; // gain into the spilling graph: 1 at the swap, falling to 0
    float swapGain = 1f; // gain on the new graph: 0 at the swap, rising to 1
    float tailOutput = 1f; // gain on the spilling graph's output: 1, or fading to 0 with spillover off
    bool tailCut; // spillover off: the old graph fades out with the swap instead of ringing out
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

    /// <summary>
    ///     The rig parameters (<see cref="RigControls" />): scene level, tempo, controller settings and sources. Targets
    ///     are writable from any thread, as for block parameters.
    /// </summary>
    public ParamBank Rig { get; }

    /// <summary>The global output EQ (block id <c>global</c>), applied to every preset.</summary>
    public GraphicEqBlock GlobalEq { get; } = new();

    /// <summary>The rig looper at the end of the chain, once <see cref="EnableLooper" /> has allocated it.</summary>
    public Looper? RigLooper => Volatile.Read(ref looper);

    /// <summary>
    ///     The looper the transport drives: the running graph's Looper block if it has one, otherwise the rig looper.
    ///     Looper commands, telemetry and export all go to this one.
    /// </summary>
    public Looper? Looper => Current?.Looper ?? RigLooper;

    /// <summary>Current tempo in BPM (the rig's <c>tempo</c>, which tap tempo also sets).</summary>
    public double Tempo {
        get => Rig.Current(tempoIndex);
        set => Rig.SetTarget(tempoIndex, (float)value);
    }

    /// <summary>The graph the audio thread is currently running (may lag a just-published graph by one callback).</summary>
    public CompiledGraph? Current => Volatile.Read(ref current);

    public long SampleTime => Volatile.Read(ref sampleTime);

    /// <summary>
    ///     Audio thread: the MIDI notes held down, from posted <see cref="EngineCommand.Note" /> commands. Every block
    ///     sees them in <see cref="BlockContext.Notes" />.
    /// </summary>
    public HeldNotes HeldNotes { get; private set; }

    /// <summary>
    ///     Input samples (left channel) for analysis on another thread, e.g. the tuner. Written by the audio thread only
    ///     while <see cref="InputTapEnabled" /> is set.
    /// </summary>
    /// <summary>
    ///     The device's effects-loop channels. Pass it to the <see cref="Graph.GraphCompiler" /> so FX Loop blocks reach
    ///     them.
    /// </summary>
    public HardwareLoop HardwareLoop { get; } = new();

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

    /// <summary>The output's BS.1770 loudness (momentary and short-term), measured after the global EQ.</summary>
    public LoudnessMeter Loudness => loudness;

    /// <summary>True while a replaced graph is still ringing out under the current one.</summary>
    public bool IsSpilling => Volatile.Read(ref tail) is not null;

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
        Rig = new(RigControls.Descriptor.Params);
        Rig.Prepare(spec.SampleRate);
        Rig.SnapToTargets();
        modulation = new(Rig, spec.SampleRate);
        metronome = new(spec.SampleRate);
        loudness = new(spec.SampleRate);
        GlobalEq.Prepare(spec);
    }

    /// <summary>
    ///     Control thread, before audio starts: allocate the rig looper (60 s of stereo is about 46 MB at 48 kHz).
    /// </summary>
    public void EnableLooper(double seconds = Looper.DefaultSeconds) =>
        Volatile.Write(ref looper, looper ?? new Looper(Spec.SampleRate, seconds));

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
        looper?.Dispose();
        GlobalEq.Dispose();
        HardwareLoop.Dispose();
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
            // A grid loop moves on to the next preset when that has a Looper block too, and keeps playing; otherwise it
            // belongs to its preset and stops (fading out in the spillover). A preset that brings its own looper stops
            // the rig's, which no longer gets the transport.
            var handedOver = previous?.LooperBlock is { } outgoing
                && outgoing.Looper is { State: not LooperState.Empty }
                && pending.LooperBlock is { } incoming
                && incoming.TakeLoop(outgoing);
            if (!handedOver) {
                previous?.Looper?.Apply(LooperAction.Stop);
            }

            if (pending.Looper is not null) {
                looper?.Apply(LooperAction.Stop);
            }

            if (previous is not null) {
                // The old graph spills over under the new one; a graph still spilling from an earlier swap retires now.
                // With spillover off it only fades out over the swap, a clean cut without tails.
                Retire(Interlocked.Exchange(ref tail, previous));
                tailInput = 1f;
                tailOutput = 1f;
                tailCut = Rig.GetTarget(spilloverIndex) < 0.5f;
                tailAge = 0;
                tailQuietFor = 0;
                swapGain = 0f;
            }
        }

        if (inputTapEnabled) {
            InputTap.Write(io.InputLeft);
        }

        HardwareLoop.BeginCallback(io.ReturnLeft, io.ReturnRight, io.Frames);

        var graph = current;
        while (commands.TryDequeue(out var command)) {
            Apply(graph, command);
        }

        if (graph is null) {
            io.OutputLeft.Clear();
            io.OutputRight.Clear();
        } else {
            var maxBlock = Spec.MaxBlockSize;
            for (var offset = 0; offset < io.Frames; offset += maxBlock) {
                var length = Math.Min(maxBlock, io.Frames - offset);
                var slice = io.Slice(offset, length);
                HardwareLoop.Offset = offset;
                modulation.Advance(slice.InputLeft, sampleTime);
                var context = new BlockContext(
                    Spec.SampleRate,
                    modulation.Tempo,
                    sampleTime,
                    HeldNotes,
                    Rig.GetTarget(effectsMixIndex),
                    Rig.GetTarget(gateOffsetIndex)
                );
                graph.ApplyModifiers(modulation, length);
                if (offset == 0) {
                    graph.ClearPeaks();
                }

                graph.Process(slice, context);
                if (graph.Analyzer is { } analyzer) {
                    var tapped = analyzer.Take();
                    if (!tapped.IsEmpty && Volatile.Read(ref telemetry) is { } analysis) {
                        analysis.WriteAnalyzer(tapped);
                    }
                }

                if (swapGain < 1f) {
                    swapGain = Ramp(slice.OutputLeft, slice.OutputRight, swapGain, 1f);
                }

                if (tail is not null) {
                    Spill(slice, context);
                }

                levelGain = RampTo(slice.OutputLeft, slice.OutputRight, levelGain, modulation.LevelGain);
                looper?.Process(slice.OutputLeft, slice.OutputRight);
                GlobalEq.Process(new(slice.OutputLeft, slice.OutputRight), context);
                loudness.Process(slice.OutputLeft, slice.OutputRight);
                if (Rig.Current(metronomeIndex) >= 0.5f) {
                    var click = MathF.Pow(10f, Rig.GetTarget(metroLevelIndex) / 20f);
                    metronome.Process(slice.OutputLeft, slice.OutputRight, sampleTime, modulation.Tempo, click);
                }

                Volatile.Write(ref sampleTime, sampleTime + length);
            }
        }

        if (!io.SendLeft.IsEmpty) {
            HardwareLoop.EndCallback(io.SendLeft, io.SendRight.IsEmpty ? io.SendLeft : io.SendRight);
        }

        var input = Peak(io.InputLeft);
        var left = Peak(io.OutputLeft);
        var right = Peak(io.OutputRight);
        UpdateMeters(input, left, right);
        var percent = Stats.Record(start, io.Frames, Spec.SampleRate);
        if (Volatile.Read(ref telemetry) is { } region) {
            region.WriteScope(io.OutputLeft);
            region.RecordCallback(
                io.Frames,
                percent,
                input,
                left,
                right,
                graph is null ? default : graph.BlockPeaks,
                graph?.Generation ?? -1,
                loudness.Momentary,
                loudness.ShortTerm
            );
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
        if (tailCut) {
            tailOutput = Ramp(outLeft, outRight, tailOutput, 0f);
        }

        var peak = 0f;
        for (var i = 0; i < length; i++) {
            io.OutputLeft[i] += outLeft[i];
            io.OutputRight[i] += outRight[i];
            peak = Math.Max(peak, Math.Max(Math.Abs(outLeft[i]), Math.Abs(outRight[i])));
        }

        tailAge += length;
        tailQuietFor = tailInput <= 0f && peak < TailSilence ? tailQuietFor + length : 0;
        if (tailQuietFor >= tailQuiet || tailAge >= tailCap || (tailCut && tailOutput <= 0f)) {
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

    void Apply(CompiledGraph? graph, in EngineCommand command) {
        switch (command.Kind) {
            case EngineCommandKind.SetBypass when graph is not null && command.Generation == graph.Generation:
                graph.SetBypass(command.Slot, command.Value != 0f);
                break;
            case EngineCommandKind.SetModifier when graph is not null && command.Generation == graph.Generation:
                graph.SetModifierEnabled(command.Slot, command.Value != 0f);
                break;
            case EngineCommandKind.TapTempo:
                Tap(command.Time);
                break;
            case EngineCommandKind.Looper:
                (graph?.Looper ?? looper)?.Apply((LooperAction)command.Slot);
                break;
            case EngineCommandKind.Note:
                HeldNotes = HeldNotes.With(command.Slot, command.Value != 0f);
                break;
            case EngineCommandKind.AllNotesOff:
                HeldNotes = default;
                break;
        }
    }

    /// <summary>
    ///     Tap tempo: the average interval of the taps that came within <see cref="TapWindowSeconds" /> of each other.
    ///     Taps carry sample times, so the result does not depend on when the command was drained.
    /// </summary>
    void Tap(long time) {
        var window = (long)(TapWindowSeconds * Spec.SampleRate);
        if (tapCount > 0
            && (time - taps[(tapCount - 1) % TapHistory] > window || time <= taps[(tapCount - 1) % TapHistory])) {
            tapCount = 0; // too long since the last tap (or out of order): start a new count
        }

        taps[tapCount % TapHistory] = time;
        tapCount++;
        var counted = Math.Min(tapCount, TapHistory);
        if (counted < 2) {
            return;
        }

        var first = taps[(tapCount - counted) % TapHistory];
        var interval = (double)(time - first) / (counted - 1);
        Rig.SetTarget(tempoIndex, (float)Math.Clamp(60 * Spec.SampleRate / interval, 30, 300));
    }

    /// <summary>A linear gain ramp from <paramref name="from" /> to <paramref name="to" /> across the block.</summary>
    static float RampTo(Span<float> left, Span<float> right, float from, float to) {
        if (from == to && to == 1f) {
            return to;
        }

        var step = (to - from) / left.Length;
        var gain = from;
        for (var i = 0; i < left.Length; i++) {
            gain += step;
            left[i] *= gain;
            right[i] *= gain;
        }

        return to;
    }
}
