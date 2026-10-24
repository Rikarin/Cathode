namespace Cathode.Dsp.Filters;

/// <summary>
///     A first-order (6 dB/oct) low cut and high cut on both channels, as the guide's Flanger and Filter have: TPT
///     one-poles, stable under modulation. A cut at or beyond its "off" end (<see cref="WetCuts.LowOff" />,
///     <see cref="WetCuts.HighOff" />) is skipped, so the signal passes untouched.
/// </summary>
public struct FirstOrderCuts {
    const float DenormalThreshold = 1e-20f;

    float low;
    float high;
    float lowLeft;
    float lowRight;
    float highLeft;
    float highRight;
    bool useLow;
    bool useHigh;

    /// <summary>TPT one-pole gain G = g / (1 + g) with g = tan(pi f / fs).</summary>
    public static float Gain(double sampleRate, double frequency) {
        var g = Math.Tan(Math.PI * Math.Clamp(frequency, 1.0, sampleRate * 0.49) / sampleRate);
        return (float)(g / (1 + g));
    }

    public void Reset() => (lowLeft, lowRight, highLeft, highRight) = (0, 0, 0, 0);

    /// <summary>Sets the cut-offs, once per block.</summary>
    public void Design(double sampleRate, float lowCut, float highCut) {
        useLow = lowCut > WetCuts.LowOff;
        useHigh = highCut < WetCuts.HighOff;
        low = useLow ? Gain(sampleRate, lowCut) : 0;
        high = useHigh ? Gain(sampleRate, highCut) : 0;
    }

    public void Tick(ref float left, ref float right) {
        if (useLow) {
            left -= Step(ref lowLeft, low, left);
            right -= Step(ref lowRight, low, right);
        }

        if (useHigh) {
            left = Step(ref highLeft, high, left);
            right = Step(ref highRight, high, right);
        }
    }

    /// <summary>Once per block, after the ticks.</summary>
    public void FlushDenormals() {
        Flush(ref lowLeft);
        Flush(ref lowRight);
        Flush(ref highLeft);
        Flush(ref highRight);
    }

    /// <summary>One TPT one-pole step; returns the low-pass output.</summary>
    static float Step(ref float state, float gain, float x) {
        var v = (x - state) * gain;
        var lowPass = v + state;
        state = lowPass + v;
        return lowPass;
    }

    static void Flush(ref float state) {
        if (MathF.Abs(state) < DenormalThreshold) {
            state = 0;
        }
    }
}
