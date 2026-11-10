using Cathode.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

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

    [Theory]
    [InlineData("setlists")]
    [InlineData("banks")]
    public async Task A_setlist_or_bank_carries_a_revision_and_a_stale_save_is_refused_with_412(string kind) {
        // Two editors read the same entry (issue #178); without If-Match the second save would replace the first.
        const string id = "shared";
        var path = $"/api/v1/{kind}/{id}";
        using var created = await client.PutAsJsonAsync(path, new { id, name = "As created" }, Token);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("\"1\"", created.Headers.ETag!.Tag);
        Assert.Equal(1, await StoredRevision(kind, id));

        using var first = await PutNamed(path, "First editor", "\"1\"");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("\"2\"", first.Headers.ETag!.Tag);

        using var stale = await PutNamed(path, "Second editor", "\"1\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        Assert.Equal(("First editor", 2), (await StoredName(kind, id), await StoredRevision(kind, id)));

        // On the revision read again it lands; the revision a body carries is the Server's to set, not the client's.
        using var current = await PutNamed(path, "Second editor", "\"2\"", 40);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(("Second editor", 3), (await StoredName(kind, id), await StoredRevision(kind, id)));

        // Without If-Match a save replaces whatever is stored, as before.
        using var unguarded = await PutNamed(path, "A script", null);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);
        Assert.Equal(4, await StoredRevision(kind, id));

        // A save made on an entry that has since been deleted does not bring it back.
        using var deleted = await client.DeleteAsync(path, Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await PutNamed(path, "Back again", "\"4\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, gone.StatusCode);
        Assert.Null(await Stored(kind, id));
    }

    async Task<HttpResponseMessage> PutNamed(string path, string name, string? ifMatch, long? revision = null) {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Content = JsonContent.Create(new { name, revision });
        if (ifMatch is not null) {
            Assert.True(request.Headers.TryAddWithoutValidation("If-Match", ifMatch));
        }

        return await client.SendAsync(request, Token);
    }

    async Task<JsonNode?> Stored(string kind, string id) =>
        (await client.GetFromJsonAsync<JsonArray>($"/api/v1/{kind}", Token))!
        .SingleOrDefault(e => e!["id"]!.GetValue<string>() == id);

    async Task<string> StoredName(string kind, string id) => (await Stored(kind, id))!["name"]!.GetValue<string>();

    async Task<long> StoredRevision(string kind, string id) => (await Stored(kind, id))!["revision"]!.GetValue<long>();

    [Theory]
    [InlineData("W/\"1\"")] // a weak tag never matches If-Match
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"latest\"")]
    [InlineData("1.5")]
    public async Task An_If_Match_that_is_not_one_revision_is_refused_with_400_and_changes_nothing(string header) {
        var before = await Live();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/live/blocks");
        request.Content = JsonContent.Create(new AddBlockRequest("volpan", 2, 1));
        Assert.True(request.Headers.TryAddWithoutValidation("If-Match", header));
        using var refused = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var after = await Live();
        Assert.Equal(before.Revision, after.Revision);
        Assert.DoesNotContain(after.Preset.Blocks, static b => b.Id == "volpan1");

        // The global settings read the header the same way.
        using var global = new HttpRequestMessage(HttpMethod.Put, "/api/v1/global");
        global.Content = JsonContent.Create(await Global());
        Assert.True(global.Headers.TryAddWithoutValidation("If-Match", header));
        using var refusedGlobal = await client.SendAsync(global, Token);
        Assert.Equal(HttpStatusCode.BadRequest, refusedGlobal.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task Without_If_Match_or_with_a_star_an_edit_applies_to_any_revision(string? header) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/live/blocks");
        request.Content = JsonContent.Create(new AddBlockRequest("volpan", 2, 1));
        if (header is not null) {
            Assert.True(request.Headers.TryAddWithoutValidation("If-Match", header));
        }

        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains((await Live()).Preset.Blocks, static b => b.Id == "volpan1");
    }

    [Fact]
    public async Task A_stale_If_Match_on_a_live_edit_is_refused_with_412_quoted_or_bare() {
        var before = await Live();
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", 2, 1));
        foreach (var header in new[] { $"\"{before.Revision}\"", $"{before.Revision}" }) {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/live/blocks");
            request.Content = JsonContent.Create(new AddBlockRequest("volpan", 2, 2));
            Assert.True(request.Headers.TryAddWithoutValidation("If-Match", header));
            using var refused = await client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        }
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
