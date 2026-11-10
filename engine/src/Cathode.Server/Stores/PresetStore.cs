using Cathode.Contracts;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     The preset library: the factory bank (<see cref="FactoryPresets" />, read-only) and the user's presets, one JSON
///     file each in <c>&lt;data&gt;/presets</c>. A factory preset cannot be saved over or deleted; its colour tag and
///     favourite flag, the user's, are kept in <c>&lt;data&gt;/factory-marks</c>. Listings (the library, preset
///     next/previous, asset usage) come from an index of summaries kept in memory
///     (<see cref="JsonFileIndex{T, TEntry}" />); only a preset that is loaded is read whole.
/// </summary>
public sealed class PresetStore(
    IOptions<ServerOptions> options,
    FactoryPresets factory,
    ILogger<PresetStore>? logger = null,
    LibraryChanges? changes = null
) {
    readonly JsonFileIndex<Preset, Indexed> index = new(
        new(
            Path.Combine(options.Value.DataDirectory, "presets"),
            "preset",
            ContractsJsonContext.Default.Preset,
            static p => p.Id,
            PresetMigrations.Upgrade,
            logger,
            LibraryChanges.For(changes, LibraryChanges.Presets)
        ),
        static (preset, modified) => new(
            Summary(preset, modified),
            [..preset.Blocks.SelectMany(static b => b.Assets.Values).Distinct()]
        )
    );

    readonly JsonFileIndex<FactoryPresetMark, FactoryPresetMark> markIndex = new(
        new(
            Path.Combine(options.Value.DataDirectory, "factory-marks"),
            "factory preset mark",
            ContractsJsonContext.Default.FactoryPresetMark,
            static m => m.Id,
            logger: logger,
            changed: LibraryChanges.For(changes, LibraryChanges.Presets)
        ),
        static (mark, _) => mark
    );

    JsonFileStore<Preset> files => index.Store;

    JsonFileStore<FactoryPresetMark> marks => markIndex.Store;

    public FactoryPresets Factory => factory;

    /// <summary>The user's presets by name, then the factory bank in bank order.</summary>
    public IReadOnlyList<PresetSummary> List() => [..User(), ..FactorySummaries()];

    /// <summary>The user's own presets, by name.</summary>
    public IReadOnlyList<PresetSummary> User() => [
        ..index.All()
            .Select(static e => e.Summary)
            .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
    ];

    public static PresetSummary Summary(Preset preset, DateTimeOffset modified) =>
        new(
            preset.Id,
            preset.Name,
            preset.Author,
            preset.Tags,
            modified,
            preset.Color,
            preset.Favorite,
            preset.Description,
            preset.Factory
        );

    /// <summary>A copy of a stored or factory preset (with the user's colour and favourite on a factory one).</summary>
    public Preset? Get(string id) {
        if (!FactoryPresets.IsFactoryId(id)) {
            return files.Get(id);
        }

        if (factory.Get(id) is not { } preset) {
            return null;
        }

        if (marks.Get(id) is { } mark) {
            preset.Color = mark.Color;
            preset.Favorite = mark.Favorite;
        }

        return preset;
    }

    public bool IsFactory(string id) => factory.Contains(id);

    /// <summary>Every asset a stored preset uses, with those presets' names (sorted); factory ones use none.</summary>
    public AssetUsage[] AssetUsage() => [
        ..index.All()
            .SelectMany(static e => e.Assets.Select(reference => (Reference: reference, e.Summary.Name)))
            .GroupBy(static use => use.Reference)
            .Select(static g => new AssetUsage(
                    g.Key,
                    [..g.Select(static use => use.Name).Order(StringComparer.OrdinalIgnoreCase)]
                )
            )
    ];

    public bool Exists(string id) => IsFactory(id) || files.Exists(id);

    /// <summary>Stores a user preset. The factory's ids are taken (409): a factory preset is saved as a copy.</summary>
    public void Save(Preset preset) {
        if (FactoryPresets.IsFactoryId(preset.Id)) {
            throw RigException.Conflict("Factory presets cannot be changed; save the preset as a new one.");
        }

        PresetShape.Check(preset); // an import takes a client's document as it is
        preset.Factory = null;
        files.Save(preset);
    }

    /// <summary>Sets the user's colour tag and favourite flag of a factory preset.</summary>
    public void Mark(string id, string? color, bool favorite) {
        if (!IsFactory(id)) {
            throw RigException.NotFound($"No factory preset '{id}'.");
        }

        if (color is null && !favorite) {
            marks.Delete(id);
        } else {
            marks.Save(new(id, color, favorite));
        }
    }

    public bool Delete(string id) =>
        FactoryPresets.IsFactoryId(id)
            ? throw RigException.Conflict("Factory presets cannot be deleted.")
            : files.Delete(id);

    IEnumerable<PresetSummary> FactorySummaries() {
        var marked = new Dictionary<string, FactoryPresetMark>(StringComparer.Ordinal);
        foreach (var mark in markIndex.All()) {
            marked[mark.Id] = mark;
        }

        return factory.Summaries.Select(s => marked.TryGetValue(s.Id, out var mark)
                ? s with { Color = mark.Color, Favorite = mark.Favorite }
                : s
        );
    }

    /// <summary>What the listings need of a stored preset: its summary and the assets its blocks use.</summary>
    sealed record Indexed(PresetSummary Summary, string[] Assets);
}
