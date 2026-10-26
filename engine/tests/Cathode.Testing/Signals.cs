namespace Cathode.Testing;

/// <summary>Deterministic test stimuli.</summary>
public static class Signals {
    public static float[] Impulse(int length, float amplitude = 1f, int position = 0) {
        var signal = new float[length];
        signal[position] = amplitude;
        return signal;
    }

    public static float[] Step(int length, float amplitude = 1f) => [..Enumerable.Repeat(amplitude, length)];

    public static float[] Sine(double frequency, double sampleRate, int length, float amplitude = 1f) {
        var signal = new float[length];
        for (var i = 0; i < length; i++) {
            signal[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
        }

        return signal;
    }

    /// <summary>
    ///     A sine with exactly <paramref name="cycles" /> periods in <paramref name="length" /> samples, so its FFT needs
    ///     no window and every harmonic lands exactly on a bin.
    /// </summary>
    public static float[] CoherentSine(int cycles, int length, float amplitude = 1f) =>
        Sine(cycles, length, length, amplitude);

    /// <summary>Seeded uniform white noise in [-amplitude, amplitude].</summary>
    public static float[] Noise(int length, int seed, float amplitude = 1f) {
        var random = new Random(seed);
        var signal = new float[length];
        for (var i = 0; i < length; i++) {
            signal[i] = (float)((random.NextDouble() * 2 - 1) * amplitude);
        }

        return signal;
    }

    /// <summary>Exponential (Farina) sine sweep from <paramref name="startHz" /> to <paramref name="endHz" />.</summary>
    public static float[] ExponentialSweep(
        double startHz,
        double endHz,
        double sampleRate,
        double seconds,
        float amplitude = 1f
    ) {
        var length = (int)(seconds * sampleRate);
        var rate = Math.Log(endHz / startHz);
        var signal = new float[length];
        for (var i = 0; i < length; i++) {
            var t = i / sampleRate;
            signal[i] = (float)(amplitude
                * Math.Sin(2 * Math.PI * startHz * seconds / rate * (Math.Exp(t / seconds * rate) - 1)));
        }

        return signal;
    }

    public static float[] Concat(params float[][] parts) => [..parts.SelectMany(static p => p)];
}
