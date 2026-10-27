using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     The preset library: the factory bank (<see cref="FactoryPresets" />, read-only) and the user's presets, one JSON
///     file each in <c>&lt;data&gt;/presets</c>. A factory preset cannot be saved over or deleted; its colour tag and
///     favourite flag, the user's, are kept in <c>&lt;data&gt;/factory-marks</c>.
/// </summary>
public sealed class PresetStore(
    IOptions<ServerOptions> options,
    FactoryPresets factory,
    ILogger<PresetStore>? logger = null
) {
    readonly JsonFileStore<Preset> files = new(
        Path.Combine(options.Value.DataDirectory, "presets"),
        "preset",
        ContractsJsonContext.Default.Preset,
        static p => p.Id,
        PresetMigrations.Upgrade,
        logger
    );

    readonly JsonFileStore<FactoryPresetMark> marks = new(
        Path.Combine(options.Value.DataDirectory, "factory-marks"),
        "factory preset mark",
        ContractsJsonContext.Default.FactoryPresetMark,
        static m => m.Id,
        logger: logger
    );

    public FactoryPresets Factory => factory;

    /// <summary>The user's presets by name, then the factory bank in bank order.</summary>
    public IReadOnlyList<PresetSummary> List() => [..User(), ..FactorySummaries()];

    /// <summary>The user's own presets, by name.</summary>
    public IReadOnlyList<PresetSummary> User() => [
        ..files.All()
            .Select(static e => Summary(e.Item, e.Modified))
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
        ..files.All()
            .SelectMany(static e => e.Item.Blocks
                    .SelectMany(static b => b.Assets.Values)
                    .Distinct()
                    .Select(reference => (Reference: reference, e.Item.Name))
            )
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
        var marked = marks.All().ToDictionary(static e => e.Item.Id, static e => e.Item, StringComparer.Ordinal);
        return factory.Summaries.Select(s => marked.TryGetValue(s.Id, out var mark)
                ? s with { Color = mark.Color, Favorite = mark.Favorite }
                : s
        );
    }
}
