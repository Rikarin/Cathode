using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Cathode.Server.Midi;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
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
        global.MapGet("/", (GlobalStore store) => TypedResults.Ok(store.Get())).WithName("GetGlobal");
        global.MapPut(
                "/",
                (GlobalSettings settings, GlobalStore store) => {
                    GlobalValidation.Check(settings);
                    return TypedResults.Ok(
                        store.Update(g => {
                                g.TempoMode = settings.TempoMode;
                                g.Tempo = Math.Clamp(settings.Tempo, 30, 300);
                                g.Metronome = settings.Metronome;
                                g.MetronomeLevel = Math.Clamp(settings.MetronomeLevel, -60, 0);
                                g.Outputs = settings.Outputs;
                                g.CopyOutput1To2 = settings.CopyOutput1To2;
                                g.Inputs = settings.Inputs;
                                g.Oversampling = settings.Oversampling;
                                g.Manual = settings.Manual;
                                g.MidiChannel = settings.MidiChannel;
                                g.MidiMappings = settings.MidiMappings;
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
                                g.StartupPreset = string.IsNullOrWhiteSpace(settings.StartupPreset)
                                    ? null
                                    : settings.StartupPreset;
                                g.IgnoreRedundantPc = settings.IgnoreRedundantPc;
                                g.PresetSpillover = settings.PresetSpillover;
                                g.ConfirmPresetChange = settings.ConfirmPresetChange;
                            }
                        )
                    );
                }
            )
            .WithName("PutGlobal")
            .WithSummary(
                "Replaces the global settings: tempo mode, metronome, outputs (mode, Boost/Pad, EQ), inputs "
                + "(calibration, gain), oversampling, Manual A-D, MIDI, footswitches, global blocks, "
                + "scene recall (Scene Revert, Default Scene), the startup preset, Ignore Redundant PC and spillover."
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
}
