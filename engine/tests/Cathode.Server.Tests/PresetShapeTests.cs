using Cathode.Contracts;
using Cathode.Server.Rig;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>Presets of a bad shape (too few scenes, null lists) never reach the rig's working copy.</summary>
public sealed class PresetShapeTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    string Presets => Path.Combine(server.DataDirectory, "presets");

    public async ValueTask InitializeAsync() => client = await server.ConnectedClientAsync();

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Storing_a_preset_without_its_eight_scenes_is_refused() {
        using var response = await client.PutAsync(
            "/api/v1/presets/bad1",
            new StringContent("""{ "name": "x", "scenes": [] }""", System.Text.Encoding.UTF8, "application/json"),
            Token
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(Presets, "bad1.json")));
    }

    [Fact]
    public async Task Loading_a_stored_file_without_its_scenes_is_refused_and_the_rig_keeps_its_preset() {
        var before = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Id;
        await File.WriteAllTextAsync(
            Path.Combine(Presets, "bad2.json"),
            """{ "id": "bad2", "name": "x", "scenes": [{}, {}, {}] }""",
            Token
        );

        using var load = await client.PutAsJsonAsync("/api/v1/live/preset/bad2", new { }, Token);
        using var scene = await client.PutAsJsonAsync("/api/v1/live/scenes/active", new SceneSelectRequest(5), Token);

        Assert.Equal(HttpStatusCode.BadRequest, load.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scene.StatusCode);
        Assert.Equal(before, (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Id);
    }

    [Fact]
    public async Task Null_lists_in_a_stored_file_load_as_empty_ones() {
        var scenes = string.Join(", ", Enumerable.Repeat("""{ "blocks": null, "controllers": null }""", 8));
        await File.WriteAllTextAsync(
            Path.Combine(Presets, "nulls.json"),
            $$"""
              { "id": "nulls", "name": "Nulls", "blocks": null, "cables": null, "modifiers": null,
                "scenes": [{{scenes}}] }
              """,
            Token
        );

        using var load = await client.PutAsJsonAsync("/api/v1/live/preset/nulls", new { }, Token);
        using var scene = await client.PutAsJsonAsync("/api/v1/live/scenes/active", new SceneSelectRequest(3), Token);

        Assert.True(load.IsSuccessStatusCode, await load.Content.ReadAsStringAsync(Token));
        Assert.Equal(HttpStatusCode.OK, scene.StatusCode);
        var live = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;
        Assert.Equal("nulls", live.Preset.Id);
        Assert.Empty(live.Preset.Cables);
    }

    [Fact]
    public async Task A_startup_preset_that_cannot_load_leaves_the_server_running() {
        await File.WriteAllTextAsync(
            Path.Combine(Presets, "bad3.json"),
            """{ "id": "bad3", "name": "x", "scenes": [] }""",
            Token
        );
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.StartupPreset = "bad3";
        (await client.PutAsJsonAsync("/api/v1/global", global, Token)).EnsureSuccessStatusCode();

        var problem = await server.Services.GetRequiredService<LiveRig>().LoadStartupAsync();

        Assert.Contains("bad3", problem, StringComparison.Ordinal);
        Assert.NotEqual("bad3", (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Id);
    }
}
