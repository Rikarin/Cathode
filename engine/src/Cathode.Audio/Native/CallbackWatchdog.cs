using System.Diagnostics;

namespace Cathode.Audio.Native;

/// <summary>
///     Tells a started stream whose callback has stopped running: the backstop for a backend that cannot tell its
///     device is gone. The caller polls it with the callback count and a <see cref="Stopwatch" /> timestamp; the
///     stream has stalled once the count has not moved for <paramref name="stall" /> while it is started. It holds no
///     clock of its own, so it can be tested without a device or a wait.
/// </summary>
public sealed class CallbackWatchdog(TimeSpan stall) {
    readonly Lock gate = new();
    bool running;
    ulong lastCallbacks;
    long lastMovedAt; // timestamp when the callback count last moved, or the stream started

    /// <summary>Whether the stream is started: a stopped one has no callbacks to miss.</summary>
    public bool Running {
        get {
            lock (gate) {
                return running;
            }
        }
    }

    /// <summary>The stream has started with <paramref name="callbacks" /> counted so far.</summary>
    public void Start(ulong callbacks, long timestamp) {
        lock (gate) {
            lastCallbacks = callbacks;
            lastMovedAt = timestamp;
            running = true;
        }
    }

    /// <summary>The stream is being stopped: its callbacks end, and that is no stall.</summary>
    public void Stop() {
        lock (gate) {
            running = false;
        }
    }

    /// <summary>
    ///     Allocation-free. Whether the started stream's count has stood at <paramref name="callbacks" /> for the stall
    ///     time by <paramref name="timestamp" />. A count that moved starts the time again.
    /// </summary>
    public bool Stalled(ulong callbacks, long timestamp) {
        lock (gate) {
            if (!running) {
                return false;
            }

            if (callbacks != lastCallbacks) {
                lastCallbacks = callbacks;
                lastMovedAt = timestamp;
                return false;
            }

            return Stopwatch.GetElapsedTime(lastMovedAt, timestamp) >= stall;
        }
    }
}
