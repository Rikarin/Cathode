namespace Cathode.EngineHost;

/// <summary>
///     MIDI clock out (docs/02-architecture.md §5.10): 24 ticks (0xF8) per quarter note at the rig's tempo, so external
///     delays, loopers and drum machines can follow it and its tap tempo. The MIDI thread calls <see cref="Advance" />
///     on every poll (about once a millisecond). The ticks are counted on the engine's sample clock, so they run at
///     the rate the synced LFOs, delays and the metronome run at and stop when the audio stops. That clock only moves
///     once per audio callback, so between callbacks the position is carried on by the OS clock, by at most one
///     callback's worth: a tick is sent within about a poll of when it is due, whatever the buffer size. Only ticks
///     are sent, no Start, Stop or Continue: Cathode has no transport.
/// </summary>
public sealed class MidiClockOut(double sampleRate, Func<string?, byte[], bool> send) {
    public const int TicksPerBeat = 24;

    /// <summary>
    ///     The most ticks one poll sends. More than this are due only after the thread stalled; the rest are dropped
    ///     rather than sent in a burst that a follower would read as a tempo jump.
    /// </summary>
    public const int MaxBurst = 3;

    static readonly byte[] Tick = [0xF8];

    readonly Lock gate = new();
    bool enabled;
    string? destination;
    bool started;
    long anchorSample;
    ulong anchorNs;
    long callbackFrames;
    double position; // in frames on the sample clock, never moving back
    double pending; // the fraction of a tick not yet sent

    /// <summary>
    ///     Any thread: turns the clock on or off and names its output (a port uid, or null for every output).
    /// </summary>
    public void Configure(bool enabled, string? destination) {
        lock (gate) {
            if (enabled != this.enabled) {
                started = false;
            }

            this.enabled = enabled;
            this.destination = destination;
        }
    }

    /// <summary>
    ///     MIDI thread: sends the ticks that have come due. <paramref name="sampleTime" /> is the engine's sample clock
    ///     (<c>CathodeEngine.SampleTime</c>), <paramref name="nowNs" /> the OS clock and <paramref name="bpm" /> the
    ///     rig's tempo now. Returns how many ticks it sent. Allocation-free.
    /// </summary>
    public int Advance(long sampleTime, ulong nowNs, double bpm) {
        lock (gate) {
            if (!enabled) {
                return 0;
            }

            if (!started || sampleTime < anchorSample) {
                // The first poll, or a new engine clock: count from here.
                started = true;
                anchorSample = sampleTime;
                anchorNs = nowNs;
                callbackFrames = 0;
                position = sampleTime;
                pending = 0;
                return 0;
            }

            if (sampleTime != anchorSample) {
                var first = callbackFrames == 0;
                callbackFrames = sampleTime - anchorSample;
                anchorSample = sampleTime;
                anchorNs = nowNs;
                if (first) {
                    // The first callback seen: its size is known only now, so nothing carried the position across
                    // it. Count from its end instead of sending its ticks at once.
                    position = sampleTime;
                    return 0;
                }
            }

            // Between callbacks the OS clock carries the position on, no further than the next callback would take it:
            // with the audio stopped it comes to rest there, and so do the ticks.
            var carried = nowNs > anchorNs ? (nowNs - anchorNs) * sampleRate / 1e9 : 0;
            var estimate = anchorSample + Math.Min(carried, callbackFrames);
            if (estimate <= position) {
                return 0;
            }

            pending += (estimate - position) / sampleRate * (bpm / 60) * TicksPerBeat;
            position = estimate;
            var due = (int)pending;
            pending -= due;
            var sent = Math.Min(due, MaxBurst);
            for (var i = 0; i < sent; i++) {
                send(destination, Tick);
            }

            return sent;
        }
    }
}
