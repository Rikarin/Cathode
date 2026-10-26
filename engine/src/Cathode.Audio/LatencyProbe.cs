using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     Measures round-trip latency through a physical loopback (output 1 cabled to input 1): emits a click every
///     period and records how many samples later its peak arrives on the input. Allocation-free on the audio thread.
/// </summary>
public sealed class LatencyProbe : IAudioProcessor {
    readonly int period;
    readonly float amplitude;
    readonly float threshold;
    readonly int skipPeriods;
    readonly int[] lags;
    long position;
    int count;
    float peak;
    int peakIndex;

    /// <summary>Clicks detected so far (excluding the settling periods).</summary>
    public int Count => Volatile.Read(ref count);

    /// <summary>Periods that ended without a detectable click.</summary>
    public int Missed { get; private set; }

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
        period = (int)(sampleRate * periodSeconds);
        this.amplitude = amplitude;
        this.threshold = threshold;
        skipPeriods = 2; // let the device settle
        lags = new int[maxMeasurements];
    }

    /// <summary>Snapshot of the measured lags in samples.</summary>
    public int[] Measurements() => [..lags.AsSpan(0, Count)];

    public void Process(in AudioIO io) {
        for (var i = 0; i < io.Frames; i++) {
            var phase = (int)(position % period);
            if (phase == 0) {
                Complete();
            }

            var x = Math.Abs(io.InputLeft[i]);
            if (x > peak) {
                peak = x;
                peakIndex = phase;
            }

            var click = phase == 0 ? amplitude : 0f;
            io.OutputLeft[i] = click;
            io.OutputRight[i] = click;
            position++;
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
        var periodIndex = position / period;
        if (periodIndex > skipPeriods) {
            if (peak >= threshold && count < lags.Length) {
                lags[count] = peakIndex;
                Volatile.Write(ref count, count + 1);
            } else if (peak < threshold) {
                Missed++;
            }
        }

        peak = 0;
        peakIndex = 0;
    }
}
