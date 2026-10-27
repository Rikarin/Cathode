using Cathode.Contracts;
using Cathode.Server.Rig;
using System.Collections.Immutable;

namespace Cathode.Server.Api;

/// <summary>Checks a global settings document before it is stored: modes, MIDI messages, channels and actions.</summary>
static class GlobalValidation {
    static readonly string[] TempoModes = ["preset", "global", "midiClock"];
    static readonly string[] Messages = ["cc", "note", "pc"];

    /// <summary>The output modes, in the order of the engine's Output Mode choice.</summary>
    public static readonly ImmutableArray<string> OutputModes = ["stereo", "sum", "copyLR"];

    public static readonly ImmutableArray<string> Oversampling = ["minimum", "linear"];

    static readonly string[] Actions = [
        "scene", "sceneNext", "scenePrevious", "presetSlot", "presetNext", "presetPrevious", "bypass", "channel",
        "external", "manual", "tap", "tuner", "metronome", "mute", "looper", "sceneLevelUp", "sceneLevelDown", "save",
        "layout", "view", "viewNext", "viewPrevious"
    ];

    public static void Check(GlobalSettings settings) {
        var errors = new List<string>();
        CheckOffsets(settings, errors);
        CheckOutputs(settings, errors);
        CheckInputs(settings, errors);
        CheckOptions(settings, errors);
        CheckMidi(settings, errors);
        CheckLayouts(settings, errors);
        if (errors.Count > 0) {
            throw new RigException(400, "The global settings are invalid.", errors);
        }
    }

    /// <summary>The tempo mode, the MIDI channel and the effects mix and noise gate offsets.</summary>
    static void CheckOffsets(GlobalSettings settings, List<string> errors) {
        if (!TempoModes.Contains(settings.TempoMode)) {
            errors.Add($"Tempo mode '{settings.TempoMode}' is not preset, global or midiClock.");
        }

        if (settings.MidiChannel is < 0 or > 16) {
            errors.Add("The MIDI channel is 0 (all) or 1-16.");
        }

        if (settings.EffectsMix is < -50f or > 50f || !float.IsFinite(settings.EffectsMix)) {
            errors.Add("The effects mix offset is -50 to 50 %.");
        }

        if (settings.NoiseGateOffset is < -20f or > 20f || !float.IsFinite(settings.NoiseGateOffset)) {
            errors.Add("The noise gate offset is -20 to 20 dB.");
        }
    }

    static void CheckOutputs(GlobalSettings settings, List<string> errors) {
        if (settings.Outputs.Count != 4) {
            errors.Add("Outputs has four entries (Outputs 1-4).");
        }

        foreach (var (output, n) in settings.Outputs.Select(static (o, i) => (o, i + 1))) {
            if (!OutputModes.Contains(output.Mode)) {
                errors.Add($"Output {n}: mode '{output.Mode}' is not stereo, sum or copyLR.");
            }

            if (output.BoostPad is < -18f or > 18f || !float.IsFinite(output.BoostPad)) {
                errors.Add($"Output {n}: Boost/Pad is -18 to 18 dB.");
            }
        }
    }

    static void CheckInputs(GlobalSettings settings, List<string> errors) {
        if (settings.Inputs.Count is < 1 or > 4) {
            errors.Add("Inputs has one to four entries.");
        }

        foreach (var (input, n) in settings.Inputs.Select(static (input, i) => (input, i + 1))) {
            if (input.FullScaleVolts is < 0.0316f or > 100f || !float.IsFinite(input.FullScaleVolts)) {
                errors.Add($"Input {n}: full scale is 0.0316 to 100 V (-30 to +40 dBV).");
            }

            if (input.Gain is < -20f or > 20f || !float.IsFinite(input.Gain)) {
                errors.Add($"Input {n}: gain is -20 to 20 dB.");
            }
        }
    }

    /// <summary>Oversampling, the default scene and the manual controllers.</summary>
    static void CheckOptions(GlobalSettings settings, List<string> errors) {
        if (!Oversampling.Contains(settings.Oversampling)) {
            errors.Add($"Oversampling '{settings.Oversampling}' is not minimum or linear.");
        }

        if (settings.DefaultScene is < -1 or >= Preset.SceneCount) {
            errors.Add("The default scene is -1 (as saved) or 0-7.");
        }

        if (settings.Manual.Count != 4) {
            errors.Add("Manual has four values (A-D).");
        }
    }

    /// <summary>The MIDI mappings' and the footswitches' messages and actions.</summary>
    static void CheckMidi(GlobalSettings settings, List<string> errors) {
        foreach (var mapping in settings.MidiMappings) {
            CheckMessage(mapping.Message, mapping.Channel, mapping.Number, errors);
            CheckAction(mapping.Action, errors);
        }

        foreach (var footswitch in settings.Footswitches.SelectMany(static l => l.Switches)) {
            CheckMessage(footswitch.Message, footswitch.Channel, footswitch.Number, errors);
            CheckAction(footswitch.Tap.Action, errors);
            if (footswitch.Hold is { } hold) {
                CheckAction(hold.Action, errors);
            }
        }
    }

    /// <summary>Each switch's view and linked layout, and the active layout.</summary>
    static void CheckLayouts(GlobalSettings settings, List<string> errors) {
        foreach (var layout in settings.Footswitches) {
            var views = Math.Max(1, layout.Views?.Count ?? 0);
            foreach (var footswitch in layout.Switches) {
                CheckSwitch(settings, layout, footswitch, views, errors);
            }
        }

        if (settings.ActiveLayout is { } active && settings.Footswitches.TrueForAll(l => l.Id != active)) {
            errors.Add($"No footswitch layout '{active}'.");
        }
    }

    static void CheckSwitch(
        GlobalSettings settings,
        FootswitchLayout layout,
        Footswitch footswitch,
        int views,
        List<string> errors
    ) {
        if (footswitch.View < 0 || footswitch.View >= views) {
            errors.Add(
                $"Switch {footswitch.Index} of '{layout.Name}' is on view {footswitch.View}; the layout has {views}."
            );
        }

        if (footswitch.Link is { } link && settings.Footswitches.TrueForAll(l => l.Id != link)) {
            errors.Add($"Switch {footswitch.Index} of '{layout.Name}' links to no layout '{link}'.");
        }
    }

    public static void CheckAction(string action) {
        var errors = new List<string>();
        CheckAction(action, errors);
        if (errors.Count > 0) {
            throw new RigException(400, errors[0]);
        }
    }

    static void CheckAction(string action, List<string> errors) {
        if (!Actions.Contains(action)) {
            errors.Add($"Unknown action '{action}'.");
        }
    }

    static void CheckMessage(string message, int channel, int number, List<string> errors) {
        if (!Messages.Contains(message) || channel is < 0 or > 16 || number is < 0 or > 127) {
            errors.Add($"{message} {number} on channel {channel} is not a valid MIDI message.");
        }
    }
}
