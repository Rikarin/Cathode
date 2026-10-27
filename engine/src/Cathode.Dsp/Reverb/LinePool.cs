namespace Cathode.Dsp.Reverb;

/// <summary>
///     Many circular delay lines of different lengths in one native buffer (one object for a whole tank, so a preset
///     load allocates little). Reads, writes and the allpass match <see cref="ReverbLine" />.
/// </summary>
sealed class LinePool(int count) : IDisposable {
    const float DenormalThreshold = 1e-20f;

    readonly Line[] lines = new Line[count];
    NativeBuffer buffer = NativeBuffer.Empty;

    /// <summary>Control thread: one line per entry of <paramref name="lineLengths" />, in samples.</summary>
    public void Allocate(ReadOnlySpan<int> lineLengths) {
        var total = 0;
        for (var i = 0; i < count; i++) {
            var length = Math.Max(2, lineLengths[i]);
            lines[i] = new() { Start = total, Length = length };
            total += length;
        }

        buffer.Dispose();
        buffer = new(total);
    }

    /// <summary>The allocated length of <paramref name="line" />.</summary>
    public int Length(int line) => lines[line].Length;

    public void Clear() {
        buffer.Clear();
        for (var i = 0; i < count; i++) {
            lines[i].Write = 0;
        }
    }

    public void Dispose() => buffer.Dispose();

    /// <summary>
    ///     The value written <paramref name="delay" /> samples ago on <paramref name="line" /> (1 = the latest).
    /// </summary>
    public float Tap(int line, int delay) {
        ref readonly var l = ref lines[line];
        var index = l.Write - delay;
        return buffer[l.Start + (index < 0 ? index + l.Length : index)];
    }

    /// <summary>A linear fractional tap, <paramref name="delay" /> in [1, length - 1].</summary>
    public float Read(int line, float delay) {
        var whole = (int)delay;
        var frac = delay - whole;
        var a = Tap(line, whole);
        var b = Tap(line, whole + 1);
        return a + (b - a) * frac;
    }

    public void Write(int line, float value) {
        ref var l = ref lines[line];
        buffer[l.Start + l.Write] = MathF.Abs(value) < DenormalThreshold ? 0f : value;
        if (++l.Write == l.Length) {
            l.Write = 0;
        }
    }

    /// <summary>
    ///     A plain delay of <paramref name="delay" /> samples: returns the old value, then writes the new one.
    /// </summary>
    public float Delay(int line, float value, int delay) {
        var output = Tap(line, delay);
        Write(line, value);
        return output;
    }

    /// <summary>
    ///     A Schroeder allpass of fractional length <paramref name="delay" />; taps read its internal node.
    /// </summary>
    public float Allpass(int line, float value, float delay, float gain) {
        var delayed = Read(line, delay);
        var node = value + gain * delayed;
        Write(line, node);
        return delayed - gain * node;
    }

    struct Line {
        public int Start;
        public int Length;
        public int Write;
    }
}
