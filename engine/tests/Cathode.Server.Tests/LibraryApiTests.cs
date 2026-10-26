using Cathode.Contracts;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

public sealed class LibraryApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;
    string verse = string.Empty;
    string chorus = string.Empty;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        verse = await SavePreset("Verse", "chorus", 0, 5);
        chorus = await SavePreset("Chorus", "delay", 0, 6);
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Setlist_entries_load_their_preset_on_their_scene() {
        var setlist = new Setlist { Name = "Friday", Entries = [new(verse, 0, "Intro"), new(chorus, 3, "Big chorus")] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));

        var state = await Read(client.PutAsJsonAsync($"/api/v1/live/setlists/{setlist.Id}/entries/1", new { }, Token));

        Assert.Equal(chorus, state.Preset.Id);
        Assert.Equal(3, state.ActiveScene);
        Assert.Single((await client.GetFromJsonAsync<Setlist[]>("/api/v1/setlists", Token))!);
    }

    [Fact]
    public async Task Bank_slots_load_presets_like_program_changes() {
        var bank = new Bank { Name = "Gig", Slots = [verse, null, chorus] };
        await Ok(client.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));

        var state = await Read(client.PutAsJsonAsync($"/api/v1/live/banks/{bank.Id}/slots/2", new { }, Token));
        using var empty = await client.PutAsJsonAsync($"/api/v1/live/banks/{bank.Id}/slots/1", new { }, Token);

        Assert.Equal(chorus, state.Preset.Id);
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
    }

    [Fact]
    public async Task References_must_exist_and_used_presets_cannot_be_deleted() {
        var broken = new Setlist { Entries = [new("does-not-exist"), new(verse, 9)] };
        using var rejected = await client.PutAsJsonAsync($"/api/v1/setlists/{broken.Id}", broken, Token);
        var tooBig = new Bank { Slots = [..Enumerable.Repeat<string?>(null, 129)] };
        using var rejectedBank = await client.PutAsJsonAsync($"/api/v1/banks/{tooBig.Id}", tooBig, Token);
        var setlist = new Setlist { Name = "Saturday", Entries = [new(verse)] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));

        using var inUse = await client.DeleteAsync($"/api/v1/presets/{verse}", Token);
        await Ok(client.DeleteAsync($"/api/v1/setlists/{setlist.Id}", Token));
        using var nowFree = await client.DeleteAsync($"/api/v1/presets/{verse}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadAsStringAsync(Token);
        Assert.Contains("does-not-exist", problem, StringComparison.Ordinal);
        Assert.Contains("Entry 2", problem, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedBank.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Contains("Saturday", await inUse.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, nowFree.StatusCode);
    }

    [Fact]
    public async Task Ids_that_are_not_safe_file_names_are_rejected() {
        using var response = await client.GetAsync("/api/v1/setlists/..%2F..%2Fetc", Token);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
    }

    async Task<string> SavePreset(string name, string type, int row, int column) {
        await Ok(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        await Ok(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest(type, row, column), Token));
        var saved = await Read(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(name, true), Token));
        return saved.Preset.Id;
    }

    async Task<LiveState> Read(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}"
        );
        return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
    }

    async Task Ok(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}"
        );
    }
}
