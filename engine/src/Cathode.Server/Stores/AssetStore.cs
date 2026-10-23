using System.Security.Cryptography;
using System.Text.Json;
using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     Content-addressed store for impulse responses (.wav) and NAM models (.nam) in <c>&lt;data&gt;/assets</c>.
///     Presets reference assets as <c>sha256:&lt;hex&gt;</c>, so the same file uploaded twice is stored once and a
///     preset always gets exactly the file it was made with.
/// </summary>
public sealed class AssetStore {
    public const long MaxBytes = 64 * 1024 * 1024;
    const string Prefix = "sha256:";

    readonly string folder;
    readonly string indexPath;
    readonly Lock gate = new();
    readonly Dictionary<string, AssetInfo> index;

    public AssetStore(IOptions<ServerOptions> options) {
        folder = Path.Combine(options.Value.DataDirectory, "assets");
        Directory.CreateDirectory(folder);
        indexPath = Path.Combine(folder, "index.json");
        index = File.Exists(indexPath)
            ? (JsonSerializer.Deserialize(File.ReadAllText(indexPath), ContractsJsonContext.Default.AssetInfoArray)
                ?? [])
            .ToDictionary(static a => a.Ref, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    public IReadOnlyList<AssetInfo> List() {
        lock (gate) {
            return [..index.Values.OrderBy(static a => a.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public AssetInfo? Get(string reference) {
        lock (gate) {
            return index.GetValueOrDefault(reference);
        }
    }

    /// <summary>The file for <paramref name="reference" />, or null if it is not in the store.</summary>
    public string? PathOf(string reference) {
        lock (gate) {
            return index.TryGetValue(reference, out var info) ? FilePath(reference, info.Kind) : null;
        }
    }

    public async Task<AssetInfo> AddAsync(Stream content, string name, CancellationToken cancellationToken) {
        var kind = Path.GetExtension(name).ToLowerInvariant() switch {
            ".nam" => "nam",
            ".wav" => "ir",
            _ => throw new RigException(
                415,
                $"Unsupported asset '{name}': upload a .nam model or a .wav impulse response."
            )
        };

        var temporary = Path.Combine(folder, $"upload-{Guid.NewGuid():n}.tmp");
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var file = File.Create(temporary)) {
            var buffer = new byte[81_920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0) {
                size += read;
                if (size > MaxBytes) {
                    file.Close();
                    File.Delete(temporary);
                    throw new RigException(413, $"Assets are limited to {MaxBytes / (1024 * 1024)} MB.");
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        if (size == 0) {
            File.Delete(temporary);
            throw new RigException(400, "The upload is empty.");
        }

        var reference = Prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
        lock (gate) {
            if (index.TryGetValue(reference, out var existing)) {
                File.Delete(temporary);
                return existing;
            }

            File.Move(temporary, FilePath(reference, kind), true);
            var info = new AssetInfo(reference, Path.GetFileName(name), kind, size, DateTimeOffset.UtcNow);
            index[reference] = info;
            SaveIndex();
            return info;
        }
    }

    public bool Delete(string reference) {
        lock (gate) {
            if (!index.Remove(reference, out var info)) {
                return false;
            }

            File.Delete(FilePath(reference, info.Kind));
            SaveIndex();
            return true;
        }
    }

    string FilePath(string reference, string kind) =>
        Path.Combine(folder, reference[Prefix.Length..] + (kind == "nam" ? ".nam" : ".wav"));

    void SaveIndex() =>
        File.WriteAllText(
            indexPath,
            JsonSerializer.Serialize(index.Values.ToArray(), ContractsJsonContext.Default.AssetInfoArray)
        );
}
