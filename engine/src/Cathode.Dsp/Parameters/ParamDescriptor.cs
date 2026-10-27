namespace Cathode.Dsp.Parameters;

public enum ParamKind {
    Float,
    Enum,
    Bool
}

/// <summary>How the editor maps a knob position to a value. The engine always works in value space.</summary>
public enum ParamTaper {
    Linear,
    Log
}

/// <summary>How the audio thread moves from the current value to a new target.</summary>
public enum ParamSmoothing {
    /// <summary>Jump immediately (enums, switches).</summary>
    Step,

    /// <summary>Linear ramp over <see cref="ParamDescriptor.SmoothingMs" />.</summary>
    Linear
}

/// <summary>
///     Describes one parameter of a block type. <see cref="Id" /> is a stable identifier used by presets and the API.
/// </summary>
/// <remarks>
///     <see cref="TypeLabels" />, for a block whose models differ in their front panels (the Circuit Amp): the
///     parameter's name per value of the block's <c>type</c> parameter, or empty where that type has no such control
///     (the editor hides it there). Null for a parameter that is the same on every type.
///     <para>
///         <see cref="Recompile" />: the parameter changes the block's <see cref="IDspBlock.LatencySamples" /> (a
///         compressor's look-ahead), which the graph compiler reads only when it compiles. The server rebuilds the
///         graph (a gapless swap) when one changes, instead of only setting the target; the block keeps the value it
///         was compiled with until then.
///     </para>
///     <para>
///         <see cref="Page" />: the editor page the parameter sits on (Basic, Tone, Advanced, ...), set by the block's
///         <see cref="BlockDescriptor" /> from its page list; null for a block with one page.
///     </para>
///     <para>
///         <see cref="TypeDefaults" />: the parameter's default per value of the block's <c>type</c> parameter, for a
///         control a model ships at another setting than the rest (the Quick Rod's Solo Boost, off); null where the
///         default is <see cref="Default" /> on every type. Every default the block uses honours it
///         (<see cref="DefaultFor" />): a new block, a type change, a value the preset does not store.
///     </para>
/// </remarks>
public sealed record ParamDescriptor(
    string Id,
    string Name,
    ParamKind Kind,
    float Min,
    float Max,
    float Default,
    string Unit = "",
    ParamTaper Taper = ParamTaper.Linear,
    ParamSmoothing Smoothing = ParamSmoothing.Linear,
    float SmoothingMs = 20f,
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<string>? TypeLabels = null,
    bool Recompile = false,
    string? Page = null,
    IReadOnlyList<float>? TypeDefaults = null
) {
    public static ParamDescriptor Float(
        string id,
        string name,
        float min,
        float max,
        float defaultValue,
        string unit = "",
        ParamTaper taper = ParamTaper.Linear,
        float smoothingMs = 20f
    ) =>
        new(id, name, ParamKind.Float, min, max, defaultValue, unit, taper, ParamSmoothing.Linear, smoothingMs);

    public static ParamDescriptor Choice(string id, string name, IReadOnlyList<string> options, int defaultIndex = 0) =>
        new(
            id,
            name,
            ParamKind.Enum,
            0,
            options.Count - 1,
            defaultIndex,
            Smoothing: ParamSmoothing.Step,
            Options: options
        );

    public static ParamDescriptor Switch(string id, string name, bool defaultValue = false) =>
        new(id, name, ParamKind.Bool, 0, 1, defaultValue ? 1 : 0, Smoothing: ParamSmoothing.Step);

    /// <summary>
    ///     The default on type <paramref name="type" /> (the value of the block's <c>type</c> parameter):
    ///     <see cref="TypeDefaults" />'s entry for it, or <see cref="Default" />.
    /// </summary>
    public float DefaultFor(int type) =>
        TypeDefaults is { } defaults && (uint)type < (uint)defaults.Count ? defaults[type] : Default;

    /// <summary>Clamps to range, rounds discrete kinds, and replaces non-finite input with the default.</summary>
    public float Sanitize(float value) => Clean(value, Default);

    /// <summary>
    ///     As <see cref="Sanitize(float)" />, non-finite input taking type <paramref name="type" />'s default.
    /// </summary>
    public float Sanitize(float value, int type) => Clean(value, DefaultFor(type));

    float Clean(float value, float fallback) {
        if (!float.IsFinite(value)) {
            return fallback;
        }

        var clamped = Math.Clamp(value, Min, Max);
        return Kind == ParamKind.Float ? clamped : MathF.Round(clamped);
    }
}
