namespace Cathode.Dsp.Filters;

/// <summary>
///     A Butterworth low or high cut of 6, 12, 18 or 24 dB/oct: a TPT one-pole and up to two state-variable sections
///     (12 dB/oct is the single Q 0.7071 section the cab's cuts have always used). Stable under modulation.
/// </summary>
public readonly record struct CutFilter(SvfCoefficients First, SvfCoefficients Second, double OnePole, int Sections, bool HasOnePole, bool HighPass) {
    /// <summary>Index of 12 dB/oct in <see cref="Slopes" />.</summary>
    public const int Slope12 = 1;

    /// <summary>Slope choices, in order, for a parameter's options.</summary>
    public static IReadOnlyList<string> Slopes { get; } = ["6 dB/oct", "12 dB/oct", "18 dB/oct", "24 dB/oct"];

    public static CutFilter Design(bool highPass, double sampleRate, double frequency, int slope) {
        var type = highPass ? BiquadType.HighPass : BiquadType.LowPass;
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        var g = Math.Tan(Math.PI * f0 / sampleRate);
        var onePole = g / (1 + g);
        return slope switch {
            0 => new(default, default, onePole, 0, true, highPass),
            2 => new(SvfCoefficients.Design(type, sampleRate, f0, 1.0), default, onePole, 1, true, highPass),
            3 => new(
                SvfCoefficients.Design(type, sampleRate, f0, 0.5412),
                SvfCoefficients.Design(type, sampleRate, f0, 1.3066),
                onePole,
                2,
                false,
                highPass
            ),
            _ => new(SvfCoefficients.Design(type, sampleRate, f0, 0.7071), default, onePole, 1, false, highPass)
        };
    }
}

/// <summary>State of one <see cref="CutFilter" /> channel.</summary>
public struct CutState {
    const double DenormalThreshold = 1e-30;

    SvfState first;
    SvfState second;
    double onePole;

    public void Reset() {
        first.Reset();
        second.Reset();
        onePole = 0;
    }

    public void Process(in CutFilter c, Span<float> samples) {
        if (c.Sections > 0) {
            first.Process(c.First, samples);
        }

        if (c.Sections > 1) {
            second.Process(c.Second, samples);
        }

        if (!c.HasOnePole) {
            return;
        }

        for (var i = 0; i < samples.Length; i++) {
            var x = samples[i];
            var v = (x - onePole) * c.OnePole;
            var low = v + onePole;
            onePole = low + v;
            samples[i] = (float)(c.HighPass ? x - low : low);
        }

        if (Math.Abs(onePole) < DenormalThreshold) {
            onePole = 0;
        }
    }
}
