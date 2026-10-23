using Cathode.Dsp;

namespace Cathode.Engine.Graph;

/// <summary>
///     An immutable execution plan plus all the state it needs: block instances, work buffers and delay lines.
///     Built and prepared on the control thread; <see cref="Process" /> runs on the audio thread and never allocates.
/// </summary>
public sealed class CompiledGraph : IDisposable {
    /// <summary>Length of the bypass crossfade.</summary>
    public const double BypassFadeMs = 10;

    readonly GraphOp[] _ops;
    readonly IDspBlock[] _blocks;
    readonly string[] _slotIds;
    readonly Dictionary<string, int> _slotById;
    readonly DelayLine[] _delays;
    readonly float[][] _left;
    readonly float[][] _right;
    readonly float[] _dryLeft;
    readonly float[] _dryRight;
    readonly BypassState[] _bypass;
    readonly float _fadeStep;

    /// <summary>Identifies this graph; commands addressed to another generation are ignored.</summary>
    public int Generation { get; }

    public ProcessSpec Spec { get; }

    /// <summary>Latency of the longest input-to-output path, in samples.</summary>
    public int LatencySamples { get; }

    public int BufferCount => _left.Length;

    public IReadOnlyList<GraphOp> Ops => _ops;

    public IReadOnlyList<string> SlotIds => _slotIds;

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
        _ops = ops;
        _blocks = blocks;
        _slotIds = slotIds;
        _delays = delays;
        LatencySamples = latencySamples;
        _slotById = new(StringComparer.Ordinal);
        for (var i = 0; i < slotIds.Length; i++) {
            _slotById.Add(slotIds[i], i);
        }

        _left = new float[bufferCount][];
        _right = new float[bufferCount][];
        for (var i = 0; i < bufferCount; i++) {
            _left[i] = GC.AllocateArray<float>(spec.MaxBlockSize, true);
            _right[i] = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        }

        _dryLeft = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        _dryRight = GC.AllocateArray<float>(spec.MaxBlockSize, true);
        _fadeStep = (float)(1.0 / Math.Max(1.0, BypassFadeMs * 0.001 * spec.SampleRate));
        _bypass = new BypassState[blocks.Length];
        for (var i = 0; i < blocks.Length; i++) {
            _bypass[i] = new(bypassed[i]);
        }
    }

    /// <summary>Slot index of block instance <paramref name="instanceId" />, or -1.</summary>
    public int SlotOf(string instanceId) => _slotById.TryGetValue(instanceId, out var slot) ? slot : -1;

    public IDspBlock Block(int slot) => _blocks[slot];

    /// <summary>Any thread: set a parameter target. Continuous values need no command queue.</summary>
    public void SetParameter(int slot, int paramIndex, float value) =>
        _blocks[slot].Params.SetTarget(paramIndex, value);

    public bool IsBypassed(int slot) => _bypass[slot].Target;

    /// <summary>Audio thread: runs the plan for one block of at most <see cref="ProcessSpec.MaxBlockSize" /> frames.</summary>
    public void Process(in AudioIO io, in BlockContext context) {
        var frames = io.Frames;
        io.OutputLeft.Clear();
        io.OutputRight.Clear();

        foreach (ref readonly var op in _ops.AsSpan()) {
            var left = _left[op.Buffer].AsSpan(0, frames);
            var right = _right[op.Buffer].AsSpan(0, frames);
            switch (op.Kind) {
                case GraphOpKind.ReadInput:
                    io.InputLeft.CopyTo(left);
                    io.InputRight.CopyTo(right);
                    break;
                case GraphOpKind.Copy:
                    _left[op.Source].AsSpan(0, frames).CopyTo(left);
                    _right[op.Source].AsSpan(0, frames).CopyTo(right);
                    break;
                case GraphOpKind.Add:
                    Accumulate(_left[op.Source].AsSpan(0, frames), left);
                    Accumulate(_right[op.Source].AsSpan(0, frames), right);
                    break;
                case GraphOpKind.Delay:
                    _delays[op.Index].Process(left, right);
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
        foreach (var block in _blocks) {
            block.Dispose();
        }
    }

    void RunBlock(int slot, in StereoBuffer io, in BlockContext context) {
        ref var bypass = ref _bypass[slot];
        var target = bypass.Target ? 0f : 1f;
        if (bypass.Mix == target) {
            if (target == 1f) {
                _blocks[slot].Process(io, context);
            }

            return; // fully bypassed: THRU
        }

        // Crossfade between the dry input and the processed signal.
        var dryLeft = _dryLeft.AsSpan(0, io.Length);
        var dryRight = _dryRight.AsSpan(0, io.Length);
        io.Left.CopyTo(dryLeft);
        io.Right.CopyTo(dryRight);
        _blocks[slot].Process(io, context);

        var mix = bypass.Mix;
        var step = target > mix ? _fadeStep : -_fadeStep;
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
        if ((uint)slot < (uint)_bypass.Length) {
            _bypass[slot].Target = bypassed;
        }
    }

    struct BypassState(bool bypassed) {
        public bool Target = bypassed;

        /// <summary>1 = fully active, 0 = fully bypassed.</summary>
        public float Mix = bypassed ? 0f : 1f;
    }
}
