namespace Cathode.Dsp;

public static class Decibels {
    /// <summary>Levels at or below this are treated as silence (gain 0).</summary>
    public const float SilenceDb = -80f;

    public static float ToGain(float db) => db <= SilenceDb ? 0f : MathF.Pow(10f, db / 20f);

    public static double ToGain(double db) => Math.Pow(10.0, db / 20.0);

    public static double FromGain(double gain) => 20.0 * Math.Log10(Math.Max(gain, 1e-12));
}
