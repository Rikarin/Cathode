namespace Cathode.Dsp.Filters;

/// <summary>
///     A Butterworth low or high cut of 6 to 36 dB/oct in 6 dB steps: a TPT one-pole and up to three state-variable
///     sections (12 dB/oct is the single Q 0.7071 section the cab's cuts have always used). Stable under modulation.
/// </summary>
public readonly record struct CutFilter(
    SvfCoefficients First,
    SvfCoefficients Second,
    SvfCoefficients Third,
    double OnePole,
    int Sections,
    bool HasOnePole,
    bool HighPass
) {
    /// <summary>Index of 12 dB/oct in <see cref="Slopes" />.</summary>
    public const int Slope12 = 1;

    /// <summary>Slope choices, in order, for a parameter's options.</summary>
    public static IReadOnlyList<string> Slopes { get; } = ["6 dB/oct", "12 dB/oct", "18 dB/oct", "24 dB/oct"];

    /// <summary><see cref="Slopes" /> continued to 30 and 36 dB/oct, for the blocks that offer them.</summary>
    public static IReadOnlyList<string> SteepSlopes { get; } = [..Slopes, "30 dB/oct", "36 dB/oct"];

    public static CutFilter Design(bool highPass, double sampleRate, double frequency, int slope) {
        var type = highPass ? BiquadType.HighPass : BiquadType.LowPass;
        var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
        var g = Math.Tan(Math.PI * f0 / sampleRate);
        var onePole = g / (1 + g);
        return slope switch {
            0 => new(default, default, default, onePole, 0, true, highPass),
            2 => new(Section(1.0), default, default, onePole, 1, true, highPass),
            3 => new(Section(0.5412), Section(1.3066), default, onePole, 2, false, highPass),
            4 => new(Section(0.618), Section(1.618), default, onePole, 2, true, highPass),
            5 => new(Section(0.5176), Section(0.7071), Section(1.9319), onePole, 3, false, highPass),
            _ => new(Section(0.7071), default, default, onePole, 1, false, highPass)
        };
        SvfCoefficients Section(double q) => SvfCoefficients.Design(type, sampleRate, f0, q);
    }
}

/// <summary>State of one <see cref="CutFilter" /> channel.</summary>
public struct CutState {
    const double DenormalThreshold = 1e-30;

    SvfState first;
    SvfState second;
    SvfState third;
    double onePole;

    public void Reset() {
        first.Reset();
        second.Reset();
        third.Reset();
        onePole = 0;
    }

    public void Process(in CutFilter c, Span<float> samples) {
        if (c.Sections > 0) {
            first.Process(c.First, samples);
        }

        if (c.Sections > 1) {
            second.Process(c.Second, samples);
        }

        if (c.Sections > 2) {
            third.Process(c.Third, samples);
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
