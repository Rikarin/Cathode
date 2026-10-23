using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cathode.Contracts;

namespace Cathode.Server.Tests;

public sealed class RealtimeAndContractTests : IAsyncLifetime {
    /// <summary>Committed OpenAPI document; the editor's TypeScript client is generated from it.</summary>
    static readonly string OpenApiPath = Path.Combine(EngineRoot(), "src", "Cathode.Server", "openapi.json");

    readonly ServerFixture server = new();
    HttpClient client = null!;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await client.PostAsJsonAsync("/api/v1/live/new", new { }, Token);
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task WebSocket_clients_get_change_events_and_telemetry() {
        using var socket = await server.Server.CreateWebSocketClient().ConnectAsync(new Uri(server.Server.BaseAddress, "ws"), Token);
        var buffer = new byte[64 * 1024];
        var first = await socket.ReceiveAsync(buffer, Token);
        Assert.Equal("connected", JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, first.Count))!["type"]!.GetValue<string>());

        (await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("volpan", 0, 1), Token)).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync("/api/v1/live/blocks/volpan1/params", new Dictionary<string, float> { ["level"] = -6 }, Token)).EnsureSuccessStatusCode();

        var seen = new HashSet<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!(seen.Contains("blockAdded") && seen.Contains("paramChanged") && seen.Contains("telemetry"))) {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
            seen.Add(message["type"]!.GetValue<string>());
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
    }

    [Fact]
    public async Task OpenApi_document_matches_the_committed_copy() {
        var current = Normalise(await client.GetStringAsync("/openapi/v1.json", Token));

        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_OPENAPI") == "1" || !File.Exists(OpenApiPath)) {
            await File.WriteAllTextAsync(OpenApiPath, current, Token);
        }

        var committed = Normalise(await File.ReadAllTextAsync(OpenApiPath, Token));
        Assert.True(
            committed == current,
            "The API changed. Review the difference, then update the committed document with CATHODE_UPDATE_OPENAPI=1 dotnet test."
        );
        Assert.Contains("\"/api/v1/live/blocks/{id}/params\"", current, StringComparison.Ordinal);
    }

    static string Normalise(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder.");
    }
}
