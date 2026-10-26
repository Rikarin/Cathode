using System.Collections.Concurrent;

namespace Cathode.Dsp.Resampling;

/// <summary>
///     How the oversamplers filter (a global setting, docs/08-blocks-parity.md §2 I/O).
///     <list type="bullet">
///         <item>
///             <b>Minimum</b>: the polyphase IIR half-bands (<see cref="HalfBand" />). Causal and with no reported
///             latency: the phase turns near the band edge instead. The default, and what Cathode always used.
///         </item>
///         <item>
///             <b>Linear</b>: windowed-sinc FIR half-bands with the same pass band and stop band, symmetric, so every
///             frequency is delayed alike. The delay is a whole number of samples that the block reports to the graph
///             compiler, which delays parallel paths to match.
///         </item>
///     </list>
/// </summary>
public enum OversamplingPhase {
    Minimum,
    Linear
}

/// <summary>A block whose oversamplers follow the global <see cref="OversamplingPhase" />; set before Prepare.</summary>
public interface IOversampledBlock {
    OversamplingPhase OversamplingPhase { get; set; }
}

/// <summary>
///     A linear-phase FIR half-band low-pass: a Kaiser-windowed sinc at a quarter of the high sample rate, of length
///     4k + 1. Every other tap off the centre is zero, so the even output phase is a plain delay and the odd phase a
///     2k-tap filter. Designed for the same transition band and stop-band depth as the IIR half-band of the same
///     <c>transition</c> (<see cref="HalfBand.Design" />): pass band up to 0.25 − transition / 2 of the high rate, stop
///     band from 0.25 + transition / 2.
/// </summary>
public sealed class LinearPhaseKernel {
    static readonly ConcurrentDictionary<(double, double), LinearPhaseKernel> Cache = new();

    LinearPhaseKernel(double[] taps) {
        Taps = taps;
        K = (taps.Length - 1) / 4;
        Odd = new float[2 * K];
        for (var i = 0; i < Odd.Length; i++) {
            Odd[i] = (float)taps[2 * i + 1];
        }
    }

    /// <summary>The whole symmetric impulse response, 4k + 1 taps at the high rate (centre tap 0.5).</summary>
    public double[] Taps { get; }

    /// <summary>The design's k: the filter delays by 2k samples at the high rate, k at the low rate.</summary>
    public int K { get; }

    /// <summary>The odd-indexed taps, oldest input first.</summary>
    internal float[] Odd { get; }

    /// <summary>The kernel for <paramref name="attenuationDb" /> and <paramref name="transition" />, designed once.</summary>
    public static LinearPhaseKernel For(double attenuationDb, double transition) =>
        Cache.GetOrAdd((attenuationDb, transition), static key => new(Design(key.Item1, key.Item2)));

    /// <summary>The magnitude of the half-band low-pass at <paramref name="frequency" /> (fraction of the high rate).</summary>
    public double Magnitude(double frequency) {
        var centre = 2 * K;
        var sum = Taps[centre];
        for (var n = 1; n <= centre; n++) {
            sum += 2 * Taps[centre + n] * Math.Cos(2 * Math.PI * frequency * n);
        }

        return Math.Abs(sum);
    }

    static double[] Design(double attenuationDb, double transition) {
        // Kaiser's estimates (Oppenheim and Schafer 7.6), with a little margin on the depth.
        var depth = attenuationDb + 5;
        var beta = 0.1102 * (depth - 8.7);
        var order = (depth - 7.95) / (2.285 * 2 * Math.PI * transition);
        var k = Math.Max(1, (int)Math.Ceiling(order / 4));
        var length = 4 * k + 1;
        var centre = 2 * k;
        var taps = new double[length];
        var norm = BesselI0(beta);
        for (var n = 0; n < length; n++) {
            var offset = n - centre;
            var ratio = (double)offset / centre;
            var window = BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - ratio * ratio))) / norm;
            var sinc = offset == 0 ? 0.5 : offset % 2 == 0 ? 0 : Math.Sin(Math.PI * offset / 2) / (Math.PI * offset);
            taps[n] = sinc * window;
        }

        taps[centre] = 0.5;
        return taps;
    }

    static double BesselI0(double x) {
        double sum = 1, term = 1;
        var half = x / 2;
        for (var k = 1; k < 64 && term > 1e-16 * sum; k++) {
            term *= half / k * (half / k);
            sum += term;
        }

        return sum;
    }
}

/// <summary>
///     One 2x up/down pair on a <see cref="LinearPhaseKernel" />, as polyphase filters at the low rate. Upsampling delays
///     by k low-rate samples, downsampling by k more, so a round trip is a pure delay of 2k (in the pass band).
/// </summary>
sealed class LinearPhaseStage(LinearPhaseKernel kernel) {
    readonly float[] up = new float[4 * kernel.K]; // the last 2k inputs, written twice so a window is contiguous
    readonly float[] downOdd = new float[4 * kernel.K];
    readonly float[] downEven = new float[2 * (kernel.K + 1)];
    int upAt;
    int oddAt;
    int evenAt;
    float previousOdd;

    public LinearPhaseKernel Kernel { get; } = kernel;

    public void Up(ReadOnlySpan<float> input, Span<float> output) {
        var taps = Kernel.Odd;
        var length = taps.Length;
        var k = Kernel.K;
        for (var i = 0; i < input.Length; i++) {
            upAt = upAt == 0 ? length - 1 : upAt - 1;
            up[upAt] = input[i];
            up[upAt + length] = input[i];
            // window[j] is the input j samples ago: x[m - j]
            var window = up.AsSpan(upAt, length);
            var sum = 0f;
            for (var j = 0; j < length; j++) {
                sum += taps[j] * window[j];
            }

            output[2 * i] = window[k];
            output[2 * i + 1] = 2f * sum;
        }
    }

    public void Down(ReadOnlySpan<float> input, Span<float> output) {
        var taps = Kernel.Odd;
        var length = taps.Length;
        var k = Kernel.K;
        var evenLength = k + 1;
        for (var i = 0; i < output.Length; i++) {
            // y[m] = 0.5 u[2m - 2k] + sum h[2j + 1] u[2m - 1 - 2j]: the odd samples start with the previous pair's
            oddAt = oddAt == 0 ? length - 1 : oddAt - 1;
            downOdd[oddAt] = previousOdd;
            downOdd[oddAt + length] = previousOdd;
            evenAt = evenAt == 0 ? evenLength - 1 : evenAt - 1;
            downEven[evenAt] = input[2 * i];
            downEven[evenAt + evenLength] = input[2 * i];
            var odd = downOdd.AsSpan(oddAt, length);
            var sum = 0f;
            for (var j = 0; j < length; j++) {
                sum += taps[j] * odd[j];
            }

            output[i] = sum + 0.5f * downEven[evenAt + k];
            previousOdd = input[2 * i + 1];
        }
    }

    public void Reset() {
        Array.Clear(up);
        Array.Clear(downOdd);
        Array.Clear(downEven);
        previousOdd = 0;
        upAt = 0;
        oddAt = 0;
        evenAt = 0;
    }

    public void FlushDenormals() {
        if (MathF.Abs(previousOdd) < 1e-20f) {
            previousOdd = 0;
        }
    }
}
