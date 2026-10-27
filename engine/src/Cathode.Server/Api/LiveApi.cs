using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>
///     The working copy (<c>/live</c>): loading and saving, undo, blocks, cables, scenes and the block tools.
///     Every edit answers with the new state.
/// </summary>
static class LiveApi {
    /// <summary>Maps the <c>/live</c> group's editing endpoints; returns the group for the other areas.</summary>
    public static RouteGroupBuilder Map(RouteGroupBuilder api) {
        var live = api.MapGroup("/live").WithTags("Live");
        live.MapGet("/", async (LiveRig rig, HttpContext http) => Respond(http, await rig.StateAsync()))
            .WithName("GetLive");
        live.MapPut(
                "/",
                async (Preset preset, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.PutAsync(preset, Expected(http)))
            )
            .WithName("PutLive")
            .WithSummary(
                "Replaces the working copy's content (undoable; the preset keeps its id). Used for A/B compare."
            );
        live.MapPost("/new", async (LiveRig rig, HttpContext http) => Respond(http, await rig.NewAsync()))
            .WithName("NewPreset");
        live.MapPut(
                "/preset/{id}",
                async (string id, LiveRig rig, HttpContext http) => Respond(http, await rig.LoadAsync(id))
            )
            .WithName("LoadPreset");
        live.MapPut(
                "/setlists/{id}/entries/{index:int}",
                async (string id, int index, PresetNavigator navigator, HttpContext http) =>
                    Respond(http, await navigator.LoadEntryAsync(id, index))
            )
            .WithName("LoadSetlistEntry")
            .WithSummary("Loads a setlist entry on its scene; preset next/previous then step through the setlist.");
        live.MapPut(
                "/banks/{id}/slots/{slot:int}",
                async (string id, int slot, PresetNavigator navigator, HttpContext http) =>
                    Respond(http, await navigator.LoadSlotAsync(id, slot))
            )
            .WithName("LoadBankSlot")
            .WithSummary("Loads the preset in a bank slot (0-127); preset next/previous then go on from that slot.");
        live.MapGet(
                "/neighbour",
                async Task<Results<Ok<PresetStep>, NoContent>>(int step, PresetNavigator navigator) =>
                    await navigator.NeighbourAsync(step) is { } target
                        ? TypedResults.Ok(target)
                        : TypedResults.NoContent()
            )
            .WithName("GetPresetNeighbour")
            .WithSummary(
                "What preset next (step 1) or previous (-1) would load, in bank order or through the active setlist; "
                + "load it with its bank slot or setlist entry (or by id, for a preset in no bank). "
                + "204 with no presets."
            );
        live.MapGet(
                "/export",
                async (LiveRig rig, PresetPackage package, CancellationToken cancellationToken) =>
                    await Export((await rig.StateAsync()).Preset, package, cancellationToken)
            )
            .Produces(200, contentType: "application/zip")
            .WithName("ExportLive")
            .WithSummary("The working copy as a preset package, saved or not (stored presets: /presets/{id}/export).");
        live.MapPost(
                "/save",
                async (SaveRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SaveAsync(request, Expected(http)))
            )
            .WithName("SavePreset");
        live.MapPost("/undo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.UndoAsync()))
            .WithName("Undo");
        live.MapPost("/redo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.RedoAsync()))
            .WithName("Redo");

        live.MapPost(
                "/blocks",
                async (AddBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.AddBlockAsync(request, Expected(http)))
            )
            .WithName("AddBlock");
        live.MapDelete(
                "/blocks/{id}",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.RemoveBlockAsync(id, Expected(http)))
            )
            .WithName("RemoveBlock");
        live.MapPut(
                "/blocks/{id}/position",
                async (string id, MoveBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.MoveBlockAsync(id, request, Expected(http)))
            )
            .WithName("MoveBlock");
        live.MapPatch(
                "/blocks/{id}/params",
                async (
                    string id,
                    string? channel,
                    long? load,
                    string? gesture,
                    Dictionary<string, float> values,
                    LiveRig rig,
                    HttpContext http
                ) => Respond(http, await rig.SetParamsAsync(id, channel, values, Expected(http), load, gesture))
            )
            .WithName("SetParams")
            .WithSummary(
                "Sets parameters (undoable). With gesture (a client-made id for one drag of a knob, up to 64 "
                + "characters), the changes of one gesture are one undo step and the next gesture is another; without, "
                + "a run of changes to the same knob is one step until a 2 s pause. With load, the change was made on "
                + "that load of a preset (LiveState.load) and is refused with 412 once another is loaded."
            );
        live.MapPut(
                "/blocks/{id}/bypass",
                async (string id, BypassRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetBypassAsync(id, request.Bypassed, Expected(http)))
            )
            .WithName("SetBypass");
        live.MapPut(
                "/blocks/{id}/channel",
                async (string id, ChannelRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetChannelAsync(id, request.Channel, Expected(http)))
            )
            .WithName("SetChannel");
        live.MapPut(
                "/blocks/{id}/assets/{slot}",
                async (string id, string slot, AssetRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetAssetAsync(id, slot, request, Expected(http)))
            )
            .WithName("SetAsset");
        live.MapPost(
                "/blocks/{id}/tonematch",
                async (string id, ToneMatchRequest request, IrTools tools, HttpContext http) =>
                    Respond(http, await tools.ToneMatchAsync(id, request, http.RequestAborted))
            )
            .WithName("LearnToneMatch")
            .WithSummary(
                "Listens to the rig while you play, then sets a Tone Match block's correction towards a reference .wav."
            );
        live.MapGet(
                "/blocks/{id}/ir",
                async (string id, IrTools tools) =>
                    TypedResults.File(await tools.ExportCabAsync(id), "audio/wav", $"{id}.wav")
            )
            .Produces<byte[]>(200, "audio/wav")
            .WithName("ExportCabIr")
            .WithSummary(
                "A Cab block's current mixed IR (every slot that plays, DynaCab and IR pack slots included, cuts "
                + "and smoothing baked in) as a stereo .wav at the engine's rate."
            );
        live.MapPost(
                "/blocks/{id}/ir",
                async (string id, CabExportRequest request, IrTools tools, HttpContext http) =>
                    TypedResults.Ok(await tools.SaveCabAsync(id, request, http.RequestAborted))
            )
            .WithName("SaveCabIr")
            .WithSummary("Saves a Cab block's current mixed IR as a new IR asset.");

        live.MapGet(
                "/blocks/{id}/cab/align",
                async (string id, IrTools tools) => TypedResults.Ok(await tools.CabAlignmentAsync(id))
            )
            .WithName("GetCabAlignment")
            .WithSummary("The Cab block's slots for its Align page: each slot's IR head, onset and delays.");

        live.MapPost(
                "/cables",
                async (CableRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.AddCableAsync(request, Expected(http)))
            )
            .WithName("AddCable");
        live.MapDelete(
                "/cables",
                async (string from, string to, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.RemoveCableAsync(new(from, to), Expected(http)))
            )
            .WithName("RemoveCable");

        live.MapPut(
                "/scenes/active",
                async (SceneSelectRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SelectSceneAsync(request.Scene))
            )
            .WithName("SelectScene");
        live.MapPatch(
                "/info",
                async (PresetInfoRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UpdateInfoAsync(request, Expected(http)))
            )
            .WithName("UpdatePresetInfo")
            .WithSummary("Renames the working copy and sets its author, tags and description (undoable).");
        MapScenes(live);
        live.MapPatch(
                "/scenes/{scene:int}",
                async (int scene, SceneUpdateRequest request, string? gesture, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.UpdateSceneAsync(scene, request, Expected(http), gesture))
            )
            .WithName("UpdateScene")
            .WithSummary(
                "Renames a scene or sets its level, controllers or MIDI. Level and controller changes coalesce into "
                + "one undo step per gesture (a client-made id, as for SetParams) or, without one, until a 2 s pause."
            );
        return live;
    }

    /// <summary>
    ///     Scene and channel tools: the Scene Manager, copy and swap, Scene Ignore, channel swap and reset.
    /// </summary>
    static void MapScenes(RouteGroupBuilder live) {
        live.MapPost(
                "/scenes/copy",
                async (ScenePairRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.CopySceneAsync(request, Expected(http)))
            )
            .WithName("CopyScene")
            .WithSummary("Copies scene From onto scene To: name, block states, level, controllers and MIDI.");
        live.MapPost(
                "/scenes/swap",
                async (ScenePairRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SwapScenesAsync(request, Expected(http)))
            )
            .WithName("SwapScenes")
            .WithSummary("Swaps two scenes; the active scene moves with its content.");
        live.MapPut(
                "/scenes/{scene:int}/blocks/{id}",
                async (int scene, string id, SceneBlockState state, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetSceneBlockAsync(scene, id, state, Expected(http)))
            )
            .WithName("SetSceneBlock")
            .WithSummary("A block's bypass and channel in one scene (the Scene Manager).");
        live.MapPut(
                "/blocks/{id}/channels/{channel}/scene-ignore",
                async (string id, string channel, SceneIgnoreRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetSceneIgnoreAsync(id, channel, request.Enabled, Expected(http)))
            )
            .WithName("SetSceneIgnore")
            .WithSummary(
                "Scene Ignore for one channel: while on it, scene changes leave the block's bypass and channel."
            );
        live.MapPut(
                "/mute",
                async (MuteRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetMutedAsync(request.Muted))
            )
            .WithName("SetMute")
            .WithSummary("Mutes or unmutes every output, the metronome and looper included; not saved anywhere.");
        live.MapPost(
                "/blocks/{id}/channels/swap",
                async (string id, CopyChannelRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SwapChannelsAsync(id, request, Expected(http)))
            )
            .WithName("SwapChannels");
        live.MapPost(
                "/blocks/{id}/channels/{channel}/reset",
                async (string id, string channel, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.ResetChannelAsync(id, channel, Expected(http)))
            )
            .WithName("ResetChannel")
            .WithSummary("Puts one channel back to the block type's defaults.");
        live.MapPost(
                "/blocks/{id}/reset",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.ResetBlockAsync(id, Expected(http)))
            )
            .WithName("ResetBlock")
            .WithSummary("Puts every channel of the block back to its type's defaults.");
    }
}
