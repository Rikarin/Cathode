namespace Cathode.Dsp.Filters;

/// <summary>
///     Circular delay line with cubic Hermite fractional reads. <see cref="Read" /> returns the signal
///     <c>delay</c> samples before the next write, so a delay of 1 is the most recently written sample and
///     integer delays are exact. Delays below 3 samples are clamped (the interpolator looks one sample ahead).
///     The buffer lives in native memory (<see cref="NativeBuffer" />).
/// </summary>
public sealed class FractionalDelayLine : IDisposable {
    public const float MinDelay = 3f;

    NativeBuffer buffer = NativeBuffer.Empty;
    int write;

    /// <summary>Control thread: room for delays up to <paramref name="capacity" /> - 3 samples.</summary>
    public void Allocate(int capacity) {
        buffer.Dispose();
        buffer = new(capacity);
        write = 0;
    }

    public void Reset() {
        buffer.Clear();
        write = 0;
    }

    public void Write(float value) {
        buffer[write] = MathF.Abs(value) < 1e-20f ? 0f : value;
        if (++write == buffer.Length) {
            write = 0;
        }
    }

    public float Read(float delay) {
        var position = write - Math.Max(MinDelay, delay);
        var index = (int)MathF.Floor(position);
        var frac = position - index;
        var y0 = At(index - 1);
        var y1 = At(index);
        var y2 = At(index + 1);
        var y3 = At(index + 2);
        var c1 = 0.5f * (y2 - y0);
        var c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
        var c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * frac + c2) * frac + c1) * frac + y1;
    }

    public void Dispose() {
        buffer.Dispose();
        buffer = NativeBuffer.Empty;
    }

    float At(int index) {
        index %= buffer.Length;
        return buffer[index < 0 ? index + buffer.Length : index];
    }
}
