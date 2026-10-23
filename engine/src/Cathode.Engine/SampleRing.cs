namespace Cathode.Engine;

/// <summary>
///     Lock-free single-producer/single-consumer ring of float samples with block writes, for taps from the audio
///     thread (tuner, analysers). When the consumer falls behind, new samples are dropped rather than blocking the
///     producer.
/// </summary>
public sealed class SampleRing {
    readonly float[] buffer;
    readonly long mask;
    long head; // consumer
    long tail; // producer
    long dropped;

    public int Capacity => buffer.Length;

    /// <summary>Samples dropped because the ring was full.</summary>
    public long Dropped => Interlocked.Read(ref dropped);

    public SampleRing(int capacity) {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a power of two >= 2.");
        }

        buffer = new float[capacity];
        mask = capacity - 1;
    }

    /// <summary>Producer: writes as many samples as fit.</summary>
    public void Write(ReadOnlySpan<float> samples) {
        var tail = this.tail;
        var free = buffer.Length - (int)(tail - Volatile.Read(ref head));
        var count = Math.Min(free, samples.Length);
        for (var i = 0; i < count; i++) {
            buffer[(tail + i) & mask] = samples[i];
        }

        if (count < samples.Length) {
            Interlocked.Add(ref dropped, samples.Length - count);
        }

        Volatile.Write(ref this.tail, tail + count);
    }

    /// <summary>Consumer: reads up to <paramref name="destination" />.Length samples and returns how many.</summary>
    public int Read(Span<float> destination) {
        var head = this.head;
        var available = (int)(Volatile.Read(ref tail) - head);
        var count = Math.Min(available, destination.Length);
        for (var i = 0; i < count; i++) {
            destination[i] = buffer[(head + i) & mask];
        }

        Volatile.Write(ref this.head, head + count);
        return count;
    }
}
