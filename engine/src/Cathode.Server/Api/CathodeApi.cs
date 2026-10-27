using Cathode.Contracts;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Globalization;

namespace Cathode.Server.Api;

/// <summary>
///     REST API v1 (docs/02-architecture.md §6). Mutations accept <c>If-Match: "&lt;revision&gt;"</c> and answer with
///     the new live state and its revision as the ETag.
/// </summary>
public static class CathodeApi {
    /// <summary>The route of one item of a collection, by id.</summary>
    internal const string ById = "/{id}";

    public static void MapCathodeApi(this WebApplication app) {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RigExceptionFilter>();
        SystemApi.Map(api);
        var live = LiveApi.Map(api);
        PresetApi.Map(api, live);
        LibraryApi.Map(api);
        ControlsApi.Map(api, live);
        WorkflowApi.MapWorkflow(api, live);
        AssetApi.Map(api);
        AnalysisApi.Map(app, api, live);
    }

    /// <summary>
    ///     Builds the package in a temporary file and streams that: ZipArchive writes some parts synchronously, which
    ///     Kestrel forbids on a response, and a file keeps large models out of memory. The file deletes itself on
    ///     close.
    /// </summary>
    internal static async Task<IResult> Export(
        Preset preset,
        PresetPackage package,
        CancellationToken cancellationToken
    ) {
        var name = string.Concat(preset.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        var file = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            81_920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous
        );
        try {
            await package.ExportAsync(preset, file, cancellationToken);
            file.Position = 0;
            return Results.File(file, "application/zip", (name.Length > 0 ? name : "preset") + PresetPackage.Extension);
        } catch {
            await file.DisposeAsync();
            throw;
        }
    }

    internal static Ok<LiveState> Respond(HttpContext http, LiveState state) {
        http.Response.Headers.ETag = $"\"{state.Revision.ToString(CultureInfo.InvariantCulture)}\"";
        return TypedResults.Ok(state);
    }

    /// <summary>The revision from <c>If-Match</c>, if the client sent one.</summary>
    internal static long? Expected(HttpContext http) {
        var header = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision)
            ? revision
            : null;
    }
}
