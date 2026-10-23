using Cathode.Audio.Native;
using Cathode.Engine;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     Reads MIDI input on its own thread (docs/02-architecture.md §5.10): every message gets the engine sample time
///     it arrived at (from the OS timestamp), and batches go to the Server, which owns the mappings. MIDI clock is
///     handled here, when following it is on: the tempo comes from the average tick interval, sample-accurately. Notes
///     are also posted to the engine, which keeps the held chord for the blocks (<see cref="CathodeEngine.HeldNotes" />).
///     Devices plugged in later are picked up within a couple of seconds.
/// </summary>
public sealed class MidiInput(CathodeEngine engine, Action<List<MidiEventMessage>> forward) : IDisposable {
    const int TicksPerBeat = 24;
    const byte Clock = 0xF8;
    const byte ActiveSensing = 0xFE;
    const int AllSoundOff = 120;
    const int AllNotesOff = 123;

    readonly List<MidiMessage> received = [];
    readonly Queue<ulong> ticks = new();
    Thread? thread;
    volatile bool running;
    volatile bool followClock;
    int knownPorts = -1;

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
                var count = NativeMidi.Ports().Count;
                if (count != knownPorts) {
                    knownPorts = count;
                    NativeMidi.Open();
                }
            }

            received.Clear();
            if (NativeMidi.Read(received) > 0) {
                var batch = Translate(received);
                if (batch.Count > 0) {
                    forward(batch);
                }
            }

            Thread.Sleep(1);
        }
    }

    List<MidiEventMessage> Translate(List<MidiMessage> messages) {
        var now = NativeMidi.NowNanoseconds();
        var engineNow = engine.SampleTime;
        var rate = engine.Spec.SampleRate;
        var batch = new List<MidiEventMessage>(messages.Count);
        foreach (var message in messages) {
            if (message.Status == Clock) {
                Tick(message.TimeNs);
                continue;
            }

            if (message.Status == ActiveSensing) {
                continue;
            }

            Notes(message);
            var age = now > message.TimeNs ? (now - message.TimeNs) * rate / 1e9 : 0;
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
            ticks.Enqueue(timeNs);
            if (ticks.Count > TicksPerBeat + 1) {
                ticks.Dequeue();
            }

            if (ticks.Count == TicksPerBeat + 1) {
                var beatNs = (double)(timeNs - ticks.Peek());
                engine.Tempo = Math.Clamp(60e9 / beatNs, 30, 300);
            }
        }
    }
}
