using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Cathode.Server.Api.CathodeApi;

namespace Cathode.Server.Api;

/// <summary>The stored presets, their packages, and the Block Library.</summary>
static class PresetApi {
    public static void Map(RouteGroupBuilder api, RouteGroupBuilder live) {
        var library = api.MapGroup("/presets").WithTags("Presets");
        library.MapGet("/", (PresetStore store) => TypedResults.Ok(store.List().ToArray()))
            .WithName("ListPresets")
            .WithSummary(
                "The user's presets by name, then the read-only factory bank's (factory: true) in bank order."
            );
        library.MapGet(
                "/list.txt",
                (PresetStore store) => TypedResults.Text(PresetListText.Build(store), "text/plain; charset=utf-8")
            )
            .Produces<string>(200, "text/plain")
            .WithName("ExportPresetList")
            .WithSummary(
                "The user's presets (not the factory bank) as plain text, alphabetical, with their scene names."
            );
        library.MapGet(
                ById,
                Results<Ok<Preset>, NotFound>(string id, PresetStore store) =>
                    store.Get(id) is { } preset ? TypedResults.Ok(preset) : TypedResults.NotFound()
            )
            .WithName("GetPreset");
        library.MapDelete(
                ById,
                Results<NoContent, NotFound>(string id, Library books) =>
                    books.DeletePreset(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeletePreset");

        library.MapGet(
                "/{id}/export",
                (string id, PresetStore store, PresetPackage package, CancellationToken cancellationToken) => {
                    var preset = store.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
                    return Export(preset, package, cancellationToken);
                }
            )
            .Produces(200, contentType: "application/zip")
            .WithName("ExportPreset");
        library.MapPost(
                "/import",
                async (HttpRequest request, PresetPackage package, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await package.ImportAsync(request.Body, cancellationToken))
            )
            .Accepts<Stream>("application/zip")
            .WithName("ImportPreset")
            .WithMetadata(new RequestSizeLimitAttribute(PresetPackage.MaxPackageBytes));

        var blockPresets = api.MapGroup("/block-presets").WithTags("Block Library");
        blockPresets.MapGet("/", (string? type, BlockPresetStore store) => TypedResults.Ok(store.List(type)))
            .WithName("ListBlockPresets")
            .WithSummary("The factory and user block presets, of one block type when ?type= is given.");
        blockPresets.MapPut(
                ById,
                (string id, BlockPreset blockPreset, BlockPresetStore store) => TypedResults.Ok(
                    store.Save(id, blockPreset)
                )
            )
            .WithName("PutBlockPreset")
            .WithSummary("Saves a user block preset (factory ones cannot be changed).");
        blockPresets.MapDelete(
                ById,
                Results<NoContent, NotFound>(string id, BlockPresetStore store) =>
                    store.Delete(id) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteBlockPreset");
        live.MapPost(
                "/blocks/{id}/preset",
                async (
                    string id,
                    ApplyBlockPresetRequest request,
                    BlockPresetStore store,
                    LiveRig rig,
                    HttpContext http
                ) => {
                    var source = store.Get(request.Preset)
                        ?? throw RigException.NotFound($"No block preset '{request.Preset}'.");
                    return Respond(
                        http,
                        request.AllChannels
                            ? await rig.ApplyBlockPresetAllAsync(id, source, Expected(http))
                            : await rig.ApplyBlockPresetAsync(id, source, request.Channel, Expected(http))
                    );
                }
            )
            .WithName("ApplyBlockPreset")
            .WithSummary(
                "Sets a channel of the block (default: the active scene's) to a block preset from the Block Library."
            );
    }
}
