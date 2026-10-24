namespace Cathode.Contracts;

/// <summary>
///     A complete rig: grid, blocks with up to four channels each, cables and eight scenes. Parameter values are stored
///     sparsely; anything not listed takes the block type's default, so new parameters need no migration.
/// </summary>
public sealed class Preset {
    public const int CurrentSchemaVersion = 1;
    public const int SceneCount = 8;

    public static readonly string[] ChannelNames = ["A", "B", "C", "D"];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Id { get; set; } = NewId();

    public string Name { get; set; } = "New Preset";

    public string? Author { get; set; }

    public List<string> Tags { get; set; } = [];

    public double Tempo { get; set; } = 120;

    public int Rows { get; set; } = 6;

    public int Columns { get; set; } = 14;

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

    public static string NewId() => Guid.NewGuid().ToString("n");

    /// <summary>The smallest working rig: Input 1 straight to Output 1 across the first row.</summary>
    public static Preset CreateDefault() =>
        new Preset {
            Blocks = [
                new() { Id = "in1", Type = BuiltInTypes.Input, Row = 0, Column = 0 },
                new() { Id = "out1", Type = BuiltInTypes.Output, Row = 0, Column = 13 }
            ],
            Cables = []
        }.WithShuntRow(0, "in1", "out1");

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

    public static bool IsBuiltIn(string type) => type is Input or Output or Shunt;
}

public sealed class PresetBlock {
    /// <summary>Stable instance id such as <c>amp1</c>; never an index.</summary>
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    /// <summary>Channel name (A-D) to parameter id to value. Only values that differ from the default need to be present.</summary>
    public Dictionary<string, Dictionary<string, float>> Channels { get; set; } = [];

    /// <summary>Asset slot to asset reference (<c>sha256:&lt;hex&gt;</c>), e.g. <c>"model"</c> or <c>"slot1"</c>.</summary>
    public Dictionary<string, string> Assets { get; set; } = [];

    /// <summary>
    ///     What bypassing does: thru, mute, muteIn, muteFxIn, muteOut or muteFxOut (the "in" modes let tails spill over,
    ///     the "out" modes keep the block listening while bypassed).
    /// </summary>
    public string BypassMode { get; set; } = "thru";

    /// <summary>
    ///     The global block this block is linked to, if any: its channels and assets come from the global library and
    ///     editing them edits every linked preset.
    /// </summary>
    public string? Global { get; set; }

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

/// <summary>Which channel each block uses and whether it is bypassed. Blocks not listed use channel A, engaged.</summary>
public sealed class Scene {
    public string Name { get; set; } = string.Empty;

    /// <summary>Scene output level in dB.</summary>
    public float Level { get; set; }

    public Dictionary<string, SceneBlockState> Blocks { get; set; } = [];

    /// <summary>Scene controllers 1-4 (0..1), modifier sources that change with the scene.</summary>
    public List<float> Controllers { get; set; } = [0, 0, 0, 0];

    public SceneBlockState StateOf(string blockId) =>
        Blocks.TryGetValue(blockId, out var state) ? state : SceneBlockState.Default;
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
}
