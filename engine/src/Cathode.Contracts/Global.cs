namespace Cathode.Contracts;

/// <summary>
///     Settings that belong to the rig rather than to any preset: tempo source, metronome, the global output EQ,
///     Manual A-D, MIDI mappings, footswitch layouts and the global block library. Stored as one document.
/// </summary>
public sealed class GlobalSettings {
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>preset (each preset's tempo), global (one tempo for everything) or midiClock (follow MIDI clock).</summary>
    public string TempoMode { get; set; } = "preset";

    /// <summary>The tempo in global mode.</summary>
    public double Tempo { get; set; } = 120;

    public bool Metronome { get; set; }

    public float MetronomeLevel { get; set; } = -12;

    /// <summary>
    ///     Effects Mix offset (−50..50 %), added to the Mix of every block whose Global Mix is on: one control for
    ///     making the whole rig wetter or drier for a room.
    /// </summary>
    public float EffectsMix { get; set; }

    /// <summary>Noise gate offset in dB (−20..20), added to every Input block's gate threshold.</summary>
    public float NoiseGateOffset { get; set; }

    /// <summary>The global output EQ by parameter id (graphic EQ bands g31...g16k and level). Sparse.</summary>
    public Dictionary<string, float> Eq { get; set; } = [];

    /// <summary>Manual A-D (0..1): modifier sources a person or pedal sets, shared by every preset.</summary>
    public List<float> Manual { get; set; } = [0, 0, 0, 0];

    /// <summary>MIDI channel the rig listens on (1-16), or 0 for all.</summary>
    public int MidiChannel { get; set; }

    public List<MidiMapping> MidiMappings { get; set; } = [];

    public List<FootswitchLayout> Footswitches { get; set; } = [];

    /// <summary>The layout in use, by id.</summary>
    public string? ActiveLayout { get; set; }

    public List<GlobalBlock> Blocks { get; set; } = [];
}

/// <summary>
///     A MIDI message bound to an action. <c>Message</c> is cc, note or pc; <c>Channel</c> 0 means any. Actions:
///     scene (Value = scene, or the message value when absent), sceneNext, scenePrevious, presetSlot (Target = bank
///     id, the PC number picks the slot), presetNext, presetPrevious, bypass (Target = block), channel (Target = block,
///     Value = 0-3), external (Value = 1-8, the CC value drives External n), manual (Value = 0-3), tap, tuner,
///     metronome, looper (Target = record, play, stop, overdub, undo, reverse, halfSpeed or clear).
/// </summary>
public sealed record MidiMapping(
    string Id,
    string Message,
    int Channel,
    int Number,
    string Action,
    string? Target = null,
    int? Value = null);

/// <summary>A set of footswitches on a generic MIDI foot controller.</summary>
public sealed record FootswitchLayout(string Id, string Name, List<Footswitch> Switches);

/// <summary>
///     One switch: the message it sends (a CC or note; value 64 or more is "down"), what a tap does, what holding it for
///     half a second does instead, and whether its LED is driven (the same CC or note, 127 when its tap action is
///     active, 0 when not).
/// </summary>
public sealed record Footswitch(
    int Index,
    string Label,
    string Message,
    int Channel,
    int Number,
    SwitchAction Tap,
    SwitchAction? Hold = null,
    bool Led = true);

/// <summary>An action, with the same vocabulary as <see cref="MidiMapping" />.</summary>
public sealed record SwitchAction(string Action, string? Target = null, int? Value = null);

/// <summary>A block shared by presets: linking a preset block to it makes its settings global.</summary>
public sealed class GlobalBlock {
    public string Id { get; set; } = Preset.NewId();

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public Dictionary<string, Dictionary<string, float>> Channels { get; set; } = [];

    public Dictionary<string, string> Assets { get; set; } = [];
}

/// <summary>Pushed on the WebSocket for every MIDI message received, for the editor's monitor and MIDI learn.</summary>
public sealed record MidiActivity(string Type, int Status, int Data1, int Data2);

/// <summary>The looper's transport, as the editor shows it.</summary>
public sealed record LooperInfo(
    string State,
    double LengthSeconds,
    double PositionSeconds,
    bool Reversed,
    bool HalfSpeed);

// Request bodies.
public sealed record BypassModeRequest(string Mode);

public sealed record TempoRequest(double Bpm);

public sealed record CopyChannelRequest(string From, string To);

public sealed record LinkGlobalRequest(string? Name = null);

public sealed record ValueRequest(float Value);

/// <summary>Arms MIDI learn: the next CC, note or program change received is bound to this action.</summary>
public sealed record MidiLearnRequest(string Action, string? Target = null, int? Value = null);
