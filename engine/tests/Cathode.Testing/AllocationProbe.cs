namespace Cathode.Testing;

/// <summary>
///     Measures managed allocations made by the current thread (the .NET stand-in for a realtime sanitizer).
/// </summary>
public static class AllocationProbe {
    const int Attempts = 3;

    /// <summary>
    ///     Runs <paramref name="warmUp" /> first (JIT tiering, static init), then returns bytes allocated by
    ///     <paramref name="action" />: the least of up to three measurements. The runtime occasionally does one-off
    ///     work on the measuring thread (a rejit, on-stack replacement, a cold type load); real allocation in the
    ///     action shows up in every measurement, a one-off does not.
    /// </summary>
    public static long Measure(Action action, Action? warmUp = null) {
        (warmUp ?? action)();
        var least = long.MaxValue;
        for (var i = 0; i < Attempts && least > 0; i++) {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }
}
