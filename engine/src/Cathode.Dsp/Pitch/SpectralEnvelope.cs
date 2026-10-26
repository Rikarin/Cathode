namespace Cathode.Dsp.Pitch;

/// <summary>
///     The all-pole (linear prediction) spectral envelope the formant correction keeps: autocorrelation from a frame's
///     power spectrum, a Gaussian lag window and a white-noise floor, then Levinson-Durbin. Shared by
///     <see cref="FormantCorrector" /> and <see cref="PhaseVocoderShifter" />, so both paths see the same envelope.
/// </summary>
public static class SpectralEnvelope {
    /// <summary>White-noise correction: the envelope spans at most about 50 dB below the frame's power.</summary>
    public const double NoiseFloor = 1e-5;

    /// <summary>
    ///     The lag window's bandwidth: the envelope does not resolve detail finer than this, so it passes over single
    ///     harmonics.
    /// </summary>
    public const double LagBandwidthHz = 120;

    /// <summary>The largest reflection coefficient, keeping the all-pole filter clear of instability.</summary>
    public const double MaxReflection = 0.999;

    /// <summary>Frames quieter than this (autocorrelation at lag 0) have no envelope.</summary>
    public const double SilencePower = 1e-12;

    /// <summary>The envelope's order at <paramref name="sampleRate" />: one per 2 kHz of sample rate (24 at 48 kHz), 12 to 48.</summary>
    public static int Order(double sampleRate) => Math.Clamp((int)Math.Round(sampleRate / 2_000), 12, 48);

    /// <summary>Control thread: the Gaussian lag window for lags 0 to <paramref name="window" />.Length - 1.</summary>
    public static void LagWindow(Span<double> window, double sampleRate) {
        for (var l = 0; l < window.Length; l++) {
            var x = 2 * Math.PI * LagBandwidthHz * l / sampleRate;
            window[l] = Math.Exp(-0.5 * x * x);
        }
    }

    /// <summary>
    ///     Levinson-Durbin on the (windowed) autocorrelation <paramref name="r" /> of length order + 1: the prediction
    ///     polynomial into <paramref name="poly" /> (poly[0] = 1), the reflection coefficients into
    ///     <paramref name="reflection" /> when it is not empty, and the prediction error returned.
    ///     <paramref name="scratch" /> is order + 1 long.
    /// </summary>
    public static double Levinson(
        ReadOnlySpan<double> r,
        Span<double> poly,
        Span<double> scratch,
        Span<float> reflection
    ) {
        var order = r.Length - 1;
        var error = r[0];
        poly.Clear();
        poly[0] = 1;
        for (var m = 1; m <= order; m++) {
            var acc = r[m];
            for (var i = 1; i < m; i++) {
                acc += poly[i] * r[m - i];
            }

            var k = Math.Clamp(-acc / error, -MaxReflection, MaxReflection);
            poly[..m].CopyTo(scratch);
            for (var i = 1; i < m; i++) {
                poly[i] = scratch[i] + k * scratch[m - i];
            }

            poly[m] = k;
            if (!reflection.IsEmpty) {
                reflection[m - 1] = (float)k;
            }

            error *= 1 - k * k;
        }

        return error;
    }
}
