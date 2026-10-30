namespace Cathode.Dsp.Blocks;

/// <summary>
///     The measuring shared by the editor's EQ response charts: an impulse is run through an equaliser set up as its
///     block sets it, and the response's spectrum is read at the asked frequencies by a direct DFT. The equalisers it
///     measures: the Graphic EQ block's (<see cref="GraphicEqResponse" />), the Drive's EQ 2
///     (<see cref="DriveEqResponse" />) and the Amp's Output EQ (<see cref="AmpEqResponse" />).
/// </summary>
public static class EqResponse {
    /// <summary>The rate a response is measured at, or the base of it where the equaliser runs oversampled.</summary>
    public const double SampleRate = 48_000;

    /// <summary>
    ///     The impulse response's length at <see cref="SampleRate" />: 0.68 s, long enough for the narrowest 31 Hz
    ///     band (Master Q 4, ±12 dB) to ring down by about 100 dB. A higher rate gets as many seconds.
    /// </summary>
    public const int Length = 32_768;

    /// <summary>The floor a silent frequency reads, dB.</summary>
    public const float FloorDb = -200f;

    /// <summary>
    ///     <paramref name="count" /> frequencies spaced evenly on a log scale from <paramref name="low" /> to
    ///     <paramref name="high" /> hertz, both ends included.
    /// </summary>
    public static float[] Frequencies(int count = 256, float low = 20f, float high = 20_000f) {
        var ratio = Math.Log(high / low);
        return [..Enumerable.Range(0, count).Select(i => (float)(low * Math.Exp(ratio * i / Math.Max(1, count - 1))))];
    }

    /// <summary>A unit impulse as long as a response at <paramref name="sampleRate" /> is measured for.</summary>
    public static float[] Impulse(double sampleRate = SampleRate) {
        var impulse = new float[(int)Math.Ceiling(Length * sampleRate / SampleRate)];
        impulse[0] = 1f;
        return impulse;
    }

    /// <summary>A response of 0 dB at every one of <paramref name="frequencies" />: an equaliser that is off.</summary>
    public static float[] Flat(ReadOnlySpan<float> frequencies) => new float[frequencies.Length];

    /// <summary>
    ///     The spectrum in dB of <paramref name="response" /> (an impulse response at <paramref name="sampleRate" />)
    ///     at each of <paramref name="frequencies" /> (hertz), times <paramref name="gain" />. The DFT stops where the
    ///     tail has died away; a silent frequency reads <see cref="FloorDb" />.
    /// </summary>
    public static float[] Spectrum(
        ReadOnlySpan<float> response,
        double sampleRate,
        ReadOnlySpan<float> frequencies,
        double gain = 1
    ) {
        var end = response.Length;
        while (end > 1 && MathF.Abs(response[end - 1]) < 1e-9f) {
            end--;
        }

        var db = new float[frequencies.Length];
        for (var f = 0; f < frequencies.Length; f++) {
            var w = 2 * Math.PI * frequencies[f] / sampleRate;
            var (stepRe, stepIm) = (Math.Cos(w), -Math.Sin(w));
            double re = 0, im = 0, zRe = 1, zIm = 0;
            for (var n = 0; n < end; n++) {
                re += response[n] * zRe;
                im += response[n] * zIm;
                (zRe, zIm) = (zRe * stepRe - zIm * stepIm, zRe * stepIm + zIm * stepRe);
            }

            var magnitude = Math.Sqrt(re * re + im * im) * gain;
            db[f] = magnitude > 0 ? Math.Max(FloorDb, (float)(20 * Math.Log10(magnitude))) : FloorDb;
        }

        return db;
    }
}
