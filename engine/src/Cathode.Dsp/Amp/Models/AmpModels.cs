namespace Cathode.Dsp.Amp;

/// <summary>The registry of amp models (see <see cref="AmpDesign" /> for how to add one).</summary>
public sealed partial record AmpDesign {
    /// <summary>
    ///     The models, in the order of the Circuit Amp block's Type choice: a model's index is its saved value, so
    ///     append only, one line per model, and never reorder or remove.
    /// </summary>
    public static IReadOnlyList<AmpDesign> All { get; } = [
        Models.Blackface.Design,
        Models.Plexi.Design,
        Models.AC30.Design,
        Models.AC15.Design,
        Models.DC30.Design,
        Models.Slo100.Design,
        Models.MarkIIC.Design,
        Models.Peavey5150.Design,
        Models.DualRectifier.Design,
        Models.Evh5150III.Design,
        Models.MarkIV.Design
    ];

    public static AmpDesign Blackface => Models.Blackface.Design;

    public static AmpDesign Plexi => Models.Plexi.Design;

    public static AmpDesign AC30 => Models.AC30.Design;
}
