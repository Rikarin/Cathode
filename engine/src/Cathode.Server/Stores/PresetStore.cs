using Cathode.Contracts;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>The preset library: one JSON file per preset in <c>&lt;data&gt;/presets</c>.</summary>
public sealed class PresetStore(IOptions<ServerOptions> options) {
    readonly JsonFileStore<Preset> files = new(
        Path.Combine(options.Value.DataDirectory, "presets"),
        "preset",
        ContractsJsonContext.Default.Preset,
        static p => p.Id
    );

    public IReadOnlyList<PresetSummary> List() => [
        ..files.All()
            .Select(static e => new PresetSummary(e.Item.Id, e.Item.Name, e.Item.Author, e.Item.Tags, e.Modified))
            .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
    ];

    public Preset? Get(string id) => files.Get(id);

    public bool Exists(string id) => files.Exists(id);

    public void Save(Preset preset) => files.Save(preset);

    public bool Delete(string id) => files.Delete(id);
}
