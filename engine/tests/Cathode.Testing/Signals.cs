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
    ///     A sine with exactly <paramref name="cycles" /> periods in <paramref name="length" /> samples, so its FFT
    ///     needs no window and every harmonic lands exactly on a bin.
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

    /// <summary>
    ///     Exponential (Farina) sine sweep from <paramref name="startHz" /> to <paramref name="endHz" />.
    /// </summary>
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

    /// <summary>
    ///     The same sound at any rate: <paramref name="tonesPerOctave" /> sines per octave from
    ///     <paramref name="lowHz" /> up to <paramref name="highHz" />, with seeded phases, together at
    ///     <paramref name="rmsDb" /> dBFS RMS, sampled at <paramref name="sampleRate" />. For comparing a block's
    ///     response at different sample rates.
    /// </summary>
    public static float[] MultiTone(
        double sampleRate,
        double seconds,
        double lowHz = 50,
        double highHz = 12_000,
        int tonesPerOctave = 3,
        double rmsDb = -20,
        int seed = 1
    ) {
        var count = (int)Math.Floor(Math.Log2(highHz / lowHz) * tonesPerOctave) + 1;
        var random = new Random(seed);
        var phases = new double[count];
        for (var t = 0; t < count; t++) {
            phases[t] = random.NextDouble() * 2 * Math.PI;
        }

        // Equal sines, each with a mean square of a² / 2.
        var amplitude = Math.Pow(10, rmsDb / 20) * Math.Sqrt(2.0 / count);
        var signal = new float[(int)Math.Round(seconds * sampleRate)];
        for (var t = 0; t < count; t++) {
            var w = 2 * Math.PI * lowHz * Math.Pow(2, (double)t / tonesPerOctave) / sampleRate;
            for (var i = 0; i < signal.Length; i++) {
                signal[i] += (float)(amplitude * Math.Sin(w * i + phases[t]));
            }
        }

        return signal;
    }

    public static float[] Concat(params float[][] parts) => [..parts.SelectMany(static p => p)];
}
