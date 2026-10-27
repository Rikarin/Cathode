using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cathode.Dsp;

/// <summary>
///     Zeroed, 64-byte-aligned float memory outside the GC heap, for DSP state. Building a graph during a preset
///     load must not allocate large managed arrays: those land on the large object heap and trigger blocking gen2
///     collections that stall the audio thread (docs/adr/0001-process-model.md, soak follow-up). Freed by
///     <see cref="Dispose" /> when a retired graph is disposed; the finalizer only catches leaks.
/// </summary>
/// <remarks>
///     The indexer is unchecked in release builds: callers keep indices in range, as DSP inner loops must.
/// </remarks>
public sealed unsafe class NativeBuffer : IDisposable {
    const nuint Alignment = 64;

    nint memory;

    /// <summary>A zero-length buffer, safe to dispose any number of times.</summary>
    public static NativeBuffer Empty { get; } = new(0);

    public int Length { get; }

    public Span<float> Span => new((void*)memory, Length);

    public ref float this[int index] {
        get {
            Debug.Assert((uint)index < (uint)Length, "NativeBuffer index out of range");
            return ref ((float*)memory)[index];
        }
    }

    public NativeBuffer(int length) {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        if (length > 0) {
            var bytes = (nuint)length * sizeof(float);
            memory = (nint)NativeMemory.AlignedAlloc(bytes, Alignment);
            NativeMemory.Clear((void*)memory, bytes);
        }
    }

    public void Clear() => Span.Clear();

    public void Dispose() {
        Free();
        GC.SuppressFinalize(this);
    }

    void Free() {
        var pointer = Interlocked.Exchange(ref memory, 0);
        if (pointer != 0) {
            NativeMemory.AlignedFree((void*)pointer);
        }
    }

    ~NativeBuffer() {
        Free();
    }
}
