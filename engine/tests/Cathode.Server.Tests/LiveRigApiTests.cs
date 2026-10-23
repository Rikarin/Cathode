using System.Net;
using System.Net.Http.Json;
using Cathode.Contracts;
using Cathode.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Cathode.Server.Tests;

public sealed class LiveRigApiTests : IAsyncLifetime {
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
    public async Task A_new_preset_is_input_to_output_through_shunts_and_runs_in_the_engine() {
        var state = await Live();

        Assert.Equal(14, state.Preset.Blocks.Count); // in, 12 shunts, out
        Assert.Equal(13, state.Preset.Cables.Count);
        await ServerFixture.Eventually(() => server.Graph is not null);
    }

    [Fact]
    public async Task Catalog_lists_blocks_with_parameters_and_asset_slots() {
        var blocks = await client.GetFromJsonAsync<BlockTypeInfo[]>("/api/v1/catalog/blocks", Token);

        Assert.Contains(blocks!, b => b.TypeId == "nam" && b.AssetSlots.Contains("model"));
        Assert.Contains(blocks!, b => b.TypeId == "cab" && b.AssetSlots.SequenceEqual(["ir1", "ir2"]));
        Assert.Contains(blocks!.Single(b => b.TypeId == "delay").Params, p => p.Id == "time" && p.Unit == "ms");
    }

    [Fact]
    public async Task Dropping_a_block_on_a_shunt_replaces_it_and_keeps_the_signal_path() {
        var state = await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 3));

        Assert.DoesNotContain(state.Preset.Blocks, b => b is { Row: 0, Column: 3, Type: "shunt" });
        Assert.Contains(state.Preset.Cables, c => c.To == "drive1");
        Assert.Contains(state.Preset.Cables, c => c.From == "drive1");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("drive1") >= 0);
    }

    [Fact]
    public async Task Parameters_reach_the_engine_only_for_the_active_channel() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));

        await Patch("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 500 });
        await Patch("/api/v1/live/blocks/delay1/params?channel=B", new Dictionary<string, float> { ["time"] = 120 });

        await ServerFixture.Eventually(() => server.Target("delay1", "time") == 500);
        var state = await Live();
        Assert.Equal(500, state.Preset.Blocks.Single(b => b.Id == "delay1").Channels["A"]["time"]);
        Assert.Equal(120, state.Preset.Blocks.Single(b => b.Id == "delay1").Channels["B"]["time"]);
        await Task.Delay(100, Token);
        Assert.Equal(500, server.Target("delay1", "time")); // channel B's edit did not leak into the running sound
    }

    [Fact]
    public async Task Scenes_switch_channels_and_bypass_without_recompiling() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 1));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));
        await Patch("/api/v1/live/blocks/drive1/params?channel=B", new Dictionary<string, float> { ["drive"] = 9 });
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("delay1") >= 0);
        var generation = server.Graph!.Generation;

        // Scene 2: drive on channel B, delay bypassed.
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(1));
        await Put("/api/v1/live/blocks/drive1/channel", new ChannelRequest("B"));
        await Put("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true));
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(0));
        await ServerFixture.Eventually(() => server.Target("drive1", "drive") == 5 && !server.IsBypassed("delay1"));

        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(1));

        await ServerFixture.Eventually(() => server.Target("drive1", "drive") == 9 && server.IsBypassed("delay1"));
        Assert.Equal(generation, server.Graph!.Generation);
        Assert.Equal(1, (await Live()).ActiveScene);
    }

    [Fact]
    public async Task Undo_and_redo_restore_structure_and_the_engine_follows() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("reverb", 0, 5));

        var undone = await Post("/api/v1/live/undo");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("reverb1") < 0);
        var redone = await Post("/api/v1/live/redo");

        Assert.DoesNotContain(undone.Preset.Blocks, b => b.Id == "reverb1");
        Assert.Contains(redone.Preset.Blocks, b => b.Id == "reverb1");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("reverb1") >= 0);
    }

    [Fact]
    public async Task Knob_drags_coalesce_into_one_undo_step() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", 0, 1));
        var before = await Live();
        for (var level = -1f; level >= -10f; level -= 1f) {
            await Patch("/api/v1/live/blocks/volpan1/params", new Dictionary<string, float> { ["level"] = level });
        }

        var undone = await Post("/api/v1/live/undo");

        Assert.Equal(
            before.Preset.Blocks.Single(b => b.Id == "volpan1").Channels.Count,
            undone.Preset.Blocks.Single(b => b.Id == "volpan1").Channels.Count
        );
        Assert.True(undone.CanUndo); // the add-block step is still there
    }

    [Fact]
    public async Task Stale_revisions_are_rejected_with_412() {
        var state = await Live();
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 1));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/live/blocks") {
            Content = JsonContent.Create(new AddBlockRequest("delay", 0, 2))
        };
        request.Headers.IfMatch.Add(new($"\"{state.Revision}\""));
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_edits_return_problem_details() {
        var badCable = await client.PostAsJsonAsync("/api/v1/live/cables", new CableRequest("in1", "out1"), Token);
        var unknownParam = await client.PatchAsJsonAsync(
            "/api/v1/live/blocks/shunt-r0c1/params",
            new Dictionary<string, float> { ["x"] = 1 },
            Token
        );
        var badChannel = await client.PutAsJsonAsync("/api/v1/live/blocks/in1/channel", new ChannelRequest("E"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, badCable.StatusCode);
        var problem = await badCable.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("next column", problem!.Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknownParam.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badChannel.StatusCode);
    }

    [Fact]
    public async Task Assets_upload_by_content_and_a_broken_model_is_rolled_back() {
        var ir = new MemoryStream();
        Cathode.Dsp.IO.WavFile.Write(
            ir,
            new(48_000, [Signals.Noise(512, 1).Select((v, i) => v * MathF.Exp(-i / 80f)).ToArray()])
        );
        var irAsset = await Upload("cab.wav", ir.ToArray());
        var again = await Upload("same-cab.wav", ir.ToArray());
        var badModel = await Upload("broken.nam", "{ \"not\": \"a model\" }"u8.ToArray());
        await Post("/api/v1/live/blocks", new AddBlockRequest("cab", 0, 4));
        await Post("/api/v1/live/blocks", new AddBlockRequest("nam", 0, 3));

        var withIr = await Put("/api/v1/live/blocks/cab1/assets/ir1", new AssetRequest(irAsset.Ref));
        using var rejected = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/nam1/assets/model",
            new AssetRequest(badModel.Ref),
            Token
        );

        Assert.Equal(irAsset.Ref, again.Ref); // stored once
        Assert.StartsWith("sha256:", irAsset.Ref, StringComparison.Ordinal);
        Assert.Equal(irAsset.Ref, withIr.Preset.Blocks.Single(b => b.Id == "cab1").Assets["ir1"]);
        Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
        Assert.Empty((await Live()).Preset.Blocks.Single(b => b.Id == "nam1").Assets);
    }

    [Fact]
    public async Task An_aida_x_model_uploads_as_a_model_and_runs_in_the_capture_amp() {
        const string model = """
            {"in_shape":[null,null,1],"in_skip":1,"out_gain":2,"layers":[
              {"type":"lstm","activation":"","shape":[null,null,2],"weights":[
                [[0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8]],
                [[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]],
                [0,0,0,0,0,0,0,0]]},
              {"type":"dense","activation":"","shape":[null,null,1],"weights":[[[0.5],[0.5]],[0]]}]}
            """;
        var asset = await Upload("crunch.json", System.Text.Encoding.UTF8.GetBytes(model));
        await Post("/api/v1/live/blocks", new AddBlockRequest("nam", 0, 3));

        var live = await Put("/api/v1/live/blocks/nam1/assets/model", new AssetRequest(asset.Ref));

        Assert.Equal("aidax", asset.Kind);
        Assert.Equal(asset.Ref, live.Preset.Blocks.Single(b => b.Id == "nam1").Assets["model"]);
        await ServerFixture.Eventually(
            () => (server.Block("nam1") as Cathode.Dsp.Blocks.CaptureAmpBlock)?.CaptureModel?.Format == "AIDA-X"
        );
    }

    [Fact]
    public async Task Presets_save_list_and_load() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("chorus", 0, 6));
        var saved = await Post("/api/v1/live/save", new SaveRequest("Clean Chorus"));
        await Post("/api/v1/live/new");

        var list = await client.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token);
        var loaded = await Put($"/api/v1/live/preset/{saved.Preset.Id}", new { });

        Assert.Contains(list!, p => p.Name == "Clean Chorus");
        Assert.False(saved.Dirty);
        Assert.Contains(loaded.Preset.Blocks, b => b.Id == "chorus1");
        Assert.False(loaded.CanUndo);
    }

    async Task<LiveState> Live() => (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

    async Task<LiveState> Post(string url, object? body = null) =>
        await Read(await client.PostAsJsonAsync(url, body ?? new { }, Token));

    async Task<LiveState> Put(string url, object body) => await Read(await client.PutAsJsonAsync(url, body, Token));

    async Task<LiveState> Patch(string url, object body) => await Read(await client.PatchAsJsonAsync(url, body, Token));

    async Task<AssetInfo> Upload(string name, byte[] content) {
        using var response =
            await client.PostAsync($"/api/v1/assets?name={name}", new ByteArrayContent(content), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
    }

    async Task<LiveState> Read(HttpResponseMessage response) {
        using (response) {
            if (!response.IsSuccessStatusCode) {
                Assert.Fail(
                    $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
                );
            }

            return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
        }
    }
}
