using Cathode.Engine.Rig;

namespace Cathode.Engine.Graph;

/// <summary>
///     Whether an engage modifier (<see cref="ModNames.EngageParam" />) has its block engaged, updated from the
///     modified value (0..1) once per sub-block. Below <see cref="Threshold" /> is the off region; the block is
///     bypassed once the value has stayed there for <see cref="OffDelayMs" />, and engaged at once: in
///     <see cref="EngageMode.Position" /> mode when the value is at or above the threshold, in
///     <see cref="EngageMode.Moving" /> mode when it moves more than <see cref="Deadband" /> from where it rested. The
///     first value only seeds it: Position takes the state that value gives, Moving starts bypassed. The defaults
///     (Position, 0.5, no delay) are the plain switch at 0.5 it always was.
/// </summary>
public struct EngageSwitch(EngageMode mode, float threshold, float offDelayMs) {
    public const float DefaultThreshold = 0.5f;
    public const float MaxOffDelayMs = 5000f;

    /// <summary>
    ///     How far below the threshold the value must fall to enter the off region; it leaves it at the threshold. Not
    ///     for Position mode without an off delay, which switches exactly at the threshold as it always did.
    /// </summary>
    public const float Hysteresis = 0.02f;

    /// <summary>How far the value must move from where it rested to count as moving (Moving mode).</summary>
    public const float Deadband = 0.03f;

    /// <summary>Where the value rests (Moving mode); NaN until the first value seeds it.</summary>
    float rest = float.NaN;

    /// <summary>Samples the value has spent in the off region so far, or −1 while it is out of it.</summary>
    long offSamples = -1;

    public EngageSwitch() : this(EngageMode.Position, DefaultThreshold, 0f) { }

    public EngageMode Mode { get; } = mode;
    public float Threshold { get; } = float.IsFinite(threshold) ? Math.Clamp(threshold, 0f, 1f) : DefaultThreshold;
    public float OffDelayMs { get; } = float.IsFinite(offDelayMs) ? Math.Clamp(offDelayMs, 0f, MaxOffDelayMs) : 0f;
    public bool Engaged { get; private set; }

    /// <summary>
    ///     The hysteresis below the threshold, at most half of it: a value of 0 is off for any threshold above 0.
    /// </summary>
    float Margin => Mode == EngageMode.Position && OffDelayMs <= 0f ? 0f : Math.Min(Hysteresis, Threshold * 0.5f);

    /// <summary>The next value seeds the switch again, as the first one did (a re-enabled modifier).</summary>
    public void Reset() {
        rest = float.NaN;
        offSamples = -1;
    }

    /// <summary>
    ///     Audio thread: takes the value for the next <paramref name="frames" /> and returns whether the block is
    ///     engaged for them. Time in the off region counts the frames of the earlier calls, so with an off delay of
    ///     <c>d</c> samples the block stays engaged for the first <c>d</c> samples of it, to sub-block resolution.
    /// </summary>
    public bool Update(float value, int frames, double sampleRate) {
        if (float.IsNaN(rest)) {
            rest = value;
            offSamples = -1;
            Engaged = Mode == EngageMode.Position && value >= Threshold;
            return Engaged;
        }

        var off = value < Threshold - Margin || (offSamples >= 0 && value < Threshold);
        if (Mode == EngageMode.Moving) {
            if (!Engaged && off && value < rest) {
                rest = value; // settling back towards the heel (or a release ramp) is not a touch
            }

            if (Math.Abs(value - rest) > Deadband) {
                rest = value;
                Engaged = true;
                offSamples = -1; // each move starts the off delay again
            }
        } else if (!off) {
            Engaged = true;
        }

        if (!off) {
            offSamples = -1;
            return Engaged;
        }

        offSamples = Math.Max(offSamples, 0);
        if (Engaged && offSamples >= OffDelayMs * 0.001 * sampleRate) {
            Engaged = false;
            rest = value;
        }

        offSamples += frames;
        return Engaged;
    }
}
