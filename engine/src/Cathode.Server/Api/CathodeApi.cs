using System.Globalization;
using System.Reflection;
using Cathode.Contracts;
using Cathode.Server.Engine;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cathode.Server.Api;

/// <summary>
///     REST API v1 (docs/02-architecture.md §6). Mutations accept <c>If-Match: "&lt;revision&gt;"</c> and answer with
///     the new live state and its revision as the ETag.
/// </summary>
public static class CathodeApi {
    public static void MapCathodeApi(this WebApplication app) {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RigExceptionFilter>();

        api.MapGet("/system", (EngineConnection engine) => TypedResults.Ok(new SystemInfo(Version, engine.Status())))
            .WithName("GetSystem");

        var catalog = api.MapGroup("/catalog").WithTags("Catalog");
        catalog.MapGet("/blocks", (BlockCatalog blocks) => TypedResults.Ok(blocks.All.ToArray()))
            .WithName("ListBlockTypes");
        catalog.MapGet("/blocks/{typeId}", Results<Ok<BlockTypeInfo>, NotFound> (string typeId, BlockCatalog blocks) =>
                blocks.Info(typeId) is { } info ? TypedResults.Ok(info) : TypedResults.NotFound())
            .WithName("GetBlockType");

        var live = api.MapGroup("/live").WithTags("Live");
        live.MapGet("/", async (LiveRig rig, HttpContext http) => Respond(http, await rig.StateAsync())).WithName("GetLive");
        live.MapPost("/new", async (LiveRig rig, HttpContext http) => Respond(http, await rig.NewAsync())).WithName("NewPreset");
        live.MapPut("/preset/{id}", async (string id, LiveRig rig, HttpContext http) => Respond(http, await rig.LoadAsync(id)))
            .WithName("LoadPreset");
        live.MapPut("/setlists/{id}/entries/{index:int}", async (string id, int index, Library books, LiveRig rig, HttpContext http) => {
            var entry = books.Entry(id, index);
            return Respond(http, await rig.LoadAsync(entry.PresetId, entry.Scene));
        }).WithName("LoadSetlistEntry");
        live.MapPut("/banks/{id}/slots/{slot:int}", async (string id, int slot, Library books, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.LoadAsync(books.Slot(id, slot)))).WithName("LoadBankSlot");
        live.MapGet("/export", async (LiveRig rig, PresetPackage package, CancellationToken cancellationToken) =>
                await Export((await rig.StateAsync()).Preset, package, cancellationToken))
            .Produces(200, contentType: "application/zip").WithName("ExportLive");
        live.MapPost("/save", async (SaveRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SaveAsync(request, Expected(http)))).WithName("SavePreset");
        live.MapPost("/undo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.UndoAsync())).WithName("Undo");
        live.MapPost("/redo", async (LiveRig rig, HttpContext http) => Respond(http, await rig.RedoAsync())).WithName("Redo");

        live.MapPost("/blocks", async (AddBlockRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.AddBlockAsync(request, Expected(http)))).WithName("AddBlock");
        live.MapDelete("/blocks/{id}", async (string id, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.RemoveBlockAsync(id, Expected(http)))).WithName("RemoveBlock");
        live.MapPut("/blocks/{id}/position", async (string id, MoveBlockRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.MoveBlockAsync(id, request, Expected(http)))).WithName("MoveBlock");
        live.MapPatch("/blocks/{id}/params", async (string id, string? channel, Dictionary<string, float> values, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SetParamsAsync(id, channel, values, Expected(http)))).WithName("SetParams");
        live.MapPut("/blocks/{id}/bypass", async (string id, BypassRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SetBypassAsync(id, request.Bypassed, Expected(http)))).WithName("SetBypass");
        live.MapPut("/blocks/{id}/channel", async (string id, ChannelRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SetChannelAsync(id, request.Channel, Expected(http)))).WithName("SetChannel");
        live.MapPut("/blocks/{id}/assets/{slot}", async (string id, string slot, AssetRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SetAssetAsync(id, slot, request, Expected(http)))).WithName("SetAsset");

        live.MapPost("/cables", async (CableRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.AddCableAsync(request, Expected(http)))).WithName("AddCable");
        live.MapDelete("/cables", async (string from, string to, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.RemoveCableAsync(new(from, to), Expected(http)))).WithName("RemoveCable");

        live.MapPut("/scenes/active", async (SceneSelectRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.SelectSceneAsync(request.Scene))).WithName("SelectScene");
        live.MapPatch("/scenes/{scene:int}", async (int scene, SceneUpdateRequest request, LiveRig rig, HttpContext http) =>
            Respond(http, await rig.UpdateSceneAsync(scene, request, Expected(http)))).WithName("UpdateScene");

        var library = api.MapGroup("/presets").WithTags("Presets");
        library.MapGet("/", (PresetStore store) => TypedResults.Ok(store.List().ToArray())).WithName("ListPresets");
        library.MapGet("/{id}", Results<Ok<Preset>, NotFound> (string id, PresetStore store) =>
            store.Get(id) is { } preset ? TypedResults.Ok(preset) : TypedResults.NotFound()).WithName("GetPreset");
        library.MapPut("/{id}", (string id, Preset preset, PresetStore store) => {
            preset.Id = id;
            store.Save(preset);
            return TypedResults.NoContent();
        }).WithName("PutPreset");
        library.MapDelete("/{id}", Results<NoContent, NotFound> (string id, Library books) =>
            books.DeletePreset(id) ? TypedResults.NoContent() : TypedResults.NotFound()).WithName("DeletePreset");

        library.MapGet("/{id}/export", (string id, PresetStore store, PresetPackage package, CancellationToken cancellationToken) => {
            var preset = store.Get(id) ?? throw RigException.NotFound($"No preset '{id}'.");
            return Export(preset, package, cancellationToken);
        }).Produces(200, contentType: "application/zip").WithName("ExportPreset");
        library.MapPost("/import", async (HttpRequest request, PresetPackage package, CancellationToken cancellationToken) =>
                TypedResults.Ok(await package.ImportAsync(request.Body, cancellationToken)))
            .Accepts<Stream>("application/zip")
            .WithName("ImportPreset")
            .WithMetadata(new RequestSizeLimitAttribute(PresetPackage.MaxPackageBytes));

        var setlists = api.MapGroup("/setlists").WithTags("Setlists");
        setlists.MapGet("/", (Library books) => TypedResults.Ok(books.Setlists())).WithName("ListSetlists");
        setlists.MapGet("/{id}", Results<Ok<Setlist>, NotFound> (string id, Library books) =>
            books.Setlist(id) is { } setlist ? TypedResults.Ok(setlist) : TypedResults.NotFound()).WithName("GetSetlist");
        setlists.MapPut("/{id}", (string id, Setlist setlist, Library books) => {
            setlist.Id = id;
            books.Save(setlist);
            return TypedResults.Ok(setlist);
        }).WithName("PutSetlist");
        setlists.MapDelete("/{id}", Results<NoContent, NotFound> (string id, Library books) =>
            books.DeleteSetlist(id) ? TypedResults.NoContent() : TypedResults.NotFound()).WithName("DeleteSetlist");

        var banks = api.MapGroup("/banks").WithTags("Banks");
        banks.MapGet("/", (Library books) => TypedResults.Ok(books.Banks())).WithName("ListBanks");
        banks.MapGet("/{id}", Results<Ok<Bank>, NotFound> (string id, Library books) =>
            books.Bank(id) is { } bank ? TypedResults.Ok(bank) : TypedResults.NotFound()).WithName("GetBank");
        banks.MapPut("/{id}", (string id, Bank bank, Library books) => {
            bank.Id = id;
            books.Save(bank);
            return TypedResults.Ok(bank);
        }).WithName("PutBank");
        banks.MapDelete("/{id}", Results<NoContent, NotFound> (string id, Library books) =>
            books.DeleteBank(id) ? TypedResults.NoContent() : TypedResults.NotFound()).WithName("DeleteBank");

        var assets = api.MapGroup("/assets").WithTags("Assets");
        assets.MapGet("/", (AssetStore store) => TypedResults.Ok(store.List().ToArray())).WithName("ListAssets");
        assets.MapPost("/", async (string name, HttpRequest request, AssetStore store, CancellationToken cancellationToken) =>
                TypedResults.Ok(await store.AddAsync(request.Body, name, cancellationToken)))
            .Accepts<Stream>("application/octet-stream")
            .WithName("UploadAsset");
        assets.MapDelete("/{reference}", Results<NoContent, NotFound> (string reference, AssetStore store) =>
            store.Delete(reference) ? TypedResults.NoContent() : TypedResults.NotFound()).WithName("DeleteAsset");

        api.MapPut("/tuner", (BypassRequest request, IEngineSync engine) => {
            engine.SetTuner(!request.Bypassed);
            return TypedResults.NoContent();
        }).WithName("SetTuner").WithSummary("Turns the tuner on (bypassed = false) or off.");

        app.Map("/ws", async (HttpContext http, EventHub hub) => {
            if (!http.WebSockets.IsWebSocketRequest) {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await hub.ServeAsync(socket, http.RequestAborted);
        }).ExcludeFromDescription();
    }

    /// <summary>
    ///     Builds the package in a temporary file and streams that: ZipArchive writes some parts synchronously, which
    ///     Kestrel forbids on a response, and a file keeps large models out of memory. The file deletes itself on close.
    /// </summary>
    static async Task<IResult> Export(Preset preset, PresetPackage package, CancellationToken cancellationToken) {
        var name = string.Concat(preset.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try {
            await package.ExportAsync(preset, file, cancellationToken);
            file.Position = 0;
            return Results.File(file, "application/zip", (name.Length > 0 ? name : "preset") + PresetPackage.Extension);
        } catch {
            await file.DisposeAsync();
            throw;
        }
    }

    static string Version =>
        typeof(CathodeApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    static Ok<LiveState> Respond(HttpContext http, LiveState state) {
        http.Response.Headers.ETag = $"\"{state.Revision.ToString(CultureInfo.InvariantCulture)}\"";
        return TypedResults.Ok(state);
    }

    /// <summary>The revision from <c>If-Match</c>, if the client sent one.</summary>
    static long? Expected(HttpContext http) {
        var header = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision) ? revision : null;
    }
}
