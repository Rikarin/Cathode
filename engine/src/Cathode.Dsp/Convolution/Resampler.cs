namespace Cathode.Dsp.Convolution;

/// <summary>
///     Offline windowed-sinc resampling for assets such as impulse responses. Control thread only; it allocates.
/// </summary>
public static class Resampler {
    const int HalfWidth = 32; // zero crossings each side at the narrowest cutoff

    /// <summary>
    ///     Resamples <paramref name="input" /> from <paramref name="fromRate" /> to <paramref name="toRate" />.
    /// </summary>
    public static float[] Resample(ReadOnlySpan<float> input, double fromRate, double toRate) {
        if (fromRate <= 0 || toRate <= 0) {
            throw new ArgumentOutOfRangeException(nameof(fromRate), "Sample rates must be positive.");
        }

        if (fromRate == toRate) {
            return [..input];
        }

        var ratio = toRate / fromRate;
        var cutoff = Math.Min(1.0, ratio) * 0.97; // just below the lower Nyquist
        var halfWidth = HalfWidth / cutoff;
        var output = new float[(int)Math.Ceiling(input.Length * toRate / fromRate - 1e-9)];
        for (var j = 0; j < output.Length; j++) {
            var t = j / ratio;
            var first = (int)Math.Ceiling(t - halfWidth);
            var last = (int)Math.Floor(t + halfWidth);
            double sum = 0;
            for (var i = Math.Max(first, 0); i <= Math.Min(last, input.Length - 1); i++) {
                var d = t - i;
                sum += input[i] * cutoff * Sinc(cutoff * d) * Blackman(d / halfWidth);
            }

            output[j] = (float)sum;
        }

        return output;
    }

    static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    /// <summary>Blackman window over [-1, 1].</summary>
    static double Blackman(double x) {
        if (Math.Abs(x) >= 1) {
            return 0;
        }

        var phase = Math.PI * (x + 1); // 0..2π
        return 0.42 - 0.5 * Math.Cos(phase) + 0.08 * Math.Cos(2 * phase);
    }
}
