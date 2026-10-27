using Cathode.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

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
    public async Task Stopping_the_server_closes_its_websockets_with_a_close_frame() {
        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        var buffer = new byte[64 * 1024];

        // A launcher's stop (SIGTERM, or the desktop app closing standard input) must not wait on open sockets.
        server.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        WebSocketReceiveResult result;
        do {
            result = await socket.ReceiveAsync(buffer, deadline.Token);
        } while (result.MessageType != WebSocketMessageType.Close);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, result.CloseStatus);
    }

    [Fact]
    public async Task WebSocket_clients_get_change_events_and_telemetry() {
        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        var buffer = new byte[64 * 1024];
        var first = await socket.ReceiveAsync(buffer, Token);
        Assert.Equal(
            "connected",
            JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, first.Count))!["type"]!.GetValue<string>()
        );

        (await client.PostAsJsonAsync(
            "/api/v1/live/blocks",
            new AddBlockRequest("volpan", 0, 1),
            Token
        )).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync(
            "/api/v1/live/blocks/volpan1/params",
            new Dictionary<string, float> { ["level"] = -6 },
            Token
        )).EnsureSuccessStatusCode();

        var seen = new HashSet<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // generous: a busy machine; frames come every 33 ms
        while (!(seen.Contains("blockAdded") && seen.Contains("paramChanged") && seen.Contains("telemetry"))) {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
            seen.Add(message["type"]!.GetValue<string>());
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
    }

    [Fact]
    public async Task Scope_frames_flow_only_while_the_scope_is_on() {
        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        var buffer = new byte[256 * 1024];
        (await client.PutAsJsonAsync("/api/v1/scope", new ScopeRequest(true), Token)).EnsureSuccessStatusCode();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // generous: a busy machine; frames come every 33 ms
        JsonNode frame;
        do {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            frame = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
        } while (frame["type"]!.GetValue<string>() != "scope");

        Assert.Equal(512, frame["samples"]!.AsArray().Count);
        Assert.Equal(12_000, frame["sampleRate"]!.GetValue<double>());
        (await client.PutAsJsonAsync("/api/v1/scope", new ScopeRequest(false), Token)).EnsureSuccessStatusCode();
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
            + FirstDifference(committed, current)
        );
        Assert.Contains("\"/api/v1/live/blocks/{id}/params\"", current, StringComparison.Ordinal);
    }

    static string FirstDifference(string committed, string current) {
        var a = committed.Split('\n');
        var b = current.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++) {
            var left = i < a.Length ? a[i] : "(end)";
            var right = i < b.Length ? b[i] : "(end)";
            if (left != right) {
                return $"\nFirst difference at line {i + 1}:\n  committed: {left}\n  current:   {right}";
            }
        }

        return string.Empty;
    }

    /// <summary>Canonical form; XML doc text carries the platform's line endings into descriptions, so unify them.</summary>
    static string Normalise(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new() { WriteIndented = true })
            .Replace("""\r\n""", """\n""", StringComparison.Ordinal)
        + "\n";

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder.");
    }
}
