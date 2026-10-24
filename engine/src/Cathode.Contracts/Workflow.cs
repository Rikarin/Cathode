namespace Cathode.Contracts;

// The editor's workflow: grid operations, snapshots, templates, preset tools, performance pages and footswitch
// overrides (docs/02-architecture.md §7.5).

/// <summary>
///     One control on a performance page: a parameter of a block, by the block's id (<c>amp1</c>) so a global page
///     works in every preset that has that block. <c>Label</c> overrides the parameter's name.
/// </summary>
public sealed record PerformControl(string Block, string Param, string? Label = null);

/// <summary>
///     A preset's own action for one switch of a footswitch layout: while the preset is loaded, the switch at
///     <c>Index</c> of layout <c>Layout</c> does this instead of what the layout says.
/// </summary>
public sealed record FootswitchOverride(
    string Layout,
    int Index,
    SwitchAction Tap,
    SwitchAction? Hold = null,
    string? Label = null
);

/// <summary>
///     A saved state of a preset's working copy, unsaved edits included, kept beside the presets so every editor sees
///     it.
/// </summary>
public sealed class PresetSnapshot {
    public string Id { get; set; } = Preset.NewId();

    /// <summary>The preset the snapshot was taken of.</summary>
    public string PresetId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public Preset Preset { get; set; } = new();
}

public sealed record SnapshotSummary(string Id, string PresetId, string Name, DateTimeOffset Created);

/// <summary>A file of Block Library entries, as exported and imported (<c>.cthblock</c>).</summary>
public sealed record BlockPresetFile(string Format, int Version, List<BlockPreset> Presets) {
    public const string FormatName = "cathode-block-presets";
    public const string Extension = ".cthblock";
}

/// <summary>Presets changed by a batch operation, and those it left alone (no matching block, or not found).</summary>
public sealed record BatchResult(IReadOnlyList<string> Changed, IReadOnlyList<string> Skipped);

// Request bodies.

/// <summary>Drops block <c>{id}</c> on <c>Target</c>: <c>swap</c> exchanges their cells, <c>replace</c> takes its place.</summary>
public sealed record DropBlockRequest(string Target, string Mode = "swap");

/// <summary>Replaces a block with a new one of <c>Type</c> in the same cell, keeping its cables.</summary>
public sealed record ReplaceBlockRequest(string Type);

/// <summary>Moves a whole row or column (<c>Axis</c>) from one index to another, shifting the ones between.</summary>
public sealed record MoveLineRequest(string Axis, int From, int To);

/// <summary>A block's settings (every channel, and its assets) pasted onto a block of the same type.</summary>
public sealed record BlockSettingsRequest(
    string Type,
    Dictionary<string, Dictionary<string, float>> Channels,
    Dictionary<string, string>? Assets = null
);

/// <summary>Sets a block's bypass and/or channel in every scene; null leaves that part alone.</summary>
public sealed record AllScenesRequest(bool? Bypassed = null, string? Channel = null);

public sealed record SnapshotRequest(string? Name = null);

public sealed record TemplateRequest(string Name);

/// <summary>A preset's metadata, changed in place; null leaves a field alone, an empty colour clears it.</summary>
public sealed record PresetMetadataRequest(string? Name = null, string? Color = null, bool? Favorite = null);

/// <summary>An asset's metadata; null leaves a field alone, an empty colour clears it.</summary>
public sealed record AssetMetadataRequest(string? Name = null, string? Color = null, bool? Favorite = null);

/// <summary>Copies a stored preset under <c>Id</c> (a new one when null) and <c>Name</c> (the original's plus "copy").</summary>
public sealed record DuplicatePresetRequest(string? Id = null, string? Name = null);

public sealed record PresetBatchRequest(List<string> Presets);

/// <summary>
///     Parameter Batch Set: <c>Param</c> = <c>Value</c> on the block with id <c>Block</c> and type <c>Type</c> in each
///     preset, on <c>Channel</c> (every channel when null).
/// </summary>
public sealed record BatchSetRequest(
    List<string> Presets,
    string Block,
    string Type,
    string Param,
    float Value,
    string? Channel = null
);

public sealed record BlockPresetExportRequest(List<string> Ids);

/// <summary>Saves every block of the working copy (all channels and modifiers) to the Block Library.</summary>
public sealed record SaveAllBlocksRequest(string? Folder = null);
