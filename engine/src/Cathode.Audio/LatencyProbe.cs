using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     Measures round-trip latency through a physical loopback (output 1 cabled to input 1): emits a click every
///     period and records how many samples later its peak arrives on the input. Allocation-free on the audio thread.
/// </summary>
public sealed class LatencyProbe : IAudioProcessor {
    readonly int _period;
    readonly float _amplitude;
    readonly float _threshold;
    readonly int _skipPeriods;
    readonly int[] _lags;
    long _position;
    int _count;
    float _peak;
    int _peakIndex;

    /// <param name="sampleRate">Stream sample rate.</param>
    /// <param name="periodSeconds">Click spacing; must exceed the latency being measured.</param>
    /// <param name="amplitude">Click level (keep it modest: it is audible).</param>
    /// <param name="threshold">Minimum input peak that counts as a detected click.</param>
    /// <param name="maxMeasurements">Measurements kept; further clicks are ignored.</param>
    public LatencyProbe(
        double sampleRate,
        double periodSeconds = 0.25,
        float amplitude = 0.5f,
        float threshold = 0.01f,
        int maxMeasurements = 64
    ) {
        _period = (int)(sampleRate * periodSeconds);
        _amplitude = amplitude;
        _threshold = threshold;
        _skipPeriods = 2; // let the device settle
        _lags = new int[maxMeasurements];
    }

    /// <summary>Clicks detected so far (excluding the settling periods).</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Periods that ended without a detectable click.</summary>
    public int Missed { get; private set; }

    /// <summary>Snapshot of the measured lags in samples.</summary>
    public int[] Measurements() => _lags.AsSpan(0, Count).ToArray();

    public void Process(in AudioIO io) {
        for (var i = 0; i < io.Frames; i++) {
            var phase = (int)(_position % _period);
            if (phase == 0) {
                Complete();
            }

            var x = Math.Abs(io.InputLeft[i]);
            if (x > _peak) {
                _peak = x;
                _peakIndex = phase;
            }

            var click = phase == 0 ? _amplitude : 0f;
            io.OutputLeft[i] = click;
            io.OutputRight[i] = click;
            _position++;
        }
    }

    /// <summary>Median of the measurements, or null if none.</summary>
    public static int? Median(IReadOnlyList<int> lags) {
        if (lags.Count == 0) {
            return null;
        }

        var sorted = lags.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    void Complete() {
        var periodIndex = _position / _period;
        if (periodIndex > _skipPeriods) {
            if (_peak >= _threshold && _count < _lags.Length) {
                _lags[_count] = _peakIndex;
                Volatile.Write(ref _count, _count + 1);
            } else if (_peak < _threshold) {
                Missed++;
            }
        }

        _peak = 0;
        _peakIndex = 0;
    }
}
