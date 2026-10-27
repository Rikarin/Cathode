namespace Cathode.Dsp.Routing;

/// <summary>
///     Where an RTA block leaves the signal it analyses: one sub-block of mono samples, written by the block on the
///     audio thread and taken by the engine after the graph has run, which hands them to the telemetry region's
///     analyser ring. Each compiled graph has its own, so a graph spilling over after a preset change never mixes into
///     it.
/// </summary>
public sealed class AnalyzerTap(int capacity) {
    readonly float[] samples = new float[capacity];
    int count;

    /// <summary>Audio thread: appends <paramref name="mono" /> (what does not fit is dropped).</summary>
    public void Write(ReadOnlySpan<float> mono) {
        var length = Math.Min(mono.Length, samples.Length - count);
        mono[..length].CopyTo(samples.AsSpan(count));
        count += length;
    }

    /// <summary>
    ///     Audio thread: the samples written since the last call, valid until the next <see cref="Write" />.
    /// </summary>
    public ReadOnlySpan<float> Take() {
        var taken = samples.AsSpan(0, count);
        count = 0;
        return taken;
    }
}
