using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Pitch;

/// <summary>
///     Classic two-tap granular shifter: two taps sweep a delay window of fixed length half a cycle apart, each faded
///     by a sin² window so the pair always sums to unity gain. The playback speed may be negative, which plays every
///     grain backwards: the reversed, shimmering grains of the Crystals effect.
/// </summary>
/// <remarks>
///     A tap's delay moves at <c>1 - speed</c> samples per sample, so for a reversed octave up (speed -2) it sweeps
///     the window three times faster than real time and each grain lasts a third of the window. The wet path delay
///     ranges over the whole window. Not pitch-synchronous: on a sustained note the taps' phase difference gives a mild
///     chorus-like warble, which is part of the sound here.
/// </remarks>
public sealed class GrainShifter : IDisposable {
    readonly FractionalDelayLine line = new();
    double grain;
    double phase;

    /// <summary>Control thread: allocates for grains of <paramref name="grainSeconds" />.</summary>
    public void Prepare(double sampleRate, double grainSeconds) {
        grain = grainSeconds * sampleRate;
        line.Allocate((int)grain + 16);
        Reset();
    }

    public void Reset() {
        line.Reset();
        phase = 0;
    }

    /// <summary>Processes one sample at playback rate <paramref name="speed" /> (negative plays grains in reverse).</summary>
    public float Process(float input, double speed) {
        line.Write(input);
        phase += (1 - speed) / grain;
        phase -= Math.Floor(phase);
        var other = phase < 0.5 ? phase + 0.5 : phase - 0.5;
        var a = MathF.Sin(MathF.PI * (float)phase);
        var b = MathF.Sin(MathF.PI * (float)other);
        return a * a * line.Read((float)(FractionalDelayLine.MinDelay + phase * grain))
            + b * b * line.Read((float)(FractionalDelayLine.MinDelay + other * grain));
    }

    public void Dispose() => line.Dispose();
}
