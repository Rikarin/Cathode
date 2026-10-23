using Cathode.Contracts;
using Cathode.Server.Rig;

namespace Cathode.Server.Api;

/// <summary>Checks a global settings document before it is stored: modes, MIDI messages, channels and actions.</summary>
static class GlobalValidation {
    static readonly string[] TempoModes = ["preset", "global", "midiClock"];
    static readonly string[] Messages = ["cc", "note", "pc"];

    static readonly string[] Actions = [
        "scene", "sceneNext", "scenePrevious", "presetSlot", "presetNext", "presetPrevious", "bypass", "channel",
        "external", "manual", "tap", "tuner", "metronome", "looper"
    ];

    public static void Check(GlobalSettings settings) {
        var errors = new List<string>();
        if (!TempoModes.Contains(settings.TempoMode)) {
            errors.Add($"Tempo mode '{settings.TempoMode}' is not preset, global or midiClock.");
        }

        if (settings.MidiChannel is < 0 or > 16) {
            errors.Add("The MIDI channel is 0 (all) or 1-16.");
        }

        if (settings.Manual.Count != 4) {
            errors.Add("Manual has four values (A-D).");
        }

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

        if (settings.ActiveLayout is { } active && settings.Footswitches.All(l => l.Id != active)) {
            errors.Add($"No footswitch layout '{active}'.");
        }

        if (errors.Count > 0) {
            throw new RigException(400, "The global settings are invalid.", errors);
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
