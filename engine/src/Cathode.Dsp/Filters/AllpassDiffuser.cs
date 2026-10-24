namespace Cathode.Dsp.Filters;

/// <summary>
///     Four Schroeder allpasses in series (about 2 to 8 ms, mutually prime at 48 kHz), which smear a signal into a
///     dense, flat-spectrum cloud without colouring it: the diffusion of a feedback path. At zero diffusion it is
///     bypassed exactly. Buffers live in native memory.
/// </summary>
public sealed class AllpassDiffuser : IDisposable {
    /// <summary>The largest allpass gain, at 100 % diffusion.</summary>
    public const float MaxGain = 0.7f;

    static readonly double[] DelaysMs = [2.1, 3.7, 5.3, 7.9];

    readonly NativeBuffer[] lines = [NativeBuffer.Empty, NativeBuffer.Empty, NativeBuffer.Empty, NativeBuffer.Empty];
    readonly int[] positions = new int[4];

    /// <summary>Control thread: sizes the allpasses for <paramref name="sampleRate" />.</summary>
    public void Prepare(double sampleRate) {
        for (var i = 0; i < lines.Length; i++) {
            lines[i].Dispose();
            lines[i] = new(Math.Max(1, (int)Math.Round(DelaysMs[i] * 0.001 * sampleRate)));
        }

        Reset();
    }

    public void Reset() {
        foreach (var line in lines) {
            line.Clear();
        }

        Array.Clear(positions);
    }

    /// <summary>One sample through the chain at <paramref name="diffusion" /> (0..1).</summary>
    public float Process(float x, float diffusion) {
        if (diffusion <= 0) {
            return x;
        }

        var g = MaxGain * Math.Min(diffusion, 1f);
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            var position = positions[i];
            var delayed = line[position];
            var v = x + g * delayed;
            v = MathF.Abs(v) < 1e-20f ? 0f : v;
            line[position] = v;
            x = delayed - g * v;
            positions[i] = position + 1 == line.Length ? 0 : position + 1;
        }

        return x;
    }

    public void Dispose() {
        for (var i = 0; i < lines.Length; i++) {
            lines[i].Dispose();
            lines[i] = NativeBuffer.Empty;
        }
    }
}
