namespace Cathode.Contracts;

/// <summary>
///     Settings that belong to the rig rather than to any preset: tempo source, metronome, the global output EQ,
///     Manual A-D, MIDI mappings, footswitch layouts and the global block library. Stored as one document.
/// </summary>
public sealed class GlobalSettings {
    /// <summary>2 after 1.0.0-rc.3: the global blocks' Amps as presets' schema 2.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    ///     preset (each preset's tempo), global (one tempo for everything) or midiClock (follow MIDI clock).
    /// </summary>
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

    /// <summary>
    ///     Cab modeling off: every Cab and IR Player block passes audio through, for playing into a real speaker
    ///     cabinet without changing the presets.
    /// </summary>
    public bool CabModelingOff { get; set; }

    /// <summary>
    ///     Outputs 1-4 (docs/08-blocks-parity.md §2): each one's mode, Boost/Pad and global EQ. Always four, Output 1
    ///     first.
    /// </summary>
    public List<OutputSettings> Outputs { get; set; } = [new(), new(), new(), new()];

    /// <summary>Plays Output 1 on Output 2 as well, unless the preset has an Output 2 block of its own.</summary>
    public bool CopyOutput1To2 { get; set; }

    /// <summary>The interface inputs, Input 1 first: each one's calibration and gain trim.</summary>
    public List<InputSettings> Inputs { get; set; } = [new()];

    /// <summary>
    ///     The oversamplers' filters in the Amp, Drive and Dynamic Distortion blocks: <c>minimum</c> (IIR, no added
    ///     latency, the default) or <c>linear</c> (linear-phase FIR, a few milliseconds of latency).
    /// </summary>
    public string Oversampling { get; set; } = "minimum";

    /// <summary>Manual A-D (0..1): modifier sources a person or pedal sets, shared by every preset.</summary>
    public List<float> Manual { get; set; } = [0, 0, 0, 0];

    /// <summary>MIDI channel the rig listens on (1-16), or 0 for all.</summary>
    public int MidiChannel { get; set; }

    public List<MidiMapping> MidiMappings { get; set; } = [];

    /// <summary>
    ///     Where scene MIDI and footswitch LEDs go: a MIDI output's uid (<see cref="MidiOutputInfo.Uid" />, from
    ///     <c>GET /api/v1/midi/outputs</c>), or null for every output. One port keeps them from looping back into a
    ///     controller that echoes what it receives; while that port is not connected, nothing is sent.
    /// </summary>
    public string? MidiOutPort { get; set; }

    public List<FootswitchLayout> Footswitches { get; set; } = [];

    /// <summary>The layout in use, by id.</summary>
    public string? ActiveLayout { get; set; }

    /// <summary>The view (page of switches) of the active layout that is in use, 0 for the first.</summary>
    public int ActiveView { get; set; }

    public List<GlobalBlock> Blocks { get; set; } = [];

    /// <summary>The global performance page: controls shown in every preset that has their block.</summary>
    public List<PerformControl> Perform { get; set; } = [];

    /// <summary>
    ///     Scene Revert: off, changes made while playing (a bypass, a channel) stay with their scene when you come back
    ///     to it; on, selecting a scene always recalls it as it was last saved.
    /// </summary>
    public bool SceneRevert { get; set; }

    /// <summary>
    ///     The scene a preset starts on when it loads: −1 for As Saved (the scene it was saved on), or 0-7.
    /// </summary>
    public int DefaultScene { get; set; } = -1;

    /// <summary>The preset loaded when the server starts, by id; null starts with an empty preset.</summary>
    public string? StartupPreset { get; set; }

    /// <summary>Ignore Redundant PC: a program change that selects the preset already loaded does nothing.</summary>
    public bool IgnoreRedundantPc { get; set; }

    /// <summary>
    ///     Preset-change spillover: on, the outgoing preset's delay and reverb tails ring out under the new one; off,
    ///     it stops with the swap fade (a clean cut).
    /// </summary>
    public bool PresetSpillover { get; set; } = true;

    /// <summary>
    ///     What the outputs play while the tuner is on: <c>mute</c> (silence, faded, the default), <c>bypass</c> (the
    ///     input dry, no blocks) or <c>passThrough</c> (the preset keeps sounding).
    /// </summary>
    public string TunerOutput { get; set; } = "mute";

    /// <summary>The tuner's A4 reference in Hz, 430..450 (440, the default, is concert pitch).</summary>
    public double TunerReference { get; set; } = 440;

    /// <summary>How the editor's tuner shows the error: <c>needle</c> (the default) or <c>strobe</c>.</summary>
    public string TunerView { get; set; } = "needle";

    /// <summary>
    ///     The tuning the tuner targets, by the editor's id: <c>chromatic</c> (the nearest note, the default) or a set
    ///     of strings such as <c>standard</c> or <c>dropD</c>, whose nearest string it then targets. An id the editor
    ///     does not know reads as chromatic.
    /// </summary>
    public string Tuning { get; set; } = "chromatic";

    /// <summary>Whether the <c>TunerOffsets</c> apply: off (None, the default) tunes every string to pitch.</summary>
    public bool TunerOffsetsOn { get; set; }

    /// <summary>
    ///     Cents (−20..20) added to each string's target in a tuning other than chromatic, string 1 (the highest)
    ///     first, for a "sweetened" tuning. Up to eight; a string without a value has none.
    /// </summary>
    public List<float> TunerOffsets { get; set; } = [0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Whether editors ask before changing presets while the working copy has unsaved edits.</summary>
    public bool ConfirmPresetChange { get; set; } = true;
}

/// <summary>One of Outputs 1-4.</summary>
public sealed class OutputSettings {
    /// <summary><c>stereo</c>, <c>sum</c> (L+R on both) or <c>copyLR</c> (left on the right too).</summary>
    public string Mode { get; set; } = "stereo";

    /// <summary>Level trim after the EQ, −18 to +18 dB.</summary>
    public float BoostPad { get; set; }

    /// <summary>The output's global EQ by parameter id (graphic EQ bands g31...g16k and level). Sparse.</summary>
    public Dictionary<string, float> Eq { get; set; } = [];
}

/// <summary>One interface input.</summary>
public sealed class InputSettings {
    /// <summary>
    ///     The peak volts at the jack that reach 0 dBFS (default 1 V, how Cathode always read its input). The amp and
    ///     drive models scale their input by it, so changing the interface's gain and this together keeps their drive.
    /// </summary>
    public float FullScaleVolts { get; set; } = 1f;

    /// <summary>A trim applied to every preset ahead of the grid, −20 to +20 dB (Input 1 Gain).</summary>
    public float Gain { get; set; }
}

/// <summary>
///     A MIDI message bound to an action. <c>Message</c> is cc, note or pc; <c>Channel</c> 0 means any. Actions:
///     scene (Value = scene, or the message value when absent), sceneNext, scenePrevious, presetSlot (Target = bank
///     id, the PC number picks the slot), presetNext, presetPrevious, bypass (Target = block), channel (Target = block,
///     Value = 0-3), external (Value = 1-8, the CC value drives External n), manual (Value = 0-3), tap, tuner,
///     metronome, looper (Target = record, play, stop, overdub, undo, reverse, halfSpeed or clear), sceneLevelUp and
///     sceneLevelDown (the active scene's level by ±1 dB, then save the preset), save (save the preset), layout
///     (Target = layout id), view (Value = view index), viewNext, viewPrevious. A CC mapped to scene without a Value
///     selects the scene its value names (0-7), and a value above 63 steps to the next scene.
/// </summary>
public sealed record MidiMapping(
    string Id,
    string Message,
    int Channel,
    int Number,
    string Action,
    string? Target = null,
    int? Value = null
);

/// <summary>
///     A set of footswitches on a generic MIDI foot controller. <c>Views</c> names its pages of switches (each switch
///     says which <see cref="Footswitch.View" /> it is on); none means one page.
/// </summary>
public sealed record FootswitchLayout(string Id, string Name, List<Footswitch> Switches, List<string>? Views = null);

/// <summary>
///     One switch: the message it sends (a CC or note; value 64 or more is "down"), what a tap does, what holding it
///     for half a second does instead, and whether its LED is driven (the same CC or note, 127 when its tap action is
///     active, 0 when not). It answers only while its <c>View</c> is the active one, and a <c>Link</c> (a layout id)
///     switches to that layout after its tap action.
/// </summary>
public sealed record Footswitch(
    int Index,
    string Label,
    string Message,
    int Channel,
    int Number,
    SwitchAction Tap,
    SwitchAction? Hold = null,
    bool Led = true,
    int View = 0,
    string? Link = null
);

/// <summary>A MIDI output of the engine host's machine. <c>Uid</c> is stable across runs.</summary>
public sealed record MidiOutputInfo(string Uid, string Name);

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

/// <summary>
///     Pushed on the WebSocket for every MIDI message received (<c>midi</c>), for the editor's monitor, the one MIDI
///     learn just bound (<c>midiLearned</c>), and MIDI learn armed or cancelled (<c>midiLearnChanged</c>, with zeros:
///     read <c>GET /midi/learn</c>).
/// </summary>
public sealed record MidiActivity(string Type, int Status, int Data1, int Data2);

/// <summary>The looper's transport, as the editor shows it.</summary>
public sealed record LooperInfo(
    string State,
    double LengthSeconds,
    double PositionSeconds,
    bool Reversed,
    bool HalfSpeed
);

// Request bodies.
public sealed record BypassModeRequest(string Mode);

public sealed record TempoRequest(double Bpm);

public sealed record CopyChannelRequest(string From, string To);

public sealed record LinkGlobalRequest(string? Name = null);

public sealed record ValueRequest(float Value);

/// <summary>Two scenes (0-7): copy <c>From</c> onto <c>To</c>, or swap them.</summary>
public sealed record ScenePairRequest(int From, int To);

public sealed record SceneIgnoreRequest(bool Enabled);

/// <summary>Mutes or unmutes every output (<c>PUT /live/mute</c>).</summary>
public sealed record MuteRequest(bool Muted);

/// <summary>
///     Arms MIDI learn: the next CC, note or program change received is bound to this action. With
///     <c>Modifier</c> (an <c>external</c> action only, its source that External), the Server also gives the loaded
///     preset that modifier once something is learnt, so a knob's learn leaves nothing behind when it is cancelled.
/// </summary>
public sealed record MidiLearnRequest(
    string Action,
    string? Target = null,
    int? Value = null,
    PresetModifier? Modifier = null
);
