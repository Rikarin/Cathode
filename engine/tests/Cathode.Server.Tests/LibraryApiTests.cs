using Cathode.Contracts;
using Cathode.Server.Midi;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.Extensions.DependencyInjection;
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

        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.MidiMappings.Add(new("pc", "pc", 0, 0, "presetSlot", bank.Id));
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
        var router = server.Services.GetRequiredService<MidiRouter>();
        var library = server.Services.GetRequiredService<Library>();

        router.OnMidi([new(0xC0, 2, 0, 0)]);
        await ServerFixture.Eventually(async () =>
            (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Id == chorus
        );
        var empty = Assert.Throws<RigException>(() => library.Slot(bank.Id, 1));

        Assert.Equal(verse, library.Slot(bank.Id, 0));
        Assert.Equal(404, empty.Status);
    }

    [Fact]
    public async Task Preset_next_and_previous_follow_the_bank_and_wrap_into_the_next_bank() {
        // Verse before Chorus: the bank's order, not the alphabet's.
        var bank = new Bank { Name = "Gig", Slots = [verse, null, chorus] };
        await Ok(client.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.MidiMappings.Add(new("next", "cc", 0, 30, "presetNext"));
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
        var factory = server.Services.GetRequiredService<Library>().Bank("factory")!;

        var first = await Read(client.PutAsJsonAsync($"/api/v1/live/banks/{bank.Id}/slots/0", new { }, Token));
        var next = await Neighbour(1);
        var previous = await Neighbour(-1);
        server.Services.GetRequiredService<MidiRouter>().OnMidi([new(0xB0, 30, 127, 0)]);
        await ServerFixture.Eventually(async () =>
            (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Id == chorus
        );
        var afterLast = await Neighbour(1);

        Assert.Equal(verse, first.Preset.Id);
        Assert.Equal(new PresetPosition(bank.Id, 0), first.Position);
        Assert.Equal(new PresetStep(chorus, null, new(bank.Id, 2)), next); // the empty slot is skipped
        // The factory bank comes first: before the Gig bank's first slot is the factory bank's last.
        var lastFactory = factory.Slots.FindLastIndex(static s => s is not null);
        Assert.Equal(new PresetStep(factory.Slots[lastFactory]!, null, new("factory", lastFactory)), previous);
        // After the last bank's last slot (and no preset outside a bank): round to the first bank.
        Assert.Equal(new PresetStep(factory.Slots[0]!, null, new("factory", 0)), afterLast);
    }

    [Fact]
    public async Task Preset_next_and_previous_stay_in_the_active_setlist() {
        var setlist = new Setlist { Name = "Friday", Entries = [new(chorus, 3), new(verse, 1)] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));
        var navigator = server.Services.GetRequiredService<PresetNavigator>();

        var loaded = await Read(client.PutAsJsonAsync($"/api/v1/live/setlists/{setlist.Id}/entries/0", new { }, Token));
        var second = await navigator.StepAsync(1);
        var wrapped = await navigator.StepAsync(1);

        Assert.Equal(new PresetPosition(Setlist: setlist.Id, Entry: 0), loaded.Position);
        Assert.Equal((verse, 1), (second!.Preset.Id, second.ActiveScene));
        Assert.Equal((chorus, 3), (wrapped!.Preset.Id, wrapped.ActiveScene));
        Assert.Equal(new PresetPosition(Setlist: setlist.Id, Entry: 0), wrapped.Position);
    }

    [Fact]
    public async Task Song_next_and_previous_follow_the_active_setlist_and_stop_at_its_ends() {
        var setlist = new Setlist { Name = "Friday", Entries = [new(verse, 2), new(chorus, 3)] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.MidiMappings.Add(new("next", "cc", 0, 31, "songNext"));
        global.MidiMappings.Add(new("previous", "cc", 0, 32, "songPrevious"));
        global.ActiveSetlist = setlist.Id; // picked, not started: the next song is the first
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
        var router = server.Services.GetRequiredService<MidiRouter>();
        var navigator = server.Services.GetRequiredService<PresetNavigator>();
        var globals = server.Services.GetRequiredService<GlobalStore>();

        router.OnMidi([new(0xB0, 31, 127, 0)]);
        await ServerFixture.Eventually(() => Task.FromResult(globals.Get().ActiveSong == 0));
        var first = await Read(client.GetAsync("/api/v1/live", Token));
        // Another preset loaded by name leaves the setlist's place where it was.
        await Ok(client.PutAsJsonAsync($"/api/v1/live/preset/{verse}", new { }, Token));
        var second = await navigator.StepSongAsync(1);
        var pastTheEnd = await navigator.StepSongAsync(1);
        router.OnMidi([new(0xB0, 32, 127, 0)]);
        await ServerFixture.Eventually(() => Task.FromResult(globals.Get().ActiveSong == 0));
        var beforeTheStart = await navigator.StepSongAsync(-1);

        Assert.Equal((verse, 2), (first.Preset.Id, first.ActiveScene));
        Assert.Equal((chorus, 3), (second!.Preset.Id, second.ActiveScene));
        Assert.Null(pastTheEnd);
        Assert.Null(beforeTheStart);
        Assert.Equal(setlist.Id, globals.Get().ActiveSetlist);
    }

    [Fact]
    public async Task A_setlist_keeps_a_missing_preset_and_songs_pass_over_it() {
        var setlist = new Setlist { Name = "Friday", Entries = [new(verse), new(chorus), new(verse, 4)] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));
        server.Services.GetRequiredService<PresetStore>().Delete(chorus); // gone behind Cathode's back
        var navigator = server.Services.GetRequiredService<PresetNavigator>();

        setlist.Name = "Friday late";
        using var kept = await client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token);
        var other = new Setlist { Entries = [new(chorus)] };
        using var added = await client.PutAsJsonAsync($"/api/v1/setlists/{other.Id}", other, Token);
        await Read(client.PutAsJsonAsync($"/api/v1/live/setlists/{setlist.Id}/entries/0", new { }, Token));
        var next = await navigator.StepSongAsync(1);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        Assert.Equal((verse, 4), (next!.Preset.Id, next.ActiveScene));
        Assert.Equal(new PresetPosition(Setlist: setlist.Id, Entry: 2), next.Position);
        Assert.Equal(2, server.Services.GetRequiredService<GlobalStore>().Get().ActiveSong);
    }

    [Fact]
    public async Task Deleting_the_active_setlist_leaves_none_active() {
        var setlist = new Setlist { Name = "Friday", Entries = [new(verse)] };
        await Ok(client.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));
        await Read(client.PutAsJsonAsync($"/api/v1/live/setlists/{setlist.Id}/entries/0", new { }, Token));
        var globals = server.Services.GetRequiredService<GlobalStore>();
        var active = globals.Get();

        await Ok(client.DeleteAsync($"/api/v1/setlists/{setlist.Id}", Token));

        Assert.Equal((setlist.Id, 0), (active.ActiveSetlist, active.ActiveSong));
        Assert.Equal((null, -1), (globals.Get().ActiveSetlist, globals.Get().ActiveSong));
        Assert.Null(await server.Services.GetRequiredService<PresetNavigator>().StepSongAsync(1));
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 1, 2)] // entry 1's preset is missing
    [InlineData(2, 1, null)] // no wrapping
    [InlineData(2, -1, 0)]
    [InlineData(0, -1, null)]
    [InlineData(9, -1, 2)] // the setlist got shorter
    [InlineData(0, 0, null)]
    public void A_song_step_passes_over_missing_presets_and_stops_at_the_ends(int from, int step, int? to) {
        var setlist = new Setlist { Entries = [new("a"), new("missing"), new("b")] };

        Assert.Equal(to, PresetNavigator.SongStep(setlist, from, step, static id => id != "missing"));
    }

    [Fact]
    public async Task Presets_in_no_bank_come_after_the_banks_by_name() {
        var factory = server.Services.GetRequiredService<Library>().Bank("factory")!;

        var loaded = await Read(client.PutAsJsonAsync($"/api/v1/live/preset/{verse}", new { }, Token));
        var previous = await Neighbour(-1);
        var next = await Neighbour(1);

        Assert.Null(loaded.Position);
        Assert.Equal(new PresetStep(chorus), previous);
        Assert.Equal(new PresetStep(factory.Slots[0]!, null, new("factory", 0)), next);
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

    Task<PresetStep?> Neighbour(int step) =>
        client.GetFromJsonAsync<PresetStep>($"/api/v1/live/neighbour?step={step}", Token);

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
