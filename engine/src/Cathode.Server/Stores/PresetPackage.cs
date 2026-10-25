using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Cathode.Contracts;
using Cathode.Server.Rig;

namespace Cathode.Server.Stores;

/// <summary>
///     The <c>.cthpreset</c> package: a zip of <c>manifest.json</c>, <c>preset.json</c> and the IRs and models the
///     preset uses as <c>assets/&lt;sha256&gt;.&lt;wav|nam&gt;</c>. Import treats the file as hostile: only those
///     exact names are accepted (nothing is ever extracted to a path taken from the zip), sizes are enforced while
///     reading rather than trusted from headers, and every asset must hash to its name before it enters the store.
/// </summary>
public sealed partial class PresetPackage(PresetStore presets, AssetStore assets) {
    public const string Extension = ".cthpreset";
    public const string Format = "cathode-preset";
    public const int Version = 1;
    public const long MaxPackageBytes = 512L * 1024 * 1024;
    const int MaxEntries = 64;
    const int MaxPresetBytes = 4 * 1024 * 1024;

    /// <summary>Writes <paramref name="preset" /> and every asset it references to <paramref name="output" />.</summary>
    public async Task ExportAsync(Preset preset, Stream output, CancellationToken cancellationToken) {
        var references = preset.Blocks.SelectMany(static b => b.Assets.Values)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var included = references.Select(r =>
                assets.Get(r)
                ?? throw new RigException(409, $"The preset uses asset {r}, which is not in the asset store.")
            )
            .ToList();

        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
        var manifest = new PackageManifest(Format, Version, preset.Id, preset.Name, included);
        await WriteJsonAsync(
            zip,
            "manifest.json",
            JsonSerializer.SerializeToUtf8Bytes(manifest, ContractsJsonContext.Default.PackageManifest),
            cancellationToken
        );
        await WriteJsonAsync(
            zip,
            "preset.json",
            JsonSerializer.SerializeToUtf8Bytes(preset, ContractsJsonContext.Default.Preset),
            cancellationToken
        );
        foreach (var asset in included) {
            var entry = zip.CreateEntry(
                $"assets/{asset.Ref[7..]}{AssetStore.Extension(asset.Kind)}",
                CompressionLevel.Optimal
            );
            await using var target = await entry.OpenAsync(cancellationToken);
            await using var source = File.OpenRead(assets.PathOf(asset.Ref)!);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    public async Task<ImportResult> ImportAsync(Stream input, CancellationToken cancellationToken) {
        // ZipArchive needs a seekable stream; buffer to a temporary file, enforcing the package size limit.
        var temporary = Path.GetTempFileName();
        try {
            await using (var file = File.Create(temporary)) {
                await CopyLimitedAsync(input, file, MaxPackageBytes, "The package", cancellationToken);
            }

            await using var stream = File.OpenRead(temporary);
            ZipArchive zip;
            try {
                zip = new(stream, ZipArchiveMode.Read);
            } catch (InvalidDataException) {
                throw new RigException(400, "This is not a .cthpreset package (not a zip file).");
            }

            using (zip) {
                return await ImportAsync(zip, cancellationToken);
            }
        } finally {
            File.Delete(temporary);
        }
    }

    async Task<ImportResult> ImportAsync(ZipArchive zip, CancellationToken cancellationToken) {
        if (zip.Entries.Count > MaxEntries) {
            throw new RigException(400, $"A package holds at most {MaxEntries} files.");
        }

        var unexpected = zip.Entries.Where(e => !AllowedName().IsMatch(e.FullName))
            .Select(static e => e.FullName)
            .ToList();
        if (unexpected.Count > 0) {
            throw new RigException(400, "The package contains unexpected files.", unexpected);
        }

        var manifest = await ReadJsonAsync(
            zip,
            "manifest.json",
            ContractsJsonContext.Default.PackageManifest,
            cancellationToken
        );
        if (manifest.Format != Format || manifest.Version > Version) {
            throw new RigException(400, $"Unsupported package format '{manifest.Format}' version {manifest.Version}.");
        }

        var preset = await ReadJsonAsync(
            zip,
            "preset.json",
            ContractsJsonContext.Default.Preset,
            cancellationToken,
            PresetMigrations.Upgrade
        );
        if (preset.SchemaVersion > Preset.CurrentSchemaVersion) {
            throw RigException.Conflict(
                $"Preset '{preset.Name}' was saved by a newer version of Cathode (schema {preset.SchemaVersion})."
            );
        }

        // Verify every asset hashes to its name before anything enters the store.
        var packaged = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(static e => e.FullName.StartsWith("assets/", StringComparison.Ordinal)
                 )) {
            var reference = "sha256:" + Path.GetFileNameWithoutExtension(entry.Name);
            await using var content = await entry.OpenAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await CopyLimitedAsync(content, new HashingSink(hash), AssetStore.MaxBytes, entry.Name, cancellationToken);
            if ("sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset()) != reference) {
                throw new RigException(400, $"Asset {entry.Name} is corrupt: its content does not match its hash.");
            }

            packaged[reference] = entry;
        }

        var missing = preset.Blocks.SelectMany(static b => b.Assets.Values)
            .Distinct(StringComparer.Ordinal)
            .Where(r => !packaged.ContainsKey(r) && assets.Get(r) is null)
            .ToList();
        if (missing.Count > 0) {
            throw new RigException(
                400,
                "The preset uses assets that are neither in the package nor in this library.",
                missing
            );
        }

        var added = 0;
        foreach (var (reference, entry) in packaged) {
            if (assets.Get(reference) is not null) {
                continue;
            }

            var name = manifest.Assets.FirstOrDefault(a => a.Ref == reference)?.Name is { Length: > 0 } original
                ? Path.ChangeExtension(Path.GetFileName(original), Path.GetExtension(entry.Name))
                : entry.Name;
            await using var content = await entry.OpenAsync(cancellationToken);
            await assets.AddAsync(content, name, cancellationToken);
            added++;
        }

        preset.Factory = null; // an exported factory preset comes back as the user's own
        var renamed = !StoreIds.IsValid(preset.Id) || FactoryPresets.IsFactoryId(preset.Id) || presets.Exists(preset.Id);
        if (renamed) {
            preset.Id = Preset.NewId(); // never overwrite a preset already in the library
        }

        presets.Save(preset);
        var summary = presets.List().Single(p => p.Id == preset.Id);
        return new(summary, added, renamed);
    }

    static async Task WriteJsonAsync(ZipArchive zip, string name, byte[] json, CancellationToken cancellationToken) {
        await using var stream = await zip.CreateEntry(name, CompressionLevel.Optimal).OpenAsync(cancellationToken);
        await stream.WriteAsync(json, cancellationToken);
    }

    static async Task<T> ReadJsonAsync<T>(
        ZipArchive zip,
        string name,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        Func<JsonNode?, JsonNode?>? upgrade = null
    )
        where T : class {
        var entry = zip.GetEntry(name) ?? throw new RigException(400, $"The package has no {name}.");
        await using var content = await entry.OpenAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(content, buffer, MaxPresetBytes, name, cancellationToken);
        try {
            var json = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            return (upgrade is null
                    ? JsonSerializer.Deserialize(json, typeInfo)
                    : upgrade(JsonNode.Parse(json))?.Deserialize(typeInfo))
                ?? throw new RigException(400, $"{name} is empty.");
        } catch (JsonException e) {
            throw new RigException(400, $"{name} is not valid: {e.Message}");
        }
    }

    /// <summary>Copies at most <paramref name="limit" /> bytes, counting what is actually read (headers can lie).</summary>
    static async Task CopyLimitedAsync(
        Stream source,
        Stream target,
        long limit,
        string what,
        CancellationToken cancellationToken
    ) {
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0) {
            total += read;
            if (total > limit) {
                throw new RigException(413, $"{what} is larger than the {limit / (1024 * 1024)} MB limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    [GeneratedRegex("""^(manifest\.json|preset\.json|assets/[0-9a-f]{64}\.(wav|nam|aidax))$""")]
    private static partial Regex AllowedName();

    /// <summary>A write-only stream that feeds a hash, so an asset can be verified without storing it.</summary>
    sealed class HashingSink(IncrementalHash hash) : Stream {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) {
            hash.AppendData(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
