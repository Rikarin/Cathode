namespace Cathode.Engine.Rig;

/// <summary>
///     The rig's beat position, which the metronome, the synced LFOs and the sequencer all read, so they stay on one
///     grid. Counted from sample 0 at the first tempo, then from an anchor at each tempo change: a change moves only
///     the beats after it, and the accent stays on the same beat of the bar (#528, #602).
/// </summary>
public sealed class BeatClock(double sampleRate) {
    long anchorTime;
    double anchorBeat;
    float tempo;

    /// <summary>The tempo the clock runs at, in BPM; 0 before the first <see cref="Update" />.</summary>
    public float Tempo => tempo;

    /// <summary>Audio thread: the tempo from <paramref name="sampleTime" /> on.</summary>
    public void Update(long sampleTime, float bpm) {
        if (bpm == tempo) {
            return;
        }

        // The first tempo counts from sample 0; a later one re-anchors where it takes over.
        if (tempo != 0) {
            anchorBeat = At(sampleTime);
            anchorTime = sampleTime;
        }

        tempo = bpm;
    }

    /// <summary>The beat at <paramref name="time" />, counted from 0 (a whole number on each beat).</summary>
    public double At(long time) => anchorBeat + (time - anchorTime) / sampleRate * ((double)tempo / 60);
}
