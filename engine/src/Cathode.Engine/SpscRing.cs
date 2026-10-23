using System.Runtime.InteropServices;

namespace Cathode.Engine;

/// <summary>
///     Bounded, lock-free, allocation-free single-producer/single-consumer queue of unmanaged items.
/// </summary>
/// <remarks>Exactly one thread may enqueue and exactly one (other) thread may dequeue.</remarks>
public sealed class SpscRing<T>
    where T : unmanaged {
    readonly T[] items;
    readonly long mask;
    PaddedLong head; // next slot to read, owned by the consumer
    PaddedLong tail; // next slot to write, owned by the producer

    public int Capacity => this.items.Length;

    /// <summary>Approximate number of queued items (exact when called from either endpoint while the other is idle).</summary>
    public int Count => (int)(Volatile.Read(ref this.tail.Value) - Volatile.Read(ref this.head.Value));

    public SpscRing(int capacity) {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a power of two >= 2.");
        }

        this.items = new T[capacity];
        mask = capacity - 1;
    }

    public bool TryEnqueue(in T item) {
        var tail = this.tail.Value;
        if (tail - Volatile.Read(ref this.head.Value) == this.items.Length) {
            return false;
        }

        this.items[tail & mask] = item;
        Volatile.Write(ref this.tail.Value, tail + 1);
        return true;
    }

    public bool TryDequeue(out T item) {
        var head = this.head.Value;
        if (head == Volatile.Read(ref this.tail.Value)) {
            item = default;
            return false;
        }

        item = this.items[head & mask];
        Volatile.Write(ref this.head.Value, head + 1);
        return true;
    }
}

/// <summary>
///     A long alone on its cache line, so the producer's and consumer's indices don't false-share.
///     (Top level because a struct nested in a generic type is generic, and generic types can't use explicit layout.)
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedLong {
    [FieldOffset(64)]
    public long Value;
}
