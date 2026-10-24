using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Cathode.Contracts;
using Cathode.Server.Api;

namespace Cathode.Server.Tests;

public class AccessTests {
    const string Token = "s3cret-token";

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_a_token_every_request_must_present_it() {
        await using var server = new ServerFixture(token: Token);
        using var anonymous = server.CreateClient();
        using var wrong = server.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new("Bearer", "guess");
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token);

        using var denied = await anonymous.GetAsync("/api/v1/system", Cancel);
        using var guessed = await wrong.GetAsync("/api/v1/system", Cancel);
        using var allowed = await client.GetAsync("/api/v1/system", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("Bearer", denied.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(HttpStatusCode.Unauthorized, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task With_a_token_the_websocket_takes_it_as_a_query_parameter_and_any_origin_may_call() {
        await using var server = new ServerFixture(token: Token);
        var sockets = server.Server.CreateWebSocketClient();

        await Assert.ThrowsAnyAsync<Exception>(() => sockets.ConnectAsync(new("ws://localhost/ws"), Cancel));
        using var socket = await sockets.ConnectAsync(new($"ws://localhost/ws?access_token={Token}"), Cancel);
        using var client = server.CreateClient();
        using var preflight = await client.SendAsync(Preflight("app://cathode"), Cancel);

        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal("app://cathode", preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("http://localhost:4200", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("https://evil.example", false)]
    public async Task Without_a_token_only_loopback_origins_pass(string origin, bool allowed) {
        await using var server = new ServerFixture();
        using var client = server.CreateClient();

        using var preflight = await client.SendAsync(Preflight(origin), Cancel);
        var request = new HttpRequestMessage(HttpMethod.Get, "/ws");
        request.Headers.Add("Origin", origin);
        using var socket = await client.SendAsync(request, Cancel);

        Assert.Equal(allowed, preflight.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(allowed ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, socket.StatusCode);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5180", null, true)]
    [InlineData("http://localhost:5180;http://[::1]:5180", null, true)]
    [InlineData("http://0.0.0.0:5180", null, false)]
    [InlineData("http://*:5180", null, false)]
    [InlineData("http://192.168.1.20:5180", "token", true)]
    public void Listening_beyond_loopback_needs_a_token(string urls, string? token, bool ok) {
        var error = Record.Exception(() => Access.RequireTokenBeyondLoopback(urls, token));

        Assert.Equal(ok, error is null);
    }

    [Fact]
    public async Task Put_live_replaces_the_content_undoably_and_keeps_the_identity() {
        await using var server = new ServerFixture();
        using var client = await server.ConnectedClientAsync();
        var before = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Cancel))!;
        var next = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Cancel))!.Preset;
        next.Id = "ignored";
        next.Name = "Version B";
        next.Blocks.Add(new() { Id = "delay1", Type = "delay", Row = 1, Column = 3 });

        using var put = await client.PutAsJsonAsync("/api/v1/live", next, Cancel);
        var replaced = (await put.Content.ReadFromJsonAsync<LiveState>(Cancel))!;
        using var undo = await client.PostAsync("/api/v1/live/undo", null, Cancel);
        var undone = (await undo.Content.ReadFromJsonAsync<LiveState>(Cancel))!;

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(before.Preset.Id, replaced.Preset.Id);
        Assert.Equal("Version B", replaced.Preset.Name);
        Assert.Contains(replaced.Preset.Blocks, b => b.Id == "delay1");
        Assert.True(replaced.CanUndo);
        Assert.Equal(before.Preset.Name, undone.Preset.Name);
        Assert.DoesNotContain(undone.Preset.Blocks, b => b.Id == "delay1");
    }

    static HttpRequestMessage Preflight(string origin) {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PATCH");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,if-match,content-type");
        return request;
    }
}
