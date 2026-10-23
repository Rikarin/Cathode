namespace Cathode.Engine.Telemetry;

/// <summary>A copy of a <see cref="TelemetryRegion" />. Reusable: <see cref="TelemetryRegion.TryRead" /> overwrites it.</summary>
public sealed class TelemetrySnapshot {
    public long Callbacks { get; set; }

    /// <summary>Frames processed since the region was created: the meters' clock.</summary>
    public long Frames { get; set; }

    public long Overruns { get; set; }

    public long DriverOverloads { get; set; }

    public double LastPercent { get; set; }

    public double MaxPercent { get; set; }

    public double SampleRate { get; set; }

    public long[] Histogram { get; } = new long[TelemetryLayout.Bins];

    public long[] SlotNumbers { get; } = new long[TelemetryLayout.MeterSlots];

    public float[] InputPeaks { get; } = new float[TelemetryLayout.MeterSlots];

    public float[] OutputLeftPeaks { get; } = new float[TelemetryLayout.MeterSlots];

    public float[] OutputRightPeaks { get; } = new float[TelemetryLayout.MeterSlots];

    /// <summary>The meter slot that holds frame <see cref="Frames" /> - 1, or -1 before the first callback.</summary>
    public long CurrentSlot => Frames == 0 ? -1 : (Frames - 1) / TelemetryLayout.FramesPerSlot;

    /// <summary>Peaks over the slots after <paramref name="slot" /> up to <see cref="CurrentSlot" />.</summary>
    public (float Input, float OutputLeft, float OutputRight) PeaksSince(long slot) {
        float input = 0, left = 0, right = 0;
        var current = CurrentSlot;
        for (var i = 0; i < TelemetryLayout.MeterSlots; i++) {
            var number = SlotNumbers[i];
            if (number > slot && number <= current) {
                input = Math.Max(input, InputPeaks[i]);
                left = Math.Max(left, OutputLeftPeaks[i]);
                right = Math.Max(right, OutputRightPeaks[i]);
            }
        }

        return (input, left, right);
    }

    public void CopyTo(TelemetrySnapshot other) {
        other.Callbacks = Callbacks;
        other.Frames = Frames;
        other.Overruns = Overruns;
        other.DriverOverloads = DriverOverloads;
        other.LastPercent = LastPercent;
        other.MaxPercent = MaxPercent;
        other.SampleRate = SampleRate;
        Histogram.CopyTo(other.Histogram, 0);
        SlotNumbers.CopyTo(other.SlotNumbers, 0);
        InputPeaks.CopyTo(other.InputPeaks, 0);
        OutputLeftPeaks.CopyTo(other.OutputLeftPeaks, 0);
        OutputRightPeaks.CopyTo(other.OutputRightPeaks, 0);
    }
}

/// <summary>
///     What a <see cref="TelemetryMonitor" /> reports per poll: peaks since the previous poll; the callback time (as a
///     percentage of budget) below which 99.9 % of callbacks fell and the highest histogram bin reached, both over the
///     window at 1 % resolution; and cumulative overruns, driver overloads and callbacks.
/// </summary>
public readonly record struct TelemetryReading(
    float InputPeak,
    float OutputPeakLeft,
    float OutputPeakRight,
    double CpuP999,
    double CpuMax,
    long Overruns,
    long DriverOverloads,
    long Callbacks
);

/// <summary>
///     Polls a <see cref="TelemetryRegion" />: meter peaks since the previous poll, and CPU over a window of
///     <see cref="Window" /> to twice that, from the difference between histogram snapshots. One per reader;
///     allocation-free after construction.
/// </summary>
public sealed class TelemetryMonitor(TelemetryRegion region) {
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2.5);

    readonly TelemetrySnapshot current = new();
    readonly TelemetrySnapshot older = new();
    readonly TelemetrySnapshot newer = new();
    long lastSlot = -1;
    bool started;

    public TelemetryRegion Region => region;

    /// <summary>Returns null when the region could not be read consistently (a writer stuck mid-update).</summary>
    public TelemetryReading? Poll() {
        if (!region.TryRead(current)) {
            return null;
        }

        if (!started) {
            current.CopyTo(older);
            current.CopyTo(newer);
            lastSlot = current.CurrentSlot;
            started = true;
        }

        var (input, left, right) = current.PeaksSince(lastSlot);
        lastSlot = current.CurrentSlot;

        // Roll the two staggered baselines so the window always spans between one and two Window lengths.
        var windowFrames = (long)(Window.TotalSeconds * Math.Max(current.SampleRate, 1));
        if (current.Frames - newer.Frames >= windowFrames) {
            newer.CopyTo(older);
            current.CopyTo(newer);
        }

        var (p999, max) = Cpu(older, current);
        return new(input, left, right, p999, max, current.Overruns, current.DriverOverloads, current.Callbacks);
    }

    static (double P999, double Max) Cpu(TelemetrySnapshot from, TelemetrySnapshot to) {
        var total = to.Callbacks - from.Callbacks;
        if (total <= 0) {
            return (0, 0);
        }

        var threshold = (long)Math.Ceiling(0.999 * total);
        long running = 0;
        double p999 = 0, max = 0;
        for (var i = 0; i < TelemetryLayout.Bins; i++) {
            var count = to.Histogram[i] - from.Histogram[i];
            if (count <= 0) {
                continue;
            }

            running += count;
            if (p999 == 0 && running >= threshold) {
                p999 = i + 1;
            }

            max = i + 1;
        }

        return (p999, max);
    }
}
