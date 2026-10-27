namespace Cathode.Dsp.Convolution;

/// <summary>One impulse response going into <see cref="IrMix.Mix" />, at its own rate.</summary>
public sealed record IrMixInput(
    float[] Samples,
    double SampleRate,
    double LevelDb = 0,
    double DelayMs = 0,
    bool Invert = false
);

/// <summary>
///     Blends impulse responses into one (Cab-Lab style): each is resampled to the target rate, optionally aligned on
///     its peak, delayed, scaled and summed, then faded at the end and optionally normalised to unit energy.
/// </summary>
public static class IrMix {
    const int FadeLength = 64;

    public static float[] Mix(
        IReadOnlyList<IrMixInput> inputs,
        double sampleRate,
        int length,
        bool align,
        bool normalize
    ) {
        if (inputs.Count == 0 || length <= 0) {
            throw new ArgumentException("Mix at least one IR into a non-empty result.");
        }

        var resampled = inputs.Select(i => Resampler.Resample(i.Samples, i.SampleRate, sampleRate)).ToArray();

        // Alignment moves every peak to the latest peak, so nothing has to start before sample 0.
        var peaks = resampled.Select(PeakIndex).ToArray();
        var target = align ? peaks.Max() : 0;
        var output = new float[length];
        for (var n = 0; n < inputs.Count; n++) {
            var input = inputs[n];
            var shift = (align ? target - peaks[n] : 0) + (int)Math.Round(input.DelayMs * 0.001 * sampleRate);
            var gain = (float)(Math.Pow(10, input.LevelDb / 20) * (input.Invert ? -1 : 1));
            var samples = resampled[n];
            for (var i = 0; i < samples.Length && i + shift < length; i++) {
                if (i + shift >= 0) {
                    output[i + shift] += samples[i] * gain;
                }
            }
        }

        for (var i = 0; i < FadeLength && i < length; i++) {
            output[length - FadeLength + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / FadeLength));
        }

        if (normalize) {
            var energy = output.Sum(static v => v * (double)v);
            if (energy > 0) {
                var scale = (float)(1 / Math.Sqrt(energy));
                for (var i = 0; i < length; i++) {
                    output[i] *= scale;
                }
            }
        }

        return output;
    }

    static int PeakIndex(float[] samples) {
        var index = 0;
        for (var i = 1; i < samples.Length; i++) {
            if (MathF.Abs(samples[i]) > MathF.Abs(samples[index])) {
                index = i;
            }
        }

        return index;
    }
}
