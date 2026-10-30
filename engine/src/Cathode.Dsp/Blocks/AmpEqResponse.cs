using Cathode.Dsp.Amp;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The Amp's Output EQ (its EQ page) as a frequency response, for the editor's chart (<see cref="EqResponse" />):
///     an impulse runs through an <see cref="AmpEqualizer" /> designed as the block designs it, ticked sample by
///     sample, at the amp's inner rate (<see cref="EqResponse.SampleRate" /> times the oversampling its Oversampling
///     setting gives there; the EQ runs oversampled wherever it is placed). Only the Output EQ: not the Input Filter on
///     the same page, nor the amp's stages or its oversampler. Off reads flat.
/// </summary>
public static class AmpEqResponse {
    /// <summary>The rate the Output EQ of <paramref name="values" /> runs at, and is measured at.</summary>
    public static double SampleRate(ParamBank values) =>
        EqResponse.SampleRate
        * CircuitAmpBlock.OversamplingFor(
            EqResponse.SampleRate,
            values.Current(CircuitAmpBlock.OversamplingSetting) >= 0.5f
        );

    /// <summary>The Output EQ's band centres, hertz; none while it is off.</summary>
    public static float[] Centers(ParamBank values) => Layout(values) is { } layout ? layout.Centers : [];

    /// <summary>
    ///     The response in dB at each of <paramref name="frequencies" /> (hertz) for an Amp block's
    ///     <paramref name="values" /> (their current values; the targets once the bank is snapped to them).
    /// </summary>
    public static float[] Of(ParamBank values, ReadOnlySpan<float> frequencies) {
        if (Layout(values) is null) {
            return EqResponse.Flat(frequencies);
        }

        var gains = new float[GraphicEqualizer.MaxBands];
        for (var b = 0; b < gains.Length; b++) {
            gains[b] = values.Current(CircuitAmpBlock.OutputEq1 + b);
        }

        var rate = SampleRate(values);
        var equalizer = new AmpEqualizer();
        equalizer.Design(LayoutIndex(values), gains, rate);
        var samples = EqResponse.Impulse(rate);
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)equalizer.Tick(samples[i]);
        }

        return EqResponse.Spectrum(samples, rate, frequencies);
    }

    /// <summary>The layout index the Output EQ setting picks: one below its value, -1 for Off.</summary>
    static int LayoutIndex(ParamBank values) =>
        Math.Clamp((int)values.Current(CircuitAmpBlock.OutputEq) - 1, -1, GraphicEqualizer.Layouts.Count - 1);

    static GraphicEqLayout? Layout(ParamBank values) =>
        LayoutIndex(values) is >= 0 and var index ? GraphicEqualizer.Layouts[index] : null;
}
