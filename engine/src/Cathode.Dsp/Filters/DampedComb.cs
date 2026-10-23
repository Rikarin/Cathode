namespace Cathode.Dsp.Filters;

/// <summary>
///     Karplus–Strong-style feedback comb: <c>y[n] = x[n] + g * lowpass(y[n - D])</c>, with a linearly interpolated
///     fractional delay and a one-pole low-pass in the loop. Linear interpolation and the one-pole never exceed unity
///     gain at any frequency, so the loop is stable for any <c>|g| &lt; 1</c> however the delay and damping move.
///     The buffer lives in native memory (<see cref="NativeBuffer" />).
/// </summary>
public sealed class DampedComb : IDisposable {
    public const float MinDelay = 1f;

    NativeBuffer buffer = NativeBuffer.Empty;
    int write;
    float lowpass;

    /// <summary>
    ///     Loop delay of the one-pole <c>lp = (1 - a) x + a lp</c> at <paramref name="frequency" />, in samples. Subtract it
    ///     from the period so the comb's peaks stay in tune when damped.
    /// </summary>
    public static double PhaseDelay(double damping, double frequency, double sampleRate) {
        var w = 2 * Math.PI * frequency / sampleRate;
        return Math.Atan2(damping * Math.Sin(w), 1 - damping * Math.Cos(w)) / w;
    }

    /// <summary>Control thread: room for delays up to <paramref name="capacity" /> - 2 samples.</summary>
    public void Allocate(int capacity) {
        buffer.Dispose();
        buffer = new(capacity);
        write = 0;
        lowpass = 0;
    }

    public void Reset() {
        buffer.Clear();
        write = 0;
        lowpass = 0;
    }

    /// <summary>
    ///     One sample. <paramref name="delay" /> counts back from the next write (1 = the previous output);
    ///     <paramref name="damping" /> is the one-pole coefficient <c>a</c> in [0, 1).
    /// </summary>
    public float Tick(float input, float delay, float feedback, float damping) {
        var position = write - (double)Math.Max(MinDelay, delay);
        var index = (int)Math.Floor(position);
        var frac = (float)(position - index);
        var delayed = At(index) + (At(index + 1) - At(index)) * frac;
        lowpass = delayed + (lowpass - delayed) * damping;
        if (MathF.Abs(lowpass) < 1e-20f) {
            lowpass = 0;
        }

        var output = input + feedback * lowpass;
        buffer[write] = MathF.Abs(output) < 1e-20f ? 0f : output;
        if (++write == buffer.Length) {
            write = 0;
        }

        return output;
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
