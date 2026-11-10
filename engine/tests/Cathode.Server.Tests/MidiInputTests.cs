using Cathode.Audio.Native;
using Cathode.Dsp;
using Cathode.Engine.Rig;
using Cathode.EngineHost;

namespace Cathode.Server.Tests;

/// <summary>
///     What <see cref="MidiInput" /> does with the messages it reads: the tempo it derives from MIDI clock, the notes
///     it hands the engine and what it forwards to the Server. The messages are made up here, so no device is
///     involved; only their timestamps need the native clock.
/// </summary>
public sealed class MidiInputTests : IDisposable {
    const int TicksPerBeat = 24;
    static readonly int TempoIndex = RigControls.Descriptor.IndexOf("tempo");

    readonly EngineHostService service = new(ProcessSpec.Default, "test");

    public void Dispose() => service.Dispose();

    float Tempo => service.Engine.Rig.GetTarget(TempoIndex);

    [Fact]
    public void Clock_sets_the_tempo_once_a_whole_beat_of_ticks_has_arrived() {
        Assert.SkipUnless(NativeAudio.IsLibraryPresent, "cth_audio not built");
        using var midi = new MidiInput(service.Engine, static _ => { });
        midi.SetFollowClock(true);
        var time = NativeMidi.NowNanoseconds();

        // 24 ticks are 23 intervals: not a beat yet.
        Assert.Null(midi.Translate(Clock(ref time, 90, TicksPerBeat)));
        Assert.Equal(120f, Tempo);

        Assert.Null(midi.Translate(Clock(ref time, 90, 1)));
        Assert.Equal(90f, Tempo, 0.01f);

        // The window is the last beat: one more beat at a new tempo replaces the old one entirely.
        midi.Translate(Clock(ref time, 140, TicksPerBeat));
        Assert.Equal(140f, Tempo, 0.01f);
    }

    [Fact]
    public void Clock_is_ignored_unless_following_and_turning_it_on_starts_a_new_window() {
        Assert.SkipUnless(NativeAudio.IsLibraryPresent, "cth_audio not built");
        using var midi = new MidiInput(service.Engine, static _ => { });
        var time = NativeMidi.NowNanoseconds();

        midi.Translate(Clock(ref time, 90, 3 * TicksPerBeat));
        Assert.Equal(120f, Tempo); // not following

        midi.SetFollowClock(true);
        midi.Translate(Clock(ref time, 90, 20));
        midi.SetFollowClock(true); // set again (a resync after a host restart): the ticks so far are forgotten
        midi.Translate(Clock(ref time, 150, TicksPerBeat));
        Assert.Equal(120f, Tempo);
        midi.Translate(Clock(ref time, 150, 1));
        Assert.Equal(150f, Tempo, 0.01f);

        midi.SetFollowClock(false);
        midi.Translate(Clock(ref time, 60, 3 * TicksPerBeat));
        Assert.Equal(150f, Tempo, 0.01f);
    }

    [Fact]
    public void Clock_tempo_stays_within_the_rig_tempo_range() {
        Assert.SkipUnless(NativeAudio.IsLibraryPresent, "cth_audio not built");
        using var midi = new MidiInput(service.Engine, static _ => { });
        midi.SetFollowClock(true);
        var time = NativeMidi.NowNanoseconds();

        midi.Translate(Clock(ref time, 1_000, TicksPerBeat + 1));
        Assert.Equal(300f, Tempo);

        // Slower than the range, yet ticks closer than the gap that counts as the clock pausing (100 ms).
        midi.Translate(Clock(ref time, 27, TicksPerBeat + 1));
        Assert.Equal(30f, Tempo);
    }

    [Fact]
    public void Notes_reach_the_engine_and_every_message_but_clock_and_sensing_is_forwarded() {
        Assert.SkipUnless(NativeAudio.IsLibraryPresent, "cth_audio not built");
        using var midi = new MidiInput(service.Engine, static _ => { });
        RunOneCallback(); // the sample clock is past zero
        var now = NativeMidi.NowNanoseconds();

        var batch = midi.Translate(
            [
                new(now, 0x92, 60, 100, 3), // note on, channel 3
                new(now, 0x90, 64, 90, 3),
                new(now, 0xF8, 0, 0, 1), // clock: handled here
                new(now, 0xFE, 0, 0, 1), // active sensing: dropped
                new(now, 0xB1, 7, 99, 3), // a controller the Server maps
                new(now, 0xFA, 0, 0, 1) // start
            ]
        );
        RunOneCallback();

        Assert.NotNull(batch);
        Assert.Equal([0x92, 0x90, 0xB1, 0xFA], batch.Select(static m => m.Status));
        Assert.Equal((7, 99), (batch[2].Data1, batch[2].Data2));
        // Stamped on the engine's sample clock: the messages are a moment old, so just before its present.
        var engineNow = service.Engine.SampleTime;
        Assert.All(batch, m => Assert.InRange(m.SampleTime, engineNow - 4_800, engineNow));
        Assert.True(service.Engine.HeldNotes.Contains(60));
        Assert.True(service.Engine.HeldNotes.Contains(64));

        // A note on with velocity 0 is a note off, like the note-off status itself.
        midi.Translate([new(now, 0x92, 60, 0, 3)]);
        RunOneCallback();
        Assert.False(service.Engine.HeldNotes.Contains(60));
        Assert.True(service.Engine.HeldNotes.Contains(64));
        midi.Translate([new(now, 0x80, 64, 0, 3)]);
        RunOneCallback();
        Assert.True(service.Engine.HeldNotes.IsEmpty);

        // All Notes Off releases what is held; another controller does not.
        midi.Translate([new(now, 0x90, 48, 80, 3), new(now, 0x90, 52, 80, 3), new(now, 0xB0, 7, 0, 3)]);
        RunOneCallback();
        Assert.True(service.Engine.HeldNotes.Contains(48) && service.Engine.HeldNotes.Contains(52));
        midi.Translate([new(now, 0xB0, 123, 0, 3)]);
        RunOneCallback();
        Assert.True(service.Engine.HeldNotes.IsEmpty);
    }

    /// <summary>
    ///     <paramref name="count" /> clock ticks at <paramref name="bpm" />, continuing from
    ///     <paramref name="time" />, which is left on the last tick.
    /// </summary>
    static List<MidiMessage> Clock(ref ulong time, double bpm, int count) {
        var interval = (ulong)Math.Round(60e9 / bpm / TicksPerBeat);
        var ticks = new List<MidiMessage>(count);
        for (var i = 0; i < count; i++) {
            time += interval;
            ticks.Add(new(time, 0xF8, 0, 0, 1));
        }

        return ticks;
    }

    /// <summary>Runs one audio callback, which applies the commands the MIDI thread posted.</summary>
    void RunOneCallback() {
        var frames = service.Engine.Spec.MaxBlockSize;
        var input = new float[frames];
        service.Engine.Process(new(input, input, new float[frames], new float[frames]));
    }
}
