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
    double sampleRate;
    double maxGrain;
    double grain;
    double phase;
    float crossfade = 1f;

    /// <summary>
    ///     Control thread: allocates for grains of up to <paramref name="maxGrainSeconds" /> (by default
    ///     <paramref name="grainSeconds" />) and starts at <paramref name="grainSeconds" />.
    /// </summary>
    public void Prepare(double sampleRate, double grainSeconds, double maxGrainSeconds = 0) {
        this.sampleRate = sampleRate;
        maxGrain = Math.Max(grainSeconds, maxGrainSeconds) * sampleRate;
        grain = grainSeconds * sampleRate;
        line.Allocate((int)maxGrain + 16);
        Reset();
    }

    /// <summary>Audio thread: a new grain length, clamped to what <see cref="Prepare" /> allocated.</summary>
    public void SetGrain(double grainSeconds) => grain = Math.Clamp(grainSeconds * sampleRate, 16, maxGrain);

    /// <summary>
    ///     Audio thread: how much of each half grain the taps crossfade over, 0..1. At 1 (the default) the windows are
    ///     the full sin² pair; shorter fades hold each tap at unity for longer and hand over quickly, still summing to one.
    /// </summary>
    public void SetCrossfade(float fraction) => crossfade = Math.Clamp(fraction, 0.02f, 1f);

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
        float wa, wb;
        if (crossfade >= 1f) {
            var a = MathF.Sin(MathF.PI * (float)phase);
            var b = MathF.Sin(MathF.PI * (float)other);
            (wa, wb) = (a * a, b * b);
        } else {
            // Rise over the first crossfade/2 of the cycle and fall over the same span after its middle: the window at
            // p + 1/2 is then always one minus the window at p.
            wa = Window((float)phase, crossfade);
            wb = 1f - wa;
        }

        return wa * line.Read((float)(FractionalDelayLine.MinDelay + phase * grain))
            + wb * line.Read((float)(FractionalDelayLine.MinDelay + other * grain));
    }

    public void Dispose() => line.Dispose();

    static float Window(float p, float fade) {
        return p < 0.5f ? Rise(p) : 1f - Rise(p - 0.5f);

        float Rise(float t) {
            var s = MathF.Sin(0.5f * MathF.PI * Math.Min(1f, t / (0.5f * fade)));
            return s * s;
        }
    }
}
