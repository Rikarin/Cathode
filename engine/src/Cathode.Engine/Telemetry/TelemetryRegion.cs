using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Cathode.Engine.Telemetry;

/// <summary>
///     Shared-memory telemetry (docs/02-architecture.md, telemetry plane): a fixed-layout region the audio thread
///     writes once per callback and any number of readers poll, in this process or another, without a lock and without
///     resetting anything. It holds the callback count, a frame clock, the CPU histogram (cumulative, so readers diff
///     two snapshots for a window), engine overruns, driver overloads, per-slot meter peaks, every block meter and each
///     modifier's current value.
/// </summary>
/// <remarks>
///     <para>
///         Consistency is a seqlock: the writer makes <see cref="TelemetryLayout.Sequence" /> odd, writes, then makes
///         it even; a reader copies the region and retries if the sequence was odd or changed. The writer never waits.
///     </para>
///     <para>
///         Meters live in <see cref="TelemetryLayout.MeterSlots" /> slots of
///         <see cref="TelemetryLayout.FramesPerSlot" /> frames each, tagged with their absolute slot number (frame /
///         FramesPerSlot). A reader takes the maximum over the slots since its previous read, so every peak is seen
///         once by every reader, at any polling rate up to the history length (64 x 256 frames, 340 ms at 48 kHz).
///     </para>
///     <para>
///         A scope ring holds the last <see cref="TelemetryLayout.ScopeLength" /> output samples (left) for the
///         editor's oscilloscope and the RTA / Tone Match analysers.
///     </para>
///     <para>
///         Across processes the region is a memory-mapped file: the host creates it next to its IPC socket and names
///         it in the <c>hello</c> reply; the server maps it read-only.
///     </para>
/// </remarks>
public sealed unsafe class TelemetryRegion : IDisposable {
    readonly MemoryMappedFile? file;
    readonly MemoryMappedViewAccessor? view;
    readonly bool owner;
    TelemetryLayout* layout;

    /// <summary>The backing file, or null for an in-memory region.</summary>
    public string? Path { get; }

    public double SampleRate => layout->SampleRate;

    /// <summary>How many samples an RTA block has written in all; it stops growing when no block is running.</summary>
    public long AnalyzerWritten => Volatile.Read(ref layout->AnalyzerWritten);

    TelemetryRegion(
        TelemetryLayout* layout,
        string? path,
        MemoryMappedFile? file,
        MemoryMappedViewAccessor? view,
        bool owner
    ) {
        this.layout = layout;
        this.file = file;
        this.view = view;
        this.owner = owner;
        Path = path;
    }

    /// <summary>A region in native memory, for an engine and readers in the same process.</summary>
    public static TelemetryRegion CreateInMemory(double sampleRate) {
        var memory = (TelemetryLayout*)NativeMemory.AlignedAlloc((nuint)sizeof(TelemetryLayout), 64);
        Initialise(memory, sampleRate);
        return new(memory, null, null, null, true);
    }

    /// <summary>Creates (or truncates) <paramref name="path" /> and maps it for writing.</summary>
    public static TelemetryRegion CreateFile(string path, double sampleRate) {
        var size = sizeof(TelemetryLayout);
        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );
        stream.SetLength(size);
        var file = MemoryMappedFile.CreateFromFile(
            stream,
            null,
            size,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            false
        );
        var view = file.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
        var mapped = Acquire(view);
        var memory = (TelemetryLayout*)(mapped + view.PointerOffset);
        Initialise(memory, sampleRate);
        return new(memory, path, file, view, true);
    }

    /// <summary>
    ///     Maps an existing region read-only. Throws <see cref="InvalidDataException" /> if it is not one.
    /// </summary>
    public static TelemetryRegion OpenFile(string path) {
        var size = sizeof(TelemetryLayout);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < size) {
            stream.Dispose();
            throw new InvalidDataException($"'{path}' is too small to be a telemetry region.");
        }

        var file = MemoryMappedFile.CreateFromFile(
            stream,
            null,
            0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            false
        );
        var view = file.CreateViewAccessor(0, size, MemoryMappedFileAccess.Read);
        var mapped = Acquire(view);
        var memory = (TelemetryLayout*)(mapped + view.PointerOffset);
        var region = new TelemetryRegion(memory, path, file, view, false);
        if (memory->Magic != TelemetryLayout.MagicValue
            || memory->Version != TelemetryLayout.CurrentVersion
            || memory->Size != size) {
            region.Dispose();
            throw new InvalidDataException($"'{path}' is not a compatible telemetry region.");
        }

        return region;
    }

    /// <summary>
    ///     Audio thread: account one callback of <paramref name="frames" /> that took <paramref name="percent" /> of
    ///     its budget, with the peaks it saw. Lock-free and allocation-free.
    /// </summary>
    public void RecordCallback(int frames, double percent, float input, float outputLeft, float outputRight) =>
        RecordCallback(frames, percent, input, outputLeft, outputRight, default, default, -1, 0f, 0f);

    /// <summary>
    ///     As above, plus the block meters of graph <paramref name="generation" /> (<c>CompiledGraph.Meters</c>: each
    ///     slot's output peak and its own meters; at most <see cref="TelemetryLayout.BlockMeters" />) and the output
    ///     loudness in LUFS. Each meter slot keeps the highest value of every block meter.
    ///     <paramref name="modifiers" /> is each modifier's value as of this callback
    ///     (<c>CompiledGraph.ModifierValues</c>, NaN where it drives nothing; at most
    ///     <see cref="TelemetryLayout.Modifiers" />): the latest, not a peak.
    /// </summary>
    public void RecordCallback(
        int frames,
        double percent,
        float input,
        float outputLeft,
        float outputRight,
        ReadOnlySpan<float> blockMeters,
        ReadOnlySpan<float> modifiers,
        int generation,
        float momentary,
        float shortTerm
    ) {
        var l = layout;
        Interlocked.Increment(ref l->Sequence); // odd: a write is in progress (and a full fence before the data)

        var start = l->Frames;
        l->Callbacks++;
        l->Frames = start + frames;
        l->LastPercent = percent;
        if (percent > l->MaxPercent) {
            l->MaxPercent = percent;
        }

        if (percent >= 100.0) {
            l->Overruns++;
        }

        l->Histogram[Math.Clamp((int)percent, 0, TelemetryLayout.Bins - 1)]++;
        l->LoudnessMomentary = momentary;
        l->LoudnessShortTerm = shortTerm;
        var blocks = Math.Min(blockMeters.Length, TelemetryLayout.BlockMeters);
        var modified = Math.Min(modifiers.Length, TelemetryLayout.Modifiers);
        l->ModifierGeneration = generation;
        l->ModifierCount = modified;
        modifiers[..modified].CopyTo(new(l->Modifier, TelemetryLayout.Modifiers));

        var first = start / TelemetryLayout.FramesPerSlot;
        var last = (start + Math.Max(frames, 1) - 1) / TelemetryLayout.FramesPerSlot;
        if (last - first >= TelemetryLayout.MeterSlots) {
            first = last - TelemetryLayout.MeterSlots + 1;
        }

        for (var slot = first; slot <= last; slot++) {
            var i = (int)(slot % TelemetryLayout.MeterSlots);
            var row = new Span<float>(l->BlockMeter + i * TelemetryLayout.BlockMeters, TelemetryLayout.BlockMeters);
            if (l->SlotNumber[i] != slot || l->BlockGeneration[i] != generation) {
                if (l->SlotNumber[i] != slot) {
                    l->InputPeak[i] = input;
                    l->OutputLeftPeak[i] = outputLeft;
                    l->OutputRightPeak[i] = outputRight;
                } else {
                    l->InputPeak[i] = Math.Max(l->InputPeak[i], input);
                    l->OutputLeftPeak[i] = Math.Max(l->OutputLeftPeak[i], outputLeft);
                    l->OutputRightPeak[i] = Math.Max(l->OutputRightPeak[i], outputRight);
                }

                l->SlotNumber[i] = slot;
                l->BlockGeneration[i] = generation;
                l->BlockMeterCount[i] = blocks;
                blockMeters[..blocks].CopyTo(row);
            } else {
                l->InputPeak[i] = Math.Max(l->InputPeak[i], input);
                l->OutputLeftPeak[i] = Math.Max(l->OutputLeftPeak[i], outputLeft);
                l->OutputRightPeak[i] = Math.Max(l->OutputRightPeak[i], outputRight);
                for (var b = 0; b < blocks; b++) {
                    row[b] = Math.Max(row[b], blockMeters[b]);
                }
            }
        }

        Volatile.Write(ref l->Sequence, l->Sequence + 1); // even again; release orders the data before it
    }

    /// <summary>
    ///     Audio thread: append output samples to the scope ring. The ring is outside the seqlock: the reader copies
    ///     the samples behind the published write count and drops any the writer overwrote meanwhile.
    /// </summary>
    public void WriteScope(ReadOnlySpan<float> samples) => Write(ref layout->ScopeWritten, layout->Scope, samples);

    /// <summary>
    ///     Audio thread: append an RTA block's samples to the analyser ring (like <see cref="WriteScope" />).
    /// </summary>
    public void WriteAnalyzer(ReadOnlySpan<float> samples) =>
        Write(ref layout->AnalyzerWritten, layout->Analyzer, samples);

    /// <summary>
    ///     Copies the most recent <paramref name="destination" />.Length output samples (at most
    ///     <see cref="TelemetryLayout.ScopeLength" /> / 2). Returns false until that many have been written, or if the
    ///     writer kept overwriting them.
    /// </summary>
    public bool ReadScope(Span<float> destination) => Read(ref layout->ScopeWritten, layout->Scope, destination);

    /// <summary>As <see cref="ReadScope" />, from the RTA block's ring.</summary>
    public bool ReadAnalyzer(Span<float> destination) =>
        Read(ref layout->AnalyzerWritten, layout->Analyzer, destination);

    /// <summary>Host control thread: publish the driver's cumulative overload (xrun) count.</summary>
    public void SetDriverOverloads(long count) => Volatile.Write(ref layout->DriverOverloads, count);

    /// <summary>
    ///     Copies a consistent snapshot into <paramref name="snapshot" />. Returns false if the writer kept the region
    ///     busy for every attempt (it then holds the previous contents).
    /// </summary>
    public bool TryRead(TelemetrySnapshot snapshot) {
        var l = layout;
        for (var attempt = 0; attempt < 64; attempt++) {
            var before = Volatile.Read(ref l->Sequence);
            if ((before & 1) != 0) {
                Thread.SpinWait(16);
                continue;
            }

            snapshot.Callbacks = l->Callbacks;
            snapshot.Frames = l->Frames;
            snapshot.Overruns = l->Overruns;
            snapshot.LastPercent = l->LastPercent;
            snapshot.MaxPercent = l->MaxPercent;
            new ReadOnlySpan<long>(l->Histogram, TelemetryLayout.Bins).CopyTo(snapshot.Histogram);
            new ReadOnlySpan<long>(l->SlotNumber, TelemetryLayout.MeterSlots).CopyTo(snapshot.SlotNumbers);
            new ReadOnlySpan<float>(l->InputPeak, TelemetryLayout.MeterSlots).CopyTo(snapshot.InputPeaks);
            new ReadOnlySpan<float>(l->OutputLeftPeak, TelemetryLayout.MeterSlots).CopyTo(snapshot.OutputLeftPeaks);
            new ReadOnlySpan<float>(l->OutputRightPeak, TelemetryLayout.MeterSlots).CopyTo(snapshot.OutputRightPeaks);
            new ReadOnlySpan<int>(l->BlockGeneration, TelemetryLayout.MeterSlots).CopyTo(snapshot.BlockGenerations);
            new ReadOnlySpan<int>(l->BlockMeterCount, TelemetryLayout.MeterSlots).CopyTo(snapshot.BlockMeterCounts);
            new ReadOnlySpan<float>(l->BlockMeter, TelemetryLayout.MeterSlots * TelemetryLayout.BlockMeters)
                .CopyTo(snapshot.BlockMeters);
            snapshot.LoudnessMomentary = l->LoudnessMomentary;
            snapshot.LoudnessShortTerm = l->LoudnessShortTerm;
            snapshot.ModifierGeneration = l->ModifierGeneration;
            snapshot.ModifierCount = l->ModifierCount;
            new ReadOnlySpan<float>(l->Modifier, TelemetryLayout.Modifiers).CopyTo(snapshot.Modifiers);

            Interlocked.MemoryBarrier(); // the copies above must complete before the sequence is checked again
            if (Volatile.Read(ref l->Sequence) == before) {
                snapshot.DriverOverloads = Volatile.Read(ref l->DriverOverloads);
                snapshot.SampleRate = l->SampleRate;
                return true;
            }
        }

        return false;
    }

    public void Dispose() {
        if (layout is null) {
            return;
        }

        if (view is not null) {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
            view.Dispose();
            file!.Dispose();
            if (owner && Path is not null) {
                try {
                    File.Delete(Path);
                } catch (IOException) {
                    // Best effort: the view is already unmapped, and a file left behind costs only its disk space.
                } catch (UnauthorizedAccessException) {
                    // As above: a region file this process may not delete is left for its owner to clean up.
                }
            }
        } else {
            NativeMemory.AlignedFree(layout);
        }

        layout = null;
    }

    static void Write(ref long writtenField, float* ring, ReadOnlySpan<float> samples) {
        var written = writtenField;
        var span = new Span<float>(ring, TelemetryLayout.ScopeLength);
        foreach (var sample in samples) {
            span[(int)(written++ % TelemetryLayout.ScopeLength)] = sample;
        }

        Volatile.Write(ref writtenField, written);
    }

    static bool Read(ref long writtenField, float* ring, Span<float> destination) {
        var count = Math.Min(destination.Length, TelemetryLayout.ScopeLength / 2);
        for (var attempt = 0; attempt < 8; attempt++) {
            var end = Volatile.Read(ref writtenField);
            if (end < count) {
                return false;
            }

            for (var i = 0; i < count; i++) {
                destination[i] = ring[(end - count + i) % TelemetryLayout.ScopeLength];
            }

            Interlocked.MemoryBarrier();
            // Valid if the writer has not wrapped round onto the oldest sample copied.
            if (Volatile.Read(ref writtenField) - end <= TelemetryLayout.ScopeLength - count) {
                return true;
            }
        }

        return false;
    }

    static byte* Acquire(MemoryMappedViewAccessor view) {
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        return pointer;
    }

    static void Initialise(TelemetryLayout* memory, double sampleRate) {
        NativeMemory.Clear(memory, (nuint)sizeof(TelemetryLayout));
        memory->Version = TelemetryLayout.CurrentVersion;
        memory->Size = sizeof(TelemetryLayout);
        memory->SampleRate = sampleRate;
        for (var i = 0; i < TelemetryLayout.MeterSlots; i++) {
            memory->SlotNumber[i] = -1;
        }

        memory->ModifierGeneration = -1;

        // Last, so a reader that checks the magic never sees a half-initialised region.
        Volatile.Write(ref memory->Magic, TelemetryLayout.MagicValue);
    }
}

/// <summary>The region's binary layout. Version it when it changes: other processes map it.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TelemetryLayout {
    public const uint MagicValue = 0x54485443; // "CTHT"
    public const int CurrentVersion = 7;
    public const int Bins = 201;
    public const int MeterSlots = 64;
    public const int FramesPerSlot = 256;
    public const int ScopeLength = 16_384; // a 4096-point RTA window fits with room for the writer

    public const int BlockMeters =
        256; // block meter values: a peak per graph slot (a 6 x 14 grid and its shunts) plus own meters

    /// <summary>
    ///     Modifier values: presets set no limit (one per parameter), and a rich one has a few dozen; any beyond this
    ///     run but are not shown.
    /// </summary>
    public const int Modifiers = 256;

    public uint Magic;
    public int Version;
    public int Size;
    public int Reserved;
    public double SampleRate;
    public long Sequence;
    public long Callbacks;
    public long Frames;
    public long Overruns;
    public long DriverOverloads;
    public double LastPercent;
    public double MaxPercent;
    public fixed long Histogram[Bins];
    public fixed long SlotNumber[MeterSlots];
    public fixed float InputPeak[MeterSlots];
    public fixed float OutputLeftPeak[MeterSlots];
    public fixed float OutputRightPeak[MeterSlots];

    /// <summary>The graph generation each meter slot's block meters belong to (their order is per graph).</summary>
    public fixed int BlockGeneration[MeterSlots];

    /// <summary>How many of <see cref="BlockMeters" /> each meter slot's row holds.</summary>
    public fixed int BlockMeterCount[MeterSlots];

    /// <summary>
    ///     Per meter slot, the highest value of each block meter, in the graph's meter order: graph slot by graph slot,
    ///     its output peak (linear) and then its own meters (<c>IBlockMeters</c>, dB) - <c>CompiledGraph.Meters</c>.
    /// </summary>
    public fixed float BlockMeter[MeterSlots * BlockMeters];

    /// <summary>The graph generation <see cref="Modifier" /> belongs to, or -1.</summary>
    public int ModifierGeneration;

    /// <summary>How many of <see cref="Modifiers" /> values <see cref="Modifier" /> holds.</summary>
    public int ModifierCount;

    /// <summary>
    ///     Each modifier's value as of the last callback, in its parameter's units, by index in the graph's modifiers
    ///     (<c>CompiledGraph.ModifierValues</c>): NaN for one that is disabled, has not run yet or engages its block.
    /// </summary>
    public fixed float Modifier[Modifiers];

    /// <summary>Output loudness (ITU-R BS.1770, LUFS) as of the last callback.</summary>
    public float LoudnessMomentary;

    public float LoudnessShortTerm;
    public long ScopeWritten;
    public fixed float Scope[ScopeLength];

    /// <summary>The RTA block's ring (the signal at its grid position), the same length as the scope's.</summary>
    public long AnalyzerWritten;

    public fixed float Analyzer[ScopeLength];
}
