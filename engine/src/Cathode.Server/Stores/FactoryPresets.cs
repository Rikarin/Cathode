using System.Text.Json;
using Cathode.Contracts;
using Cathode.Server.Rig;

namespace Cathode.Server.Stores;

/// <summary>
///     The factory bank: full presets that recreate well-known guitar tones (<see cref="FactoryRecipes" />), built into
///     the Server and read-only, as the factory Block Library is (<see cref="FactoryBlockPresets" />). They list, load
///     and export like the user's presets; saving one stores a copy, and they cannot be renamed or deleted. They are
///     built on first use, not at start-up, and each <see cref="Get" /> hands out a fresh copy.
/// </summary>
public sealed class FactoryPresets(BlockCatalog catalog) {
    public const string IdPrefix = "factory-";
    public const string BankId = "factory";
    public const string BankName = "Factory";
    public const string Author = "Cathode";

    /// <summary>
    ///     The most a factory preset may cost, in percent of one core at 48 kHz: the sum of its blocks' measured costs
    ///     (<see cref="Dsp.BlockCosts" />, the editor's "Est. %"), with headroom left under the whole core.
    /// </summary>
    public const double CpuBudget = 80;

    /// <summary>When the bank was last revised: the time its presets list as modified.</summary>
    public static DateTimeOffset Revised { get; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    readonly Lazy<Contents> contents = new(() => Contents.Of(catalog));

    /// <summary>What is wrong with the recipes (unknown ids, values out of range); empty when all is well.</summary>
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
            Id = BankId,
            Name = BankName,
            Factory = true,
            Slots = [..contents.Value.Summaries.Select(static s => s.Id)]
        };

    /// <summary>
    ///     Builds every recipe with the level trims <paramref name="trims" /> gives (the stored ones by default), for
    ///     the bank and for the tests and the leveling that measure it.
    /// </summary>
    public static (List<Preset> Presets, List<string> Problems) Build(
        BlockCatalog catalog,
        Func<string, FactoryLevels.Trim?>? trims = null
    ) {
        var problems = new List<string>();
        var presets = FactoryRecipes.All.Select(r => r.Build(catalog, (trims ?? FactoryLevels.Of)(r.Id), problems))
            .ToList();
        foreach (var duplicate in presets.GroupBy(static p => p.Id).Where(static g => g.Count() > 1)) {
            problems.Add($"{duplicate.Count()} presets have the id '{duplicate.Key}'.");
        }

        if (presets.Count > Contracts.Bank.SlotCount) {
            problems.Add($"{presets.Count} presets do not fit one bank of {Contracts.Bank.SlotCount} slots.");
        }

        return (presets, problems);
    }

    sealed record Contents(
        IReadOnlyDictionary<string, string> Json,
        IReadOnlyList<PresetSummary> Summaries,
        IReadOnlyList<string> Problems
    ) {
        public static Contents Of(BlockCatalog catalog) {
            var (presets, problems) = Build(catalog);
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
