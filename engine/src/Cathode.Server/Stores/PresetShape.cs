using Cathode.Contracts;
using Cathode.Server.Rig;

namespace Cathode.Server.Stores;

/// <summary>
///     The shape every preset must have before it is stored or loaded: the JSON reader takes <c>"blocks": null</c> or
///     three scenes as well as a whole preset, and the rig indexes all eight scenes and walks every list.
/// </summary>
public static class PresetShape {
    /// <summary>
    ///     Replaces missing lists and maps with empty ones and drops null entries, then refuses (400) what cannot be
    ///     repaired: a scene count other than <see cref="Preset.SceneCount" />.
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
    }
}
