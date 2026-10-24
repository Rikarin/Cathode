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
}

/// <summary>Sets a block's channel (default: the one the active scene uses) to a block preset.</summary>
public sealed record ApplyBlockPresetRequest(string Preset, string? Channel = null);
