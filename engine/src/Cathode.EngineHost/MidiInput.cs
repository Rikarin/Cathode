using Cathode.Audio.Native;
using Cathode.Engine;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     Reads MIDI input on its own thread (docs/02-architecture.md §5.10): every message gets the engine sample time
///     it arrived at (from the OS timestamp), and batches go to the Server, which owns the mappings. MIDI clock is
///     handled here, when following it is on: the tempo comes from the average tick interval, sample-accurately. Notes
///     are also posted to the engine, which keeps the held chord for the blocks
///     (<see cref="CathodeEngine.HeldNotes" />). Devices plugged in later, or unplugged and plugged back in, are
///     picked up within a couple of seconds.
///     The same thread sends MIDI clock when a <see cref="MidiClockOut" /> is given and turned on.
/// </summary>
public sealed class MidiInput(
    CathodeEngine engine,
    Action<List<MidiEventMessage>> forward,
    MidiClockOut? clockOut = null
) : IDisposable {
    const int TicksPerBeat = 24;
    const byte Clock = 0xF8;
    const byte ClockStart = 0xFA;
    const byte ClockContinue = 0xFB;
    const byte ClockStop = 0xFC;
    const byte ActiveSensing = 0xFE;
    const int AllSoundOff = 120;
    const int AllNotesOff = 123;

    /// <summary>
    ///     Longer than the tick interval of the slowest tempo followed (30 BPM: 83 ms), so a longer silence is the
    ///     clock pausing, not a tempo.
    /// </summary>
    const ulong ClockGapNs = 100_000_000;

    readonly List<MidiMessage> received = [];
    readonly Queue<ulong> ticks = new();
    Thread? thread;
    volatile bool running;
    volatile bool followClock;
    ulong lastTick;
    int knownPorts = -1;
    uint knownGeneration;

    public void SetFollowClock(bool enabled) {
        followClock = enabled;
        lock (ticks) {
            ticks.Clear();
        }
    }

    public void Start() {
        if (thread is not null || !NativeMidi.IsAvailable) {
            return;
        }

        running = true;
        thread = new(Run) { IsBackground = true, Name = "cathode-midi" };
        thread.Start();
    }

    public void Dispose() {
        running = false;
        thread?.Join();
        thread = null;
        NativeMidi.Close();
    }

    void Run() {
        var lastScan = DateTime.MinValue;
        while (running) {
            if (DateTime.UtcNow - lastScan > TimeSpan.FromSeconds(2)) {
                lastScan = DateTime.UtcNow;
                // Only two numbers: listing the ports allocates. The count alone misses a device that was unplugged
                // and back between two scans, or swapped for another; the shim's generation does not. Both are read
                // before the sources are connected, so a change during that is seen by the next scan.
                var generation = NativeMidi.Generation();
                var count = NativeMidi.PortCount();
                if (count != knownPorts || generation != knownGeneration) {
                    knownPorts = count;
                    knownGeneration = generation;
                    NativeMidi.Open();
                }
            }

            received.Clear();
            if (NativeMidi.Read(received) > 0 && Translate(received) is { } batch) {
                forward(batch);
            }

            clockOut?.Advance(engine.SampleTime, NativeMidi.NowNanoseconds(), engine.Tempo);
            Thread.Sleep(1);
        }
    }

    /// <summary>
    ///     MIDI thread: handles clock ticks and notes here and returns what the Server should get, or null when nothing
    ///     is left. The batch is allocated only for such messages, so a running MIDI clock (about 50 ticks a second,
    ///     nearly every poll) or active sensing allocates nothing (docs/adr/0001-process-model.md).
    /// </summary>
    public List<MidiEventMessage>? Translate(List<MidiMessage> messages) {
        var now = NativeMidi.NowNanoseconds();
        var engineNow = engine.SampleTime;
        var rate = engine.Spec.SampleRate;
        List<MidiEventMessage>? batch = null;
        foreach (var message in messages) {
            if (message.Status == Clock) {
                Tick(message.TimeNs);
                continue;
            }

            if (message.Status == ActiveSensing) {
                continue;
            }

            if (message.Status is ClockStart or ClockContinue or ClockStop) {
                ResetClock(); // forwarded too: the Server may map transport
            }

            Notes(message);
            var age = now > message.TimeNs ? (now - message.TimeNs) * rate / 1e9 : 0;
            batch ??= new(messages.Count);
            batch.Add(new(message.Status, message.Data1, message.Data2, engineNow - (long)age));
        }

        return batch;
    }

    /// <summary>
    ///     Note on and off (and All Notes Off / All Sound Off) go straight to the engine, any channel, so blocks that
    ///     follow a keyboard (the Pitch block's MIDI key source) see the held chord without a trip through the Server.
    /// </summary>
    void Notes(MidiMessage message) {
        switch (message.Status & 0xF0) {
            case 0x90:
                engine.Post(EngineCommand.Note(message.Data1, message.Data2 > 0));
                break;
            case 0x80:
                engine.Post(EngineCommand.Note(message.Data1, false));
                break;
            case 0xB0 when message.Data1 is AllSoundOff or AllNotesOff:
                engine.Post(EngineCommand.AllNotesOff());
                break;
        }
    }

    void Tick(ulong timeNs) {
        if (!followClock) {
            return;
        }

        lock (ticks) {
            // The tempo comes from an unbroken run of ticks only: one measured against a tick from before a pause
            // would read as a very slow beat.
            if (ticks.Count > 0 && timeNs > lastTick && timeNs - lastTick > ClockGapNs) {
                ticks.Clear();
            }

            lastTick = timeNs;
            ticks.Enqueue(timeNs);
            if (ticks.Count > TicksPerBeat + 1) {
                ticks.Dequeue();
            }

            if (ticks.Count == TicksPerBeat + 1) {
                double beatNs = timeNs - ticks.Peek();
                engine.Tempo = Math.Clamp(60e9 / beatNs, 30, 300);
            }
        }
    }

    /// <summary>Start, Continue and Stop begin a new run of ticks: the tempo holds until a beat of them is in.</summary>
    void ResetClock() {
        lock (ticks) {
            ticks.Clear();
        }
    }
}
