using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>
///     The editor's workflow endpoints (docs/02-architecture.md §7.5): revert and clear, snapshots, templates, grid
///     operations, all-scene actions, the Library's whole-block entries and files, preset tools, performance pages and
///     footswitch overrides. Live edits answer with the new state, like the rest of <c>/live</c>, and are undoable.
/// </summary>
static class WorkflowApi {
    public static void MapWorkflow(RouteGroupBuilder api, RouteGroupBuilder live) {
        live.MapPost(
                "/revert",
                async (LiveRig rig, HttpContext http) => Respond(http, await rig.RevertAsync(Expected(http)))
            )
            .WithName("RevertPreset")
            .WithSummary("Discards the edits: the working copy goes back to the saved version (undoable).");
        live.MapPost(
                "/clear",
                async (LiveRig rig, HttpContext http) => Respond(http, await rig.ClearAsync(Expected(http)))
            )
            .WithName("ClearPreset")
            .WithSummary("Empties the grid and the name, keeping the preset's id (undoable).");

        live.MapPost(
                "/snapshots",
                async (SnapshotRequest request, LiveRig rig, SnapshotStore store) =>
                    TypedResults.Ok(store.Add((await rig.StateAsync()).Preset, request.Name))
            )
            .WithName("TakeSnapshot")
            .WithSummary("Stores the working copy, unsaved edits included, as a timestamped snapshot.");
        live.MapPost(
                "/snapshots/{id}/load",
                async (string id, SnapshotStore store, LiveRig rig, HttpContext http) => {
                    var snapshot = store.Get(id) ?? throw RigException.NotFound($"No snapshot '{id}'.");
                    return Respond(http, await rig.LoadSnapshotAsync(snapshot, Expected(http)));
                }
            )
            .WithName("LoadSnapshot")
            .WithSummary("Puts a snapshot into the working copy (undoable).");
        var snapshots = api.MapGroup("/snapshots").WithTags("Snapshots");
        snapshots.MapGet("/", (string? preset, SnapshotStore store) => TypedResults.Ok(store.List(preset)))
            .WithName("ListSnapshots")
            .WithSummary("Snapshots, newest first; of one preset when ?preset= is given.");
        snapshots.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, SnapshotStore store) =>
                    store.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteSnapshot");

        live.MapPost(
                "/templates",
                async (TemplateRequest request, LiveRig rig, TemplateStore store) =>
                    TypedResults.Ok(store.Add((await rig.StateAsync()).Preset, request.Name))
            )
            .WithName("SaveTemplate")
            .WithSummary("Saves the working copy as a template.");
        live.MapPost(
                "/templates/{id}/new",
                async (string id, TemplateStore store, LiveRig rig, HttpContext http) => {
                    var template = store.Get(id) ?? throw RigException.NotFound($"No template '{id}'.");
                    return Respond(http, await rig.NewFromTemplateAsync(template));
                }
            )
            .WithName("NewFromTemplate")
            .WithSummary("Starts a new preset (a fresh id) from a template.");
        var templates = api.MapGroup("/templates").WithTags("Templates");
        templates.MapGet("/", (TemplateStore store) => TypedResults.Ok(store.List())).WithName("ListTemplates");
        templates.MapDelete(
                "/{id}",
                Results<NoContent, NotFound>(string id, TemplateStore store) =>
                    store.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteTemplate");

        live.MapPut(
                "/blocks/{id}/type",
                async (string id, ReplaceBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.ReplaceBlockAsync(id, request.Type, Expected(http)))
            )
            .WithName("ReplaceBlock")
            .WithSummary("Replaces a block with a new block of another type in its cell, keeping its cables.");
        live.MapPost(
                "/blocks/{id}/drop",
                async (string id, DropBlockRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.DropBlockAsync(id, request, Expected(http)))
            )
            .WithName("DropBlock")
            .WithSummary(
                "Drops a block on another: swap exchanges their cells (the cables stay put), replace takes the target's cell and cables."
            );
        live.MapPost(
                "/grid/move",
                async (MoveLineRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.MoveLineAsync(request, Expected(http)))
            )
            .WithName("MoveGridLine")
            .WithSummary("Moves a whole row or column, shifting the ones between.");
        live.MapDelete(
                "/blocks/{id}/cables",
                async (string id, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.DisconnectAsync(id, Expected(http)))
            )
            .WithName("DisconnectBlock")
            .WithSummary("Removes every cable into and out of a block.");
        live.MapPut(
                "/blocks/{id}/settings",
                async (string id, BlockSettingsRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.PasteSettingsAsync(id, request, Expected(http)))
            )
            .WithName("PasteBlockSettings")
            .WithSummary("Replaces every channel (and the assets) of a block with another same-type block's.");
        live.MapPut(
                "/blocks/{id}/all-scenes",
                async (string id, AllScenesRequest request, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetAllScenesAsync(id, request, Expected(http)))
            )
            .WithName("SetBlockInAllScenes")
            .WithSummary("Sets a block's bypass and/or channel in every scene.");
        live.MapPut(
                "/perform",
                async (List<PerformControl> controls, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetPerformAsync(controls, Expected(http)))
            )
            .WithName("SetPerformPage")
            .WithSummary("Replaces the preset's performance page.");
        live.MapPut(
                "/footswitch-overrides",
                async (List<FootswitchOverride> overrides, LiveRig rig, HttpContext http) =>
                    Respond(http, await rig.SetFootswitchOverridesAsync(overrides, Expected(http)))
            )
            .WithName("SetFootswitchOverrides")
            .WithSummary("Replaces the preset's own footswitch actions.");
        live.MapPost(
                "/block-presets",
                async (SaveAllBlocksRequest request, LiveRig rig, BlockPresetStore store, BlockCatalog catalog) =>
                    TypedResults.Ok(SaveAll((await rig.StateAsync()).Preset, request, store, catalog))
            )
            .WithName("SaveAllBlocks")
            .WithSummary("Saves every block of the working copy (all channels and modifiers) to the Block Library.");

        var presets = api.MapGroup("/presets").WithTags("Presets");
        presets.MapPatch(
                "/{id}/metadata",
                async (string id, PresetMetadataRequest request, LiveRig rig) => {
                    await rig.SetPresetMetadataAsync(id, request);
                    return TypedResults.NoContent();
                }
            )
            .WithName("SetPresetMetadata")
            .WithSummary("Renames a preset or sets its colour tag or favourite flag, stored and live.");
        presets.MapPost(
                "/{id}/duplicate",
                (string id, DuplicatePresetRequest request, PresetTools tools) =>
                    TypedResults.Ok(tools.Duplicate(id, request))
            )
            .WithName("DuplicatePreset");
        presets.MapPost(
                "/reset-amps",
                (PresetBatchRequest request, PresetTools tools) => TypedResults.Ok(tools.ResetAmps(request.Presets))
            )
            .WithName("ResetAmpBlocks")
            .WithSummary(
                "Sets every Circuit Amp parameter of the presets back to its default, except the model's "
                + "front-panel controls."
            );
        presets.MapPost(
                "/batch-set",
                (BatchSetRequest request, PresetTools tools) => TypedResults.Ok(tools.BatchSet(request))
            )
            .WithName("BatchSetParameter")
            .WithSummary("Sets one parameter on the matching block (same id and type) of each preset.");

        api.MapPatch(
                "/assets/{reference}/metadata",
                (string reference, AssetMetadataRequest request, AssetStore store) =>
                    TypedResults.Ok(store.Update(reference, request))
            )
            .WithTags("Assets")
            .WithName("SetAssetMetadata")
            .WithSummary("Renames an asset or sets its colour tag or favourite flag.");
        api.MapGet("/assets/usage", (PresetStore presets) => TypedResults.Ok(presets.AssetUsage()))
            .WithTags("Assets")
            .WithName("GetAssetUsage")
            .WithSummary("Which stored presets use each asset, so a library can say what deleting one would break.");

        var blockPresets = api.MapGroup("/block-presets").WithTags("Block Library");
        blockPresets.MapPost(
                "/export",
                (BlockPresetExportRequest request, BlockPresetStore store) => TypedResults.Ok(store.Export(request.Ids))
            )
            .WithName("ExportBlockPresets")
            .WithSummary("Block Library entries as a .cthblock file.");
        blockPresets.MapPost(
                "/import",
                (BlockPresetFile file, BlockPresetStore store) => TypedResults.Ok(store.Import(file))
            )
            .WithName("ImportBlockPresets")
            .WithSummary("Adds the entries of a .cthblock file as new user entries.");
    }

    static BlockPreset[] SaveAll(
        Preset preset,
        SaveAllBlocksRequest request,
        BlockPresetStore store,
        BlockCatalog catalog
    ) {
        var scene = preset.Scenes[0];
        var title = preset.Name.Length > 0 ? preset.Name : "Untitled";
        return [
            ..preset.Blocks.Where(b => catalog.Descriptor(b.Type) is not null && !BuiltInTypes.IsBuiltIn(b.Type))
                .Select(b => store.Save(
                        Preset.NewId(),
                        new() {
                            Type = b.Type,
                            Name = $"{title} {catalog.Info(b.Type)!.Name} ({b.Id})",
                            Folder = string.IsNullOrWhiteSpace(request.Folder) ? preset.Name : request.Folder,
                            Values = new(b.Channels.GetValueOrDefault(scene.StateOf(b.Id).Channel) ?? []),
                            Channels = b.Channels.ToDictionary(
                                static c => c.Key,
                                static c => new Dictionary<string, float>(c.Value)
                            ),
                            Modifiers = [..preset.Modifiers.Where(m => m.Block == b.Id)],
                            Assets = new(b.Assets)
                        }
                    )
                )
        ];
    }
}
