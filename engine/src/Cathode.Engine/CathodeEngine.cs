using System.Diagnostics;
using Cathode.Dsp;
using Cathode.Engine.Graph;

namespace Cathode.Engine;

/// <summary>
/// The real-time processing loop. The control side publishes graphs and posts commands; the audio thread calls
/// <see cref="Process"/>, which picks up the latest graph, drains commands and renders.
/// </summary>
/// <remarks>
/// Graph hand-over is RCU-style: <see cref="Publish"/> stores a pending graph, the audio thread swaps it in at the
/// next callback boundary and parks the old one for <see cref="CollectRetired"/>. Nothing is disposed on the
/// audio thread. (Crossfaded preset changes and spillover tails arrive in Phase 5.)
/// </remarks>
public sealed class CathodeEngine : IAudioProcessor
{
    private readonly SpscRing<EngineCommand> _commands;
    private readonly Lock _postLock = new();
    private CompiledGraph? _pending;
    private CompiledGraph? _current;
    private CompiledGraph? _retired;
    private long _sampleTime;
    private bool _inputTapEnabled;

    public CathodeEngine(ProcessSpec spec, int commandCapacity = 1024)
    {
        Spec = spec;
        _commands = new SpscRing<EngineCommand>(commandCapacity);
    }

    public ProcessSpec Spec { get; }

    public CallbackStats Stats { get; } = new();

    public double Tempo { get; set; } = 120;

    /// <summary>The graph the audio thread is currently running (may lag a just-published graph by one callback).</summary>
    public CompiledGraph? Current => Volatile.Read(ref _current);

    public long SampleTime => Volatile.Read(ref _sampleTime);

    /// <summary>
    /// Input samples (left channel) for analysis on another thread, e.g. the tuner. Written by the audio thread only
    /// while <see cref="InputTapEnabled"/> is set.
    /// </summary>
    public SampleRing InputTap { get; } = new(16_384);

    /// <summary>Any thread: start or stop feeding <see cref="InputTap"/>.</summary>
    public bool InputTapEnabled
    {
        get => Volatile.Read(ref _inputTapEnabled);
        set => Volatile.Write(ref _inputTapEnabled, value);
    }

    /// <summary>Control side: hand a prepared graph to the audio thread.</summary>
    public void Publish(CompiledGraph graph)
    {
        if (graph.Spec != Spec)
        {
            throw new ArgumentException("Graph was compiled for a different process spec.", nameof(graph));
        }

        // A graph that was still pending was never seen by the audio thread, so it is safe to dispose here.
        Interlocked.Exchange(ref _pending, graph)?.Dispose();
    }

    /// <summary>Control side: take ownership of the graph the audio thread most recently replaced, if any.</summary>
    public CompiledGraph? CollectRetired() => Interlocked.Exchange(ref _retired, null);

    /// <summary>Control side, any thread: queue a discrete event. Returns false when the queue is full.</summary>
    public bool Post(in EngineCommand command)
    {
        lock (_postLock)
        {
            return _commands.TryEnqueue(command);
        }
    }

    /// <summary>Audio thread.</summary>
    public void Process(in AudioIO io)
    {
        var start = Stopwatch.GetTimestamp();

        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending is not null)
        {
            var previous = _current;
            Volatile.Write(ref _current, pending);
            if (previous is not null)
            {
                // If the control side has not collected the last retired graph it is simply dropped to the GC.
                Interlocked.Exchange(ref _retired, previous);
            }
        }

        if (_inputTapEnabled)
        {
            InputTap.Write(io.InputLeft);
        }

        var graph = _current;
        while (_commands.TryDequeue(out var command))
        {
            if (graph is not null && command.Generation == graph.Generation)
            {
                Apply(graph, command);
            }
        }

        if (graph is null)
        {
            io.OutputLeft.Clear();
            io.OutputRight.Clear();
        }
        else
        {
            var maxBlock = Spec.MaxBlockSize;
            for (var offset = 0; offset < io.Frames; offset += maxBlock)
            {
                var length = Math.Min(maxBlock, io.Frames - offset);
                graph.Process(io.Slice(offset, length), new BlockContext(Spec.SampleRate, Tempo, _sampleTime));
                Volatile.Write(ref _sampleTime, _sampleTime + length);
            }
        }

        Stats.Record(start, io.Frames, Spec.SampleRate);
    }

    private static void Apply(CompiledGraph graph, in EngineCommand command)
    {
        switch (command.Kind)
        {
            case EngineCommandKind.SetBypass:
                graph.SetBypass(command.Slot, command.Value != 0f);
                break;
        }
    }
}
