using Cathode.Dsp;
using Cathode.Dsp.Blocks;
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

    /// <summary>Identifies this graph; commands addressed to another generation are ignored.</summary>
    public int Generation { get; }

    public ProcessSpec Spec { get; }

    /// <summary>Latency of the longest input-to-output path, in samples.</summary>
    public int LatencySamples { get; }

    public int BufferCount => left.Length;

    public IReadOnlyList<GraphOp> Ops => ops;

    public IReadOnlyList<string> SlotIds => slotIds;

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
        ModifierBinding[]? modifiers = null
    ) {
        this.modifiers = modifiers ?? [];
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
        bypass = new BypassState[blocks.Length];
        for (var i = 0; i < blocks.Length; i++) {
            bypass[i] = new(bypassed[i], bypassModes[i]);
        }
    }

    public IReadOnlyList<ModifierBinding> Modifiers => modifiers;

    /// <summary>True when a modifier follows the input pitch, so the host must run pitch detection.</summary>
    public bool UsesPitch => modifiers.Any(static m => m.Source == ModSource.Pitch);

    /// <summary>
    ///     Audio thread, once per sub-block before <see cref="Process" />: drive every modified parameter from its source,
    ///     ramping across the sub-block.
    /// </summary>
    public void ApplyModifiers(Modulation modulation, int frames) {
        var seconds = frames / Spec.SampleRate;
        foreach (ref var modifier in modifiers.AsSpan()) {
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
            blocks[modifier.Slot].Params.Modulate(modifier.ParamIndex, modifier.Value, frames);
        }
    }

    /// <summary>Slot index of block instance <paramref name="instanceId" />, or -1.</summary>
    public int SlotOf(string instanceId) => slotById.TryGetValue(instanceId, out var slot) ? slot : -1;

    /// <summary>Allocation-free <see cref="SlotOf(string)" />, for ids parsed straight out of an IPC frame.</summary>
    public int SlotOf(ReadOnlySpan<char> instanceId) => slotByName.TryGetValue(instanceId, out var slot) ? slot : -1;

    public IDspBlock Block(int slot) => blocks[slot];

    /// <summary>Any thread: set a parameter target. Continuous values need no command queue.</summary>
    public void SetParameter(int slot, int paramIndex, float value) => blocks[slot].Params.SetTarget(paramIndex, value);

    public bool IsBypassed(int slot) => bypass[slot].Target;

    public BypassKind BypassModeOf(int slot) => bypass[slot].Mode;

    /// <summary>Audio thread: runs the plan for one block of at most <see cref="ProcessSpec.MaxBlockSize" /> frames.</summary>
    public void Process(in AudioIO io, in BlockContext context) {
        var frames = io.Frames;
        io.OutputLeft.Clear();
        io.OutputRight.Clear();

        foreach (ref readonly var op in ops.AsSpan()) {
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
                    break;
                case GraphOpKind.WriteOutput:
                    Accumulate(left, io.OutputLeft);
                    Accumulate(right, io.OutputRight);
                    break;
                case GraphOpKind.Clear:
                    left.Clear();
                    right.Clear();
                    break;
                case GraphOpKind.MuxInput:
                    ((IRowInputBlock)blocks[op.Index]).MixInput(
                        op.Row,
                        new(this.left[op.Source].Span[..frames], this.right[op.Source].Span[..frames]),
                        new(left, right)
                    );
                    break;
                case GraphOpKind.SidechainInput:
                    if (op.Row < 0) {
                        // The grid input; the dry buffers are free between blocks.
                        var inputLeft = dryLeft.Span[..frames];
                        var inputRight = dryRight.Span[..frames];
                        io.InputLeft.CopyTo(inputLeft);
                        io.InputRight.CopyTo(inputRight);
                        ((ISidechainBlock)blocks[op.Index]).SidechainInput(-1, new(inputLeft, inputRight));
                    } else {
                        ((ISidechainBlock)blocks[op.Index]).SidechainInput(op.Row, new(left, right));
                    }

                    break;
                case GraphOpKind.ScaleRow:
                    ((OutputBlock)blocks[op.Index]).ScaleInput(op.Row, new(left, right));
                    break;
            }
        }
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

    void RunBlock(int slot, in StereoBuffer io, in BlockContext context) {
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
    float releaseMs) {
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
}
