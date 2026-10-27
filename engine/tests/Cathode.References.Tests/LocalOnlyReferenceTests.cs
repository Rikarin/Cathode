using Cathode.Testing.References;

namespace Cathode.References.Tests;

/// <summary>
///     Local-only references (T3K and other non-redistributable licences) must never enter the repository, wherever a
///     copy lands: no file anywhere in the source tree may carry a local-only reference's cache name, its source name,
///     or (when the manifest records it) its exact size and SHA-256.
/// </summary>
public class LocalOnlyReferenceTests {
    /// <summary>
    ///     Build output, dependencies and other git-ignored or foreign folders, which are never committed.
    /// </summary>
    static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) {
        ".git",
        ".claude",
        ".idea",
        ".vs",
        ".nx",
        "artifacts",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "TestResults"
    };

    static ReferenceManifest Manifest { get; } = ReferenceManifest.Load(ReferenceManifest.Locate());

    [Fact]
    public void No_local_only_reference_is_in_the_source_tree() {
        var manifestPath = ReferenceManifest.Locate();
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, "..", "..", "..", ".."));
        var local = Manifest.References.Where(static r => !r.Redistributable).ToList();
        var names = local.Select(static r => r.FileName)
            .Concat(
                local.Where(static r => r.SourceName is not null)
                    .Select(static r => Path.GetFileName(r.SourceName!) + Extension(r))
            )
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bySize = local.Where(static r => r.Sha256 is not null && r.Size is not null)
            .ToLookup(static r => r.Size!.Value);

        var found = new List<string>();
        foreach (var file in Files(root)) {
            var info = new FileInfo(file);
            if (names.Contains(info.Name)) {
                found.Add(file);
                continue;
            }

            if (bySize.Contains(info.Length)) {
                var hash = ReferenceCache.Sha256Of(file);
                if (bySize[info.Length].Any(r => string.Equals(r.Sha256, hash, StringComparison.OrdinalIgnoreCase))) {
                    found.Add(file);
                }
            }
        }

        Assert.True(found.Count == 0, $"Local-only reference files in the source tree: {string.Join(", ", found)}");
    }

    static string Extension(ReferenceEntry entry) => Path.GetExtension(entry.FileName);

    static IEnumerable<string> Files(string folder) {
        foreach (var file in Directory.EnumerateFiles(folder)) {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(folder)) {
            if (!Skipped.Contains(Path.GetFileName(child))) {
                foreach (var file in Files(child)) {
                    yield return file;
                }
            }
        }
    }
}
