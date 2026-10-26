using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Looping;
using Cathode.Dsp.Routing;
using Cathode.Engine.Rig;

namespace Cathode.Engine.Graph;

/// <summary>The compiled form of <see cref="BypassModes" />.</summary>
public enum BypassKind {
    Thru,
    Mute,
    MuteIn,
    MuteFxIn,
    MuteOut,
    MuteFxOut
}

/// <summary>
///     An immutable execution plan plus all the state it needs: block instances, work buffers and delay lines.
///     Built and prepared on the control thread; <see cref="Process" /> runs on the audio thread and never allocates.
/// </summary>
public sealed class CompiledGraph : IDisposable {
    /// <summary>Length of the bypass crossfade.</summary>
    public const double BypassFadeMs = 10;

    readonly GraphOp[] ops;
    readonly IDspBlock[] blocks;
    readonly string[] slotIds;
    readonly Dictionary<string, int> slotById;
    readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> slotByName;
    readonly DelayLine[] delays;
    readonly NativeBuffer[] left;
    readonly NativeBuffer[] right;
    readonly NativeBuffer dryLeft;
    readonly NativeBuffer dryRight;
    readonly BypassState[] bypass;
    readonly float fadeStep;
    readonly ModifierBinding[] modifiers;
    readonly float[] modifierValues;
    readonly ILooperBlock? looperBlock;
    readonly bool[] outputs = new bool[NodeTypes.OutputCount];
    readonly float[] meters;
    readonly int[] meterOffsets;
    readonly string[] meterLayout;
    readonly IBlockMeters[] ownMeters;
    readonly int[] ownOffsets;

    /// <summary>The standard meter every block has, first in its <see cref="MeterIds" />: its output peak (linear).</summary>
    public const string PeakMeter = "peak";

    /// <summary>Identifies this graph; commands addressed to another generation are ignored.</summary>
    public int Generation { get; }

    public ProcessSpec Spec { get; }

    /// <summary>Latency of the longest input-to-output path, in samples.</summary>
    public int LatencySamples { get; }

    public int BufferCount => left.Length;

    public IReadOnlyList<GraphOp> Ops => ops;

    public IReadOnlyList<string> SlotIds => slotIds;

    /// <summary>The looper of the graph's Looper block, or null: while it runs, the engine's looper transport drives it.</summary>
    public Looper? Looper => looperBlock?.Looper;

    /// <summary>The graph's Looper block, for handing a running loop over to the next preset's.</summary>
    public ILooperBlock? LooperBlock => looperBlock;

    /// <summary>
    ///     Audio thread: every block meter, slot by slot (<see cref="MeterOffset" />), each slot's <see cref="MeterIds" />
    ///     in order. The first of each slot is <see cref="PeakMeter" />, its output peak since <see cref="ClearPeaks" />
    ///     (0 for a block that did not run, such as a bypassed Thru block); the rest are the block's own
    ///     (<see cref="IBlockMeters" />) as of the last <see cref="CaptureMeters" />.
    /// </summary>
    public ReadOnlySpan<float> Meters => meters;

    /// <summary>How many values <see cref="Meters" /> holds.</summary>
    public int MeterCount => meters.Length;

    public IReadOnlyList<ModifierBinding> Modifiers => modifiers;

    /// <summary>
    ///     Audio thread: each modifier's value in its parameter's units, by index in <see cref="Modifiers" />, as of
    ///     the last <see cref="ApplyModifiers" />: NaN for one that is disabled or has not run yet, and for an engage
    ///     modifier (it drives the bypass, not a value). The telemetry region shows it, so knobs follow their modifier.
    /// </summary>
    public ReadOnlySpan<float> ModifierValues => modifierValues;

    /// <summary>The RTA block's tap, when the graph has one: the engine sends what it holds to the analyser.</summary>
    public AnalyzerTap? Analyzer { get; }

    /// <summary>The blocks on the longest input-to-output path, in order, with the latency each adds.</summary>
    public IReadOnlyList<LatencyStep> LatencyPath { get; init; } = [];

    /// <summary>The engine's test tone (the cab resonance helper), when the compiler was given one.</summary>
    public TestTone? Tone { get; init; }

    /// <summary>
    ///     The graph's view of the hardware inputs, when the compiler had them and a block reads one: the engine fades it
    ///     out when the graph is replaced.
    /// </summary>
    public InputPort? Inputs { get; }

    /// <summary>True when a modifier follows the input pitch, so the host must run pitch detection.</summary>
    public bool UsesPitch => modifiers.Any(static m => m.Source == ModSource.Pitch);

    internal CompiledGraph(
        int generation,
        ProcessSpec spec,
        GraphOp[] ops,
        int bufferCount,
        IDspBlock[] blocks,
        string[] slotIds,
        bool[] bypassed,
        BypassKind[] bypassModes,
        DelayLine[] delays,
        int latencySamples,
        ModifierBinding[]? modifiers = null,
        AnalyzerTap? analyzer = null,
        InputPort? inputs = null
    ) {
        this.modifiers = modifiers ?? [];
        modifierValues = new float[this.modifiers.Length];
        modifierValues.AsSpan().Fill(float.NaN);
        Analyzer = analyzer;
        Inputs = inputs;
        Generation = generation;
        Spec = spec;
        this.ops = ops;
        this.blocks = blocks;
        this.slotIds = slotIds;
        this.delays = delays;
        LatencySamples = latencySamples;
        slotById = new(StringComparer.Ordinal);
        for (var i = 0; i < slotIds.Length; i++) {
            slotById.Add(slotIds[i], i);
        }

        slotByName = slotById.GetAlternateLookup<ReadOnlySpan<char>>();
        looperBlock = blocks.OfType<ILooperBlock>().FirstOrDefault();
        foreach (var op in ops) {
            if (op.Kind == GraphOpKind.WriteOutput) {
                outputs[Math.Max(op.Index, 0)] = true;
            }
        }

        // Work buffers in native memory: pinned managed arrays would live on the pinned object heap, which only gen2
        // collections clean up, so every preset load would push the host towards a blocking gen2.
        left = new NativeBuffer[bufferCount];
        right = new NativeBuffer[bufferCount];
        for (var i = 0; i < bufferCount; i++) {
            left[i] = new(spec.MaxBlockSize);
            right[i] = new(spec.MaxBlockSize);
        }

        dryLeft = new(spec.MaxBlockSize);
        dryRight = new(spec.MaxBlockSize);
        fadeStep = (float)(1.0 / Math.Max(1.0, BypassFadeMs * 0.001 * spec.SampleRate));
        // Few arrays and no per-slot ones: a preset load's allocation is budgeted (HostAllocationTests).
        meterOffsets = new int[blocks.Length];
        var meterCount = 0;
        var metered = 0;
        for (var i = 0; i < blocks.Length; i++) {
            meterOffsets[i] = meterCount;
            meterCount++;
            if (blocks[i] is IBlockMeters own) {
                meterCount += own.MeterIds.Count;
                metered++;
            }
        }

        meters = new float[meterCount];
        meterLayout = new string[meterCount];
        ownMeters = new IBlockMeters[metered];
        ownOffsets = new int[metered];
        metered = 0;
        for (var i = 0; i < blocks.Length; i++) {
            meterLayout[meterOffsets[i]] = PeakMeter;
            if (blocks[i] is IBlockMeters own) {
                ownMeters[metered] = own;
                ownOffsets[metered++] = meterOffsets[i] + 1;
                for (var m = 0; m < own.MeterIds.Count; m++) {
                    meterLayout[meterOffsets[i] + 1 + m] = own.MeterIds[m];
                }
            }
        }

        bypass = new BypassState[blocks.Length];
        for (var i = 0; i < blocks.Length; i++) {
            bypass[i] = new(bypassed[i], bypassModes[i]);
        }
    }

    /// <summary>
    ///     Audio thread, once per sub-block before <see cref="Process" />: drive every modified parameter from its source,
    ///     ramping across the sub-block.
    /// </summary>
    public void ApplyModifiers(Modulation modulation, int frames) {
        var seconds = frames / Spec.SampleRate;
        for (var i = 0; i < modifiers.Length; i++) {
            ref var modifier = ref modifiers[i];
            if (!modifier.Enabled) {
                modifierValues[i] = float.NaN;
                continue;
            }

            var target = Modulation.Map(
                modulation.Value(modifier.Source),
                modifier.Curve,
                modifier.Min,
                modifier.Max,
                modifier.Log
            );
            var ms = target > modifier.Value ? modifier.AttackMs : modifier.ReleaseMs;
            modifier.Value = ms <= 0 || float.IsNaN(modifier.Value)
                ? target
                : modifier.Value + (target - modifier.Value) * (float)(1 - Math.Exp(-seconds / (ms * 0.001)));
            if (modifier.ParamIndex == ModifierBinding.Engage) {
                modifierValues[i] = float.NaN;
                var bypassed = modifier.Value < 0.5f;
                if (bypass[modifier.Slot].Target != bypassed) {
                    SetBypass(modifier.Slot, bypassed);
                }
            } else {
                modifierValues[i] = modifier.Value;
                blocks[modifier.Slot].Params.Modulate(modifier.ParamIndex, modifier.Value, frames);
            }
        }
    }

    /// <summary>Slot index of block instance <paramref name="instanceId" />, or -1.</summary>
    public int SlotOf(string instanceId) => slotById.TryGetValue(instanceId, out var slot) ? slot : -1;

    /// <summary>Allocation-free <see cref="SlotOf(string)" />, for ids parsed straight out of an IPC frame.</summary>
    public int SlotOf(ReadOnlySpan<char> instanceId) => slotByName.TryGetValue(instanceId, out var slot) ? slot : -1;

    public IDspBlock Block(int slot) => blocks[slot];

    /// <summary>Whether the grid has a live node for output <paramref name="output" /> (0 = Output 1, 1-3 = Outputs 2-4).</summary>
    public bool HasOutput(int output) => outputs[output];

    /// <summary>
    ///     The id of each value in <see cref="Meters" />: slot by slot, <see cref="PeakMeter" /> and then the block's own,
    ///     so each <see cref="PeakMeter" /> starts the next slot's meters. The host sends it with the slot ids.
    /// </summary>
    public string[] MeterLayout => meterLayout;

    /// <summary>The ids of slot <paramref name="slot" />'s meters: <see cref="PeakMeter" />, then the block's own.</summary>
    public ArraySegment<string> MeterIds(int slot) =>
        new(
            meterLayout,
            meterOffsets[slot],
            (slot + 1 < meterOffsets.Length ? meterOffsets[slot + 1] : meterLayout.Length) - meterOffsets[slot]
        );

    /// <summary>Where slot <paramref name="slot" />'s meters start in <see cref="Meters" />.</summary>
    public int MeterOffset(int slot) => meterOffsets[slot];

    /// <summary>
    ///     The index in <see cref="Meters" /> of block <paramref name="instanceId" />'s meter <paramref name="meter" />,
    ///     or -1.
    /// </summary>
    public int MeterIndex(string instanceId, string meter = PeakMeter) {
        var slot = SlotOf(instanceId);
        var index = slot < 0 ? -1 : MeterIds(slot).AsSpan().IndexOf(meter);
        return index < 0 ? -1 : meterOffsets[slot] + index;
    }

    /// <summary>Audio thread: starts a new peak measurement for the <see cref="PeakMeter" />s.</summary>
    public void ClearPeaks() {
        for (var i = 0; i < meterOffsets.Length; i++) {
            meters[meterOffsets[i]] = 0f;
        }
    }

    /// <summary>
    ///     Audio thread, once per callback after <see cref="Process" />: reads the blocks' own meters into
    ///     <see cref="Meters" />.
    /// </summary>
    public void CaptureMeters() {
        for (var i = 0; i < ownMeters.Length; i++) {
            ownMeters[i].ReadMeters(meters.AsSpan(ownOffsets[i], ownMeters[i].MeterIds.Count));
        }
    }

    /// <summary>Any thread: set a parameter target. Continuous values need no command queue.</summary>
    public void SetParameter(int slot, int paramIndex, float value) => blocks[slot].Params.SetTarget(paramIndex, value);

    public bool IsBypassed(int slot) => bypass[slot].Target;

    public BypassKind BypassModeOf(int slot) => bypass[slot].Mode;

    /// <summary>
    ///     Audio thread: runs the plan for one block of at most <see cref="ProcessSpec.MaxBlockSize" /> frames. Output 1
    ///     goes to <paramref name="io" />; Outputs 2-4 are added into <paramref name="outputs" /> (dropped without).
    /// </summary>
    public void Process(in AudioIO io, in BlockContext context, GraphOutputs? outputs = null) {
        var frames = io.Frames;
        io.OutputLeft.Clear();
        io.OutputRight.Clear();

        foreach (ref readonly var op in ops.AsSpan()) {
            if (op.Kind == GraphOpKind.SideInput) {
                SideInput(op, io, frames);
                continue;
            }

            var left = this.left[op.Buffer].Span[..frames];
            var right = this.right[op.Buffer].Span[..frames];
            switch (op.Kind) {
                case GraphOpKind.ReadInput:
                    io.InputLeft.CopyTo(left);
                    io.InputRight.CopyTo(right);
                    break;
                case GraphOpKind.Copy:
                    this.left[op.Source].Span[..frames].CopyTo(left);
                    this.right[op.Source].Span[..frames].CopyTo(right);
                    break;
                case GraphOpKind.Add:
                    Accumulate(this.left[op.Source].Span[..frames], left);
                    Accumulate(this.right[op.Source].Span[..frames], right);
                    break;
                case GraphOpKind.Delay:
                    delays[op.Index].Process(left, right);
                    break;
                case GraphOpKind.Process:
                    RunBlock(op.Index, new(left, right), context);
                    Tone?.Render(Generation, op.Index, left, right, Spec.SampleRate);
                    break;
                case GraphOpKind.WriteOutput when op.Index <= 0:
                    Accumulate(left, io.OutputLeft);
                    Accumulate(right, io.OutputRight);
                    break;
                case GraphOpKind.WriteOutput:
                    if (outputs is not null) {
                        Accumulate(left, outputs.Left(op.Index, frames));
                        Accumulate(right, outputs.Right(op.Index, frames));
                    }

                    break;
                case GraphOpKind.Clear:
                    left.Clear();
                    right.Clear();
                    break;
                case GraphOpKind.ScaleRow:
                    ((OutputBlock)blocks[op.Index]).ScaleInput(op.Row, new(left, right));
                    break;
                case GraphOpKind.SideInput:
                    // Handled above: a side input takes no row buffers.
                    break;
            }
        }

        Inputs?.Advance(frames);
    }

    public void Dispose() {
        foreach (var block in blocks) {
            block.Dispose();
        }

        foreach (var buffer in left.Concat(right)) {
            buffer.Dispose();
        }

        dryLeft.Dispose();
        dryRight.Dispose();
    }

    /// <summary>
    ///     A <see cref="GraphOpKind.SideInput" />: hands the block buffer <c>Source</c> (or the grid input), as a cabled
    ///     input to add into <c>Buffer</c> or, without one, as a tap.
    /// </summary>
    void SideInput(in GraphOp op, in AudioIO io, int frames) {
        Span<float> signalLeft;
        Span<float> signalRight;
        if (op.Source < 0) {
            // The grid input; the dry buffers are free between blocks.
            signalLeft = dryLeft.Span[..frames];
            signalRight = dryRight.Span[..frames];
            io.InputLeft.CopyTo(signalLeft);
            io.InputRight.CopyTo(signalRight);
        } else {
            signalLeft = left[op.Source].Span[..frames];
            signalRight = right[op.Source].Span[..frames];
        }

        var block = (ISideInputBlock)blocks[op.Index];
        if (op.Buffer < 0) {
            block.SideInput(SideInputKind.Tap, op.Row, new(signalLeft, signalRight), default);
        } else {
            block.SideInput(
                SideInputKind.Cable,
                op.Row,
                new(signalLeft, signalRight),
                new(left[op.Buffer].Span[..frames], right[op.Buffer].Span[..frames])
            );
        }
    }

    void RunBlock(int slot, in StereoBuffer io, in BlockContext context) {
        RunBypassed(slot, io, context);
        var offset = meterOffsets[slot];
        var peak = meters[offset];
        for (var i = 0; i < io.Length; i++) {
            peak = Math.Max(peak, Math.Max(Math.Abs(io.Left[i]), Math.Abs(io.Right[i])));
        }

        meters[offset] = peak;
    }

    void RunBypassed(int slot, in StereoBuffer io, in BlockContext context) {
        ref var bypass = ref this.bypass[slot];
        var target = bypass.Target ? 0f : 1f;
        var steady = bypass.Mix == target;
        switch (bypass.Mode) {
            case BypassKind.MuteIn or BypassKind.MuteFxIn:
                RunWithFadedInput(slot, target, io, context);
                return;
            case BypassKind.MuteOut or BypassKind.MuteFxOut:
                RunWithFadedOutput(slot, target, io, context);
                return;
            case BypassKind.Thru or BypassKind.Mute when steady:
                if (target == 1f) {
                    blocks[slot].Process(io, context);
                } else if (bypass.Mode == BypassKind.Mute) {
                    io.Left.Clear();
                    io.Right.Clear();
                }

                return; // fully bypassed THRU leaves the input as it is
        }

        // Thru or Mute, mid-fade: crossfade between the processed signal and dry (Thru) or silence (Mute).
        var dryLeft = this.dryLeft.Span[..io.Length];
        var dryRight = this.dryRight.Span[..io.Length];
        if (bypass.Mode == BypassKind.Thru) {
            io.Left.CopyTo(dryLeft);
            io.Right.CopyTo(dryRight);
        } else {
            dryLeft.Clear();
            dryRight.Clear();
        }

        blocks[slot].Process(io, context);
        var mix = bypass.Mix;
        for (var i = 0; i < io.Length; i++) {
            mix = Step(mix, target);
            io.Left[i] = dryLeft[i] + (io.Left[i] - dryLeft[i]) * mix;
            io.Right[i] = dryRight[i] + (io.Right[i] - dryRight[i]) * mix;
        }

        bypass.Mix = mix;
    }

    /// <summary>
    ///     Mute In and Mute FX In: the block always runs, on its input faded by the bypass mix, so its tails continue
    ///     after a bypass. Mute FX In adds the dry signal back as the input fades out.
    /// </summary>
    void RunWithFadedInput(int slot, float target, in StereoBuffer io, in BlockContext context) {
        ref var bypass = ref this.bypass[slot];
        var dryLeft = this.dryLeft.Span[..io.Length];
        var dryRight = this.dryRight.Span[..io.Length];
        io.Left.CopyTo(dryLeft);
        io.Right.CopyTo(dryRight);
        var mix = bypass.Mix;
        var start = mix;
        for (var i = 0; i < io.Length; i++) {
            mix = Step(mix, target);
            io.Left[i] *= mix;
            io.Right[i] *= mix;
        }

        blocks[slot].Process(io, context);
        if (bypass.Mode == BypassKind.MuteFxIn) {
            var gain = start;
            for (var i = 0; i < io.Length; i++) {
                gain = Step(gain, target);
                io.Left[i] += dryLeft[i] * (1f - gain);
                io.Right[i] += dryRight[i] * (1f - gain);
            }
        }

        bypass.Mix = mix;
    }

    /// <summary>
    ///     Mute Out and Mute FX Out: the block always runs on its full input, so it keeps listening while bypassed and its
    ///     tails are already there when it is engaged; its output is faded by the bypass mix. Mute FX Out puts the dry
    ///     signal in its place, Mute Out silence.
    /// </summary>
    void RunWithFadedOutput(int slot, float target, in StereoBuffer io, in BlockContext context) {
        ref var bypass = ref this.bypass[slot];
        if (bypass.Mix == target && target == 1f) {
            blocks[slot].Process(io, context);
            return;
        }

        var dryLeft = this.dryLeft.Span[..io.Length];
        var dryRight = this.dryRight.Span[..io.Length];
        if (bypass.Mode == BypassKind.MuteFxOut) {
            io.Left.CopyTo(dryLeft);
            io.Right.CopyTo(dryRight);
        } else {
            dryLeft.Clear();
            dryRight.Clear();
        }

        blocks[slot].Process(io, context);
        var mix = bypass.Mix;
        for (var i = 0; i < io.Length; i++) {
            mix = Step(mix, target);
            io.Left[i] = dryLeft[i] + (io.Left[i] - dryLeft[i]) * mix;
            io.Right[i] = dryRight[i] + (io.Right[i] - dryRight[i]) * mix;
        }

        bypass.Mix = mix;
    }

    float Step(float mix, float target) =>
        target > mix ? Math.Min(target, mix + fadeStep) : Math.Max(target, mix - fadeStep);

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    /// <summary>
    ///     Audio thread only (<see cref="EngineCommandKind.SetModifier" />): a re-enabled modifier starts again from its
    ///     source's value rather than from where it stopped; a disabled one leaves its parameter at
    ///     <paramref name="baseValue" /> (an engage modifier leaves the bypass to the bypass commands).
    /// </summary>
    internal void SetModifierEnabled(int index, bool enabled, float baseValue) {
        if ((uint)index >= (uint)modifiers.Length) {
            return;
        }

        ref var modifier = ref modifiers[index];
        modifier.Enabled = enabled;
        modifier.Value = float.NaN;
        modifierValues[index] = float.NaN;
        if (!enabled && modifier.ParamIndex != ModifierBinding.Engage) {
            blocks[modifier.Slot].Params.SetTarget(modifier.ParamIndex, baseValue);
        }
    }

    /// <summary>Audio thread only (the engine applies queued <see cref="EngineCommandKind.SetBypass" /> commands here).</summary>
    internal void SetBypass(int slot, bool bypassed) {
        if ((uint)slot >= (uint)bypass.Length) {
            return;
        }

        if (bypass[slot].Target && !bypassed && blocks[slot] is IEngageAware engageAware) {
            engageAware.Engage();
        }

        bypass[slot].Target = bypassed;
    }

    struct BypassState(bool bypassed, BypassKind mode) {
        public readonly BypassKind Mode = mode;
        public bool Target = bypassed;

        /// <summary>1 = fully active, 0 = fully bypassed.</summary>
        public float Mix = bypassed ? 0f : 1f;
    }
}

/// <summary>A compiled <see cref="ModifierDefinition" /> plus its slew state.</summary>
public struct ModifierBinding(
    int slot,
    int paramIndex,
    ModSource source,
    ModCurve curve,
    float min,
    float max,
    bool log,
    float attackMs,
    float releaseMs
) {
    /// <summary>
    ///     The <see cref="ParamIndex" /> of a modifier that engages and bypasses its block
    ///     (<see cref="ModNames.EngageParam" />).
    /// </summary>
    public const int Engage = -2;

    public int Slot { get; } = slot;
    public int ParamIndex { get; } = paramIndex;
    public ModSource Source { get; } = source;
    public ModCurve Curve { get; } = curve;
    public float Min { get; } = min;
    public float Max { get; } = max;
    public bool Log { get; } = log;
    public float AttackMs { get; } = attackMs;
    public float ReleaseMs { get; } = releaseMs;

    /// <summary>The slewed value; NaN until the first sub-block, which then starts at its target.</summary>
    public float Value { get; set; } = float.NaN;

    /// <summary>Whether it drives its parameter (false for a channel-scoped modifier while its block is elsewhere).</summary>
    public bool Enabled { get; set; } = true;
}
