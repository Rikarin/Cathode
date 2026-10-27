using Cathode.Contracts;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

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
    public void Storing_a_preset_without_its_eight_scenes_is_refused() {
        var store = server.Services.GetRequiredService<PresetStore>();

        var refused = Assert.Throws<RigException>(() => store.Save(new() { Id = "bad1", Scenes = [] }));

        Assert.Equal(400, refused.Status);
        Assert.False(File.Exists(Path.Combine(Presets, "bad1.json")));
    }

    [Fact]
    public async Task A_grid_larger_than_six_by_fourteen_is_refused_and_the_rig_keeps_its_preset() {
        var live = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;
        var next = live.Preset;
        next.Rows = 5000;
        next.Columns = 5000;

        using var response = await client.PutAsJsonAsync("/api/v1/live", next, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "1 to 6 rows and 1 to 14 columns",
            await response.Content.ReadAsStringAsync(Token),
            StringComparison.Ordinal
        );
        Assert.Equal(6, (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset.Rows);
    }

    [Fact]
    public async Task Blocks_and_cables_that_do_not_fit_the_grid_are_refused_with_every_problem() {
        var next = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset;
        next.Blocks.Add(new() { Id = "far", Type = "chorus", Row = 6, Column = 3 });
        next.Blocks.Add(new() { Id = "twin", Type = "chorus", Row = Preset.DefaultRow, Column = 0 });
        next.Blocks.Add(new() { Id = "in1", Type = "delay", Row = 0, Column = 5 });
        next.Cables.Add(new() { From = "in1", To = "ghost" });
        next.Cables.Add(new() { From = "out1", To = "far" });

        using var response = await client.PutAsJsonAsync("/api/v1/live", next, Token);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>(Token);
        var errors = problem!["errors"]!.AsArray().Select(static e => e!.GetValue<string>()).ToList();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            [
                "Block 'far' at (6,3) is outside the 6x14 grid.",
                "Block 'twin' shares cell (2,0) with 'in1'.",
                "Two blocks have the id 'in1'.",
                "Cable 'in1' -> 'ghost' refers to a block that does not exist.",
                "Cable 'out1' -> 'far' does not run from one column to the next (shunts bridge gaps)."
            ],
            errors
        );
    }

    [Fact]
    public async Task More_modifiers_than_the_telemetry_reports_are_refused() {
        var next = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Preset;
        next.Modifiers = [
            ..Enumerable.Range(0, Preset.MaxModifiers + 1)
                .Select(static i => new PresetModifier { Block = "out1", Param = $"p{i}", Source = "manualA" })
        ];

        using var response = await client.PutAsJsonAsync("/api/v1/live", next, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 256", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public void Storing_or_importing_a_grid_of_the_wrong_shape_is_refused() {
        var store = server.Services.GetRequiredService<PresetStore>();
        var preset = Preset.CreateDefault();
        preset.Id = "tall";
        preset.Rows = Preset.MaxRows + 1;

        var refused = Assert.Throws<RigException>(() => store.Save(preset));

        Assert.Equal(400, refused.Status);
        Assert.False(File.Exists(Path.Combine(Presets, "tall.json")));
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
