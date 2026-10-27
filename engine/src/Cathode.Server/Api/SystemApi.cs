using Cathode.Contracts;
using Cathode.Server.Engine;
using Cathode.Server.Rig;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection;

namespace Cathode.Server.Api;

/// <summary>The system information, latency report and block catalog endpoints.</summary>
static class SystemApi {
    static string Version =>
        typeof(CathodeApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";

    public static void Map(RouteGroupBuilder api) {
        api.MapGet("/system", (EngineConnection engine) => TypedResults.Ok(new SystemInfo(Version, engine.Status())))
            .WithName("GetSystem");

        api.MapGet("/system/latency", async (EngineConnection engine) => TypedResults.Ok(await engine.LatencyAsync()))
            .WithName("GetLatency")
            .WithSummary(
                "The latency from instrument to speaker: the device's input and output latency, the buffer, and "
                + "the graph's latency along its longest path, block by block."
            );

        var catalog = api.MapGroup("/catalog").WithTags("Catalog");
        catalog.MapGet("/blocks", (BlockCatalog blocks) => TypedResults.Ok(blocks.All.ToArray()))
            .WithName("ListBlockTypes");
        catalog.MapGet(
                "/blocks/{typeId}",
                Results<Ok<BlockTypeInfo>, NotFound>(string typeId, BlockCatalog blocks) =>
                    blocks.Info(typeId) is { } info ? TypedResults.Ok(info) : TypedResults.NotFound()
            )
            .WithName("GetBlockType");

        catalog.MapGet("/rig", (BlockCatalog blocks) => TypedResults.Ok(blocks.Rig))
            .WithName("GetRigControls")
            .WithSummary("The rig parameters: controller settings, scene level, tempo and modifier sources.");
    }
}
