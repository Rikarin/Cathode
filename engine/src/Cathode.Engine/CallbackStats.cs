using System.Diagnostics;

namespace Cathode.Engine;

/// <summary>
///     Allocation-free timing statistics for audio callbacks: a histogram of callback duration as a percentage of
///     the real-time budget (1 % bins, last bin catches everything at or above 200 %).
/// </summary>
public sealed class CallbackStats {
    const int Bins = 201;

    readonly long[] histogram = new long[Bins];
    long count;
    long overruns;
    int resetRequested;

    public long Count => Volatile.Read(ref count);

    /// <summary>Callbacks that took longer than their real-time budget (a dropout on real hardware).</summary>
    public long Overruns => Volatile.Read(ref overruns);

    public double MaxPercent { get; private set; }
    public double LastPercent { get; private set; }

    /// <summary>Audio thread: record one callback that started at <paramref name="startTimestamp" />.</summary>
    public void Record(long startTimestamp, int frames, double sampleRate) {
        if (Volatile.Read(ref resetRequested) != 0) {
            Reset();
            Volatile.Write(ref resetRequested, 0);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        var percent = elapsed / (frames / sampleRate) * 100.0;
        LastPercent = percent;
        if (percent > MaxPercent) {
            MaxPercent = percent;
        }

        if (percent >= 100.0) {
            Interlocked.Increment(ref overruns);
        }

        histogram[Math.Min(Bins - 1, (int)percent)]++;
        Interlocked.Increment(ref count);
    }

    /// <summary>Budget percentage below which <paramref name="quantile" /> of callbacks fall (bin resolution: 1 %).</summary>
    public double Percentile(double quantile) {
        var total = Count;
        if (total == 0) {
            return 0;
        }

        var threshold = (long)Math.Ceiling(quantile * total);
        long running = 0;
        for (var i = 0; i < Bins; i++) {
            running += histogram[i];
            if (running >= threshold) {
                return i + 1;
            }
        }

        return Bins;
    }

    /// <summary>
    ///     Any thread: start a new measurement window. The audio thread clears the statistics before its next record,
    ///     so the reset never races a callback in progress.
    /// </summary>
    public void RequestReset() => Volatile.Write(ref resetRequested, 1);

    /// <summary>Clears the statistics. Only safe when no callback is recording (offline use, or the audio thread).</summary>
    public void Reset() {
        Array.Clear(histogram);
        count = 0;
        overruns = 0;
        MaxPercent = 0;
        LastPercent = 0;
    }
}
