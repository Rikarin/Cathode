using Cathode.Audio.Native;
using Cathode.Testing;
using System.Diagnostics;

namespace Cathode.Engine.Tests;

/// <summary>
///     The rule behind <see cref="NativeAudioSession.DeviceStatus" /> reporting a stalled stream as lost, without a
///     device: the count of callbacks and the time are made up.
/// </summary>
public class CallbackWatchdogTests {
    static readonly long Origin = Stopwatch.GetTimestamp();

    readonly CallbackWatchdog watchdog = new(NativeAudioSession.CallbackStall);

    [Fact]
    public void A_started_stream_whose_count_stands_still_for_the_stall_time_has_stalled() {
        watchdog.Start(100, At(0));

        Assert.False(watchdog.Stalled(100, At(0)));
        Assert.False(watchdog.Stalled(100, At(1.99)));
        Assert.True(watchdog.Stalled(100, At(2)));
        Assert.True(watchdog.Stalled(100, At(60)));
    }

    [Fact]
    public void A_callback_starts_the_time_again() {
        watchdog.Start(0, At(0));

        // Polled every quarter of a second, as the host does, with the count moving each time: never stalled.
        for (var poll = 1; poll <= 40; poll++) {
            Assert.False(watchdog.Stalled((ulong)poll, At(poll * 0.25)));
        }

        // The callbacks stop at 10 s: lost two seconds after the last one seen, not two seconds after the start.
        Assert.False(watchdog.Stalled(40, At(11.9)));
        Assert.True(watchdog.Stalled(40, At(12)));

        // One slow poll is not a stall either: the count moved since the poll before it.
        watchdog.Start(40, At(20));
        Assert.False(watchdog.Stalled(41, At(30)));
        Assert.False(watchdog.Stalled(41, At(31.9)));
    }

    [Fact]
    public void A_stream_that_is_not_started_never_stalls() {
        Assert.False(watchdog.Running);
        Assert.False(watchdog.Stalled(0, At(100)));

        watchdog.Start(5, At(100));
        Assert.True(watchdog.Running);
        watchdog.Stop();

        Assert.False(watchdog.Running);
        Assert.False(watchdog.Stalled(5, At(200)));

        // Started again, the time counts from the new start, whatever went by while it was stopped.
        watchdog.Start(5, At(300));
        Assert.False(watchdog.Stalled(5, At(301.9)));
        Assert.True(watchdog.Stalled(5, At(302)));
    }

    [Fact]
    public void Polling_allocates_nothing() {
        watchdog.Start(0, At(0));
        var count = 0UL;

        Assert.Equal(0, AllocationProbe.Measure(() => watchdog.Stalled(count++ / 2, At(count))));
    }

    static long At(double seconds) => Origin + (long)(seconds * Stopwatch.Frequency);
}
