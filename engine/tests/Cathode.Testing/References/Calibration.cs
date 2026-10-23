namespace Cathode.Testing.References;

/// <summary>
///     Input levels in volts. The circuit amp takes volts (1.0 = 1 V at the guitar jack); a capture takes digital full
///     scale, and its interface calibration says how many volts that was. Both are fed the same voltage.
/// </summary>
/// <remarks>
///     Calibration follows the NAM convention: <c>X dBu</c> is the RMS level of a sine peaking at 0 dBFS, so full scale
///     is <c>√2 · 0.7746 V · 10^(X/20)</c> peak (11.5 dBu: 4.12 V).
/// </remarks>
public static class Calibration {
    /// <summary>0 dBu in volts RMS (1 mW into 600 Ω).</summary>
    public const double DbuVolts = 0.774_596_669;

    /// <summary>
    ///     Calibration assumed for display when a capture's is unknown and not fitted; typical of audio interfaces' line
    ///     inputs.
    /// </summary>
    public const double TypicalInputDbu = 12;

    /// <summary>Peak volts at digital full scale for an input calibrated to <paramref name="dbu" />.</summary>
    public static double VoltsAtFullScale(double dbu) => Math.Sqrt(2) * DbuVolts * Math.Pow(10, dbu / 20);

    public static double DbuForVoltsAtFullScale(double volts) => 20 * Math.Log10(volts / (Math.Sqrt(2) * DbuVolts));

    /// <summary>A signal in volts as the capture's interface would have recorded it.</summary>
    public static float[] ToDigital(ReadOnlySpan<float> volts, double dbu) {
        var scale = (float)(1 / VoltsAtFullScale(dbu));
        var result = new float[volts.Length];
        for (var i = 0; i < volts.Length; i++) {
            result[i] = volts[i] * scale;
        }

        return result;
    }
}
