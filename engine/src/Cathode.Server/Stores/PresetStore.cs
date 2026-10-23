using System.Text.Json;
using System.Text.RegularExpressions;
using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>The preset library: one JSON file per preset in <c>&lt;data&gt;/presets</c>.</summary>
public sealed partial class PresetStore {
    readonly string folder;

    public PresetStore(IOptions<ServerOptions> options) {
        folder = Path.Combine(options.Value.DataDirectory, "presets");
        Directory.CreateDirectory(folder);
    }

    public IReadOnlyList<PresetSummary> List() =>
        [
            .. Directory.EnumerateFiles(folder, "*.json")
                .Select(path => (Path: path, Preset: TryRead(path)))
                .Where(static p => p.Preset is not null)
                .Select(static p => new PresetSummary(p.Preset!.Id, p.Preset.Name, p.Preset.Author, p.Preset.Tags, File.GetLastWriteTimeUtc(p.Path)))
                .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
        ];

    public Preset? Get(string id) => File.Exists(PathOf(id)) ? TryRead(PathOf(id)) : null;

    public void Save(Preset preset) {
        var path = PathOf(preset.Id);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preset, ContractsJsonContext.Default.Preset));
        File.Move(temporary, path, true); // atomic replace: a crash never leaves a half-written preset
    }

    public bool Delete(string id) {
        var path = PathOf(id);
        if (!File.Exists(path)) {
            return false;
        }

        File.Delete(path);
        return true;
    }

    string PathOf(string id) =>
        IdPattern().IsMatch(id) ? Path.Combine(folder, id + ".json") : throw new RigException(400, $"Invalid preset id '{id}'.");

    static Preset? TryRead(string path) {
        try {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ContractsJsonContext.Default.Preset);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>Ids are file names, so only safe characters (no separators, no dots).</summary>
    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex IdPattern();
}
