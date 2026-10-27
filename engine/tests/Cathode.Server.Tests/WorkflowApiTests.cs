using Cathode.Contracts;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     The editor's workflow on the live rig: revert, clear, snapshots, templates and the grid operations.
/// </summary>
public sealed class WorkflowApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Post("/api/v1/live/new", new { });
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Revert_goes_back_to_the_saved_version_and_can_be_undone() {
        using var unsaved = await client.PostAsJsonAsync("/api/v1/live/revert", new { }, Token);
        Assert.Equal(HttpStatusCode.Conflict, unsaved.StatusCode);

        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));
        await Post("/api/v1/live/save", new SaveRequest("Saved"));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 5));
        var reverted = await Post("/api/v1/live/revert", new { });
        var undone = await Post("/api/v1/live/undo", new { });

        Assert.DoesNotContain(reverted.Preset.Blocks, static b => b.Id == "delay1");
        Assert.Contains(reverted.Preset.Blocks, static b => b.Id == "drive1");
        Assert.False(reverted.Dirty);
        Assert.Contains(undone.Preset.Blocks, static b => b.Id == "delay1");
    }

    [Fact]
    public async Task Clear_empties_the_grid_and_the_name_but_keeps_the_id() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));
        var before = await Post("/api/v1/live/save", new SaveRequest("Full"));

        var cleared = await Post("/api/v1/live/clear", new { });

        Assert.Equal(before.Preset.Id, cleared.Preset.Id);
        Assert.Equal(string.Empty, cleared.Preset.Name);
        Assert.Equal(14, cleared.Preset.Blocks.Count); // in, 12 shunts, out
        Assert.True(cleared is { Dirty: true, CanUndo: true });
    }

    [Fact]
    public async Task Snapshots_keep_unsaved_edits_and_load_list_and_delete() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("chorus", 2, 2));
        var taken = await Json<SnapshotSummary>(
            client.PostAsJsonAsync("/api/v1/live/snapshots", new SnapshotRequest(), Token)
        );
        await Delete("/api/v1/live/blocks/chorus1");
        var loaded = await Post($"/api/v1/live/snapshots/{taken.Id}/load", new { });
        var listed = await Json<SnapshotSummary[]>(
            client.GetAsync($"/api/v1/snapshots?preset={taken.PresetId}", Token)
        );
        using var deleted = await client.DeleteAsync($"/api/v1/snapshots/{taken.Id}", Token);
        using var gone = await client.PostAsJsonAsync($"/api/v1/live/snapshots/{taken.Id}/load", new { }, Token);

        Assert.Contains(loaded.Preset.Blocks, static b => b.Id == "chorus1");
        Assert.Equal(taken.PresetId, loaded.Preset.Id);
        Assert.Contains(listed, s => s.Id == taken.Id);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Templates_are_saved_and_start_new_presets() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("reverb", 2, 4));
        var template = await Json<PresetSummary>(
            client.PostAsJsonAsync("/api/v1/live/templates", new TemplateRequest("Ambient"), Token)
        );
        await Post("/api/v1/live/new", new { });

        var started = await Post($"/api/v1/live/templates/{template.Id}/new", new { });
        var listed = await Json<PresetSummary[]>(client.GetAsync("/api/v1/templates", Token));
        using var deleted = await client.DeleteAsync($"/api/v1/templates/{template.Id}", Token);

        Assert.Contains(started.Preset.Blocks, static b => b.Id == "reverb1");
        Assert.NotEqual(template.Id, started.Preset.Id);
        Assert.Contains(listed, static t => t is { Name: "Ambient" });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Replacing_a_block_keeps_its_cell_and_cables() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));

        var replaced = await Put("/api/v1/live/blocks/drive1/type", new ReplaceBlockRequest("chorus"));

        var chorus = replaced.Preset.Blocks.Single(static b => b.Id == "chorus1");
        Assert.Equal((2, 3), (chorus.Row, chorus.Column));
        Assert.DoesNotContain(replaced.Preset.Blocks, static b => b.Id == "drive1");
        Assert.Contains(replaced.Preset.Cables, static c => c.To == "chorus1");
        Assert.Contains(replaced.Preset.Cables, static c => c.From == "chorus1");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("chorus1") >= 0);
    }

    [Fact]
    public async Task Dropping_swaps_two_blocks_with_the_wiring_in_place_or_replaces_the_target() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 6));

        var swapped = await Post("/api/v1/live/blocks/drive1/drop", new DropBlockRequest("delay1"));
        var drive = swapped.Preset.Blocks.Single(static b => b.Id == "drive1");
        var delay = swapped.Preset.Blocks.Single(static b => b.Id == "delay1");
        Assert.Equal((2, 6, 2, 3), (drive.Row, drive.Column, delay.Row, delay.Column));
        Assert.Contains(swapped.Preset.Cables, static c => c is { From: "shunt-r2c2", To: "delay1" });
        Assert.Contains(swapped.Preset.Cables, static c => c is { From: "drive1", To: "shunt-r2c7" });

        var replaced = await Post("/api/v1/live/blocks/drive1/drop", new DropBlockRequest("delay1", "replace"));
        Assert.DoesNotContain(replaced.Preset.Blocks, static b => b.Id == "delay1");
        Assert.Equal(3, replaced.Preset.Blocks.Single(static b => b.Id == "drive1").Column);
        Assert.Contains(replaced.Preset.Cables, static c => c.To == "drive1");
        Assert.Contains(replaced.Preset.Blocks, static b => b is { Type: "shunt", Row: 2, Column: 6 });
        Assert.Equal(
            replaced.Preset.Blocks.Count(static b => b.Row == 2) - 1,
            replaced.Preset.Cables.Count
        ); // the row is still one unbroken path
    }

    [Fact]
    public async Task A_row_moves_with_its_cables_and_a_column_shifts_the_ones_between() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));

        var moved = await Post("/api/v1/live/grid/move", new MoveLineRequest("row", 2, 3));
        Assert.All(moved.Preset.Blocks, static b => Assert.Equal(3, b.Row));
        Assert.Equal(13, moved.Preset.Cables.Count);

        var column = await Post("/api/v1/live/grid/move", new MoveLineRequest("column", 3, 5));
        Assert.Equal(5, column.Preset.Blocks.Single(static b => b.Id == "drive1").Column);
        Assert.Equal(3, column.Preset.Blocks.Single(static b => b.Id == "shunt-r2c4").Column);
        Assert.All(
            column.Preset.Cables,
            c => Assert.Equal(
                column.Preset.Blocks.Single(b => b.Id == c.From).Column + 1,
                column.Preset.Blocks.Single(b => b.Id == c.To).Column
            )
        );
        using var outside = await client.PostAsJsonAsync(
            "/api/v1/live/grid/move",
            new MoveLineRequest("row", 2, 9),
            Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
    }

    [Fact]
    public async Task A_column_moved_across_others_keeps_every_block_on_the_signal_path_and_undo_restores_it() {
        // Row 2 runs In to Out through shunts; a drive at column 3 also feeds a chorus branch below (row 3), which
        // rejoins at column 5, and a delay sits in the path at column 6.
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));
        await Post("/api/v1/live/blocks", new AddBlockRequest("chorus", 3, 4));
        await Post("/api/v1/live/cables", new CableRequest("drive1", "chorus1"));
        await Post("/api/v1/live/cables", new CableRequest("chorus1", "shunt-r2c5"));
        var original = await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 6));
        var onPath = OnPath(original.Preset);
        Assert.Contains("chorus1", onPath);

        // Across others, rightwards: the drive leaves the chorus branch behind and lands after the delay.
        var right = await Post("/api/v1/live/grid/move", new MoveLineRequest("column", 3, 8));
        Assert.Equal(8, right.Preset.Blocks.Single(static b => b.Id == "drive1").Column);
        AssertWired(right.Preset, onPath);
        Assert.Equal(
            ["delay1", "drive1"],
            Order(right.Preset).Where(static id => id is "delay1" or "drive1")
        );

        // And back leftwards, past the delay and the chorus, to column 1.
        var left = await Post("/api/v1/live/grid/move", new MoveLineRequest("column", 8, 1));
        Assert.Equal(1, left.Preset.Blocks.Single(static b => b.Id == "drive1").Column);
        AssertWired(left.Preset, onPath);

        // A column with an empty cell where a path must cross: column 4 holds only the chorus (row 3) and a shunt.
        var branch = await Post("/api/v1/live/grid/move", new MoveLineRequest("column", 4, 10));
        AssertWired(branch.Preset, onPath);

        await Post("/api/v1/live/undo", new { });
        await Post("/api/v1/live/undo", new { });
        var undone = await Post("/api/v1/live/undo", new { });
        Assert.Equal(Layout(original.Preset), Layout(undone.Preset));
        Assert.Equal(Wiring(original.Preset), Wiring(undone.Preset));
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("drive1") >= 0);
        return;

        static void AssertWired(Preset preset, IReadOnlySet<string> expected) {
            var columns = preset.Blocks.ToDictionary(static b => b.Id, static b => b.Column);
            Assert.All(preset.Cables, c => Assert.Equal(columns[c.From] + 1, columns[c.To]));
            Assert.Equal(Blocks(expected), Blocks(OnPath(preset)));
        }

        static string[] Blocks(IEnumerable<string> ids) =>
            [..ids.Where(static id => !id.StartsWith("shunt", StringComparison.Ordinal)).Order()];

        static IEnumerable<string> Order(Preset preset) =>
            preset.Blocks.OrderBy(static b => b.Column).Select(static b => b.Id);

        static string[] Layout(Preset preset) =>
            [..preset.Blocks.Select(static b => $"{b.Id}@{b.Row},{b.Column}").Order()];

        static string[] Wiring(Preset preset) => [..preset.Cables.Select(static c => $"{c.From}>{c.To}").Order()];
    }

    /// <summary>The blocks on a signal path: reached from an Input and reaching an Output.</summary>
    static HashSet<string> OnPath(Preset preset) {
        var downstream = Reach(preset, BuiltInTypes.Input, static c => (c.From, c.To));
        var upstream = Reach(preset, BuiltInTypes.Output, static c => (c.To, c.From));
        downstream.IntersectWith(upstream);
        return downstream;

        static HashSet<string> Reach(Preset preset, string start, Func<PresetCable, (string, string)> step) {
            var seen = preset.Blocks.Where(b => b.Type == start).Select(static b => b.Id).ToHashSet();
            var queue = new Queue<string>(seen);
            while (queue.TryDequeue(out var id)) {
                foreach (var (here, next) in preset.Cables.Select(step)) {
                    if (here == id && seen.Add(next)) {
                        queue.Enqueue(next);
                    }
                }
            }

            return seen;
        }
    }

    [Fact]
    public async Task Disconnect_removes_every_cable_of_a_block() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));

        using var response = await client.DeleteAsync("/api/v1/live/blocks/drive1/cables", Token);
        var state = await Read(response);

        Assert.DoesNotContain(state.Preset.Cables, static c => c.From == "drive1" || c.To == "drive1");
        Assert.True(state.CanUndo);
    }

    [Fact]
    public async Task Pasting_settings_replaces_every_channel_of_a_same_type_block() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 2));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));
        await Patch("/api/v1/live/blocks/delay2/params?channel=D", new Dictionary<string, float> { ["mix"] = 10 });
        var settings = new BlockSettingsRequest(
            "delay",
            new() { ["A"] = new() { ["time"] = 333 }, ["B"] = new() { ["time"] = 444 } }
        );

        var pasted = await Put("/api/v1/live/blocks/delay2/settings", settings);
        using var wrong = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/delay2/settings",
            settings with { Type = "chorus" },
            Token
        );

        var delay = pasted.Preset.Blocks.Single(static b => b.Id == "delay2");
        Assert.Equal(333, delay.Channels["A"]["time"]);
        Assert.Equal(444, delay.Channels["B"]["time"]);
        Assert.False(delay.Channels.ContainsKey("D"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        await ServerFixture.Eventually(() => server.Target("delay2", "time") == 333);
    }

    [Fact]
    public async Task All_scene_actions_set_the_channel_and_bypass_in_every_scene() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 2));

        await Put("/api/v1/live/blocks/delay1/all-scenes", new AllScenesRequest(Channel: "C"));
        var state = await Put("/api/v1/live/blocks/delay1/all-scenes", new AllScenesRequest(true));

        Assert.All(state.Preset.Scenes, static s => Assert.Equal(new(true, "C"), s.StateOf("delay1")));
        await ServerFixture.Eventually(() => server.IsBypassed("delay1"));
        using var bad = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/delay1/all-scenes",
            new AllScenesRequest(Channel: "Z"),
            Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    async Task<LiveState> Post(string url, object body) => await Read(await client.PostAsJsonAsync(url, body, Token));

    async Task<LiveState> Delete(string url) => await Read(await client.DeleteAsync(url, Token));

    async Task<LiveState> Put(string url, object body) => await Read(await client.PutAsJsonAsync(url, body, Token));

    async Task<LiveState> Patch(string url, object body) => await Read(await client.PatchAsJsonAsync(url, body, Token));

    static async Task<T> Json<T>(Task<HttpResponseMessage> call) {
        using var response = await call;
        await Succeeded(response);
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    static async Task<LiveState> Read(HttpResponseMessage response) {
        using (response) {
            await Succeeded(response);
            return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
        }
    }

    static async Task Succeeded(HttpResponseMessage response) {
        if (!response.IsSuccessStatusCode) {
            Assert.Fail(
                $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
            );
        }
    }
}
