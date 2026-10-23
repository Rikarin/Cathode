namespace Cathode.Testing;

/// <summary>Measures managed allocations made by the current thread (the .NET stand-in for a realtime sanitizer).</summary>
public static class AllocationProbe {
    /// <summary>
    ///     Runs <paramref name="warmUp" /> first (JIT tiering, static init), then returns bytes allocated by
    ///     <paramref name="action" />.
    /// </summary>
    public static long Measure(Action action, Action? warmUp = null) {
        (warmUp ?? action)();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
