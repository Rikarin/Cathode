using Cathode.Contracts;
using Cathode.Server.Stores;

namespace Cathode.Server.Rig;

/// <summary>
///     Preset next/previous in bank order, as a foot controller steps through its numbered presets: the slots of the
///     current bank, then on into the next bank (the banks in the library's order, the factory bank first), wrapping
///     from the last to the first. Presets in no bank come after the banks, by name, so every preset is reachable.
///     While a setlist is active (the preset was loaded from one of its entries and is still loaded), the steps go
///     through that setlist's entries instead, wrapping within it, each on its entry's scene.
/// </summary>
/// <remarks>
///     Where the loaded preset stands is the bank slot or setlist entry it was loaded from
///     (<see cref="LiveState.Position" />); loaded by name, it stands at its first slot in bank order. A preset in no
///     bank and not saved yet stands nowhere: next is the first preset, previous the last.
///     Song next/previous (<see cref="StepSongAsync" />) are separate: they go through the active setlist kept in the
///     global settings, whatever preset is loaded meanwhile, and survive a restart.
/// </remarks>
public sealed class PresetNavigator(Library library, PresetStore presets, LiveRig rig, GlobalStore globals) {
    /// <summary>What a step of <paramref name="step" /> (1 next, -1 previous) would load (null: no presets).</summary>
    public async Task<PresetStep?> NeighbourAsync(int step) {
        var state = await rig.StateAsync();
        return Neighbour(state.Preset.Id, state.Position, step);
    }

    /// <summary>Loads the next (1) or previous (-1) preset; null, and nothing loaded, with no presets.</summary>
    public async Task<LiveState?> StepAsync(int step) =>
        await NeighbourAsync(step) is { } target
            ? await rig.LoadAsync(target.PresetId, target.Scene, target.Position)
            : null;

    /// <summary>Loads a bank slot, remembering it, so next/previous go on from that slot.</summary>
    public Task<LiveState> LoadSlotAsync(string bank, int slot) =>
        rig.LoadAsync(library.Slot(bank, slot), null, new(bank, slot));

    /// <summary>
    ///     Loads a setlist entry on its scene, remembering it, so next/previous go through the setlist; the setlist
    ///     becomes the active one (<see cref="GlobalSettings.ActiveSetlist" />), at that song.
    /// </summary>
    public async Task<LiveState> LoadEntryAsync(string setlist, int index) {
        var entry = library.Entry(setlist, index);
        var state = await rig.LoadAsync(entry.PresetId, entry.Scene, new(Setlist: setlist, Entry: index));
        var active = globals.Get();
        if (active.ActiveSetlist != setlist || active.ActiveSong != index) {
            globals.Update(g => {
                    g.ActiveSetlist = setlist;
                    g.ActiveSong = index;
                }
            );
        }

        return state;
    }

    /// <summary>
    ///     Loads the next (1) or previous (-1) song of the active setlist; null, and nothing loaded, with no setlist
    ///     active or no song that way (song steps stop at either end of the set rather than wrapping round).
    /// </summary>
    public async Task<LiveState?> StepSongAsync(int step) {
        var settings = globals.Get();
        if (settings.ActiveSetlist is not { } id || library.Setlist(id) is not { } setlist) {
            return null;
        }

        return SongStep(setlist, settings.ActiveSong, step, presets.Exists) is { } index
            ? await LoadEntryAsync(id, index)
            : null;
    }

    /// <summary>
    ///     The entry a song step of <paramref name="step" /> goes to from entry <paramref name="from" /> (−1: before
    ///     the first), passing over entries whose preset is missing; null at either end.
    /// </summary>
    public static int? SongStep(Setlist setlist, int from, int step, Func<string, bool> exists) {
        var count = setlist.Entries.Count;
        var direction = Math.Sign(step);
        for (var i = Math.Clamp(from, -1, count) + direction; direction != 0 && i >= 0 && i < count; i += direction) {
            if (exists(setlist.Entries[i].PresetId)) {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    ///     The preset <paramref name="step" /> places on from <paramref name="presetId" /> at <paramref name="at" />.
    /// </summary>
    public PresetStep? Neighbour(string presetId, PresetPosition? at, int step) {
        var (stops, index) = Stops(presetId, at);
        if (stops.Count == 0) {
            return null;
        }

        // Standing nowhere: next starts from before the first, previous from after the last.
        var from = index >= 0 ? index : step > 0 ? -1 : stops.Count;
        return stops[(((from + step) % stops.Count) + stops.Count) % stops.Count];
    }

    /// <summary>The order to step through, and where the loaded preset stands in it (-1: nowhere).</summary>
    (List<PresetStep> Stops, int Index) Stops(string presetId, PresetPosition? at) {
        if (at is { Setlist: { } setlistId, Entry: { } entry }
            && library.Setlist(setlistId) is { } setlist
            && entry >= 0
            && entry < setlist.Entries.Count
            && setlist.Entries[entry].PresetId == presetId) {
            List<PresetStep> entries = [
                ..setlist.Entries.Select((e, i) =>
                    new PresetStep(e.PresetId, e.Scene, new(Setlist: setlistId, Entry: i))
                )
            ];
            return (entries, entry);
        }

        var stops = new List<PresetStep>();
        var banked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bank in library.Banks()) {
            for (var slot = 0; slot < bank.Slots.Count; slot++) {
                if (bank.Slots[slot] is { } id) {
                    stops.Add(new(id, null, new(bank.Id, slot)));
                    banked.Add(id);
                }
            }
        }

        stops.AddRange(
            presets.List()
                .Where(p => !banked.Contains(p.Id))
                .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static p => new PresetStep(p.Id))
        );
        var index = at is { Bank: { } bankId, Slot: { } atSlot }
            ? stops.FindIndex(s => s.PresetId == presetId && s.Position?.Bank == bankId && s.Position.Slot == atSlot)
            : -1;
        return (stops, index >= 0 ? index : stops.FindIndex(s => s.PresetId == presetId));
    }
}
