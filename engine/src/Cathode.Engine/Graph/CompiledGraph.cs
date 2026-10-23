using Cathode.Dsp;

namespace Cathode.Engine.Graph;

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
    readonly DelayLine[] delays;
    readonly float[][] left;
    readonly float[][] right;
    readonly float[] dryLeft;
    readonly float[] dryRight;
    readonly BypassState[] bypass;
    readonly float fadeStep;

    /// <summary>Identifies this graph; commands addressed to another generation are ignored.</summary>
    public int Generation { get; }

    public ProcessSpec Spec { get; }

    /// <summary>Latency of the longest input-to-output path, in samples.</summary>
    public int LatencySamples { get; }

    public int BufferCount => this.left.Length;

    public IReadOnlyList<GraphOp> Ops => this.ops;

    public IReadOnlyList<string> SlotIds => this.slotIds;

    internal CompiledGraph(
        int generation,
        ProcessSpec spec,
        GraphOp[] ops,
        int bufferCount,
        IDspBlock[] blocks,
        string[] slotIds,
        bool[] bypassed,
        DelayLine[] delays,
        int latencySamples
    ) {
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

        this.left = new float[bufferCount][];
        this.right = new float[bufferCount][];
        for (var i = 0; i < bufferCount; i++) {
            this.left[i] = GC.AllocateArray<float>(spec.MaxBlockSize, true);
            this.right[i] = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        }

        this.dryLeft = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        this.dryRight = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        fadeStep = (float)(1.0 / Math.Max(1.0, BypassFadeMs * 0.001 * spec.SampleRate));
        this.bypass = new BypassState[blocks.Length];
        for (var i = 0; i < blocks.Length; i++) {
            this.bypass[i] = new(bypassed[i]);
        }
    }

    /// <summary>Slot index of block instance <paramref name="instanceId" />, or -1.</summary>
    public int SlotOf(string instanceId) => slotById.TryGetValue(instanceId, out var slot) ? slot : -1;

    public IDspBlock Block(int slot) => this.blocks[slot];

    /// <summary>Any thread: set a parameter target. Continuous values need no command queue.</summary>
    public void SetParameter(int slot, int paramIndex, float value) =>
        this.blocks[slot].Params.SetTarget(paramIndex, value);

    public bool IsBypassed(int slot) => this.bypass[slot].Target;

    /// <summary>Audio thread: runs the plan for one block of at most <see cref="ProcessSpec.MaxBlockSize" /> frames.</summary>
    public void Process(in AudioIO io, in BlockContext context) {
        var frames = io.Frames;
        io.OutputLeft.Clear();
        io.OutputRight.Clear();

        foreach (ref readonly var op in this.ops.AsSpan()) {
            var left = this.left[op.Buffer].AsSpan(0, frames);
            var right = this.right[op.Buffer].AsSpan(0, frames);
            switch (op.Kind) {
                case GraphOpKind.ReadInput:
                    io.InputLeft.CopyTo(left);
                    io.InputRight.CopyTo(right);
                    break;
                case GraphOpKind.Copy:
                    this.left[op.Source].AsSpan(0, frames).CopyTo(left);
                    this.right[op.Source].AsSpan(0, frames).CopyTo(right);
                    break;
                case GraphOpKind.Add:
                    Accumulate(this.left[op.Source].AsSpan(0, frames), left);
                    Accumulate(this.right[op.Source].AsSpan(0, frames), right);
                    break;
                case GraphOpKind.Delay:
                    this.delays[op.Index].Process(left, right);
                    break;
                case GraphOpKind.Process:
                    RunBlock(op.Index, new(left, right), context);
                    break;
                case GraphOpKind.WriteOutput:
                    Accumulate(left, io.OutputLeft);
                    Accumulate(right, io.OutputRight);
                    break;
            }
        }
    }

    public void Dispose() {
        foreach (var block in this.blocks) {
            block.Dispose();
        }
    }

    void RunBlock(int slot, in StereoBuffer io, in BlockContext context) {
        ref var bypass = ref this.bypass[slot];
        var target = bypass.Target ? 0f : 1f;
        if (bypass.Mix == target) {
            if (target == 1f) {
                this.blocks[slot].Process(io, context);
            }

            return; // fully bypassed: THRU
        }

        // Crossfade between the dry input and the processed signal.
        var dryLeft = this.dryLeft.AsSpan(0, io.Length);
        var dryRight = this.dryRight.AsSpan(0, io.Length);
        io.Left.CopyTo(dryLeft);
        io.Right.CopyTo(dryRight);
        this.blocks[slot].Process(io, context);

        var mix = bypass.Mix;
        var step = target > mix ? fadeStep : -fadeStep;
        for (var i = 0; i < io.Length; i++) {
            mix = step > 0 ? Math.Min(target, mix + step) : Math.Max(target, mix + step);
            io.Left[i] = dryLeft[i] + (io.Left[i] - dryLeft[i]) * mix;
            io.Right[i] = dryRight[i] + (io.Right[i] - dryRight[i]) * mix;
        }

        bypass.Mix = mix;
    }

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    /// <summary>Audio thread only (the engine applies queued <see cref="EngineCommandKind.SetBypass" /> commands here).</summary>
    internal void SetBypass(int slot, bool bypassed) {
        if ((uint)slot < (uint)this.bypass.Length) {
            this.bypass[slot].Target = bypassed;
        }
    }

    struct BypassState(bool bypassed) {
        public bool Target = bypassed;

        /// <summary>1 = fully active, 0 = fully bypassed.</summary>
        public float Mix = bypassed ? 0f : 1f;
    }
}
