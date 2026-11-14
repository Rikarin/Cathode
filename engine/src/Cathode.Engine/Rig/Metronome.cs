namespace Cathode.Engine.Rig;

/// <summary>
///     A click on every beat of the rig tempo, accented on the first of four, locked to the sample clock. Added after
///     the looper, so it is never recorded into a loop.
/// </summary>
public sealed class Metronome(double sampleRate) {
    const double ClickMs = 25;
    const int BeatsPerBar = 4;

    readonly int clickSamples = (int)(ClickMs * 0.001 * sampleRate);
    int remaining;
    double frequency;
    double phase;
    long lastBeat = -1;
    long anchorTime;
    double anchorBeat;
    double anchorTempo;

    /// <summary>
    ///     Audio thread: add clicks for the frames starting at <paramref name="sampleTime" />. The grid is counted
    ///     from an anchor rather than from sample 0, so a tempo change moves only the beats after it: the clicks
    ///     already played stay where the player heard them, and the accent stays on the same beat of the bar.
    /// </summary>
    public void Process(Span<float> left, Span<float> right, long sampleTime, double tempo, float gain) {
        if (tempo != anchorTempo) {
            // The grid ran at the old tempo up to here (metronome on or off), so only the beats from now on change.
            anchorBeat += (sampleTime - anchorTime) * anchorTempo / (sampleRate * 60);
            anchorTime = sampleTime;
            anchorTempo = tempo;
        }

        var samplesPerBeat = sampleRate * 60 / tempo;
        for (var i = 0; i < left.Length; i++) {
            var beat = (long)(anchorBeat + (sampleTime + i - anchorTime) / samplesPerBeat);
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
