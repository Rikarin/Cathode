namespace Cathode.Dsp.Reverb;

/// <summary>A circular delay line in native memory with integer taps and a linear fractional read.</summary>
sealed class ReverbLine : IDisposable {
    const float DenormalThreshold = 1e-20f;

    NativeBuffer buffer = NativeBuffer.Empty;
    int write;

    public int Length => buffer.Length;

    public void Allocate(int length) {
        buffer.Dispose();
        buffer = new(length);
        write = 0;
    }

    public void Clear() {
        buffer.Clear();
        write = 0;
    }

    public void Dispose() => buffer.Dispose();

    /// <summary>The value written <paramref name="delay" /> samples ago (1 = the most recent write).</summary>
    public float Tap(int delay) {
        var index = write - delay;
        return buffer[index < 0 ? index + buffer.Length : index];
    }

    /// <summary>A fractional tap, <paramref name="delay" /> in [1, Length - 1].</summary>
    public float Read(float delay) {
        var whole = (int)delay;
        var frac = delay - whole;
        var a = Tap(whole);
        var b = Tap(whole + 1);
        return a + (b - a) * frac;
    }

    public void Write(float value) {
        buffer[write] = MathF.Abs(value) < DenormalThreshold ? 0f : value;
        if (++write == buffer.Length) {
            write = 0;
        }
    }

    /// <summary>
    ///     A plain delay of <paramref name="delay" /> samples: returns the old value, then writes the new one.
    /// </summary>
    public float Delay(float value, int delay) {
        var output = Tap(delay);
        Write(value);
        return output;
    }

    /// <summary>
    ///     A Schroeder allpass of length <paramref name="delay" /> (fractional, so it can be modulated) built on this
    ///     line; taps into the line read its internal node.
    /// </summary>
    public float Allpass(float value, float delay, float gain) {
        var delayed = Read(delay);
        var node = value + gain * delayed;
        Write(node);
        return delayed - gain * node;
    }
}
