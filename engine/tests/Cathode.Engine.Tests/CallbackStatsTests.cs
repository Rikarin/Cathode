using System.Diagnostics;

namespace Cathode.Engine.Tests;

/// <summary>The callback statistics' overrun counts across a measurement window's reset.</summary>
public sealed class CallbackStatsTests {
    /// <summary>
    ///     #603: the window's count goes back to 0 at the first callback after a reset, the total does not, so a reader
    ///     counting from the total adds nothing twice, whether or not a callback has run since the reset.
    /// </summary>
    [Fact]
    public void The_total_overrun_count_survives_a_window_reset() {
        var stats = new CallbackStats();
        var late = Stopwatch.GetTimestamp() - Stopwatch.Frequency; // a callback that took a second

        stats.Record(late, 64, 48_000);
        stats.Record(late, 64, 48_000);
        stats.RequestReset();
        Assert.Equal((2L, 2L), (stats.Overruns, stats.TotalOverruns)); // no callback yet: the reset is still pending

        stats.Record(late, 64, 48_000);

        Assert.Equal((1L, 3L), (stats.Overruns, stats.TotalOverruns));
    }
}
