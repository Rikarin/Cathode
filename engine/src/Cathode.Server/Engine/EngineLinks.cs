using Cathode.Engine.Telemetry;
using Cathode.Ipc;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Engine;

/// <summary>
///     Opens links to the engine host for <see cref="EngineConnection" />. The Server's own is
///     <see cref="IpcEngineLinks" />; the tests replace it with an engine host in the same process, which the Server
///     itself does not reference (docs/02-architecture.md §3).
/// </summary>
public interface IEngineLinks {
    /// <summary>The engine mode <c>/api/v1/system</c> reports: <c>ipc</c>, or <c>inprocess</c> in tests.</summary>
    string Mode { get; }

    /// <summary>A connected link; retries until the host is up or <paramref name="cancellationToken" /> ends.</summary>
    Task<IEngineLink> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>
///     A link to an engine host in this process, whose telemetry region the Server reads directly instead of mapping
///     the file named in the host's <c>hello</c>.
/// </summary>
public interface ILocalEngineLink : IEngineLink {
    TelemetryRegion TelemetryRegion { get; }
}

/// <summary>Production: a separate cathode-host process on the configured Unix domain socket.</summary>
public sealed class IpcEngineLinks(IOptions<ServerOptions> options) : IEngineLinks {
    public string Mode => "ipc";

    public async Task<IEngineLink> ConnectAsync(CancellationToken cancellationToken) {
        var client = new IpcEngineClient(options.Value.Engine.SocketPath);
        await client.ConnectAsync(TimeSpan.FromDays(365), cancellationToken); // keeps retrying until the host is up
        return client;
    }
}
