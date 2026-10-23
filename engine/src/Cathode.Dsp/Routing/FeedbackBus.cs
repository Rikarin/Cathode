using System.Numerics;

namespace Cathode.Dsp.Routing;

/// <summary>
///     Carries a Feedback Send's signal to its Feedback Return, <see cref="Delay" /> samples later. The delay is one
///     maximum block, so whichever of the pair runs first in a sub-block, the return only reads what the send has already
///     written, and the result does not depend on how the callback is split. The return consumes what it reads: when the
///     send stops running (bypassed), the return falls silent instead of repeating stale audio.
/// </summary>
public sealed class FeedbackBus : IDisposable {
    readonly NativeBuffer left;
    readonly NativeBuffer right;
    readonly int mask;
    long written;
    long read;

    /// <summary>Samples between a send and its return.</summary>
    public int Delay { get; }

    public FeedbackBus(int maxBlockSize) {
        Delay = maxBlockSize;
        var size = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * maxBlockSize));
        mask = size - 1;
        left = new(size);
        right = new(size);
    }

    public void Reset() {
        left.Clear();
        right.Clear();
        written = 0;
        read = 0;
    }

    /// <summary>Audio thread: the send appends <paramref name="gain" /> times its input.</summary>
    public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right, float gain) {
        var l = this.left.Span;
        var r = this.right.Span;
        for (var i = 0; i < left.Length; i++) {
            var index = (int)(written++ & mask);
            l[index] = left[i] * gain;
            r[index] = right[i] * gain;
        }
    }

    /// <summary>Audio thread: the return takes the next block, <see cref="Delay" /> samples behind the send's clock.</summary>
    public void Read(Span<float> left, Span<float> right) {
        var l = this.left.Span;
        var r = this.right.Span;
        for (var i = 0; i < left.Length; i++) {
            var index = (int)((read++ - Delay) & mask);
            left[i] = l[index];
            right[i] = r[index];
            l[index] = 0f;
            r[index] = 0f;
        }
    }

    public void Dispose() {
        left.Dispose();
        right.Dispose();
    }
}
