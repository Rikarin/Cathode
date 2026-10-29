using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Engine.Telemetry;
using Cathode.Ipc;
using System.Diagnostics;

namespace Cathode.Engine;

/// <summary>
///     The real-time processing loop. The control side publishes graphs and posts commands; the audio thread calls
///     <see cref="Process" />, which picks up the latest graph, drains commands and renders.
/// </summary>
/// <remarks>
///     <para>
///         Graph hand-over is RCU-style: <see cref="Publish" /> stores a pending graph and the audio thread swaps it in
///         at the next callback boundary. Nothing is disposed on the audio thread: replaced graphs go to an unbounded,
///         allocation-free list (linked through the graphs themselves) that <see cref="CollectRetired" /> drains.
///     </para>
///     <para>
///         Swaps are gapless with <b>spillover</b> (docs/02-architecture.md §5.5): the new graph fades in over
///         <see cref="SwapFadeMs" /> while the old one keeps running on a fading input, so its delay and reverb tails
///         ring out underneath. The old graph retires once its output has stayed below -90 dB for 4.1 s (longer than
///         any delay repeat), or after <see cref="TailCapSeconds" />. A change that arrives while an older graph is
///         still spilling fades that one out over <see cref="SwapFadeMs" /> instead of cutting its tail dead, so for
///         one fade three graphs run.
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

    const double TapWindowSeconds = 2.5;
    const int TapHistory = 4;
    const int Outputs = NodeTypes.OutputCount;

    readonly SpscRing<EngineCommand> commands;
    readonly Lock collectLock = new();
    CompiledGraph? retired; // pushed by the audio thread, newest first
    CompiledGraph? collected; // taken by the control side, handed out one at a time
    readonly Spillover[] spills;
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
    readonly int inputGainIndex = RigControls.Descriptor.IndexOf("in1Gain");
    readonly int calibrationIndex = RigControls.Descriptor.IndexOf("in1Cal");
    readonly int copyIndex = RigControls.Descriptor.IndexOf("copyOut2");
    readonly int muteIndex = RigControls.Descriptor.IndexOf("mute");
    readonly int tunerIndex = RigControls.Descriptor.IndexOf("tuner");
    readonly int tunerOutIndex = RigControls.Descriptor.IndexOf("tunerOut");

    readonly int[] modeIndex =
        [..Enumerable.Range(1, Outputs).Select(static k => RigControls.Descriptor.IndexOf($"out{k}Mode"))];

    readonly int[] trimIndex =
        [..Enumerable.Range(1, Outputs).Select(static k => RigControls.Descriptor.IndexOf($"out{k}Level"))];

    readonly GraphicEqBlock[] outputEqs = [new(), new(), new(), new()];
    readonly float[] trims = [1f, 1f, 1f, 1f];
    readonly bool[] active = new bool[Outputs];
    readonly float[] outputPeak = new float[Outputs];
    readonly float[] publishedPeak = new float[Outputs];
    readonly GraphOutputs graphOutputs;
    readonly NativeBuffer gainedLeft;
    readonly NativeBuffer gainedRight;
    float inputGain = 1f;
    bool copyToSecond;
    int outputPeakReset;
    readonly int cabsOffIndex = RigControls.Descriptor.IndexOf("cabsOff");
    readonly int spilloverIndex = RigControls.Descriptor.IndexOf("spillover");
    readonly LoudnessMeter loudness;
    int tapCount;
    float levelGain = 1f;
    float muteGain = 1f; // the mute's fade, 0 when muted
    float dryMix; // the tuner's Bypass crossfade, 1 when the outputs play the dry input
    Looper? looper;
    Spillover tail; // the graph replaced by the latest swap, ringing out
    Spillover evicted; // the graph that was spilling when another swap came, fading out over one swap fade
    float swapGain = 1f; // gain on the new graph: 0 at the swap, rising to 1
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
    ///     The rig parameters (<see cref="RigControls" />): scene level, tempo, controller settings and sources.
    ///     Targets are writable from any thread, as for block parameters.
    /// </summary>
    public ParamBank Rig { get; }

    /// <summary>Output 1's global EQ (block id <c>global</c>), applied to every preset.</summary>
    public GraphicEqBlock GlobalEq => outputEqs[0];

    /// <summary>
    ///     The cab resonance helper's sine: the host points it at a block of the running graph, whose output it then
    ///     replaces. Never part of a preset.
    /// </summary>
    public TestTone TestTone { get; } = new();

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

    /// <summary>
    ///     The graph the audio thread is currently running (may lag a just-published graph by one callback).
    /// </summary>
    public CompiledGraph? Current => Volatile.Read(ref current);

    public long SampleTime => Volatile.Read(ref sampleTime);

    /// <summary>
    ///     Audio thread: the MIDI notes held down, from posted <see cref="EngineCommand.Note" /> commands. Every block
    ///     sees them in <see cref="BlockContext.Notes" />.
    /// </summary>
    public HeldNotes HeldNotes { get; private set; }

    /// <summary>
    ///     Input samples (left channel) for analysis on another thread, e.g. the tuner. Written by the audio thread
    ///     only while <see cref="InputTapEnabled" /> is set.
    /// </summary>
    /// <summary>
    ///     The device's effects-loop channels. Pass it to the <see cref="Graph.GraphCompiler" /> so FX Loop blocks
    ///     reach them.
    /// </summary>
    public HardwareLoop HardwareLoop { get; } = new();

    /// <summary>
    ///     The device's input sources (Input 1–4, USB 5/6 and 7/8). Pass it to the <see cref="Graph.GraphCompiler" />
    ///     so blocks with a hardware input choice reach them.
    /// </summary>
    public HardwareInputs Inputs { get; } = new();

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
    public bool IsSpilling =>
        Volatile.Read(ref spills[0].Graph) is not null || Volatile.Read(ref spills[1].Graph) is not null;

    public CathodeEngine(ProcessSpec spec, int commandCapacity = 1024) {
        Spec = spec;
        commands = new(commandCapacity);
        spills = [new(spec.MaxBlockSize), new(spec.MaxBlockSize)];
        tail = spills[0];
        evicted = spills[1];
        swapStep = (float)(1.0 / Math.Max(1.0, SwapFadeMs * 0.001 * spec.SampleRate));
        tailCap = (long)(TailCapSeconds * spec.SampleRate);
        tailQuiet = (long)(TailQuietMs * 0.001 * spec.SampleRate);
        Rig = new(RigControls.Descriptor.Params);
        Rig.Prepare(spec.SampleRate);
        Rig.SnapToTargets();
        modulation = new(Rig, spec.SampleRate);
        metronome = new(spec.SampleRate);
        foreach (var eq in outputEqs) {
            eq.Prepare(spec);
        }

        loudness = new(spec.SampleRate);
        graphOutputs = new(spec.MaxBlockSize);
        gainedLeft = new(spec.MaxBlockSize);
        gainedRight = new(spec.MaxBlockSize);
    }

    /// <summary>
    ///     Output <paramref name="output" />'s global EQ (0 = Output 1, block id <c>global</c>; 1-3 =
    ///     <c>global2</c>..).
    /// </summary>
    public GraphicEqBlock OutputEq(int output) => outputEqs[output];

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
    ///     Control side: take ownership of one graph the audio thread has finished with, or null. Call it until it
    ///     returns null; the caller disposes what it gets.
    /// </summary>
    public CompiledGraph? CollectRetired() {
        lock (collectLock) {
            collected ??= Interlocked.Exchange(ref retired, null);
            var graph = collected;
            if (graph is not null) {
                collected = graph.NextRetired;
                graph.NextRetired = null;
            }

            return graph;
        }
    }

    /// <summary>
    ///     Control side, after the audio has stopped: frees the engine's buffers and any graphs it still holds.
    /// </summary>
    public void Dispose() {
        while (CollectRetired() is { } graph) {
            graph.Dispose();
        }

        foreach (var spill in spills) {
            Interlocked.Exchange(ref spill.Graph, null)?.Dispose();
            spill.Dispose();
        }

        looper?.Dispose();
        foreach (var eq in outputEqs) {
            eq.Dispose();
        }

        graphOutputs.Dispose();
        gainedLeft.Dispose();
        gainedRight.Dispose();
        HardwareLoop.Dispose();
        Inputs.Dispose();
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
            SwapIn(pending);
        }

        if (inputTapEnabled) {
            InputTap.Write(io.InputLeft);
        }

        HardwareLoop.BeginCallback(io.ReturnLeft, io.ReturnRight, io.Frames);
        Inputs.BeginCallback(io.InputLeft, io.InputRight, io.Sources, io.SourceMask, io.Frames);
        var bus = io.Outputs;
        if (bus is not null) {
            BeginOutputs(io, bus);
        }

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
                RenderSubBlock(graph, Gained(io.Slice(offset, length)), bus, offset, length);
            }
        }

        if (!io.SendLeft.IsEmpty) {
            HardwareLoop.EndCallback(io.SendLeft, io.SendRight.IsEmpty ? io.SendLeft : io.SendRight);
        }

        var input = Peak(io.InputLeft);
        var left = Peak(io.OutputLeft);
        var right = Peak(io.OutputRight);
        UpdateMeters(input, left, right);
        outputPeak[0] = Math.Max(outputPeak[0], Math.Max(left, right));
        PublishOutputPeaks();
        var percent = Stats.Record(start, io.Frames, Spec.SampleRate);
        if (Volatile.Read(ref telemetry) is { } region) {
            WriteTelemetry(region, graph, io, percent, input, left, right);
        }
    }

    /// <summary>Audio thread: the published graph takes over from the running one, which spills over.</summary>
    void SwapIn(CompiledGraph pending) {
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
            SpillOver(previous);
        }
    }

    /// <summary>
    ///     The old graph spills over under the new one. A graph still spilling from an earlier swap fades out over one
    ///     swap fade rather than stopping dead; only a third change inside that fade retires it at once. With spillover
    ///     off the old graph only fades out over the swap, a clean cut without tails.
    /// </summary>
    void SpillOver(CompiledGraph previous) {
        previous.Inputs?.FadeOut(); // like the grid input, the hardware inputs fade out of the old graph
        if (tail.Graph is not null) {
            Retire(Interlocked.Exchange(ref evicted.Graph, null));
            (tail, evicted) = (evicted, tail);
            evicted.Cut = true;
        }

        tail.Start(previous, Rig.GetTarget(spilloverIndex) < 0.5f);
        swapGain = 0f;
    }

    /// <summary>
    ///     One sub-block (at most <see cref="ProcessSpec.MaxBlockSize" /> frames at <paramref name="offset" />): the
    ///     graph, the spilling graphs, then the rig's level, looper, outputs and metronome.
    /// </summary>
    void RenderSubBlock(CompiledGraph graph, in AudioIO slice, OutputBus? bus, int offset, int length) {
        HardwareLoop.Offset = offset;
        Inputs.Offset = offset;
        modulation.Advance(slice.InputLeft, sampleTime);
        var context = new BlockContext(
            Spec.SampleRate,
            modulation.Tempo,
            sampleTime,
            HeldNotes,
            Rig.GetTarget(effectsMixIndex),
            Rig.GetTarget(gateOffsetIndex),
            Rig.GetTarget(cabsOffIndex) >= 0.5f,
            Rig.GetTarget(calibrationIndex)
        );
        FindActiveOutputs(graph, bus);
        graphOutputs.Clear(length);
        graph.ApplyModifiers(modulation, length);
        if (offset == 0) {
            graph.ClearPeaks();
        }

        graph.Process(slice, context, graphOutputs);
        if (graph.Analyzer is { } analyzer) {
            WriteAnalyzer(analyzer);
        }

        FadeInGraph(slice, length);
        if (evicted.Graph is not null) {
            Spill(evicted, slice, context);
        }

        if (tail.Graph is not null) {
            Spill(tail, slice, context);
        }

        ApplyLevel(slice, length);
        looper?.Process(slice.OutputLeft, slice.OutputRight);
        if (active[1] && copyToSecond) {
            slice.OutputLeft.CopyTo(graphOutputs.Left(1, length));
            slice.OutputRight.CopyTo(graphOutputs.Right(1, length));
        }

        FinishOutput(0, slice.OutputLeft, slice.OutputRight, context);
        loudness.Process(slice.OutputLeft, slice.OutputRight);
        if (Rig.Current(metronomeIndex) >= 0.5f) {
            var click = MathF.Pow(10f, Rig.GetTarget(metroLevelIndex) / 20f);
            metronome.Process(slice.OutputLeft, slice.OutputRight, sampleTime, modulation.Tempo, click);
        }

        MuteAndFinishOutputs(slice, bus, offset, length, context);
        Volatile.Write(ref sampleTime, sampleTime + length);
    }

    void WriteAnalyzer(AnalyzerTap analyzer) {
        var tapped = analyzer.Take();
        if (!tapped.IsEmpty && Volatile.Read(ref telemetry) is { } analysis) {
            analysis.WriteAnalyzer(tapped);
        }
    }

    /// <summary>After a swap, the new graph's outputs fade in.</summary>
    void FadeInGraph(in AudioIO slice, int length) {
        if (swapGain >= 1f) {
            return;
        }

        var from = swapGain;
        swapGain = Ramp(slice.OutputLeft, slice.OutputRight, from, 1f);
        for (var k = 1; k < Outputs; k++) {
            if (active[k]) {
                Ramp(graphOutputs.Left(k, length), graphOutputs.Right(k, length), from, 1f);
            }
        }
    }

    /// <summary>The scene level, ramped, on every output.</summary>
    void ApplyLevel(in AudioIO slice, int length) {
        var level = levelGain;
        levelGain = RampTo(slice.OutputLeft, slice.OutputRight, level, modulation.LevelGain);
        for (var k = 1; k < Outputs; k++) {
            if (active[k]) {
                RampTo(graphOutputs.Left(k, length), graphOutputs.Right(k, length), level, levelGain);
            }
        }
    }

    /// <summary>
    ///     Mute: every output fades out (and back in) as a graph swap does, metronome and looper included. While the
    ///     tuner is on, its Tuner Output mutes the same way (Mute), crossfades every output to the dry input (Bypass)
    ///     or leaves them alone (Pass Through). Outputs 2-4 are finished and copied to the device.
    /// </summary>
    void MuteAndFinishOutputs(in AudioIO slice, OutputBus? bus, int offset, int length, in BlockContext context) {
        var tunerOut = Rig.GetTarget(tunerIndex) >= 0.5f
            ? (int)Rig.GetTarget(tunerOutIndex)
            : RigControls.TunerPassThrough;
        var dryFrom = dryMix;
        var dryTo = tunerOut == RigControls.TunerBypass ? 1f : 0f;
        var bypassing = dryFrom != 0f || dryTo != 0f;
        if (bypassing) {
            dryMix = Crossfade(slice.InputLeft, slice.InputRight, slice.OutputLeft, slice.OutputRight, dryFrom, dryTo);
        }

        var muteFrom = muteGain;
        var muteTo = Rig.GetTarget(muteIndex) >= 0.5f || tunerOut == RigControls.TunerMute ? 0f : 1f;
        var muting = muteFrom != 1f || muteTo != 1f;
        if (muting) {
            muteGain = Ramp(slice.OutputLeft, slice.OutputRight, muteFrom, muteTo);
        }

        for (var k = 1; k < Outputs; k++) {
            if (!active[k]) {
                continue;
            }

            var auxLeft = graphOutputs.Left(k, length);
            var auxRight = graphOutputs.Right(k, length);
            FinishOutput(k, auxLeft, auxRight, context);
            if (bypassing) {
                Crossfade(slice.InputLeft, slice.InputRight, auxLeft, auxRight, dryFrom, dryTo);
            }

            if (muting) {
                Ramp(auxLeft, auxRight, muteFrom, muteTo);
            }

            auxLeft.CopyTo(bus!.Left(k).Slice(offset, length));
            auxRight.CopyTo(bus.Right(k).Slice(offset, length));
            outputPeak[k] = Math.Max(outputPeak[k], Math.Max(Peak(auxLeft), Peak(auxRight)));
        }
    }

    void WriteTelemetry(
        TelemetryRegion region,
        CompiledGraph? graph,
        in AudioIO io,
        double percent,
        float input,
        float left,
        float right
    ) {
        graph?.CaptureMeters();
        region.WriteScope(io.OutputLeft);
        region.RecordCallback(
            io.Frames,
            percent,
            input,
            left,
            right,
            graph is null ? default : graph.Meters,
            graph is null ? default : graph.ModifierValues,
            graph?.Generation ?? -1,
            loudness.Momentary,
            loudness.ShortTerm
        );
        region.RecordControllers(
            new(
                graph is not null, // the controllers advance with the graph's sub-blocks
                modulation.SequencerStage,
                modulation.SequencerStages,
                modulation.Value(ModSource.Lfo1),
                modulation.Value(ModSource.Lfo2),
                modulation.Value(ModSource.Adsr1),
                modulation.Value(ModSource.Adsr2)
            )
        );
    }

    /// <summary>
    ///     Control side: the peak of each output (Output 1, then Outputs 2-4, the larger of left and right, after the
    ///     global EQ and Boost/Pad) since the previous call, into <paramref name="peaks" /> (at least four).
    /// </summary>
    public void ReadOutputPeaks(Span<float> peaks) {
        for (var k = 0; k < Outputs; k++) {
            peaks[k] = Volatile.Read(ref publishedPeak[k]);
        }

        Volatile.Write(ref outputPeakReset, 1);
    }

    /// <summary>
    ///     The DI out (the input as the converter gave it) and silence on Outputs 2-4 until they are rendered.
    /// </summary>
    static void BeginOutputs(in AudioIO io, OutputBus bus) {
        var frames = io.Frames;
        if (bus.HasDi) {
            io.InputLeft.CopyTo(bus.DiLeft);
            io.InputRight.CopyTo(bus.DiRight);
        }

        for (var k = 1; k < Outputs; k++) {
            if (bus.IsMapped(k)) {
                bus.Left(k)[..frames].Clear();
                bus.Right(k)[..frames].Clear();
            }
        }
    }

    /// <summary>
    ///     The sub-block with Input 1 Gain applied to its input (the DI out and the input meter are before it).
    /// </summary>
    AudioIO Gained(in AudioIO slice) {
        var length = slice.Frames;
        var left = gainedLeft.Span[..length];
        var right = gainedRight.Span[..length];
        slice.InputLeft.CopyTo(left);
        slice.InputRight.CopyTo(right);
        var target = Decibels.ToGain(Rig.GetTarget(inputGainIndex));
        GainRamp.Apply(left, inputGain, target);
        GainRamp.Apply(right, inputGain, target);
        inputGain = target;
        return new(left, right, slice.OutputLeft, slice.OutputRight);
    }

    /// <summary>
    ///     Which of Outputs 2-4 to render this sub-block: those the device plays that the running or spilling graph
    ///     feeds, and Output 2 when it copies Output 1 (unless the grid has an Output 2 of its own).
    /// </summary>
    void FindActiveOutputs(CompiledGraph graph, OutputBus? bus) {
        var spilling = tail.Graph;
        var fading = evicted.Graph;
        copyToSecond = Rig.GetTarget(copyIndex) >= 0.5f && !graph.HasOutput(1);
        for (var k = 1; k < Outputs; k++) {
            active[k] = bus is not null
                && bus.IsMapped(k)
                && (graph.HasOutput(k)
                    || spilling?.HasOutput(k) == true
                    || fading?.HasOutput(k) == true
                    || (k == 1 && copyToSecond));
        }
    }

    /// <summary>Output <paramref name="output" />'s mode, global EQ and Boost/Pad.</summary>
    void FinishOutput(int output, Span<float> left, Span<float> right, in BlockContext context) {
        switch ((int)Rig.GetTarget(modeIndex[output])) {
            case RigControls.SumLeftRight:
                for (var i = 0; i < left.Length; i++) {
                    var mono = 0.5f * (left[i] + right[i]);
                    left[i] = mono;
                    right[i] = mono;
                }

                break;
            case RigControls.CopyLeftToRight:
                left.CopyTo(right);
                break;
        }

        outputEqs[output].Process(new(left, right), context);
        var trim = Decibels.ToGain(Rig.GetTarget(trimIndex[output]));
        trims[output] = RampTo(left, right, trims[output], trim);
    }

    void PublishOutputPeaks() {
        if (Volatile.Read(ref outputPeakReset) != 0) {
            Volatile.Write(ref outputPeakReset, 0);
            for (var k = 0; k < Outputs; k++) {
                Volatile.Write(ref publishedPeak[k], outputPeak[k]);
            }
        } else {
            for (var k = 0; k < Outputs; k++) {
                Volatile.Write(ref publishedPeak[k], Math.Max(publishedPeak[k], outputPeak[k]));
            }
        }

        Array.Clear(outputPeak);
    }

    /// <summary>Runs a replaced graph on a fading copy of the input and adds its outputs (the tails).</summary>
    void Spill(Spillover spill, in AudioIO io, in BlockContext context) {
        var length = io.Frames;
        var inLeft = spill.InLeft.Span[..length];
        var inRight = spill.InRight.Span[..length];
        var outLeft = spill.OutLeft.Span[..length];
        var outRight = spill.OutRight.Span[..length];
        if (spill.Input > 0f) {
            io.InputLeft.CopyTo(inLeft);
            io.InputRight.CopyTo(inRight);
            spill.Input = Ramp(inLeft, inRight, spill.Input, 0f);
        } else {
            inLeft.Clear();
            inRight.Clear();
        }

        var graph = spill.Graph!;
        var outputs = spill.Outputs;
        outputs.Clear(length);
        graph.Process(new(inLeft, inRight, outLeft, outRight), context, outputs);
        if (spill.Cut) {
            CutSpill(spill, graph, outLeft, outRight, length);
        }

        var peak = AddTail(io, outLeft, outRight);
        AddAuxTails(graph, outputs, length);
        spill.Age += length;
        spill.QuietFor = spill.Input <= 0f && peak < TailSilence ? spill.QuietFor + length : 0;
        if (spill.QuietFor >= tailQuiet || spill.Age >= tailCap || (spill.Cut && spill.Output <= 0f)) {
            Retire(Interlocked.Exchange(ref spill.Graph, null));
        }
    }

    /// <summary>A spilling graph that is being cut fades out on every output it feeds.</summary>
    void CutSpill(Spillover spill, CompiledGraph graph, Span<float> outLeft, Span<float> outRight, int length) {
        var cutFrom = spill.Output;
        spill.Output = Ramp(outLeft, outRight, spill.Output, 0f);
        for (var k = 1; k < Outputs; k++) {
            if (active[k] && graph.HasOutput(k)) {
                Ramp(spill.Outputs.Left(k, length), spill.Outputs.Right(k, length), cutFrom, 0f);
            }
        }
    }

    /// <summary>Adds a spilling graph's Output 1 to the main output; returns its peak.</summary>
    static float AddTail(in AudioIO io, ReadOnlySpan<float> outLeft, ReadOnlySpan<float> outRight) {
        var peak = 0f;
        for (var i = 0; i < outLeft.Length; i++) {
            io.OutputLeft[i] += outLeft[i];
            io.OutputRight[i] += outRight[i];
            peak = Math.Max(peak, Math.Max(Math.Abs(outLeft[i]), Math.Abs(outRight[i])));
        }

        return peak;
    }

    /// <summary>Adds a spilling graph's Outputs 2-4 to the running graph's.</summary>
    void AddAuxTails(CompiledGraph graph, GraphOutputs outputs, int length) {
        for (var k = 1; k < Outputs; k++) {
            if (active[k] && graph.HasOutput(k)) {
                Add(outputs.Left(k, length), graphOutputs.Left(k, length));
                Add(outputs.Right(k, length), graphOutputs.Right(k, length));
            }
        }
    }

    static void Add(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
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

    /// <summary>
    ///     Crossfades <paramref name="left" /> and <paramref name="right" /> towards the dry input, the dry share
    ///     moving from <paramref name="mix" /> towards <paramref name="target" /> at the swap fade's rate.
    /// </summary>
    float Crossfade(
        ReadOnlySpan<float> dryLeft,
        ReadOnlySpan<float> dryRight,
        Span<float> left,
        Span<float> right,
        float mix,
        float target
    ) {
        for (var i = 0; i < left.Length; i++) {
            mix = target > mix ? Math.Min(target, mix + swapStep) : Math.Max(target, mix - swapStep);
            // As two gains, so the ends are exact: the processed signal alone at 0, the dry input alone at 1.
            left[i] = left[i] * (1f - mix) + dryLeft[i] * mix;
            right[i] = right[i] * (1f - mix) + dryRight[i] * mix;
        }

        return mix;
    }

    /// <summary>
    ///     Audio thread: hand a graph to the control side. A lock-free push onto a list linked through the graphs, so
    ///     it never allocates and never drops one however many swaps come between two <see cref="CollectRetired" />
    ///     sweeps.
    /// </summary>
    void Retire(CompiledGraph? graph) {
        if (graph is null) {
            return;
        }

        CompiledGraph? head;
        do {
            head = Volatile.Read(ref retired);
            graph.NextRetired = head;
        } while (Interlocked.CompareExchange(ref retired, graph, head) != head);
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
                graph.SetModifierEnabled(command.Slot, float.IsNaN(command.Value), command.Value);
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

    /// <summary>A replaced graph ringing out under the current one, with its own buffers and fades.</summary>
    sealed class Spillover(int maxBlockSize) : IDisposable {
        public readonly NativeBuffer InLeft = new(maxBlockSize);
        public readonly NativeBuffer InRight = new(maxBlockSize);
        public readonly NativeBuffer OutLeft = new(maxBlockSize);
        public readonly NativeBuffer OutRight = new(maxBlockSize);
        public readonly GraphOutputs Outputs = new(maxBlockSize);
        public CompiledGraph? Graph;
        public float Input; // gain into the graph: 1 at the swap, falling to 0
        public float Output = 1f; // gain on its output: 1, or fading to 0 when cut
        public bool Cut; // fading out over one swap fade instead of ringing out
        public long Age;
        public long QuietFor;

        /// <summary>Audio thread: take over <paramref name="graph" /> as it is replaced.</summary>
        public void Start(CompiledGraph graph, bool cut) {
            Input = 1f;
            Output = 1f;
            Cut = cut;
            Age = 0;
            QuietFor = 0;
            Volatile.Write(ref Graph, graph);
        }

        public void Dispose() {
            InLeft.Dispose();
            InRight.Dispose();
            OutLeft.Dispose();
            OutRight.Dispose();
            Outputs.Dispose();
        }
    }
}
