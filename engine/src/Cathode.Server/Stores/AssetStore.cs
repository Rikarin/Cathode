using System.Security.Cryptography;
using System.Text.Json;
using Cathode.Contracts;
using Cathode.Dsp.Convolution;
using Cathode.Dsp.IO;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     Content-addressed store for impulse responses (.wav), multi-position cab IR packs (.cabgrid: a zip with a manifest)
///     and amp captures (NAM .nam, AIDA-X .aidax/.json) in <c>&lt;data&gt;/assets</c>.
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

    /// <summary>
    ///     Import settings from the upload's query (<c>processing</c>: asIs, autoTrim or minimumPhase; <c>polarity</c>;
    ///     <c>channels</c>: both, left, right or sum; <c>maxLengthMs</c>: 5 to 10,000).
    /// </summary>
    public static IrImportSettings ImportSettings(
        string? processing,
        bool? polarity,
        string? channels,
        double? maxLengthMs
    ) {
        var p = processing switch {
            null or "asIs" => IrProcessing.AsIs,
            "autoTrim" => IrProcessing.AutoTrim,
            "minimumPhase" => IrProcessing.MinimumPhase,
            _ => throw new RigException(
                400,
                $"Unknown IR processing '{processing}': use asIs, autoTrim or minimumPhase."
            )
        };
        var c = channels switch {
            null or "both" => IrChannels.Both,
            "left" => IrChannels.Left,
            "right" => IrChannels.Right,
            "sum" => IrChannels.Sum,
            _ => throw new RigException(400, $"Unknown IR channels '{channels}': use both, left, right or sum.")
        };
        if (maxLengthMs is { } ms && (ms is < 5 or > 10_000 || !double.IsFinite(ms))) {
            throw new RigException(400, "An IR's maximum length must be 5 ms to 10 s.");
        }

        return new(p, polarity == true, c, maxLengthMs);
    }

    /// <summary>
    ///     Adds an impulse response processed on import (<see cref="IrImport" />: trimmed or made minimum phase, its
    ///     polarity corrected, a channel chosen, cut to a length); the processed file is what is stored and hashed. Other
    ///     assets, and default settings, are stored as uploaded.
    /// </summary>
    public async Task<AssetInfo> AddAsync(
        Stream content,
        string name,
        IrImportSettings import,
        CancellationToken cancellationToken
    ) {
        if (import.IsDefault || !string.Equals(Path.GetExtension(name), ".wav", StringComparison.OrdinalIgnoreCase)) {
            return await AddAsync(content, name, cancellationToken);
        }

        using var original = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0) {
            if (original.Length + read > MaxBytes) {
                throw new RigException(413, $"Assets are limited to {MaxBytes / (1024 * 1024)} MB.");
            }

            original.Write(buffer, 0, read);
        }

        original.Position = 0;
        AudioData audio;
        try {
            audio = WavFile.Read(original);
        } catch (Exception e) when (e is InvalidDataException or EndOfStreamException) {
            throw new RigException(400, $"{name}: {e.Message}");
        }

        if (audio.Frames == 0) {
            throw new RigException(400, $"{name} is empty.");
        }

        using var processed = new MemoryStream();
        WavFile.Write(processed, IrImport.Process(audio, import));
        processed.Position = 0;
        return await AddAsync(processed, name, cancellationToken);
    }

    public async Task<AssetInfo> AddAsync(Stream content, string name, CancellationToken cancellationToken) {
        var kind = Path.GetExtension(name).ToLowerInvariant() switch {
            ".nam" => "nam",
            ".aidax" or ".json" => "aidax",
            ".wav" => "ir",
            ".cabgrid" => "cabgrid",
            _ => throw new RigException(
                415,
                $"Unsupported asset '{name}': upload a .nam or .aidax model, a .wav impulse response or a .cabgrid IR pack."
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

    /// <summary>True for asset kinds that go into an amp's model slot.</summary>
    public static bool IsModel(string kind) => kind is "nam" or "aidax";

    /// <summary>
    ///     True when an asset of <paramref name="kind" /> can go into <paramref name="slot" />: models into
    ///     <c>model</c>, impulse responses or IR packs into the Cab's <c>slot1</c> … <c>slot4</c> (what a slot plays
    ///     depends on its Source), impulse responses elsewhere.
    /// </summary>
    public static bool Fits(string slot, string kind) =>
        slot == "model"
            ? IsModel(kind)
            : slot.StartsWith("slot", StringComparison.Ordinal)
                ? kind is "ir" or "cabgrid"
                : kind == "ir";

    /// <summary>
    ///     The extension a stored asset of <paramref name="kind" /> gets. The amp picks the model runtime by extension.
    /// </summary>
    public static string Extension(string kind) =>
        kind switch {
            "nam" => ".nam",
            "aidax" => ".aidax",
            "cabgrid" => ".cabgrid",
            _ => ".wav"
        };

    string FilePath(string reference, string kind) =>
        Path.Combine(folder, reference[Prefix.Length..] + Extension(kind));

    void SaveIndex() =>
        File.WriteAllText(
            indexPath,
            JsonSerializer.Serialize(index.Values.ToArray(), ContractsJsonContext.Default.AssetInfoArray)
        );
}
