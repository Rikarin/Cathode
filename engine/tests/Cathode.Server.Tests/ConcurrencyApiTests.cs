using Cathode.Contracts;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     Edits made on state another client or the rig has changed since: a stale replacement of the global settings,
///     and a knob value made on an earlier preset load (issue #75).
/// </summary>
public sealed class ConcurrencyApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Post("/api/v1/live/new");
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Global_settings_carry_a_revision_and_a_stale_replacement_is_refused_with_412() {
        using var read = await client.GetAsync("/api/v1/global", Token);
        var etag = read.Headers.ETag!;
        var global = (await read.Content.ReadFromJsonAsync<GlobalSettings>(Token))!;

        // Another client (here the rig itself, a Manual knob) changes the settings after this one read them.
        await Ok(client.PutAsJsonAsync("/api/v1/live/manual/2", new ValueRequest(0.5f), Token));
        global.Metronome = !global.Metronome;
        using var stale = new HttpRequestMessage(HttpMethod.Put, "/api/v1/global");
        stale.Content = JsonContent.Create(global);
        stale.Headers.IfMatch.Add(etag);
        using var refused = await client.SendAsync(stale, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Equal(0.5f, (await Global()).Manual[2]); // the other change was not put back

        // On the revision just read, the change lands and answers with the next revision.
        using var reread = await client.GetAsync("/api/v1/global", Token);
        var fresh = (await reread.Content.ReadFromJsonAsync<GlobalSettings>(Token))!;
        fresh.Metronome = !fresh.Metronome;
        using var current = new HttpRequestMessage(HttpMethod.Put, "/api/v1/global");
        current.Content = JsonContent.Create(fresh);
        current.Headers.IfMatch.Add(reread.Headers.ETag!);
        using var accepted = await client.SendAsync(current, Token);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.NotEqual(reread.Headers.ETag, accepted.Headers.ETag);
        Assert.Equal(fresh.Metronome, (await Global()).Metronome);
    }

    [Fact]
    public async Task A_param_change_made_on_an_earlier_preset_load_is_refused_with_412() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", 2, 1));
        var before = await Live();

        // Made on this load, it lands.
        var landed = await Patch(
            $"/api/v1/live/blocks/volpan1/params?load={before.Load}",
            new Dictionary<string, float> { ["level"] = -3 }
        );
        Assert.Equal(-3f, landed.Preset.Blocks.Single(static b => b.Id == "volpan1").Channels["A"]["level"]);

        // Another preset is loaded (a program change, another editor): a knob value still on the wire is refused.
        var loaded = await Post("/api/v1/live/new");
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", 2, 1));
        Assert.NotEqual(before.Load, loaded.Load);
        using var stale = await client.PatchAsJsonAsync(
            $"/api/v1/live/blocks/volpan1/params?load={before.Load}",
            new Dictionary<string, float> { ["level"] = -9 },
            Token
        );

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var volpan = (await Live()).Preset.Blocks.Single(static b => b.Id == "volpan1");
        Assert.False(volpan.Channels.TryGetValue("A", out var values) && values.ContainsKey("level"));
    }

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task<LiveState> Live() => (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

    async Task<LiveState> Post(string url, object? body = null) =>
        await Read(await client.PostAsJsonAsync(url, body ?? new { }, Token));

    async Task<LiveState> Patch(string url, object body) => await Read(await client.PatchAsJsonAsync(url, body, Token));

    async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    async Task<LiveState> Read(HttpResponseMessage response) {
        using (response) {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
            return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
        }
    }
}
