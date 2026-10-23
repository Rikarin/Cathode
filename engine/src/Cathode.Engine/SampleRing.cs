namespace Cathode.Engine;

/// <summary>
///     Lock-free single-producer/single-consumer ring of float samples with block writes, for taps from the audio thread
///     (tuner, analysers). When the consumer falls behind, new samples are dropped rather than blocking the producer.
/// </summary>
public sealed class SampleRing {
    readonly float[] _buffer;
    readonly long _mask;
    long _head; // consumer
    long _tail; // producer
    long _dropped;

    public SampleRing(int capacity) {
        if (capacity < 2 || (capacity & (capacity - 1)) != 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a power of two >= 2.");
        }

        _buffer = new float[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>Samples dropped because the ring was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Producer: writes as many samples as fit.</summary>
    public void Write(ReadOnlySpan<float> samples) {
        var tail = _tail;
        var free = _buffer.Length - (int)(tail - Volatile.Read(ref _head));
        var count = Math.Min(free, samples.Length);
        for (var i = 0; i < count; i++) {
            _buffer[(tail + i) & _mask] = samples[i];
        }

        if (count < samples.Length) {
            Interlocked.Add(ref _dropped, samples.Length - count);
        }

        Volatile.Write(ref _tail, tail + count);
    }

    /// <summary>Consumer: reads up to <paramref name="destination" />.Length samples and returns how many.</summary>
    public int Read(Span<float> destination) {
        var head = _head;
        var available = (int)(Volatile.Read(ref _tail) - head);
        var count = Math.Min(available, destination.Length);
        for (var i = 0; i < count; i++) {
            destination[i] = _buffer[(head + i) & _mask];
        }

        Volatile.Write(ref _head, head + count);
        return count;
    }
}
