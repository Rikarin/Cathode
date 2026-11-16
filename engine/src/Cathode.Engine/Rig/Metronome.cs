namespace Cathode.Engine.Rig;

/// <summary>
///     A click on every beat of the rig's <see cref="BeatClock" />, accented on the first of four. It reads the beat
///     the synced LFOs and the sequencer read, so they click and move together (#602). Added after the looper, so it
///     is never recorded into a loop.
/// </summary>
public sealed class Metronome(double sampleRate) {
    const double ClickMs = 25;
    const int BeatsPerBar = 4;

    readonly int clickSamples = (int)(ClickMs * 0.001 * sampleRate);
    int remaining;
    double frequency;
    double phase;
    long lastBeat = -1;
    long nextTime;

    /// <summary>
    ///     Audio thread: add clicks for the frames starting at <paramref name="sampleTime" />, on the beats of
    ///     <paramref name="beats" />. Switched back on between beats, it waits for the next one rather than clicking
    ///     off the grid.
    /// </summary>
    public void Process(Span<float> left, Span<float> right, long sampleTime, BeatClock beats, float gain) {
        if (sampleTime != nextTime) {
            // Off since the last call: the beat already under way has been missed, and a click cut off stays cut.
            var now = beats.At(sampleTime);
            lastBeat = now == Math.Floor(now) ? (long)now - 1 : (long)now;
            remaining = 0;
        }

        nextTime = sampleTime + left.Length;
        for (var i = 0; i < left.Length; i++) {
            var beat = (long)beats.At(sampleTime + i);
            if (beat != lastBeat) {
                lastBeat = beat;
                remaining = clickSamples;
                frequency = beat % BeatsPerBar == 0 ? 1_500 : 1_000;
                phase = 0;
            }

            if (remaining > 0) {
                var envelope = (float)remaining / clickSamples;
                var click = gain * envelope * envelope * (float)Math.Sin(phase);
                phase += 2 * Math.PI * frequency / sampleRate;
                remaining--;
                left[i] += click;
                right[i] += click;
            }
        }
    }
}
