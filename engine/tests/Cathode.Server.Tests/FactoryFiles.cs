using Cathode.Contracts;
using Cathode.Server.Stores;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     The factory bank's source files (<c>src/Cathode.Server/Factory</c>), for the tests that rewrite them: one
///     <c>.cthpreset</c> per preset, named by its id after <see cref="FactoryPresets.IdPrefix" />, written as the
///     editor exports one but with the preset's JSON indented and every entry dated
///     <see cref="FactoryPresets.Revised" />, so a rewrite that changes nothing leaves the file as it was.
/// </summary>
static class FactoryFiles {
    static readonly JsonSerializerOptions Indented = new(ContractsJsonContext.Default.Options) {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Folder => Path.Combine(EngineRoot(), "src", "Cathode.Server", "Factory");

    /// <summary>Writes <paramref name="preset" /> over its package in the source tree.</summary>
    public static void Write(Preset preset) {
        var path = Path.Combine(
            Folder,
            "presets",
            preset.Id[FactoryPresets.IdPrefix.Length..] + PresetPackage.Extension
        );
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var manifest = new PackageManifest(PresetPackage.Format, PresetPackage.Version, preset.Id, preset.Name, []);
        Entry(zip, "manifest.json", JsonSerializer.Serialize(manifest, ContractsJsonContext.Default.PackageManifest));
        Entry(zip, "preset.json", JsonSerializer.Serialize(preset, Indented) + "\n");
    }

    static void Entry(ZipArchive zip, string name, string text) {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FactoryPresets.Revised;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder.");
    }
}
