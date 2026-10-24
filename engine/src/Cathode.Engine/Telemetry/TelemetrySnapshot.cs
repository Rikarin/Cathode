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

    public int[] BlockGenerations { get; } = new int[TelemetryLayout.MeterSlots];

    public int[] BlockCounts { get; } = new int[TelemetryLayout.MeterSlots];

    /// <summary>Block peaks, <see cref="TelemetryLayout.BlockMeters" /> per meter slot.</summary>
    public float[] BlockPeaks { get; } = new float[TelemetryLayout.MeterSlots * TelemetryLayout.BlockMeters];

    public float LoudnessMomentary { get; set; }

    public float LoudnessShortTerm { get; set; }

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

    /// <summary>
    ///     The highest peak of each grid block over the slots after <paramref name="slot" /> up to
    ///     <see cref="CurrentSlot" />, for the newest graph generation among them, into <paramref name="destination" />
    ///     (by graph slot). Returns that generation and how many blocks it has, or (-1, 0) when no slot is new.
    /// </summary>
    public (int Generation, int Count) BlockPeaksSince(long slot, Span<float> destination) {
        var current = CurrentSlot;
        long newest = -1;
        var generation = -1;
        for (var i = 0; i < TelemetryLayout.MeterSlots; i++) {
            if (SlotNumbers[i] > slot && SlotNumbers[i] <= current && SlotNumbers[i] > newest) {
                newest = SlotNumbers[i];
                generation = BlockGenerations[i];
            }
        }

        destination.Clear();
        if (newest < 0 || generation < 0) {
            return (-1, 0);
        }

        var count = 0;
        for (var i = 0; i < TelemetryLayout.MeterSlots; i++) {
            if (SlotNumbers[i] <= slot || SlotNumbers[i] > current || BlockGenerations[i] != generation) {
                continue;
            }

            count = Math.Max(count, BlockCounts[i]);
            var row = BlockPeaks.AsSpan(i * TelemetryLayout.BlockMeters, Math.Min(BlockCounts[i], destination.Length));
            for (var b = 0; b < row.Length; b++) {
                destination[b] = Math.Max(destination[b], row[b]);
            }
        }

        return (generation, count);
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
        BlockGenerations.CopyTo(other.BlockGenerations, 0);
        BlockCounts.CopyTo(other.BlockCounts, 0);
        BlockPeaks.CopyTo(other.BlockPeaks, 0);
        other.LoudnessMomentary = LoudnessMomentary;
        other.LoudnessShortTerm = LoudnessShortTerm;
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
    readonly float[] blockPeaks = new float[TelemetryLayout.BlockMeters];
    long lastSlot = -1;
    bool started;

    public TelemetryRegion Region => region;

    /// <summary>Each grid block's peak since the previous poll, by graph slot (<see cref="BlockCount" /> of them).</summary>
    public ReadOnlySpan<float> BlockPeaks => blockPeaks.AsSpan(0, BlockCount);

    /// <summary>The graph generation <see cref="BlockPeaks" /> belong to, or -1.</summary>
    public int BlockGeneration { get; private set; } = -1;

    public int BlockCount { get; private set; }

    /// <summary>The output's momentary loudness (400 ms), LUFS, as of the last poll.</summary>
    public float LoudnessMomentary { get; private set; }

    /// <summary>The output's short-term loudness (3 s), LUFS, as of the last poll.</summary>
    public float LoudnessShortTerm { get; private set; }

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
        (BlockGeneration, BlockCount) = current.BlockPeaksSince(lastSlot, blockPeaks);
        LoudnessMomentary = current.LoudnessMomentary;
        LoudnessShortTerm = current.LoudnessShortTerm;
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
