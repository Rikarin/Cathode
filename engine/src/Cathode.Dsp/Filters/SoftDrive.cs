namespace Cathode.Dsp.Filters;

/// <summary>
///     The gentle saturation the modulation blocks offer as Drive (a BBD's or a tube stage's): a tanh blended in with
///     the drive amount. At 0 it is an exact bypass; at full drive small signals gain up to 24 dB and peaks round off
///     at full scale.
/// </summary>
public static class SoftDrive {
    /// <summary>Saturates <paramref name="x" /> by <paramref name="drive" /> (0..1).</summary>
    public static float Apply(float x, float drive) {
        if (drive <= 0) {
            return x;
        }

        var gain = 1 + 15 * drive;
        return (1 - drive) * x + drive * MathF.Tanh(gain * x) / MathF.Tanh(gain);
    }

    /// <summary>
    ///     The same rounding at full scale without the small-signal gain, for a feedback path: the slope is 1 at zero
    ///     and falls from there, so it never raises a loop's gain. <see cref="Apply" /> inside a loop multiplies the
    ///     loop gain by up to 16.
    /// </summary>
    public static float Limit(float x, float drive) => drive <= 0 ? x : (1 - drive) * x + drive * MathF.Tanh(x);
}
