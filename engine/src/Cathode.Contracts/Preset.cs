using System.Collections.Immutable;

namespace Cathode.Contracts;

/// <summary>
///     A complete rig: grid, blocks with up to four channels each, cables and eight scenes. Parameter values are stored
///     sparsely; anything not listed takes the block type's default, so new parameters need no migration.
/// </summary>
public sealed class Preset {
    public const int CurrentSchemaVersion = 2;
    public const int SceneCount = 8;

    /// <summary>The most rows a grid has: the editor's grid and the Mixer's and Output's six row inputs.</summary>
    public const int MaxRows = 6;

    /// <summary>The most columns a grid has, as on the Axe-Fx III.</summary>
    public const int MaxColumns = 14;

    /// <summary>
    ///     The most modifiers a preset holds: the engine reports each one's value to the editor, and its telemetry has
    ///     room for this many (<c>TelemetryLayout.Modifiers</c>).
    /// </summary>
    public const int MaxModifiers = 256;

    public static readonly ImmutableArray<string> ChannelNames = ["A", "B", "C", "D"];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Id { get; set; } = NewId();

    public string Name { get; set; } = "New Preset";

    public string? Author { get; set; }

    /// <summary>A sentence or two on what the preset is and how to play it, or null.</summary>
    public string? Description { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>
    ///     True for a preset of the factory bank, which ships with Cathode and cannot be overwritten, renamed or
    ///     deleted (saving one stores a copy); null for the user's own.
    /// </summary>
    public bool? Factory { get; set; }

    public double Tempo { get; set; } = 120;

    public int Rows { get; set; } = MaxRows;

    public int Columns { get; set; } = MaxColumns;

    public List<PresetBlock> Blocks { get; set; } = [];

    public List<PresetCable> Cables { get; set; } = [];

    public List<Scene> Scenes { get; set; } =
        [..Enumerable.Range(1, SceneCount).Select(static n => new Scene { Name = $"Scene {n}" })];

    /// <summary>Parameters driven by controllers (docs/02-architecture.md §5.4).</summary>
    public List<PresetModifier> Modifiers { get; set; } = [];

    /// <summary>
    ///     Controller settings (LFO rates and shapes, ADSR times, envelope and sequencer settings) by rig parameter id,
    ///     e.g. <c>lfo1Rate</c>. Sparse: missing ones take their default.
    /// </summary>
    public Dictionary<string, float> Controllers { get; set; } = [];

    /// <summary>A colour tag for pickers and Manage Presets (a CSS colour name or hex), or null.</summary>
    public string? Color { get; set; }

    public bool Favorite { get; set; }

    /// <summary>The preset's performance page: the controls it puts in front of a player.</summary>
    public List<PerformControl> Perform { get; set; } = [];

    /// <summary>Switches of the global footswitch layouts that do something else while this preset is loaded.</summary>
    public List<FootswitchOverride> FootswitchOverrides { get; set; } = [];

    /// <summary>
    ///     The scene that was active when the preset was saved: where it starts when the global Default Scene is As
    ///     Saved.
    /// </summary>
    public int SavedScene { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("n");

    /// <summary>The row an empty preset's Input-to-Output line runs on: the third, leaving two free above it.</summary>
    public const int DefaultRow = 2;

    /// <summary>The smallest working rig: Input 1 straight to Output 1 across <see cref="DefaultRow" />.</summary>
    public static Preset CreateDefault() =>
        new Preset {
            Blocks = [
                new() { Id = "in1", Type = BuiltInTypes.Input, Row = DefaultRow, Column = 0 },
                new() { Id = "out1", Type = BuiltInTypes.Output, Row = DefaultRow, Column = 13 }
            ],
            Cables = []
        }.WithShuntRow(DefaultRow, "in1", "out1");

    /// <summary>Fills the cells between two blocks on one row with shunts and cables.</summary>
    public Preset WithShuntRow(int row, string from, string to) {
        var start = Blocks.Single(b => b.Id == from).Column;
        var end = Blocks.Single(b => b.Id == to).Column;
        var previous = from;
        for (var column = start + 1; column < end; column++) {
            var id = $"shunt-r{row}c{column}";
            Blocks.Add(new() { Id = id, Type = BuiltInTypes.Shunt, Row = row, Column = column });
            Cables.Add(new() { From = previous, To = id });
            previous = id;
        }

        Cables.Add(new() { From = previous, To = to });
        return this;
    }
}

public static class BuiltInTypes {
    public const string Input = "input";
    public const string Output = "output";
    public const string Shunt = "shunt";

    /// <summary>Outputs 2-4, placeable grid outputs mapped to device channels in the audio settings.</summary>
    public const string Output2 = "output2";

    public const string Output3 = "output3";
    public const string Output4 = "output4";

    public static bool IsBuiltIn(string type) => type is Input or Shunt || IsOutput(type);

    public static bool IsOutput(string type) => type is Output or Output2 or Output3 or Output4;
}

public sealed class PresetBlock {
    /// <summary>Stable instance id such as <c>amp1</c>; never an index.</summary>
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    /// <summary>
    ///     Channel name (A-D) to parameter id to value. Only values that differ from the default need to be present.
    /// </summary>
    public Dictionary<string, Dictionary<string, float>> Channels { get; set; } = [];

    /// <summary>
    ///     Asset slot to asset reference (<c>sha256:&lt;hex&gt;</c>), e.g. <c>"model"</c> or <c>"slot1"</c>.
    /// </summary>
    public Dictionary<string, string> Assets { get; set; } = [];

    /// <summary>
    ///     What bypassing does: thru, mute, muteIn, muteFxIn, muteOut or muteFxOut (the "in" modes let tails spill
    ///     over, the "out" modes keep the block listening while bypassed).
    /// </summary>
    public string BypassMode { get; set; } = "thru";

    /// <summary>
    ///     The global block this block is linked to, if any: its channels and assets come from the global library and
    ///     editing them edits every linked preset.
    /// </summary>
    public string? Global { get; set; }

    /// <summary>
    ///     Scene Ignore: the channels (A-D) on which the block ignores scene changes. While its current channel is one
    ///     of them, selecting a scene leaves the block's bypass and channel as they are; the editor, footswitches and
    ///     MIDI still change them, and a channel not listed makes it follow the scenes again.
    /// </summary>
    public List<string> SceneIgnore { get; set; } = [];

    /// <summary>Whether the block ignores scene changes on <paramref name="channel" />.</summary>
    public bool IgnoresScenes(string channel) => SceneIgnore.Contains(channel);

    /// <summary>Parameter values of <paramref name="channel" />, creating the map if needed.</summary>
    public Dictionary<string, float> Channel(string channel) {
        if (!Channels.TryGetValue(channel, out var values)) {
            values = [];
            Channels[channel] = values;
        }

        return values;
    }
}

public sealed class PresetCable {
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;
}

/// <summary>
///     Which channel each block uses and whether it is bypassed. Blocks not listed use channel A, engaged.
/// </summary>
public sealed class Scene {
    public string Name { get; set; } = string.Empty;

    /// <summary>Scene output level in dB.</summary>
    public float Level { get; set; }

    public Dictionary<string, SceneBlockState> Blocks { get; set; } = [];

    /// <summary>Scene controllers 1-4 (0..1), modifier sources that change with the scene.</summary>
    public List<float> Controllers { get; set; } = [0, 0, 0, 0];

    /// <summary>
    ///     Up to <see cref="SceneMidiMessage.MaxPerScene" /> MIDI messages sent through the rig's MIDI output whenever
    ///     this scene is selected, or its preset loads on it: program changes and CCs for other gear.
    /// </summary>
    public List<SceneMidiMessage> Midi { get; set; } = [];

    public SceneBlockState StateOf(string blockId) =>
        Blocks.TryGetValue(blockId, out var state) ? state : SceneBlockState.Default;
}

/// <summary>
///     One scene MIDI message: a program change (<c>pc</c>: <see cref="Number" /> is the program) or a control change
///     (<c>cc</c>: controller <see cref="Number" />, value <see cref="Value" />), on MIDI channel 1-16.
/// </summary>
public sealed record SceneMidiMessage(string Type = "pc", int Channel = 1, int Number = 0, int Value = 0) {
    public const int MaxPerScene = 8;

    /// <summary>The bytes on the wire.</summary>
    public byte[] Bytes() =>
        Type == "cc"
            ? [(byte)(0xB0 | (Channel - 1)), (byte)Number, (byte)Value]
            : [(byte)(0xC0 | (Channel - 1)), (byte)Number];

    /// <summary>What is wrong with this message, or null.</summary>
    public string? Problem() =>
        Type is not ("pc" or "cc") ? $"Scene MIDI type '{Type}' is not pc or cc."
        : Channel is < 1 or > 16 ? "Scene MIDI channels are 1-16."
        : Number is < 0 or > 127 || Value is < 0 or > 127 ? "Scene MIDI numbers and values are 0-127."
        : null;
}

public sealed record SceneBlockState(bool Bypassed = false, string Channel = "A") {
    public static SceneBlockState Default { get; } = new();
}

/// <summary>Presets in performance order. Each entry is a song part: a preset and the scene to start on.</summary>
public sealed class Setlist {
    public string Id { get; set; } = Preset.NewId();

    public string Name { get; set; } = "New Setlist";

    public List<SetlistEntry> Entries { get; set; } = [];
}

public sealed record SetlistEntry(string PresetId, int Scene = 0, string? Label = null);

/// <summary>Numbered preset slots, as a MIDI program change addresses them (0-127).</summary>
public sealed class Bank {
    public const int SlotCount = 128;

    public string Id { get; set; } = Preset.NewId();

    public string Name { get; set; } = "New Bank";

    /// <summary>
    ///     True for the factory bank, which lists the factory presets and cannot be changed; null otherwise.
    /// </summary>
    public bool? Factory { get; set; }

    /// <summary>Preset id per slot; null or missing slots are empty.</summary>
    public List<string?> Slots { get; set; } = [];
}

/// <summary>One modified parameter; see Cathode.Engine.Graph.ModifierDefinition for the semantics.</summary>
public sealed class PresetModifier {
    public string Block { get; set; } = string.Empty;

    public string Param { get; set; } = string.Empty;

    /// <summary>lfo1, lfo2, adsr1, adsr2, seq, env, pitch, manualA-D, scene1-4 or ext1-8.</summary>
    public string Source { get; set; } = "lfo1";

    public float Min { get; set; }

    public float Max { get; set; } = 1f;

    /// <summary>linear, exp, log or scurve.</summary>
    public string Curve { get; set; } = "linear";

    public float AttackMs { get; set; }

    public float ReleaseMs { get; set; }

    /// <summary>
    ///     The channel (A-D) the modifier applies on, or null for every channel of its block. On another channel the
    ///     parameter keeps that channel's own value.
    /// </summary>
    public string? Channel { get; set; }
}
