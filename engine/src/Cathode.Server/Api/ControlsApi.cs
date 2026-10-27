using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Midi;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Globalization;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>
///     Phase 5: bypass modes, modifiers, controllers, tempo, channels, global blocks, globals, looper, MIDI.
/// </summary>
static class ControlsApi {
    public static void Map(RouteGroupBuilder api, RouteGroupBuilder live) {
        live.MapPut(
                "/blocks/{id}/bypass-mode",
                async (string id, BypassModeRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetBypassModeAsync(id, request.Mode, Expected(http)))
            )
            .WithName("SetBypassMode")
            .WithSummary(
                "thru, mute, muteIn, muteFxIn, muteOut or muteFxOut; the 'in' modes let delay and reverb tails "
                + "spill over, the 'out' modes keep the block listening while bypassed."
            );
        live.MapPut(
                "/modifiers",
                async (List<PresetModifier> modifiers, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetModifiersAsync(modifiers, Expected(http)))
            )
            .WithName("SetModifiers")
            .WithSummary("Replaces every modifier of the working copy.");
        live.MapPatch(
                "/controllers",
                async (Dictionary<string, float> values, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetControllersAsync(values, Expected(http)))
            )
            .WithName("SetControllers")
            .WithSummary("Controller settings by rig parameter id (lfo1Rate, adsr2Attack, envGain, seq3, ...).");
        live.MapPut(
                "/tempo",
                async (TempoRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetTempoAsync(request.Bpm, Expected(http)))
            )
            .WithName("SetTempo");
        live.MapPost(
                "/tap",
                (LiveRig rig) => {
                    rig.Tap();
                    return TypedResults.NoContent();
                }
            )
            .WithName("TapTempo");
        live.MapPost(
                "/blocks/{id}/channels/copy",
                async (string id, CopyChannelRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.CopyChannelAsync(id, request, Expected(http)))
            )
            .WithName("CopyChannel");
        live.MapPost(
                "/blocks/{id}/global",
                async (string id, LinkGlobalRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.LinkGlobalAsync(id, request.Name, Expected(http)))
            )
            .WithName("MakeGlobal")
            .WithSummary("Moves the block's settings to a new global block and links the block to it.");
        live.MapPut(
                "/blocks/{id}/global/{globalId}",
                async (string id, string globalId, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UseGlobalAsync(id, globalId, Expected(http)))
            )
            .WithName("UseGlobal");
        live.MapDelete(
                "/blocks/{id}/global",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UnlinkGlobalAsync(id, Expected(http)))
            )
            .WithName("UnlinkGlobal");
        live.MapDelete(
                "/globals/{globalId}",
                async (string globalId, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.DeleteGlobalAsync(globalId, Expected(http)))
            )
            .WithName("DeleteGlobalBlock")
            .WithSummary("Deletes a global block; the blocks linked to it keep its settings as their own.");
        live.MapPut(
                "/manual/{index:int}",
                Results<NoContent, BadRequest>(int index, ValueRequest request, LiveRig rig) => {
                    if (index is < 0 or > 3) {
                        return TypedResults.BadRequest();
                    }

                    rig.SetManual(index, request.Value);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetManual")
            .WithSummary("Manual A-D (index 0-3), 0..1.");
        live.MapPut(
                "/external/{index:int}",
                Results<NoContent, BadRequest>(int index, ValueRequest request, LiveRig rig) => {
                    if (index is < 0 or > 7) {
                        return TypedResults.BadRequest();
                    }

                    rig.SetExternal(index, request.Value);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetExternal")
            .WithSummary("External 1-8 (index 0-7), 0..1: what a pedal or CC would set.");

        var global = api.MapGroup("/global").WithTags("Global");
        global.MapGet(
                "/",
                (GlobalStore store, HttpContext http) => {
                    var (settings, revision) = store.Versioned();
                    return RespondGlobal(http, settings, revision);
                }
            )
            .WithName("GetGlobal")
            .WithSummary("The global settings, with their revision as the ETag (send it back as If-Match on a PUT).");
        global.MapPut(
                "/",
                (GlobalSettings settings, GlobalStore store, HttpContext http) => {
                    GlobalValidation.Check(settings);
                    var (saved, revision) = store.UpdateIf(Expected(http), g => Replace(g, settings))
                        ?? throw new RigException(
                            412,
                            "The global settings changed (another editor or the rig). Reload and try again."
                        );
                    return RespondGlobal(http, saved, revision);
                }
            )
            .WithName("PutGlobal")
            .WithSummary(
                "Replaces the global settings: tempo mode, metronome, outputs (mode, Boost/Pad, EQ), inputs "
                + "(calibration, gain), oversampling, the tuner output, Manual A-D, MIDI, footswitches, global blocks, "
                + "scene recall (Scene Revert, Default Scene), the startup preset, Ignore Redundant PC and spillover. "
                + "With If-Match (the revision a GET or PUT answered with as the ETag), refused with 412 when another "
                + "change came first."
            );

        var looper = api.MapGroup("/looper").WithTags("Looper");
        looper.MapPost(
                "/{action}",
                Results<NoContent, BadRequest<string>>(string action, IEngineSync engine) => {
                    string[] actions = ["record", "play", "stop", "overdub", "undo", "reverse", "halfSpeed", "clear"];
                    if (!actions.Contains(action)) {
                        return TypedResults.BadRequest($"Unknown looper action '{action}'.");
                    }

                    engine.Looper(action);
                    return TypedResults.NoContent();
                }
            )
            .WithName("LooperAction")
            .WithSummary("record, play, stop, overdub, undo, reverse, halfSpeed or clear.");
        looper.MapGet(
                "/export",
                async (IEngineSync engine) => {
                    var path = Path.Combine(Path.GetTempPath(), $"cathode-loop-{Guid.NewGuid():n}.wav");
                    try {
                        await engine.ExportLooperAsync(path);
                    } catch (IpcException e) {
                        throw new RigException(409, e.Message);
                    } catch (IOException e) {
                        throw new RigException(503, e.Message); // the engine is not connected
                    }

                    var file = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81_920,
                        FileOptions.DeleteOnClose | FileOptions.Asynchronous
                    );
                    return Results.File(file, "audio/wav", "loop.wav");
                }
            )
            .Produces(200, contentType: "audio/wav")
            .WithName("ExportLoop");

        var midi = api.MapGroup("/midi").WithTags("MIDI");
        midi.MapGet("/outputs", async (EngineConnection engine) => TypedResults.Ok(await engine.MidiOutputsAsync()))
            .WithName("GetMidiOutputs")
            .WithSummary(
                "The MIDI outputs of the engine host's machine, for the MIDI output setting (empty while no engine is "
                + "connected)."
            );
        midi.MapGet("/learn", (MidiRouter router) => TypedResults.Ok(router.Learning)).WithName("GetMidiLearn");
        midi.MapPut(
                "/learn",
                (MidiLearnRequest request, MidiRouter router) => {
                    GlobalValidation.CheckAction(request.Action);
                    router.Learn(request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("StartMidiLearn")
            .WithSummary(
                "The next CC, note or program change received is mapped to this action ('midiLearned' event)."
            );
        midi.MapDelete(
                "/learn",
                (MidiRouter router) => {
                    router.Learn(null);
                    return TypedResults.NoContent();
                }
            )
            .WithName("CancelMidiLearn");
    }

    /// <summary>Answers with the global settings and their revision as the ETag (as <c>/live</c> does).</summary>
    static Ok<GlobalSettings> RespondGlobal(HttpContext http, GlobalSettings settings, long revision) {
        http.Response.Headers.ETag = $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";
        return TypedResults.Ok(settings);
    }

    /// <summary>What a <c>PUT /global</c> stores: the settings sent, clamped.</summary>
    static void Replace(GlobalSettings g, GlobalSettings settings) {
        g.TempoMode = settings.TempoMode;
        g.Tempo = Math.Clamp(settings.Tempo, 30, 300);
        g.Metronome = settings.Metronome;
        g.MetronomeLevel = Math.Clamp(settings.MetronomeLevel, -60, 0);
        g.Outputs = settings.Outputs;
        g.CopyOutput1To2 = settings.CopyOutput1To2;
        g.Inputs = settings.Inputs;
        g.Oversampling = settings.Oversampling;
        g.TunerOutput = settings.TunerOutput;
        g.Manual = settings.Manual;
        g.MidiChannel = settings.MidiChannel;
        g.MidiMappings = settings.MidiMappings;
        g.MidiOutPort = NullIfBlank(settings.MidiOutPort);
        g.Footswitches = settings.Footswitches;
        g.ActiveLayout = settings.ActiveLayout;
        g.ActiveView = Math.Max(0, settings.ActiveView);
        g.Blocks = settings.Blocks;
        g.Perform = settings.Perform;
        g.EffectsMix = settings.EffectsMix;
        g.NoiseGateOffset = settings.NoiseGateOffset;
        g.CabModelingOff = settings.CabModelingOff;
        g.SceneRevert = settings.SceneRevert;
        g.DefaultScene = settings.DefaultScene;
        g.StartupPreset = NullIfBlank(settings.StartupPreset);
        g.IgnoreRedundantPc = settings.IgnoreRedundantPc;
        g.PresetSpillover = settings.PresetSpillover;
        g.ConfirmPresetChange = settings.ConfirmPresetChange;
    }

    static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
