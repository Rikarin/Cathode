using Cathode.Contracts;
using Cathode.Ipc;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>The Server's engine link against a fake host that misbehaves as a crashing cathode-host does.</summary>
public sealed class EngineLinkTests : IAsyncDisposable {
    readonly FakeHost host = new(
        Path.Combine(Path.GetTempPath(), $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock")
    ); // Unix socket paths are limited to ~104 bytes on macOS

    CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => host.DisposeAsync();

    [Fact]
    public async Task A_host_that_dies_mid_request_neither_wedges_the_rig_nor_the_link() {
        await using var server = new ServerFixture(host.SocketPath);
        using var client = await server.ConnectedClientAsync();

        // The host dies while it compiles the graph of a structural edit, which holds the rig's lock.
        host.DieOn = static m => m.Type == IpcTypes.LoadGraph;
        using var added = await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);

        // The edit stands, as it would with no engine connected: the resync after the reconnection sends it.
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync(Token));
        var state = await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);
        Assert.Contains(state!.Preset.Blocks, static b => b.Id == "delay1");

        await ServerFixture.Eventually(async () => host.Connections >= 2
            && (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!.Engine.Connected
        );
        var resent = host.Received.LastOrDefault(static m => m.Type == IpcTypes.LoadGraph);
        Assert.Contains(resent!.Graph!.Nodes, static n => n.Id == "delay1");

        // Later edits reach the new link.
        using var removed = await client.DeleteAsync("/api/v1/live/blocks/delay1", Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync(Token));
    }
}
