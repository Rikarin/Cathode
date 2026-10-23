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
    private readonly IReadOnlyList<ParamDescriptor> _descriptors;
    private readonly float[] _target;
    private readonly float[] _seenTarget;
    private readonly float[] _current;
    private readonly float[] _step;
    private readonly int[] _remaining;
    private readonly int[] _rampSamples;

    public ParamBank(IReadOnlyList<ParamDescriptor> descriptors) {
        _descriptors = descriptors;
        var count = descriptors.Count;
        _target = new float[count];
        _seenTarget = new float[count];
        _current = new float[count];
        _step = new float[count];
        _remaining = new int[count];
        _rampSamples = new int[count];
        for (var i = 0; i < count; i++) {
            _target[i] = descriptors[i].Default;
        }

        SnapToTargets();
    }

    public int Count => _descriptors.Count;

    public ParamDescriptor Descriptor(int index) => _descriptors[index];

    /// <summary>Control side, any thread. The value is sanitised against the descriptor.</summary>
    public void SetTarget(int index, float value) =>
        Volatile.Write(ref _target[index], _descriptors[index].Sanitize(value));

    public float GetTarget(int index) => Volatile.Read(ref _target[index]);

    /// <summary>Current smoothed value (audio thread).</summary>
    public float Current(int index) => _current[index];

    public bool IsRamping(int index) => _remaining[index] > 0;

    public void Prepare(double sampleRate) {
        for (var i = 0; i < _descriptors.Count; i++) {
            var d = _descriptors[i];
            _rampSamples[i] = d.Smoothing == ParamSmoothing.Step
                ? 0
                : Math.Max(1, (int)Math.Round(d.SmoothingMs * 0.001 * sampleRate));
        }
    }

    /// <summary>Jumps every smoother to its target (used on reset and preset load).</summary>
    public void SnapToTargets() {
        for (var i = 0; i < _target.Length; i++) {
            var t = Volatile.Read(ref _target[i]);
            _seenTarget[i] = t;
            _current[i] = t;
            _remaining[i] = 0;
        }
    }

    /// <summary>Audio thread: start ramps toward any targets changed since the last block.</summary>
    public void BeginBlock() {
        for (var i = 0; i < _target.Length; i++) {
            var t = Volatile.Read(ref _target[i]);
            if (t == _seenTarget[i]) {
                continue;
            }

            _seenTarget[i] = t;
            var ramp = _rampSamples[i];
            if (ramp == 0) {
                _current[i] = t;
                _remaining[i] = 0;
            } else {
                _remaining[i] = ramp;
                _step[i] = (t - _current[i]) / ramp;
            }
        }
    }

    /// <summary>Audio thread: advances the smoother by <paramref name="frames" /> and returns the new value.</summary>
    public float Advance(int index, int frames) {
        var remaining = _remaining[index];
        if (remaining == 0) {
            return _current[index];
        }

        if (frames >= remaining) {
            _remaining[index] = 0;
            _current[index] = _seenTarget[index];
        } else {
            _remaining[index] = remaining - frames;
            _current[index] += _step[index] * frames;
        }

        return _current[index];
    }
}
