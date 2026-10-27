using Cathode.Dsp;
using Cathode.Dsp.Native;
using Cathode.Ipc;
using Cathode.Server.Rig;
using System.Text.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     The Server runs no blocks and loads no native code (docs/02-architecture.md §3): what it knows about blocks and
///     the wire must not depend on the host's native stack.
/// </summary>
public sealed class LayeringTests {
    [Fact]
    public void The_servers_catalog_is_the_one_the_host_runs() {
        // The host adds the native blocks; they replace a block's code, never its descriptor or asset slots.
        var server = new BlockCatalog(BlockRegistry.CreateDefault());
        var host = new BlockCatalog(BlockRegistry.CreateDefault().AddNativeBlocks());

        Assert.Equal(Json(host.All), Json(server.All));
    }

    [Fact]
    public void The_server_and_the_ipc_contract_reference_no_engine_host_audio_or_native_code() {
        string[] forbidden = ["Cathode.EngineHost", "Cathode.Audio", "Cathode.Dsp.Native"];

        var server = typeof(Program).Assembly.GetReferencedAssemblies().Select(static a => a.Name);
        var ipc = typeof(IpcMessage).Assembly.GetReferencedAssemblies().Select(static a => a.Name);

        Assert.Empty(server.Intersect(forbidden));
        Assert.DoesNotContain(ipc, static name => name?.StartsWith("Cathode.", StringComparison.Ordinal) == true);
    }

    static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
