using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Filters;

/// <summary>
///     The Low Cut and High Cut many blocks put on their wet signal: second-order Butterworth high- and low-pass SVFs
///     on both channels. A cut at or beyond its "off" end (<see cref="LowOff" />, <see cref="HighOff" />) is skipped,
///     so the signal passes untouched.
/// </summary>
public struct WetCuts {
    public const float LowOff = 20f;
    public const float HighOff = 20_000f;

    SvfCoefficients low;
    SvfCoefficients high;
    SvfState lowLeft;
    SvfState lowRight;
    SvfState highLeft;
    SvfState highRight;
    bool useLow;
    bool useHigh;

    /// <summary>A low-cut parameter from <see cref="LowOff" /> (off) to <paramref name="max" /> Hz.</summary>
    public static ParamDescriptor LowCutParam(string id, string name, float max = 2_000f) =>
        ParamDescriptor.Float(id, name, LowOff, max, LowOff, "Hz", ParamTaper.Log);

    /// <summary>A high-cut parameter from <paramref name="min" /> Hz to <see cref="HighOff" /> (off).</summary>
    public static ParamDescriptor HighCutParam(string id, string name, float min = 500f) =>
        ParamDescriptor.Float(id, name, min, HighOff, HighOff, "Hz", ParamTaper.Log);

    public void Reset() {
        lowLeft.Reset();
        lowRight.Reset();
        highLeft.Reset();
        highRight.Reset();
    }

    /// <summary>Sets the cut-offs, once per block.</summary>
    public void Design(double sampleRate, float lowCut, float highCut) {
        useLow = lowCut > LowOff;
        useHigh = highCut < HighOff;
        if (useLow) {
            low = SvfCoefficients.Design(BiquadType.HighPass, sampleRate, lowCut, 0.7071);
        }

        if (useHigh) {
            high = SvfCoefficients.Design(BiquadType.LowPass, sampleRate, highCut, 0.7071);
        }
    }

    public void Tick(ref float left, ref float right) {
        if (useLow) {
            left = (float)lowLeft.Tick(low, left);
            right = (float)lowRight.Tick(low, right);
        }

        if (useHigh) {
            left = (float)highLeft.Tick(high, left);
            right = (float)highRight.Tick(high, right);
        }
    }

    /// <summary>Once per block, after the ticks.</summary>
    public void FlushDenormals() {
        lowLeft.FlushDenormals();
        lowRight.FlushDenormals();
        highLeft.FlushDenormals();
        highRight.FlushDenormals();
    }
}
