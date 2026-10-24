using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     The Block Library: the factory block presets (<see cref="FactoryBlockPresets" />, read-only) and the user's, one
///     JSON file each in <c>&lt;data&gt;/block-presets</c>. Saved presets are checked against the block catalogue.
/// </summary>
public sealed class BlockPresetStore(IOptions<ServerOptions> options, BlockCatalog catalog) {
    readonly JsonFileStore<BlockPreset> files = new(
        Path.Combine(options.Value.DataDirectory, "block-presets"),
        "block preset",
        ContractsJsonContext.Default.BlockPreset,
        static p => p.Id
    );

    /// <summary>Factory presets first, then the user's, each by name; only <paramref name="type" />'s when given.</summary>
    public BlockPreset[] List(string? type = null) => [
        ..FactoryBlockPresets.All.Where(p => type is null || p.Type == type),
        ..files.All()
            .Select(static e => e.Item)
            .Where(p => type is null || p.Type == type)
            .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
    ];

    public BlockPreset? Get(string id) =>
        FactoryBlockPresets.All.FirstOrDefault(p => p.Id == id) ?? (StoreIds.IsValid(id) ? files.Get(id) : null);

    /// <summary>Saves a user preset under <paramref name="id" />; values are sanitised against the block type.</summary>
    public BlockPreset Save(string id, BlockPreset preset) {
        if (FactoryBlockPresets.All.Any(p => p.Id == id) || id.StartsWith("factory-", StringComparison.Ordinal)) {
            throw RigException.Conflict("Factory block presets cannot be changed; save it under another id.");
        }

        var descriptor = catalog.Descriptor(preset.Type)
            ?? throw new RigException(400, $"'{preset.Type}' is not a block type with parameters.");
        var unknown = preset.Values.Keys.Where(k => descriptor.IndexOf(k) < 0).ToList();
        if (unknown.Count > 0) {
            throw new RigException(
                400,
                $"{descriptor.Name} has no parameter(s) {string.Join(", ", unknown)}.",
                unknown
            );
        }

        if (string.IsNullOrWhiteSpace(preset.Name)) {
            throw new RigException(400, "A block preset needs a name.");
        }

        var saved = new BlockPreset {
            Id = id,
            Type = preset.Type,
            Name = preset.Name.Trim(),
            Factory = false,
            Values = preset.Values.ToDictionary(
                static v => v.Key,
                v => descriptor.Params[descriptor.IndexOf(v.Key)].Sanitize(v.Value)
            )
        };
        files.Save(saved);
        return saved;
    }

    public bool Delete(string id) =>
        FactoryBlockPresets.All.Any(p => p.Id == id)
            ? throw RigException.Conflict("Factory block presets cannot be deleted.")
            : files.Delete(id);
}
