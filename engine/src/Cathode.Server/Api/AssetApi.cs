using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cathode.Server.Api;

/// <summary>The asset library: uploads (with IR processing), IR mixes, deletion and file content.</summary>
static class AssetApi {
    public static void Map(RouteGroupBuilder api) {
        var assets = api.MapGroup("/assets").WithTags("Assets");
        assets.MapGet("/", (AssetStore store) => TypedResults.Ok(store.List().ToArray())).WithName("ListAssets");
        assets.MapPost(
                "/",
                async (
                    string name,
                    HttpRequest request,
                    AssetStore store,
                    CancellationToken cancellationToken,
                    string? processing,
                    bool? polarity,
                    string? channels,
                    double? maxLengthMs
                ) => {
                    var import = AssetStore.ImportSettings(processing, polarity, channels, maxLengthMs);
                    if (request.ContentLength > AssetStore.MaxBytes) {
                        throw AssetStore.TooLarge();
                    }

                    try {
                        return TypedResults.Ok(await store.AddAsync(request.Body, name, import, cancellationToken));
                    } catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge) {
                        throw AssetStore.TooLarge(); // a chunked body past the request limit
                    }
                }
            )
            .Accepts<Stream>("application/octet-stream")
            // The store's own limit, not Kestrel's default of 30,000,000 bytes.
            .WithMetadata(new RequestSizeLimitAttribute(AssetStore.MaxBytes))
            .WithName("UploadAsset")
            .WithSummary(
                "Uploads an asset. A .wav IR can be processed on import: processing (asIs, autoTrim, "
                + "minimumPhase), polarity (make the main peak positive), channels (both, left, right, sum) and "
                + "maxLengthMs."
            );
        assets.MapPost(
                "/mix",
                async (IrMixRequest request, IrTools tools, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tools.MixAsync(request, cancellationToken))
            )
            .WithName("MixImpulseResponses")
            .WithSummary("Blends impulse responses (levels, delays, polarity, peak alignment) into a new IR asset.");
        assets.MapDelete(
                "/{reference}",
                Results<NoContent, NotFound>(string reference, AssetStore store) =>
                    store.Delete(reference) ? TypedResults.NoContent() : TypedResults.NotFound()
            )
            .WithName("DeleteAsset");
        assets.MapGet(
                "/{reference}/content",
                Results<FileStreamHttpResult, NotFound>(string reference, AssetStore store) =>
                    store.Get(reference) is { } info && store.PathOf(reference) is { } path && File.Exists(path)
                        ? TypedResults.File(
                            File.OpenRead(path),
                            info.Kind switch {
                                "ir" => "audio/wav",
                                "cabgrid" => "application/zip",
                                _ => "application/json"
                            },
                            info.Name
                        )
                        : TypedResults.NotFound()
            )
            .WithName("GetAssetContent")
            .WithSummary("The asset's file, e.g. an impulse response for the editor's frequency-response plot.");
    }
}
