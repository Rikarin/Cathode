using Cathode.Contracts;
using Cathode.Server.Api;
using Cathode.Server.Rig;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Cathode.Server.Stores;

/// <summary>
///     The whole library as one file (<c>.cthbackup</c>), to move a rig to another machine or to keep a copy: a zip of
///     <c>manifest.json</c>, the global settings (<c>global.json</c>), the user's presets, block presets, templates,
///     snapshots, setlists and banks and the marks on factory presets (one JSON file each, in a folder per kind, named
///     by id), and every asset as <c>assets/&lt;sha256&gt;.&lt;wav|cabgrid|nam|aidax&gt;</c> with the asset index
///     (<c>assets.json</c>: names, colours, favourites). Factory content is not in it: every Cathode has its own.
/// </summary>
/// <remarks>
///     A restore adds to the library: an entry with the id of one already there replaces it, everything else stays,
///     and the global settings are replaced. Like a preset package, the file is treated as hostile: only those names
///     are accepted, nothing is extracted to a path taken from the zip, sizes are enforced while reading, every asset
///     must hash to its name, and every entry goes in through its store, which checks it. The whole backup is read
///     before anything is written, so a file that is not a backup, or was made by a newer Cathode, changes nothing.
/// </remarks>
public sealed partial class LibraryBackup(
    PresetStore presets,
    BlockPresetStore blockPresets,
    Library library,
    SnapshotStore snapshots,
    TemplateStore templates,
    AssetStore assets,
    GlobalStore globals
) : IDisposable {
    public const string Extension = ".cthbackup";
    public const string Format = "cathode-backup";
    public const int Version = 1;

    /// <summary>The largest backup a restore takes: a library of captures and IRs runs to gigabytes.</summary>
    public const long MaxBackupBytes = 16L * 1024 * 1024 * 1024;

    const int MaxEntries = 100_000;
    const string ManifestName = "manifest.json";
    const string GlobalName = "global.json";
    const string AssetIndexName = "assets.json";
    const string Presets = "presets";
    const string FactoryMarks = "factory-marks";
    const string BlockPresets = "block-presets";
    const string Templates = "templates";
    const string Snapshots = "snapshots";
    const string Setlists = "setlists";
    const string Banks = "banks";

    /// <summary>One restore at a time: two would interleave their writes.</summary>
    readonly SemaphoreSlim gate = new(1, 1);

    public void Dispose() => gate.Dispose();

    static string App =>
        typeof(LibraryBackup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "dev";

    /// <summary>Writes the library as it is now to <paramref name="output" />.</summary>
    public async Task WriteAsync(Stream output, CancellationToken cancellationToken) {
        var context = ContractsJsonContext.Default;
        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
        await AddAsync(
            zip,
            ManifestName,
            new(Format, Version, DateTimeOffset.UtcNow, App),
            context.BackupManifest,
            cancellationToken
        );
        await AddAsync(zip, GlobalName, globals.Get(), context.GlobalSettings, cancellationToken);
        foreach (var summary in presets.User()) {
            await AddAsync(zip, Presets, summary.Id, presets.Get(summary.Id), context.Preset, cancellationToken);
        }

        foreach (var mark in presets.Marks()) {
            await AddAsync(zip, FactoryMarks, mark.Id, mark, context.FactoryPresetMark, cancellationToken);
        }

        foreach (var preset in blockPresets.List().Where(static p => !p.Factory)) {
            await AddAsync(zip, BlockPresets, preset.Id, preset, context.BlockPreset, cancellationToken);
        }

        foreach (var summary in templates.List()) {
            await AddAsync(zip, Templates, summary.Id, templates.Get(summary.Id), context.Preset, cancellationToken);
        }

        foreach (var summary in snapshots.List()) {
            await AddAsync(
                zip,
                Snapshots,
                summary.Id,
                snapshots.Get(summary.Id),
                context.PresetSnapshot,
                cancellationToken
            );
        }

        foreach (var setlist in library.Setlists()) {
            await AddAsync(zip, Setlists, setlist.Id, setlist, context.Setlist, cancellationToken);
        }

        foreach (var bank in library.Banks().Where(static b => b.Id != FactoryPresets.BankId)) {
            await AddAsync(zip, Banks, bank.Id, bank, context.Bank, cancellationToken);
        }

        // The user's assets: every Cathode has the factory ones itself.
        var stored = assets.List().Where(static a => a.Factory != true).ToList();
        await AddAsync(zip, AssetIndexName, [..stored], context.AssetInfoArray, cancellationToken);
        foreach (var asset in stored) {
            await AddAssetAsync(zip, asset, cancellationToken);
        }
    }

    /// <summary>
    ///     Reads a backup from <paramref name="input" /> and puts what it holds into the library. Refused as a whole
    ///     (400, 409, 413), with nothing written, when it is not a backup this Cathode reads.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(Stream input, CancellationToken cancellationToken) {
        // ZipArchive needs a seekable stream; buffer to a temporary file, enforcing the size limit.
        var temporary = Path.GetTempFileName();
        try {
            await using (var file = File.Create(temporary)) {
                await PresetPackage.CopyLimitedAsync(input, file, MaxBackupBytes, "The backup", cancellationToken);
            }

            await using var stream = File.OpenRead(temporary);
            ZipArchive zip;
            try {
                zip = new(stream, ZipArchiveMode.Read);
            } catch (InvalidDataException) {
                throw new RigException(400, $"This is not a {Extension} backup (not a zip file).");
            }

            await using (zip) {
                await gate.WaitAsync(cancellationToken);
                try {
                    return await RestoreAsync(zip, cancellationToken);
                } finally {
                    gate.Release();
                }
            }
        } finally {
            File.Delete(temporary);
        }
    }

    async Task<RestoreResult> RestoreAsync(ZipArchive zip, CancellationToken cancellationToken) {
        // Everything is read before anything is written.
        var context = ContractsJsonContext.Default;
        var manifest = await ReadManifestAsync(zip, cancellationToken);
        var settings = zip.GetEntry(GlobalName) is null
            ? null
            : GlobalStore.Upgrade(
                await PresetPackage.ReadJsonAsync(
                    zip,
                    GlobalName,
                    context.GlobalSettings,
                    cancellationToken,
                    GlobalStore.UpgradeJson
                )
            );
        var index = zip.GetEntry(AssetIndexName) is null
            ? []
            : await PresetPackage.ReadJsonAsync(zip, AssetIndexName, context.AssetInfoArray, cancellationToken);
        var readPresets = await ReadAsync(zip, Presets, context.Preset, PresetMigrations.Upgrade, cancellationToken);
        var readMarks = await ReadAsync(zip, FactoryMarks, context.FactoryPresetMark, null, cancellationToken);
        var readBlockPresets = await ReadAsync(
            zip,
            BlockPresets,
            context.BlockPreset,
            BlockPresetStore.Upgrade,
            cancellationToken
        );
        var readTemplates = await ReadAsync(
            zip,
            Templates,
            context.Preset,
            PresetMigrations.Upgrade,
            cancellationToken
        );
        var readSnapshots = await ReadAsync(
            zip,
            Snapshots,
            context.PresetSnapshot,
            PresetMigrations.UpgradeSnapshot,
            cancellationToken
        );
        var readSetlists = await ReadAsync(zip, Setlists, context.Setlist, null, cancellationToken);
        var readBanks = await ReadAsync(zip, Banks, context.Bank, null, cancellationToken);
        var newer = readPresets.Concat(readTemplates)
            .Select(static e => e.Item)
            .Concat(readSnapshots.Select(static e => e.Item.Preset))
            .FirstOrDefault(static p => p is not null && p.SchemaVersion > Preset.CurrentSchemaVersion);
        if (newer is not null) {
            throw RigException.Conflict(
                $"The backup was made by a newer version of Cathode ({manifest.App}): preset '{newer.Name}' has "
                + $"schema {newer.SchemaVersion}."
            );
        }

        var packaged = await PresetPackage.VerifiedAssetsAsync(zip, cancellationToken);

        // Assets first, then presets, then what points at presets.
        var skipped = new List<string>();
        var addedAssets = await AddAssetsAsync(packaged, index, skipped, cancellationToken);

        var restoredPresets = Write(
            readPresets,
            "Preset",
            static p => p.Name,
            (id, preset) => {
                preset.Id = id;
                presets.Save(preset);
            },
            skipped
        );
        foreach (var (id, mark) in readMarks.Where(e => presets.IsFactory(e.Id))) {
            presets.Mark(id, mark.Color, mark.Favorite); // a mark on a factory preset this Cathode lacks is dropped
        }

        var restoredBlockPresets = Write(
            readBlockPresets,
            "Block preset",
            static p => p.Name,
            (id, preset) => blockPresets.Save(id, preset),
            skipped
        );
        var restoredTemplates = Write(
            readTemplates,
            "Template",
            static p => p.Name,
            (id, template) => {
                template.Id = id;
                templates.Restore(template);
            },
            skipped
        );
        var restoredSnapshots = Write(
            readSnapshots,
            "Snapshot",
            static s => s.Name,
            (id, snapshot) => {
                snapshot.Id = id;
                snapshot.Preset ??= new();
                snapshots.Restore(snapshot);
            },
            skipped
        );
        var restoredBanks = Write(
            readBanks.Where(static e => e.Id != FactoryPresets.BankId),
            "Bank",
            static b => b.Name,
            (id, bank) => {
                bank.Id = id;
                bank.Slots ??= [];
                library.Save(bank);
            },
            skipped
        );
        var restoredSetlists = Write(
            readSetlists,
            "Setlist",
            static s => s.Name,
            (id, setlist) => {
                setlist.Id = id;
                setlist.Entries = [..(setlist.Entries ?? []).Where(static e => e is not null)];
                library.Save(setlist);
            },
            skipped
        );
        return new(
            restoredPresets,
            restoredBlockPresets,
            restoredTemplates,
            restoredSnapshots,
            restoredSetlists,
            restoredBanks,
            addedAssets,
            settings is not null && RestoreSettings(settings, skipped),
            skipped
        );
    }

    /// <summary>
    ///     The backup's manifest, once the zip is known to hold a backup's files and nothing else: one of this format,
    ///     no newer than this Cathode reads.
    /// </summary>
    static async Task<BackupManifest> ReadManifestAsync(ZipArchive zip, CancellationToken cancellationToken) {
        if (zip.Entries.Count > MaxEntries) {
            throw new RigException(400, $"A backup holds at most {MaxEntries} files.");
        }

        var unexpected = zip.Entries.Where(static e => !IsAllowed(e.FullName))
            .Select(static e => e.FullName)
            .Take(20)
            .ToList();
        if (unexpected.Count > 0) {
            throw new RigException(400, "The backup contains unexpected files.", unexpected);
        }

        var manifest = await PresetPackage.ReadJsonAsync(
            zip,
            ManifestName,
            ContractsJsonContext.Default.BackupManifest,
            cancellationToken
        );
        return manifest.Format == Format && manifest.Version <= Version
            ? manifest
            : throw new RigException(
                400,
                $"Unsupported backup format '{manifest.Format}' version {manifest.Version}."
            );
    }

    /// <summary>
    ///     Adds the backup's assets the library lacks, then gives every asset the backup's index describes its name,
    ///     colour and favourite flag from there. Returns how many were added.
    /// </summary>
    async Task<int> AddAssetsAsync(
        Dictionary<string, ZipArchiveEntry> packaged,
        AssetInfo[] index,
        List<string> skipped,
        CancellationToken cancellationToken
    ) {
        var added = 0;
        foreach (var (_, entry) in packaged.Where(p => assets.Get(p.Key) is null)) {
            try {
                await using var content = await entry.OpenAsync(cancellationToken);
                await assets.AddAsync(content, entry.Name, cancellationToken);
                added++;
            } catch (RigException e) {
                skipped.Add(Reason($"Asset {entry.Name}", e));
            }
        }

        foreach (var recorded in index.Where(static a => a is { Ref: not null, Name: not null })) {
            assets.Describe(recorded);
        }

        return added;
    }

    /// <summary>
    ///     Replaces the global settings as a <c>PUT /global</c> would; false (with the reason) if invalid.
    /// </summary>
    bool RestoreSettings(GlobalSettings settings, List<string> skipped) {
        try {
            GlobalValidation.Check(settings);
        } catch (RigException e) {
            skipped.Add(Reason("Global settings", e));
            return false;
        }

        globals.Update(g => ControlsApi.Replace(g, settings));
        return true;
    }

    /// <summary>
    ///     Stores each entry through <paramref name="save" />; one its store refuses is left out, with the reason in
    ///     <paramref name="skipped" />. Returns how many were stored.
    /// </summary>
    static int Write<T>(
        IEnumerable<(string Id, T Item)> entries,
        string kind,
        Func<T, string?> nameOf,
        Action<string, T> save,
        List<string> skipped
    ) {
        var written = 0;
        foreach (var (id, item) in entries) {
            try {
                save(id, item);
                written++;
            } catch (RigException e) {
                skipped.Add(Reason($"{kind} '{(nameOf(item) is { Length: > 0 } name ? name : id)}'", e));
            }
        }

        return written;
    }

    static string Reason(string what, RigException e) =>
        e.Errors.Count > 0 ? $"{what}: {e.Message} {string.Join(" ", e.Errors)}" : $"{what}: {e.Message}";

    /// <summary>The entries of <paramref name="folder" />, each with the id its file is named by.</summary>
    static async Task<List<(string Id, T Item)>> ReadAsync<T>(
        ZipArchive zip,
        string folder,
        JsonTypeInfo<T> typeInfo,
        Func<JsonNode?, JsonNode?>? upgrade,
        CancellationToken cancellationToken
    )
        where T : class {
        var prefix = folder + "/";
        var entries = new List<(string, T)>();
        foreach (var name in zip.Entries.Select(static e => e.FullName)
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                     .Distinct(StringComparer.Ordinal)) {
            entries.Add(
                (
                    Path.GetFileNameWithoutExtension(name),
                    await PresetPackage.ReadJsonAsync(zip, name, typeInfo, cancellationToken, upgrade)
                )
            );
        }

        return entries;
    }

    static Task AddAsync<T>(
        ZipArchive zip,
        string name,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    ) =>
        PresetPackage.WriteJsonAsync(
            zip,
            name,
            JsonSerializer.SerializeToUtf8Bytes(value, typeInfo),
            cancellationToken
        );

    /// <summary>
    ///     Adds <c>&lt;folder&gt;/&lt;id&gt;.json</c>. An entry that went away since it was listed, or whose id is not
    ///     a file name (a file edited by hand), is left out.
    /// </summary>
    static Task AddAsync<T>(
        ZipArchive zip,
        string folder,
        string id,
        T? value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
        where T : class =>
        value is null || !StoreIds.IsValid(id)
            ? Task.CompletedTask
            : AddAsync(zip, $"{folder}/{id}.json", value, typeInfo, cancellationToken);

    async Task AddAssetAsync(ZipArchive zip, AssetInfo asset, CancellationToken cancellationToken) {
        FileStream source;
        try {
            source = File.OpenRead(assets.PathOf(asset.Ref) ?? string.Empty);
        } catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or ArgumentException) {
            return; // deleted since it was listed, or its file is gone: the index entry alone restores nothing
        }

        await using (source) {
            // Fastest: captures and IRs are large, and a backup of gigabytes should not take minutes.
            var entry = zip.CreateEntry(
                $"assets/{asset.Ref[7..]}{AssetStore.Extension(asset.Kind)}",
                CompressionLevel.Fastest
            );
            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    /// <summary>
    ///     The manifest, the settings, the asset index, entries named by a store id in their kind's folder, and assets
    ///     named by their hash with the extension the store gives their kind.
    /// </summary>
    static bool IsAllowed(string name) =>
        AllowedName().IsMatch(name)
        && (!name.StartsWith("assets/", StringComparison.Ordinal)
            || (AssetStore.KindOf(name) is { } kind && AssetStore.Extension(kind) == Path.GetExtension(name)));

    [GeneratedRegex(
        """^(manifest\.json|global\.json|assets\.json|assets/[0-9a-f]{64}\.[a-z]+|"""
        + """(presets|factory-marks|block-presets|templates|snapshots|setlists|banks)/[A-Za-z0-9-]{1,64}\.json)\z"""
    )]
    private static partial Regex AllowedName();
}
