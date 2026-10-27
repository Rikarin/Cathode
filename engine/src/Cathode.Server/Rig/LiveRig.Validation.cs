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

    static void CheckScene(int scene) {
        if (scene is < 0 or >= Preset.SceneCount) {
            throw new RigException(400, $"Scenes are numbered 0 to {Preset.SceneCount - 1}.");
        }
    }
}
