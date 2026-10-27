using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     Snapshots of working copies (<c>&lt;data&gt;/snapshots</c>, one JSON file each), so the desktop app and a
///     browser share them.
/// </summary>
public sealed class SnapshotStore(IOptions<ServerOptions> options, ILogger<SnapshotStore>? logger = null) {
    readonly JsonFileStore<PresetSnapshot> files = new(
        Path.Combine(options.Value.DataDirectory, "snapshots"),
        "snapshot",
        ContractsJsonContext.Default.PresetSnapshot,
        static s => s.Id,
        PresetMigrations.UpgradeSnapshot,
        logger
    );

    /// <summary>Newest first; only <paramref name="presetId" />'s when given.</summary>
    public SnapshotSummary[] List(string? presetId = null) => [
        ..files.All()
            .Select(static e => e.Item)
            .Where(s => presetId is null || s.PresetId == presetId)
            .OrderByDescending(static s => s.Created)
            .Select(static s => new SnapshotSummary(s.Id, s.PresetId, s.Name, s.Created))
    ];

    public PresetSnapshot? Get(string id) => StoreIds.IsValid(id) ? files.Get(id) : null;

    /// <summary>
    ///     Stores <paramref name="preset" /> as it is now, named after it and the time unless given a name.
    /// </summary>
    public SnapshotSummary Add(Preset preset, string? name) {
        var created = DateTimeOffset.UtcNow;
        var snapshot = new PresetSnapshot {
            PresetId = preset.Id,
            Name = string.IsNullOrWhiteSpace(name)
                ? $"{preset.Name} {created.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : name.Trim(),
            Created = created,
            Preset = preset
        };
        files.Save(snapshot);
        return new(snapshot.Id, snapshot.PresetId, snapshot.Name, snapshot.Created);
    }

    public bool Delete(string id) => StoreIds.IsValid(id) && files.Delete(id);
}

/// <summary>Preset templates (<c>&lt;data&gt;/templates</c>): starting points for New from template.</summary>
public sealed class TemplateStore(IOptions<ServerOptions> options, ILogger<TemplateStore>? logger = null) {
    readonly JsonFileStore<Preset> files = new(
        Path.Combine(options.Value.DataDirectory, "templates"),
        "template",
        ContractsJsonContext.Default.Preset,
        static p => p.Id,
        PresetMigrations.Upgrade,
        logger
    );

    public PresetSummary[] List() => [
        ..files.All()
            .Select(static e => PresetStore.Summary(e.Item, e.Modified))
            .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
    ];

    public Preset? Get(string id) => StoreIds.IsValid(id) ? files.Get(id) : null;

    /// <summary>Stores <paramref name="preset" /> (a copy) as a new template called <paramref name="name" />.</summary>
    public PresetSummary Add(Preset preset, string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new RigException(400, "A template needs a name.");
        }

        preset.Id = Preset.NewId();
        preset.Name = name.Trim();
        files.Save(preset);
        return PresetStore.Summary(preset, DateTimeOffset.UtcNow);
    }

    public bool Delete(string id) => StoreIds.IsValid(id) && files.Delete(id);
}
