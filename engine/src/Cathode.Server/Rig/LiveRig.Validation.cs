using Cathode.Contracts;

namespace Cathode.Server.Rig;

/// <summary>
///     The checks every operation makes before it edits the working copy: the expected revision, and that a cell,
///     channel, scene or scene's MIDI messages exist and are valid. Each throws a <see cref="RigException" /> with the
///     HTTP status the API answers with.
/// </summary>
public sealed partial class LiveRig {
    void Check(long? expected) {
        if (expected is { } value && value != revision) {
            throw new RigException(
                412,
                $"The rig changed (revision {revision}, expected {value}). Reload and try again."
            );
        }
    }

    /// <summary>
    ///     An edit made on an earlier load of a preset than the working copy's (see <see cref="LiveState.Load" />).
    /// </summary>
    void CheckLoad(long? loaded) {
        if (loaded is { } value && value != load) {
            throw new RigException(
                412,
                $"Another preset was loaded (load {load}, expected {value}); the change was made on the one before."
            );
        }
    }

    /// <summary>A gesture id is the client's own token: anything short.</summary>
    static void CheckGesture(string? gesture) {
        if (gesture is { Length: 0 or > MaxGestureLength }) {
            throw new RigException(400, $"A gesture id is 1 to {MaxGestureLength} characters.");
        }
    }

    void CheckCell(int row, int column) {
        if (row < 0 || row >= preset.Rows || column < 0 || column >= preset.Columns) {
            throw new RigException(400, $"Cell ({row},{column}) is outside the {preset.Rows}x{preset.Columns} grid.");
        }
    }

    static void CheckChannel(string channel, int channels) {
        var index = Preset.ChannelNames.IndexOf(channel);
        if (index < 0 || index >= channels) {
            throw new RigException(
                400,
                $"Channel '{channel}' does not exist; this block has "
                + $"{string.Join(", ", Preset.ChannelNames[..channels])}."
            );
        }
    }

    static void CheckSceneMidi(List<SceneMidiMessage> messages) {
        if (messages.Count > SceneMidiMessage.MaxPerScene) {
            throw new RigException(400, $"A scene sends at most {SceneMidiMessage.MaxPerScene} MIDI messages.");
        }

        if (messages.Select(static m => m.Problem()).FirstOrDefault(static p => p is not null) is { } problem) {
            throw new RigException(400, problem);
        }
    }

    /// <summary>A tempo as the rig takes it: 30 to 300 BPM.</summary>
    static double ClampTempo(double bpm) => Math.Clamp(bpm, 30, 300);

    /// <summary>A scene level as the rig takes it: -60 to +12 dB.</summary>
    static float ClampSceneLevel(float db) => Math.Clamp(db, -60f, 12f);

    /// <summary>
    ///     Holds the parts of a whole preset a client sent (<see cref="PutAsync" />) that have no check of their own
    ///     there to what their endpoints take. Brought into range: the tempo, every scene's level and its four
    ///     controllers, and each block's channel values (ids the block type lacks are dropped, as a block preset's
    ///     are). Scene entries of blocks the preset does not have are dropped, as removing a block drops them, and a
    ///     built-in block is not bypassed. Refused (400), with every problem: a scene's MIDI messages
    ///     (<see cref="CheckSceneMidi" />) and a scene channel its block does not have.
    /// </summary>
    void SanitizeContent(Preset next) {
        next.Tempo = ClampTempo(next.Tempo);
        foreach (var block in next.Blocks) {
            if (catalog.Descriptor(block.Type) is { } descriptor) {
                block.Channels = block.Channels.ToDictionary(
                    static c => c.Key,
                    c => descriptor.Sanitized(c.Value),
                    StringComparer.Ordinal
                );
            }
        }

        var problems = new List<string>();
        for (var index = 0; index < next.Scenes.Count; index++) {
            var scene = next.Scenes[index];
            scene.Level = ClampSceneLevel(scene.Level);
            var controllers = scene.Controllers;
            scene.Controllers = [
                ..Enumerable.Range(0, 4).Select(i => i < controllers.Count ? Math.Clamp(controllers[i], 0f, 1f) : 0f)
            ];
            CheckSceneMidi(scene.Midi);
            scene.Blocks = SceneStates(next, scene, index, problems);
        }

        if (problems.Count > 0) {
            throw new RigException(400, "The preset is not valid.", problems);
        }
    }

    /// <summary>
    ///     A scene's block states for the blocks of <paramref name="next" />; a channel its block does not have is
    ///     added to <paramref name="problems" />.
    /// </summary>
    Dictionary<string, SceneBlockState> SceneStates(Preset next, Scene scene, int index, List<string> problems) {
        var states = new Dictionary<string, SceneBlockState>(StringComparer.Ordinal);
        foreach (var block in next.Blocks) {
            if (!scene.Blocks.TryGetValue(block.Id, out var state)) {
                continue;
            }

            var channels = catalog.Descriptor(block.Type)?.Channels ?? Preset.ChannelNames.Length;
            var channel = state.Channel is null ? -1 : Preset.ChannelNames.IndexOf(state.Channel);
            if (channel < 0 || channel >= channels) {
                problems.Add(
                    $"Scene {index + 1}: block '{block.Id}' has no channel '{state.Channel}'; it has "
                    + $"{string.Join(", ", Preset.ChannelNames[..channels])}."
                );
                continue;
            }

            states[block.Id] = BuiltInTypes.IsBuiltIn(block.Type) ? state with { Bypassed = false } : state;
        }

        return states;
    }

    static void CheckScene(int scene) {
        if (scene is < 0 or >= Preset.SceneCount) {
            throw new RigException(400, $"Scenes are numbered 0 to {Preset.SceneCount - 1}.");
        }
    }
}
