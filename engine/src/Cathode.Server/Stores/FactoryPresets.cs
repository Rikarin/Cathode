using Cathode.Contracts;
using Cathode.Dsp.Parameters;
using Cathode.Server.Rig;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cathode.Server.Stores;

/// <summary>
///     The factory bank: full presets that recreate well-known guitar tones, built into the Server and read-only, as
///     the factory Block Library is (<see cref="FactoryBlockPresets" />). They list, load and export like the user's
///     presets; saving one stores a copy, and they cannot be renamed or deleted. They are read on first use, not at
///     start-up, and each <see cref="Get" /> hands out a fresh copy.
/// </summary>
/// <remarks>
///     Each preset is a <c>.cthpreset</c> package in <c>src/Cathode.Server/Factory/presets</c>, as the editor exports
///     one (<see cref="PresetPackage" />, without assets), embedded in the Server. <c>Factory/bank.json</c> lists the
///     files in bank order, each with its scenes' lifts over the bank's loudness (<see cref="FactoryLevels" />). The
///     file name is the preset's id after <see cref="IdPrefix" />; the id, author and factory flag in the package are
///     replaced, so a preset exported from the editor replaces one as it is.
/// </remarks>
public sealed class FactoryPresets(BlockCatalog catalog) {
    public const string IdPrefix = "factory-";
    public const string BankId = "factory";
    public const string BankName = "Factory";
    public const string Author = "Cathode";

    /// <summary>The embedded bank list and the embedded preset files' prefix (the csproj's LogicalName).</summary>
    const string BankResource = "factory/bank.json";

    const string PresetResource = "factory/presets/";

    /// <summary>
    ///     The most a factory preset may cost, in percent of one core at 48 kHz: the sum of its blocks' measured costs
    ///     (<see cref="Dsp.BlockCosts" />, the editor's "Est. %"), with headroom left under the whole core.
    /// </summary>
    public const double CpuBudget = 80;

    /// <summary>When the bank was last revised: the time its presets list as modified.</summary>
    public static DateTimeOffset Revised { get; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    readonly Lazy<Contents> contents = new(() => Contents.Of(catalog));

    /// <summary>A bank slot: the preset's file (its id after <see cref="IdPrefix" />) and its scenes' lifts.</summary>
    public sealed record Entry(string File, float[] Lifts) {
        public string Id => IdPrefix + File;
    }

    /// <summary>What is wrong with the files (unknown ids, values out of range); empty when all is well.</summary>
    public IReadOnlyList<string> Problems => contents.Value.Problems;

    /// <summary>The bank's presets in bank order.</summary>
    public IReadOnlyList<PresetSummary> Summaries => contents.Value.Summaries;

    /// <summary>Whether <paramref name="id" /> is a factory id (shipped or not): users cannot take one.</summary>
    public static bool IsFactoryId(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public bool Contains(string id) => IsFactoryId(id) && contents.Value.Json.ContainsKey(id);

    /// <summary>A copy of a factory preset, the caller's to change.</summary>
    public Preset? Get(string id) =>
        IsFactoryId(id) && contents.Value.Json.TryGetValue(id, out var json)
            ? JsonSerializer.Deserialize(json, ContractsJsonContext.Default.Preset)
            : null;

    /// <summary>The factory bank: its presets in slot order.</summary>
    public Bank Bank() =>
        new() {
            Id = BankId, Name = BankName, Factory = true, Slots = [..contents.Value.Summaries.Select(static s => s.Id)]
        };

    /// <summary>The bank's slots in order, as <c>bank.json</c> lists them.</summary>
    public static IReadOnlyList<Entry> Entries() =>
        JsonNode.Parse(Read(BankResource) ?? throw new InvalidOperationException("No factory bank.json."))!["presets"]!
            .AsArray()
            .Select(static e => new Entry(
                    e!["file"]!.GetValue<string>(),
                    [..e["lifts"]!.AsArray().Select(static l => l!.GetValue<float>())]
                )
            )
            .ToList();

    /// <summary>
    ///     Reads every preset of the bank, for the bank and for the tests and the leveling that measure it. Every file
    ///     that is missing, unlisted or unreadable, every unknown block or parameter and every value out of range goes
    ///     into the problems.
    /// </summary>
    public static (List<Preset> Presets, List<string> Problems) Load(BlockCatalog catalog) {
        var problems = new List<string>();
        var presets = new List<Preset>();
        var entries = Entries();
        foreach (var entry in entries) {
            using var stream = Open(ResourceOf(entry));
            if (stream is null) {
                problems.Add($"{entry.File}: bank.json lists it, but there is no presets/{entry.File}.cthpreset.");
                continue;
            }

            try {
                var preset = Unpack(stream, entry);
                Validate(preset, entry, catalog, problems);
                presets.Add(preset);
            } catch (Exception e) when (e is JsonException
                                            or RigException
                                            or InvalidDataException
                                            or InvalidOperationException) {
                problems.Add($"{entry.File}: {e.Message}");
            }
        }

        var listed = entries.Select(ResourceOf).ToHashSet(StringComparer.Ordinal);
        foreach (var name in typeof(FactoryPresets).Assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(PresetResource, StringComparison.Ordinal) && !listed.Contains(n))) {
            problems.Add($"{name[PresetResource.Length..]}: not in bank.json, so not in the bank.");
        }

        foreach (var duplicate in entries.GroupBy(static e => e.File).Where(static g => g.Count() > 1)) {
            problems.Add($"bank.json lists '{duplicate.Key}' {duplicate.Count()} times.");
        }

        if (entries.Count > Contracts.Bank.SlotCount) {
            problems.Add($"{entries.Count} presets do not fit one bank of {Contracts.Bank.SlotCount} slots.");
        }

        return (presets, problems);
    }

    static string ResourceOf(Entry entry) => PresetResource + entry.File + PresetPackage.Extension;

    /// <summary>
    ///     A package's preset read as an import reads one (its manifest checked, upgraded to the current schema), as
    ///     the bank's own. A factory preset carries no assets: a package with any is refused.
    /// </summary>
    static Preset Unpack(Stream stream, Entry entry) {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Any(static e => e.FullName is not ("manifest.json" or "preset.json"))) {
            throw new InvalidDataException("a factory package holds only manifest.json and preset.json.");
        }

        var manifest = JsonSerializer.Deserialize(
            Text(zip, "manifest.json"),
            ContractsJsonContext.Default.PackageManifest
        );
        if (manifest?.Format != PresetPackage.Format || manifest.Version > PresetPackage.Version) {
            throw new InvalidDataException("not a .cthpreset package this version reads.");
        }

        var node = PresetMigrations.Upgrade(JsonNode.Parse(Text(zip, "preset.json")));
        var preset = node.Deserialize(ContractsJsonContext.Default.Preset)
            ?? throw new InvalidOperationException("the file is not a preset.");
        PresetShape.Check(preset);
        preset.Id = entry.Id;
        preset.Author = Author;
        preset.Factory = true;
        preset.Favorite = false;
        preset.Color = null;
        return preset;
    }

    /// <summary>Unknown block types and parameters, and values their parameters would not take as they are.</summary>
    static void Validate(Preset preset, Entry entry, BlockCatalog catalog, List<string> problems) {
        void Problem(string text) => problems.Add($"{entry.File}: {text}");

        if (entry.Lifts.Length > preset.Scenes.Count) {
            Problem($"bank.json gives {entry.Lifts.Length} lifts for {preset.Scenes.Count} scenes.");
        }

        foreach (var block in preset.Blocks) {
            if (block.Assets.Count > 0) {
                Problem($"{block.Id} uses assets; a factory preset may not.");
            }

            if (catalog.Descriptor(block.Type) is not { } descriptor) {
                if (catalog.Info(block.Type) is null) {
                    Problem($"no block type '{block.Type}' ({block.Id}).");
                }

                continue;
            }

            foreach (var (channel, values) in block.Channels) {
                ValidateChannel(block.Id, channel, values, descriptor, Problem);
            }
        }
    }

    static void ValidateChannel(
        string block,
        string channel,
        Dictionary<string, float> values,
        BlockDescriptor descriptor,
        Action<string> problem
    ) {
        foreach (var (id, value) in values) {
            var index = descriptor.IndexOf(id);
            if (index < 0) {
                problem($"{block} has no parameter '{id}'.");
            } else if (descriptor.Params[index].Sanitize(value) != value) {
                problem($"{block}.{id} on {channel}: {value} is out of range.");
            }
        }
    }

    static string Text(ZipArchive zip, string name) {
        using var reader = new StreamReader(
            (zip.GetEntry(name) ?? throw new InvalidDataException($"the package has no {name}.")).Open()
        );
        return reader.ReadToEnd();
    }

    static Stream? Open(string name) => typeof(FactoryPresets).Assembly.GetManifestResourceStream(name);

    static string? Read(string name) {
        using var stream = Open(name);
        if (stream is null) {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    sealed record Contents(
        IReadOnlyDictionary<string, string> Json,
        IReadOnlyList<PresetSummary> Summaries,
        IReadOnlyList<string> Problems
    ) {
        public static Contents Of(BlockCatalog catalog) {
            var (presets, problems) = Load(catalog);
            var json = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var preset in presets) {
                json.TryAdd(preset.Id, JsonSerializer.Serialize(preset, ContractsJsonContext.Default.Preset));
            }

            return new(
                json,
                [..presets.DistinctBy(static p => p.Id).Select(static p => PresetStore.Summary(p, Revised))],
                problems
            );
        }
    }
}
