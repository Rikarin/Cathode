using Cathode.Contracts;
using Cathode.Dsp.Blocks;

namespace Cathode.Server.Stores;

/// <summary>
///     The shape the global settings must have before they are stored or used. The JSON reader takes
///     <c>"outputs": [null]</c> or <c>"eq": null</c> as well as a whole document, and the rig walks every list and map
///     on each graph sync. What <see cref="PresetShape" /> is to a preset.
/// </summary>
public static class GlobalShape {
    /// <summary>
    ///     Replaces missing lists, maps and names with empty ones (or the default) and drops null entries. Values are
    ///     left as they are: <c>GlobalValidation.Check</c> judges those.
    /// </summary>
    public static void Repair(GlobalSettings settings) {
        var defaults = new GlobalSettings();
        settings.TempoMode ??= defaults.TempoMode;
        settings.Oversampling ??= defaults.Oversampling;
        settings.TunerOutput ??= defaults.TunerOutput;
        settings.TunerView ??= defaults.TunerView;
        settings.Tuning ??= defaults.Tuning;
        settings.Outputs = settings.Outputs is null ? defaults.Outputs : Present(settings.Outputs);
        settings.Inputs = settings.Inputs is null ? defaults.Inputs : Present(settings.Inputs);
        settings.Manual ??= defaults.Manual;
        settings.TunerOffsets ??= defaults.TunerOffsets;
        settings.MidiMappings = Present(settings.MidiMappings);
        settings.Footswitches = [
            ..Present(settings.Footswitches)
                .Select(static l => l with {
                        Switches = Present(l.Switches), Views = l.Views is null ? null : Present(l.Views)
                    }
                )
        ];
        settings.Blocks = Present(settings.Blocks);
        settings.Perform = Present(settings.Perform);
        foreach (var output in settings.Outputs) {
            output.Mode ??= new OutputSettings().Mode;
            output.Eq ??= [];
        }

        foreach (var block in settings.Blocks) {
            block.Name ??= string.Empty;
            block.Type ??= string.Empty;
            block.Channels = (block.Channels ?? []).Where(static c => c.Value is not null)
                .ToDictionary(static c => c.Key, static c => c.Value);
            block.Assets = (block.Assets ?? []).Where(static a => a.Value is not null)
                .ToDictionary(static a => a.Key, static a => a.Value);
        }
    }

    /// <summary>
    ///     <see cref="Repair" />, and then what a file may hold that a <c>PUT /global</c> would refuse and that would
    ///     otherwise stop the Server or every later save: a startup preset or active setlist whose id cannot be an id
    ///     is forgotten, and an output's EQ keeps only the Graphic EQ's own parameters, within their ranges.
    /// </summary>
    public static void RepairStored(GlobalSettings settings) {
        Repair(settings);
        if (settings.StartupPreset is { } preset && !StoreIds.IsValid(preset)) {
            settings.StartupPreset = null;
        }

        if (settings.ActiveSetlist is { } setlist && !StoreIds.IsValid(setlist)) {
            settings.ActiveSetlist = null;
            settings.ActiveSong = -1;
        }

        foreach (var output in settings.Outputs) {
            var eq = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var param in GraphicEqBlock.Info.Params) {
                if (output.Eq.TryGetValue(param.Id, out var value) && float.IsFinite(value)) {
                    eq[param.Id] = Math.Clamp(value, param.Min, param.Max);
                }
            }

            output.Eq = eq;
        }
    }

    static List<T> Present<T>(List<T>? items)
        where T : class => [..(items ?? []).Where(static i => i is not null)];
}
