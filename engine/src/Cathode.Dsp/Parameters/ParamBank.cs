namespace Cathode.Dsp.Parameters;

/// <summary>
///     Parameter targets plus per-parameter smoothing state for one block instance.
/// </summary>
/// <remarks>
///     Targets are written with <see cref="SetTarget" /> from any thread: an aligned 32-bit write is atomic,
///     so continuous knob changes need no queue. The audio thread calls <see cref="BeginBlock" /> once per
///     callback to pick up new targets and then advances the smoothers.
/// </remarks>
public sealed class ParamBank {
    readonly IReadOnlyList<ParamDescriptor> descriptors;
    readonly float[] target;
    readonly float[] seenTarget;
    readonly float[] current;
    readonly float[] step;
    readonly int[] remaining;
    readonly int[] rampSamples;

    public int Count => this.descriptors.Count;

    public ParamBank(IReadOnlyList<ParamDescriptor> descriptors) {
        this.descriptors = descriptors;
        var count = descriptors.Count;
        this.target = new float[count];
        seenTarget = new float[count];
        current = new float[count];
        step = new float[count];
        this.remaining = new int[count];
        rampSamples = new int[count];
        for (var i = 0; i < count; i++) {
            this.target[i] = descriptors[i].Default;
        }

        SnapToTargets();
    }

    public ParamDescriptor Descriptor(int index) => this.descriptors[index];

    /// <summary>Control side, any thread. The value is sanitised against the descriptor.</summary>
    public void SetTarget(int index, float value) =>
        Volatile.Write(ref this.target[index], this.descriptors[index].Sanitize(value));

    public float GetTarget(int index) => Volatile.Read(ref this.target[index]);

    /// <summary>Current smoothed value (audio thread).</summary>
    public float Current(int index) => current[index];

    public bool IsRamping(int index) => this.remaining[index] > 0;

    public void Prepare(double sampleRate) {
        for (var i = 0; i < this.descriptors.Count; i++) {
            var d = this.descriptors[i];
            rampSamples[i] = d.Smoothing == ParamSmoothing.Step
                ? 0
                : Math.Max(1, (int)Math.Round(d.SmoothingMs * 0.001 * sampleRate));
        }
    }

    /// <summary>Jumps every smoother to its target (used on reset and preset load).</summary>
    public void SnapToTargets() {
        for (var i = 0; i < this.target.Length; i++) {
            var t = Volatile.Read(ref this.target[i]);
            seenTarget[i] = t;
            current[i] = t;
            this.remaining[i] = 0;
        }
    }

    /// <summary>Audio thread: start ramps toward any targets changed since the last block.</summary>
    public void BeginBlock() {
        for (var i = 0; i < this.target.Length; i++) {
            var t = Volatile.Read(ref this.target[i]);
            if (t == seenTarget[i]) {
                continue;
            }

            seenTarget[i] = t;
            var ramp = rampSamples[i];
            if (ramp == 0) {
                current[i] = t;
                this.remaining[i] = 0;
            } else {
                this.remaining[i] = ramp;
                step[i] = (t - current[i]) / ramp;
            }
        }
    }

    /// <summary>Audio thread: advances the smoother by <paramref name="frames" /> and returns the new value.</summary>
    public float Advance(int index, int frames) {
        var remaining = this.remaining[index];
        if (remaining == 0) {
            return current[index];
        }

        if (frames >= remaining) {
            this.remaining[index] = 0;
            current[index] = seenTarget[index];
        } else {
            this.remaining[index] = remaining - frames;
            current[index] += step[index] * frames;
        }

        return current[index];
    }
}
