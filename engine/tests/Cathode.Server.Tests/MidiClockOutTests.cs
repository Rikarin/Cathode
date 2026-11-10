using Cathode.EngineHost;
using Cathode.Testing;

namespace Cathode.Server.Tests;

/// <summary>
///     MIDI clock out: 24 ticks per quarter note counted on the engine's sample clock, polled as the MIDI thread polls
///     it (once a millisecond) while the audio callback moves the sample clock a buffer at a time.
/// </summary>
public sealed class MidiClockOutTests {
    const double SampleRate = 48_000;
    const ulong PollNs = 1_000_000;

    readonly List<(string? Port, byte Status, ulong TimeNs)> sent = [];
    ulong now = 5_000_000_000;
    long sampleTime = 123_456;
    double untilCallback;

    [Theory]
    [InlineData(120, 64)]
    [InlineData(120, 512)]
    [InlineData(93.5, 256)]
    [InlineData(240, 2_048)]
    public void Ticks_come_24_to_the_quarter_note_at_the_tempo_whatever_the_buffer(double bpm, int buffer) {
        var clock = Clock();
        clock.Configure(true, "out-1");

        Run(clock, 4_000, bpm, buffer);

        // Four seconds, less the first callback (the count starts at its end): the count is exact to a tick, and no
        // tick is further from its place than a poll or two.
        var perSecond = bpm / 60 * MidiClockOut.TicksPerBeat;
        var expected = (4 - buffer / SampleRate) * perSecond;
        Assert.InRange(sent.Count, expected - 2, expected + 1);
        Assert.All(
            sent,
            static m => {
                Assert.Equal("out-1", m.Port);
                Assert.Equal(0xF8, m.Status);
            }
        );
        var tickMs = 60_000 / bpm / MidiClockOut.TicksPerBeat;
        var intervals = sent.Zip(sent.Skip(1), static (a, b) => (b.TimeNs - a.TimeNs) / 1e6).ToList();
        Assert.All(intervals, ms => Assert.InRange(ms, tickMs - 2.5, tickMs + 2.5));
        Assert.Equal(tickMs, intervals.Average(), 0.05);
    }

    [Fact]
    public void The_ticks_follow_a_change_of_tempo() {
        var clock = Clock();
        clock.Configure(true, null);
        Run(clock, 1_000, 60, 256);
        var slow = sent.Count;

        Run(clock, 1_000, 180, 256);

        Assert.InRange(slow, 23, 24);
        Assert.InRange(sent.Count - slow, 71, 73);
    }

    [Fact]
    public void The_ticks_stop_when_the_audio_stops_and_pick_up_when_it_runs_again() {
        var clock = Clock();
        clock.Configure(true, null);
        Run(clock, 500, 120, 512);
        var running = sent.Count;

        Run(clock, 2_000, 120, 0); // the sample clock stands still
        var stopped = sent.Count - running;
        Run(clock, 500, 120, 512);

        Assert.InRange(running, 22, 24);
        Assert.InRange(stopped, 0, 1); // at most the tick that was due within the last callback
        Assert.InRange(sent.Count - running - stopped, 22, 25);
    }

    [Fact]
    public void Nothing_is_sent_until_it_is_turned_on_and_turning_it_on_does_not_send_what_went_before() {
        var clock = Clock();
        Run(clock, 1_000, 120, 256);
        Assert.Empty(sent);

        clock.Configure(true, null);
        clock.Advance(sampleTime, now, 120);
        Assert.Empty(sent);

        clock.Configure(false, null);
        Run(clock, 1_000, 120, 256);
        Assert.Empty(sent);
    }

    [Fact]
    public void A_stalled_thread_does_not_send_the_missed_ticks_in_a_burst() {
        var clock = Clock();
        clock.Configure(true, null);
        clock.Advance(sampleTime, now, 120);

        // Half a second without a poll, with the audio still running: 24 ticks were due.
        sampleTime += 24_000;
        now += 500 * PollNs;
        clock.Advance(sampleTime, now, 120);
        now += PollNs;
        clock.Advance(sampleTime, now, 120);

        Assert.InRange(sent.Count, 0, MidiClockOut.MaxBurst);
    }

    [Fact]
    public void A_new_engine_clock_starts_the_count_again() {
        var clock = Clock();
        clock.Configure(true, null);
        Run(clock, 250, 120, 256);
        var before = sent.Count;

        sampleTime = 0; // the host restarted its engine
        Run(clock, 1_000, 120, 256);

        Assert.InRange(sent.Count - before, 46, 49);
    }

    [Fact]
    public void Sending_the_clock_allocates_nothing() {
        var ticks = 0;
        var clock = new MidiClockOut(
            SampleRate,
            (_, _) => {
                ticks++;
                return true;
            }
        );
        clock.Configure(true, "out-1");
        clock.Advance(sampleTime, now, 120);

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 2_000; i++) {
                    now += PollNs;
                    sampleTime += 48;
                    clock.Advance(sampleTime, now, 120);
                }
            }
        );

        Assert.Equal(0, allocated);
        Assert.True(ticks >= 94, $"{ticks} ticks in two seconds at 120 BPM");
    }

    MidiClockOut Clock() =>
        new(
            SampleRate,
            (port, bytes) => {
                sent.Add((port, bytes[0], now));
                Assert.Single(bytes);
                return true;
            }
        );

    /// <summary>
    ///     Polls once a millisecond for <paramref name="milliseconds" />, while an audio callback of
    ///     <paramref name="buffer" /> frames moves the sample clock whenever one is due (never, when 0).
    /// </summary>
    void Run(MidiClockOut clock, int milliseconds, double bpm, int buffer) {
        var callbackMs = buffer / SampleRate * 1_000;
        for (var i = 0; i < milliseconds; i++) {
            now += PollNs;
            if (buffer > 0) {
                untilCallback -= 1;
                while (untilCallback <= 0) {
                    sampleTime += buffer;
                    untilCallback += callbackMs;
                }
            }

            clock.Advance(sampleTime, now, bpm);
        }
    }
}
