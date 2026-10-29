using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Stores;

/// <summary>
///     Setlists and banks on top of the preset store, keeping references intact: setlists and banks may only point
///     at presets that exist, and a preset cannot be deleted while something still uses it. The factory bank
///     (<see cref="FactoryPresets.Bank" />) comes first and is read-only.
/// </summary>
/// <remarks>
///     A preset can still go missing behind Cathode's back (its file deleted or moved by hand): a setlist saved again
///     keeps an entry that already pointed at it, so the rest of the setlist stays editable, but gains no new one.
/// </remarks>
public sealed class Library(
    IOptions<ServerOptions> options,
    PresetStore presets,
    GlobalStore globals,
    ILogger<Library>? logger = null
) {
    readonly Lock gate = new();

    readonly JsonFileStore<Setlist> setlists = new(
        Path.Combine(options.Value.DataDirectory, "setlists"),
        "setlist",
        ContractsJsonContext.Default.Setlist,
        static s => s.Id,
        logger: logger
    );

    readonly JsonFileStore<Bank> banks = new(
        Path.Combine(options.Value.DataDirectory, "banks"),
        "bank",
        ContractsJsonContext.Default.Bank,
        static b => b.Id,
        logger: logger
    );

    public Setlist[] Setlists() =>
        [..setlists.All().Select(static e => e.Item).OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)];

    public Setlist? Setlist(string id) => setlists.Get(id);

    public Bank[] Banks() => [
        presets.Factory.Bank(),
        ..banks.All().Select(static e => e.Item).OrderBy(static b => b.Name, StringComparer.OrdinalIgnoreCase)
    ];

    public Bank? Bank(string id) => id == FactoryPresets.BankId ? presets.Factory.Bank() : banks.Get(id);

    public void Save(Setlist setlist) {
        lock (gate) {
            var problems = new List<string>();
            HashSet<string> kept = [..setlists.Get(setlist.Id)?.Entries.Select(static e => e.PresetId) ?? []];
            for (var i = 0; i < setlist.Entries.Count; i++) {
                var entry = setlist.Entries[i];
                if (!presets.Exists(entry.PresetId) && !kept.Contains(entry.PresetId)) {
                    problems.Add($"Entry {i + 1}: no preset '{entry.PresetId}'.");
                }

                if (entry.Scene is < 0 or >= Preset.SceneCount) {
                    problems.Add($"Entry {i + 1}: scenes are numbered 0 to {Preset.SceneCount - 1}.");
                }
            }

            Reject(problems, "setlist");
            setlists.Save(setlist);
        }
    }

    public void Save(Bank bank) {
        if (bank.Id == FactoryPresets.BankId) {
            throw RigException.Conflict("The factory bank cannot be changed; put its presets in a bank of your own.");
        }

        lock (gate) {
            var problems = new List<string>();
            if (bank.Slots.Count > Contracts.Bank.SlotCount) {
                problems.Add($"A bank has {Contracts.Bank.SlotCount} slots; this one has {bank.Slots.Count}.");
            }

            for (var slot = 0; slot < bank.Slots.Count; slot++) {
                if (bank.Slots[slot] is { } id && !presets.Exists(id)) {
                    problems.Add($"Slot {slot}: no preset '{id}'.");
                }
            }

            Reject(problems, "bank");
            bank.Factory = null;
            banks.Save(bank);
        }
    }

    /// <summary>Deletes a setlist; if it was the active one, no setlist is active any more.</summary>
    public bool DeleteSetlist(string id) {
        if (!setlists.Delete(id)) {
            return false;
        }

        if (globals.Get().ActiveSetlist == id) {
            globals.Update(g => {
                    if (g.ActiveSetlist == id) {
                        g.ActiveSetlist = null;
                        g.ActiveSong = -1;
                    }
                }
            );
        }

        return true;
    }

    public bool DeleteBank(string id) =>
        id == FactoryPresets.BankId
            ? throw RigException.Conflict("The factory bank cannot be deleted.")
            : banks.Delete(id);

    /// <summary>Deletes a preset unless a setlist or bank still uses it (409, naming them).</summary>
    public bool DeletePreset(string id) {
        if (FactoryPresets.IsFactoryId(id)) {
            throw RigException.Conflict("Factory presets cannot be deleted.");
        }

        lock (gate) {
            var users = Setlists()
                .Where(s => s.Entries.Exists(e => e.PresetId == id))
                .Select(static s => $"setlist '{s.Name}'")
                .Concat(
                    banks.All()
                        .Select(static e => e.Item)
                        .Where(b => b.Slots.Contains(id))
                        .Select(static b => $"bank '{b.Name}'")
                )
                .ToList();
            if (users.Count > 0) {
                throw new RigException(409, $"The preset is still used by {string.Join(", ", users)}.", users);
            }

            return presets.Delete(id);
        }
    }

    /// <summary>The preset and scene a setlist entry points at.</summary>
    public SetlistEntry Entry(string setlistId, int index) {
        var setlist = Setlist(setlistId) ?? throw RigException.NotFound($"No setlist '{setlistId}'.");
        return index >= 0 && index < setlist.Entries.Count
            ? setlist.Entries[index]
            : throw RigException.NotFound(
                $"Setlist '{setlist.Name}' has {setlist.Entries.Count} entries; there is no entry {index}."
            );
    }

    /// <summary>The preset in a bank slot.</summary>
    public string Slot(string bankId, int slot) {
        var bank = Bank(bankId) ?? throw RigException.NotFound($"No bank '{bankId}'.");
        return slot >= 0 && slot < bank.Slots.Count && bank.Slots[slot] is { } id
            ? id
            : throw RigException.NotFound($"Slot {slot} of bank '{bank.Name}' is empty.");
    }

    static void Reject(List<string> problems, string kind) {
        if (problems.Count > 0) {
            throw new RigException(400, $"The {kind} refers to things that do not exist.", problems);
        }
    }
}
