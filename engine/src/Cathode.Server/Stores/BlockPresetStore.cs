using Cathode.Contracts;
using Cathode.Dsp.Parameters;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

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
        static p => p.Id,
        Upgrade
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

        var channels = preset.Channels?.Where(c => Preset.ChannelNames.IndexOf(c.Key) is var i
            && i >= 0
            && i < descriptor.Channels
            )
            .ToDictionary(static c => c.Key, c => Sanitize(descriptor, c.Value));
        var saved = new BlockPreset {
            Id = id,
            Type = preset.Type,
            Name = preset.Name.Trim(),
            Factory = false,
            Values = Sanitize(descriptor, preset.Values),
            Folder = string.IsNullOrWhiteSpace(preset.Folder) ? null : preset.Folder.Trim(),
            Channels = channels,
            Modifiers = preset.Modifiers?.Where(m => descriptor.IndexOf(m.Param) >= 0 || m.Param == "engage")
                .Select(static m => new PresetModifier {
                        Param = m.Param,
                        Source = m.Source,
                        Min = m.Min,
                        Max = m.Max,
                        Curve = m.Curve,
                        AttackMs = m.AttackMs,
                        ReleaseMs = m.ReleaseMs
                    }
                )
                .ToList(),
            Assets = preset.Assets is { Count: > 0 } assets ? new(assets) : null
        };
        files.Save(saved);
        return saved;
    }

    /// <summary>Adds every entry of an exported file as a new user entry (fresh ids); factory flags are dropped.</summary>
    public BlockPreset[] Import(BlockPresetFile file) {
        if (file.Format != BlockPresetFile.FormatName) {
            throw new RigException(400, $"Not a Block Library file (format '{file.Format}').");
        }

        return [..file.Presets.Select(p => Save(Preset.NewId(), p))];
    }

    /// <summary>The entries <paramref name="ids" /> as a file to save.</summary>
    public BlockPresetFile Export(IEnumerable<string> ids) =>
        new(
            BlockPresetFile.FormatName,
            1,
            [..ids.Select(id => Get(id) ?? throw RigException.NotFound($"No block preset '{id}'."))]
        );

    static Dictionary<string, float> Sanitize(BlockDescriptor descriptor, Dictionary<string, float> values) =>
        values.Where(v => descriptor.IndexOf(v.Key) >= 0)
            .ToDictionary(static v => v.Key, v => descriptor.Params[descriptor.IndexOf(v.Key)].Sanitize(v.Value));

    public bool Delete(string id) =>
        FactoryBlockPresets.All.Any(p => p.Id == id)
            ? throw RigException.Conflict("Factory block presets cannot be deleted.")
            : files.Delete(id);

    /// <summary>A stored block preset brought up to date (<see cref="PresetMigrations.UpgradeAmpChannel(JsonObject?)" />).</summary>
    static JsonNode? Upgrade(JsonNode? node) {
        if (node is JsonObject preset && preset["type"]?.GetValue<string>() == "circuitamp") {
            PresetMigrations.UpgradeAmpChannel(preset["values"] as JsonObject);
            foreach (var (_, channel) in preset["channels"] as JsonObject ?? []) {
                PresetMigrations.UpgradeAmpChannel(channel as JsonObject);
            }
        }

        return node;
    }
}
