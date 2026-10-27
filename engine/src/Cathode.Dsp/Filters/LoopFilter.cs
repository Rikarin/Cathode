using System.Runtime.CompilerServices;

namespace Cathode.Dsp.Filters;

/// <summary>
///     The filters a delay puts in its feedback path, designed once per block and shared by every line: a low and a
///     high cut of 6, 12, 18 or 24 dB/oct (the slopes of <see cref="CutFilter.Slopes" />) with a resonance on the last
///     section, and two peaking bands. A cut at its "off" end (<see cref="WetCuts.LowOff" />,
///     <see cref="WetCuts.HighOff" />) and a band at 0 dB are skipped, so the default path is exactly the 12 dB/oct
///     Butterworth cuts the delays always had.
/// </summary>
public readonly record struct LoopFilter(
    LoopFilter.Cut Low,
    LoopFilter.Cut High,
    bool UsePeak1,
    SvfCoefficients Peak1,
    bool UsePeak2,
    SvfCoefficients Peak2
) {
    public const float ButterworthQ = 0.7071f;

    public bool Active => Low.Use || High.Use || UsePeak1 || UsePeak2;

    /// <summary>
    ///     An upper bound of the gain at any frequency: the product of each section's peak (a resonant cut section's
    ///     resonance peak, a boosting band's gain). A loop whose feedback times this exceeds one can grow without
    ///     limit.
    /// </summary>
    public double MaxGain { get; init; } = 1;

    /// <summary>
    ///     A loop filter with cuts at <paramref name="lowCut" /> and <paramref name="highCut" /> Hz (slope indices into
    ///     <see cref="CutFilter.Slopes" />, resonance 0.7071 = Butterworth) and peaking bands of the given gains in dB.
    /// </summary>
    public static LoopFilter Design(
        double sampleRate,
        float lowCut,
        int lowSlope,
        float lowQ,
        float highCut,
        int highSlope,
        float highQ,
        (float Frequency, float Q, float GainDb) peak1 = default,
        (float Frequency, float Q, float GainDb) peak2 = default
    ) {
        var useLow = lowCut > WetCuts.LowOff;
        var useHigh = highCut < WetCuts.HighOff;
        var gain = (useLow ? Cut.PeakGain(lowSlope, lowQ) : 1)
            * (useHigh ? Cut.PeakGain(highSlope, highQ) : 1)
            * Math.Max(1, Decibels.ToGain((double)peak1.GainDb))
            * Math.Max(1, Decibels.ToGain((double)peak2.GainDb));
        return new(
            useLow ? Cut.Design(true, sampleRate, lowCut, lowSlope, lowQ) : default,
            useHigh ? Cut.Design(false, sampleRate, highCut, highSlope, highQ) : default,
            peak1.GainDb != 0,
            peak1.GainDb != 0
                ? SvfCoefficients.Design(BiquadType.Peak, sampleRate, peak1.Frequency, peak1.Q, peak1.GainDb)
                : default,
            peak2.GainDb != 0,
            peak2.GainDb != 0
                ? SvfCoefficients.Design(BiquadType.Peak, sampleRate, peak2.Frequency, peak2.Q, peak2.GainDb)
                : default
        ) { MaxGain = gain };
    }

    /// <summary>One cut: an optional TPT one-pole and up to two state-variable sections.</summary>
    public readonly record struct Cut(
        bool Use,
        bool HighPass,
        bool HasOnePole,
        double OnePole,
        int Sections,
        SvfCoefficients First,
        SvfCoefficients Second
    ) {
        /// <summary>
        ///     The largest gain of a cut of <paramref name="slope" /> and resonance <paramref name="q" />: the peak of
        ///     its resonant section, <c>Q / √(1 - 1/(4Q²))</c> above a Q of 1/√2, else unity.
        /// </summary>
        public static double PeakGain(int slope, double q) {
            var resonance = q / ButterworthQ;
            var sectionQ = slope switch {
                0 => 0,
                2 => resonance,
                3 => 1.3066 * resonance,
                _ => q
            };
            return sectionQ > 1 / Math.Sqrt(2) ? sectionQ / Math.Sqrt(1 - 1 / (4 * sectionQ * sectionQ)) : 1;
        }

        public static Cut Design(bool highPass, double sampleRate, double frequency, int slope, double q) {
            var type = highPass ? BiquadType.HighPass : BiquadType.LowPass;
            var f0 = Math.Clamp(frequency, 1.0, sampleRate * 0.49);
            var g = Math.Tan(Math.PI * f0 / sampleRate);
            var onePole = g / (1 + g);
            var resonance = q / ButterworthQ; // scales the last section's Q; 1 keeps the Butterworth alignment
            return slope switch {
                0 => new(true, highPass, true, onePole, 0, default, default),
                2 => new(
                    true,
                    highPass,
                    true,
                    onePole,
                    1,
                    SvfCoefficients.Design(type, sampleRate, f0, resonance),
                    default
                ),
                3 => new(
                    true,
                    highPass,
                    false,
                    0,
                    2,
                    SvfCoefficients.Design(type, sampleRate, f0, 0.5412),
                    SvfCoefficients.Design(type, sampleRate, f0, 1.3066 * resonance)
                ),
                _ => new(true, highPass, false, 0, 1, SvfCoefficients.Design(type, sampleRate, f0, q), default)
            };
        }
    }
}

/// <summary>The state of one channel of a <see cref="LoopFilter" />.</summary>
public struct LoopFilterState {
    const double DenormalThreshold = 1e-30;

    CutState low;
    CutState high;
    SvfState peak1;
    SvfState peak2;

    public void Reset() {
        low.Reset();
        high.Reset();
        peak1.Reset();
        peak2.Reset();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Tick(in LoopFilter filter, float x) {
        double y = x;
        if (filter.Low.Use) {
            y = low.Tick(filter.Low, y);
        }

        if (filter.High.Use) {
            y = high.Tick(filter.High, y);
        }

        if (filter.UsePeak1) {
            y = peak1.Tick(filter.Peak1, y);
        }

        if (filter.UsePeak2) {
            y = peak2.Tick(filter.Peak2, y);
        }

        return (float)y;
    }

    public void FlushDenormals() {
        low.FlushDenormals();
        high.FlushDenormals();
        peak1.FlushDenormals();
        peak2.FlushDenormals();
    }

    struct CutState {
        SvfState first;
        SvfState second;
        double pole;

        public void Reset() {
            first.Reset();
            second.Reset();
            pole = 0;
        }

        public double Tick(in LoopFilter.Cut c, double x) {
            if (c.Sections > 0) {
                x = first.Tick(c.First, x);
            }

            if (c.Sections > 1) {
                x = second.Tick(c.Second, x);
            }

            if (!c.HasOnePole) {
                return x;
            }

            var v = (x - pole) * c.OnePole;
            var lowPass = v + pole;
            pole = lowPass + v;
            return c.HighPass ? x - lowPass : lowPass;
        }

        public void FlushDenormals() {
            first.FlushDenormals();
            second.FlushDenormals();
            if (Math.Abs(pole) < DenormalThreshold) {
                pole = 0;
            }
        }
    }
}
