namespace Cathode.Dsp.Filters;

/// <summary>
///     One band layout of a <see cref="GraphicEqualizer" />: its centres, labels and bandwidth.
/// </summary>
/// <param name="Name">The type's name, as a parameter option.</param>
/// <param name="Centers">Band centres in hertz, low to high (at most <see cref="GraphicEqualizer.MaxBands" />).</param>
/// <param name="Labels">The band labels the editor shows ("125", "1k").</param>
/// <param name="Q">The bands' Q at a Master Q of 1.</param>
/// <param name="Passive">
///     A passive-style layout: broad bands whose Q follows the gain, and shelves at both ends, whatever the Q mode and
///     shelf switch say.
/// </param>
public sealed record GraphicEqLayout(string Name, float[] Centers, string[] Labels, float Q, bool Passive = false) {
    public int Count => Centers.Length;
}

/// <summary>
///     The graphic equaliser shared by the Graphic EQ block, the Drive's EQ page and the amp's output EQ: up to ten
///     stereo bands (<see cref="EqBand" />) laid out by a <see cref="GraphicEqLayout" />. Bands are peaks, optionally
///     with the outermost two as shelves. In Variable Q mode (and on the passive layouts) a band narrows as it is pushed
///     harder: its Q runs from 40 % of the layout's near 0 dB to the full value at ±12 dB, so gentle settings are broad,
///     as on an analogue graphic. A flat band costs nothing, so a flat EQ is bit-transparent.
/// </summary>
/// <remarks>
///     The passive layouts (Mark, 5-, 4- and 3-band, console) are approximations by broad, overlapping bands and end
///     shelves at the usual centres of those networks, not component-level models of them: neighbouring bands interact as
///     their skirts overlap, but a cut does not change the other bands' corners as a real passive network would.
/// </remarks>
public sealed class GraphicEqualizer {
    public const int MaxBands = 10;

    /// <summary>Q of an octave-wide band.</summary>
    public const float OctaveQ = 1.4142f;

    /// <summary>Q of a 2/3-octave band.</summary>
    const float TwoThirdsQ = 2.145f;

    /// <summary>Q of the passive layouts' bands, broad enough that neighbours overlap.</summary>
    const float PassiveQ = 0.7f;

    readonly EqBand[] bands = [..Enumerable.Range(0, MaxBands).Select(static _ => new EqBand())];
    int layout = -1;

    /// <summary>The layouts, in parameter order. The first is the classic ten-band octave EQ.</summary>
    public static IReadOnlyList<GraphicEqLayout> Layouts { get; } = [
        new(
            "10 Band",
            [31.25f, 62.5f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f, 16_000f],
            ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"],
            OctaveQ
        ),
        new(
            "10 Band 2/3 Oct",
            [63f, 100f, 160f, 250f, 400f, 630f, 1_000f, 1_600f, 2_500f, 4_000f],
            ["63", "100", "160", "250", "400", "630", "1k", "1.6k", "2.5k", "4k"],
            TwoThirdsQ
        ),
        new(
            "8 Band",
            [62.5f, 125f, 250f, 500f, 1_000f, 2_000f, 4_000f, 8_000f],
            ["63", "125", "250", "500", "1k", "2k", "4k", "8k"],
            OctaveQ
        ),
        new(
            "7 Band",
            [100f, 200f, 400f, 800f, 1_600f, 3_200f, 6_400f],
            ["100", "200", "400", "800", "1.6k", "3.2k", "6.4k"],
            OctaveQ
        ),
        new("5 Band", [100f, 300f, 1_000f, 3_000f, 8_000f], ["100", "300", "1k", "3k", "8k"], 1f),
        new("5 Band Mark", [80f, 240f, 750f, 2_200f, 6_600f], ["80", "240", "750", "2.2k", "6.6k"], PassiveQ, true),
        new("5 Band Passive", [100f, 400f, 1_000f, 3_000f, 8_000f], ["100", "400", "1k", "3k", "8k"], PassiveQ, true),
        new("4 Band Passive", [100f, 700f, 2_500f, 6_000f], ["Bass", "Middle", "Treble", "Presence"], PassiveQ, true),
        new("3 Band Passive", [100f, 700f, 3_000f], ["Bass", "Middle", "Treble"], PassiveQ, true),
        new("3 Band Console", [100f, 1_000f, 10_000f], ["Low", "Mid", "High"], PassiveQ, true)
    ];

    /// <summary>The layout names, for a Type parameter's options.</summary>
    public static IReadOnlyList<string> LayoutNames { get; } = [..Layouts.Select(static l => l.Name)];

    /// <summary>
    ///     The name band <paramref name="band" /> (0-based) has on each layout, with <paramref name="suffix" />, or empty
    ///     where the layout has fewer bands: a <see cref="Parameters.ParamDescriptor.TypeLabels" /> list.
    /// </summary>
    public static string[] BandLabels(int band, string suffix = " Hz") =>
        [..Layouts.Select(l => band < l.Count ? Label(l, band, suffix) : string.Empty)];

    /// <summary>The variable-Q law: 40 % of <paramref name="q" /> near 0 dB, the full value at ±12 dB.</summary>
    public static float VariableQ(float q, float gainDb) => q * (0.4f + 0.6f * Math.Min(1f, MathF.Abs(gainDb) / 12f));

    /// <summary>The filter type of band <paramref name="band" /> of <paramref name="layout" />.</summary>
    public static BiquadType BandType(GraphicEqLayout layout, int band, bool shelves) =>
        !(shelves || layout.Passive) ? BiquadType.Peak
        : band == 0 ? BiquadType.LowShelf
        : band == layout.Count - 1 ? BiquadType.HighShelf
        : BiquadType.Peak;

    public void Reset() {
        foreach (var band in bands) {
            band.Reset();
        }
    }

    /// <summary>
    ///     Equalises <paramref name="io" /> in place with <paramref name="gains" /> (dB, one per band of the layout; extra
    ///     entries are ignored). <paramref name="moving" /> says which gains are still ramping, so a band on its way to
    ///     0 dB keeps running until it gets there.
    /// </summary>
    public void Process(
        in StereoBuffer io,
        double sampleRate,
        int layoutIndex,
        ReadOnlySpan<float> gains,
        ReadOnlySpan<bool> moving,
        float masterQ,
        bool variableQ,
        bool shelves
    ) {
        layoutIndex = Math.Clamp(layoutIndex, 0, Layouts.Count - 1);
        if (layoutIndex != layout) {
            layout = layoutIndex; // the bands move: start them clean
            Reset();
        }

        var l = Layouts[layoutIndex];
        var q = l.Q * masterQ;
        var proportional = variableQ || l.Passive;
        for (var b = 0; b < l.Count; b++) {
            var gain = gains[b];
            var type = BandType(l, b, shelves);

            // A shelf's corner sits half a band out from its centre, so it covers the band it replaces.
            var frequency = type switch {
                BiquadType.LowShelf => l.Centers[b] * 1.4142f,
                BiquadType.HighShelf => l.Centers[b] / 1.4142f,
                _ => l.Centers[b]
            };
            var bandQ = type == BiquadType.Peak ? proportional ? VariableQ(q, gain) : q : 0.7071f;
            bands[b].Process(io, type, sampleRate, frequency, bandQ, gain, moving[b]);
        }
    }

    static string Label(GraphicEqLayout layout, int band, string suffix) =>
        char.IsDigit(layout.Labels[band][0]) ? layout.Labels[band] + suffix : layout.Labels[band];
}
