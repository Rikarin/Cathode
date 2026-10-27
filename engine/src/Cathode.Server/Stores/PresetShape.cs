using Cathode.Contracts;
using Cathode.Server.Rig;

namespace Cathode.Server.Stores;

/// <summary>
///     The shape every preset must have before it is stored or loaded: the JSON reader takes <c>"blocks": null</c> or
///     three scenes as well as a whole preset, and the rig indexes all eight scenes and walks every list. The grid
///     must be one the editor can show and the engine can report on in full.
/// </summary>
public static class PresetShape {
    /// <summary>
    ///     Replaces missing lists and maps with empty ones and drops null entries, then refuses (400) what cannot be
    ///     repaired: a scene count other than <see cref="Preset.SceneCount" />, a grid of the wrong shape
    ///     (<see cref="GridProblems" />) or more than <see cref="Preset.MaxModifiers" /> modifiers.
    /// </summary>
    public static void Check(Preset preset) {
        preset.Tags = [..(preset.Tags ?? []).Where(static t => t is not null)];
        preset.Blocks = [..(preset.Blocks ?? []).Where(static b => b is not null)];
        preset.Cables = [..(preset.Cables ?? []).Where(static c => c is not null)];
        preset.Scenes = [..(preset.Scenes ?? []).Where(static s => s is not null)];
        preset.Modifiers = [..(preset.Modifiers ?? []).Where(static m => m is not null)];
        preset.Controllers ??= [];
        preset.Perform = [..(preset.Perform ?? []).Where(static p => p is not null)];
        preset.FootswitchOverrides = [..(preset.FootswitchOverrides ?? []).Where(static f => f is not null)];
        foreach (var block in preset.Blocks) {
            block.Channels = (block.Channels ?? []).Where(static c => c.Value is not null)
                .ToDictionary(static c => c.Key, static c => c.Value);
            block.Assets = (block.Assets ?? []).Where(static a => a.Value is not null)
                .ToDictionary(static a => a.Key, static a => a.Value);
            block.SceneIgnore ??= [];
        }

        foreach (var scene in preset.Scenes) {
            scene.Blocks = (scene.Blocks ?? []).Where(static b => b.Value is not null)
                .ToDictionary(static b => b.Key, static b => b.Value);
            scene.Controllers ??= [0, 0, 0, 0];
            scene.Midi = [..(scene.Midi ?? []).Where(static m => m is not null)];
        }

        if (preset.Scenes.Count != Preset.SceneCount) {
            throw new RigException(400, $"A preset has exactly {Preset.SceneCount} scenes.");
        }

        var problems = GridProblems(preset);
        if (preset.Modifiers.Count > Preset.MaxModifiers) {
            problems.Add($"{preset.Modifiers.Count} modifiers: a preset holds at most {Preset.MaxModifiers}.");
        }

        if (problems.Count > 0) {
            throw new RigException(400, "The preset is not valid.", problems);
        }
    }

    /// <summary>
    ///     What is wrong with the grid: its size (1 to <see cref="Preset.MaxRows" /> rows and 1 to
    ///     <see cref="Preset.MaxColumns" /> columns), blocks without an id or type, with a duplicate id, outside the
    ///     grid or sharing a cell, and cables between blocks that do not exist, not from one column to the next, from
    ///     an output, into an input, or twice. The engine's compiler checks the same grid rules; here a client's
    ///     document is refused with a 400 before it is stored or replaces the working copy.
    /// </summary>
    public static List<string> GridProblems(Preset preset) {
        if (preset.Rows is < 1 or > Preset.MaxRows || preset.Columns is < 1 or > Preset.MaxColumns) {
            // Every block would be reported against a grid that cannot be.
            return [
                $"The grid is {preset.Rows}x{preset.Columns}: it has 1 to {Preset.MaxRows} rows "
                + $"and 1 to {Preset.MaxColumns} columns."
            ];
        }

        var problems = new List<string>();
        var byId = BlockProblems(preset, problems);
        CableProblems(preset, byId, problems);
        return problems;
    }

    /// <summary>Adds the blocks' problems to <paramref name="problems" />; returns them by id (first wins).</summary>
    static Dictionary<string, PresetBlock> BlockProblems(Preset preset, List<string> problems) {
        var byId = new Dictionary<string, PresetBlock>(StringComparer.Ordinal);
        var cells = new Dictionary<(int, int), string>();
        foreach (var block in preset.Blocks) {
            if (string.IsNullOrWhiteSpace(block.Id)) {
                problems.Add($"A block at ({block.Row},{block.Column}) has no id.");
                continue;
            }

            if (!byId.TryAdd(block.Id, block)) {
                problems.Add($"Two blocks have the id '{block.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(block.Type)) {
                problems.Add($"Block '{block.Id}' has no type.");
            }

            if (block.Row < 0 || block.Row >= preset.Rows || block.Column < 0 || block.Column >= preset.Columns) {
                problems.Add(
                    $"Block '{block.Id}' at ({block.Row},{block.Column}) is outside the "
                    + $"{preset.Rows}x{preset.Columns} grid."
                );
            } else if (!cells.TryAdd((block.Row, block.Column), block.Id)) {
                problems.Add(
                    $"Block '{block.Id}' shares cell ({block.Row},{block.Column}) with "
                    + $"'{cells[(block.Row, block.Column)]}'."
                );
            }
        }

        return byId;
    }

    static void CableProblems(Preset preset, Dictionary<string, PresetBlock> byId, List<string> problems) {
        var cables = new HashSet<(string, string)>();
        foreach (var cable in preset.Cables) {
            var name = $"Cable '{cable.From}' -> '{cable.To}'";
            if (byId.GetValueOrDefault(cable.From ?? string.Empty) is not { } from
                || byId.GetValueOrDefault(cable.To ?? string.Empty) is not { } to) {
                problems.Add($"{name} refers to a block that does not exist.");
            } else if (to.Column != from.Column + 1) {
                problems.Add($"{name} does not run from one column to the next (shunts bridge gaps).");
            } else if (BuiltInTypes.IsOutput(from.Type)) {
                problems.Add($"{name} starts at an output.");
            } else if (to.Type == BuiltInTypes.Input) {
                problems.Add($"{name} ends at an input.");
            } else if (!cables.Add((from.Id, to.Id))) {
                problems.Add($"{name} is there twice.");
            }
        }
    }
}
