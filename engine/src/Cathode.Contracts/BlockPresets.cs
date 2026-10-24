namespace Cathode.Contracts;

/// <summary>
///     A named setting of one block type, kept in the Block Library: the factory's (the guide's "types" that are
///     presets of the same algorithm, such as the many choruses, flangers and reverbs) and the user's own. Applying one
///     sets a channel of a block to it; it never adds a Type parameter that would fight the knobs.
/// </summary>
public sealed class BlockPreset {
    public string Id { get; set; } = Preset.NewId();

    /// <summary>The block type id it is for, e.g. <c>chorus</c>.</summary>
    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>A factory preset ships with Cathode and cannot be changed or deleted.</summary>
    public bool Factory { get; set; }

    /// <summary>Parameter values by id. Sparse: anything not listed takes the block type's default.</summary>
    public Dictionary<string, float> Values { get; set; } = [];

    /// <summary>A folder (category) in the Library, or null for the top level.</summary>
    public string? Folder { get; set; }

    /// <summary>
    ///     A whole-block entry: every channel's values (<see cref="Values" /> is then the channel it was saved from).
    ///     Null for a single-channel entry.
    /// </summary>
    public Dictionary<string, Dictionary<string, float>>? Channels { get; set; }

    /// <summary>A whole-block entry's modifiers, with <c>Block</c> left empty: recalled onto the target block.</summary>
    public List<PresetModifier>? Modifiers { get; set; }

    /// <summary>A whole-block entry's assets (IRs, captures) by slot.</summary>
    public Dictionary<string, string>? Assets { get; set; }
}

/// <summary>
///     Sets a block's channel (default: the one the active scene uses) to a block preset, or with <c>AllChannels</c>
///     every channel (a whole-block entry's own channels, modifiers and assets).
/// </summary>
public sealed record ApplyBlockPresetRequest(string Preset, string? Channel = null, bool AllChannels = false);
