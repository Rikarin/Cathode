using Cathode.Contracts;
using Cathode.Server.Midi;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     Manage Presets' tools (metadata, duplicate, Reset Amp blocks, Parameter Batch Set), the Library's whole-block
///     entries and files, performance pages and footswitch views, links and per-preset overrides.
/// </summary>
public sealed class PresetToolsApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Live(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Metadata_changes_the_stored_and_live_preset_and_duplicates_get_their_own_id() {
        var saved = await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Lead"), Token));
        var id = saved.Preset.Id;

        await Ok(
            client.PatchAsJsonAsync(
                $"/api/v1/presets/{id}/metadata",
                new PresetMetadataRequest("Solo", "red", true),
                Token
            )
        );
        var copy = await Json<PresetSummary>(
            client.PostAsJsonAsync($"/api/v1/presets/{id}/duplicate", new DuplicatePresetRequest("lead-copy"), Token)
        );
        var listed = await Json<PresetSummary[]>(client.GetAsync("/api/v1/presets", Token));
        var live = await Json<LiveState>(client.GetAsync("/api/v1/live", Token));
        using var taken = await client.PostAsJsonAsync(
            $"/api/v1/presets/{id}/duplicate",
            new DuplicatePresetRequest("lead-copy"),
            Token
        );

        Assert.Contains(listed, p => p is { Name: "Solo", Color: "red", Favorite: true } && p.Id == id);
        Assert.Equal(("lead-copy", "Solo copy"), (copy.Id, copy.Name));
        Assert.Equal(("Solo", "red"), (live.Preset.Name, live.Preset.Color));
        Assert.False(live.Dirty);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    }

    [Fact]
    public async Task Asset_metadata_renames_and_tags_an_impulse_response() {
        using var upload = new ByteArrayContent(Wav());
        var asset = await Json<AssetInfo>(client.PostAsync("/api/v1/assets?name=room.wav", upload, Token));

        var changed = await Json<AssetInfo>(
            client.PatchAsJsonAsync(
                $"/api/v1/assets/{asset.Ref}/metadata",
                new AssetMetadataRequest("Room mic", "blue", true),
                Token
            )
        );
        var cleared = await Json<AssetInfo>(
            client.PatchAsJsonAsync(
                $"/api/v1/assets/{asset.Ref}/metadata",
                new AssetMetadataRequest(Color: string.Empty),
                Token
            )
        );

        Assert.Equal(("Room mic", "blue", true), (changed.Name, changed.Color, changed.Favorite));
        Assert.Null(cleared.Color);
        Assert.True(cleared.Favorite);
    }

    [Fact]
    public async Task Asset_usage_names_the_stored_presets_that_use_each_asset() {
        using var upload = new ByteArrayContent(Wav());
        var asset = await Json<AssetInfo>(client.PostAsync("/api/v1/assets?name=room.wav", upload, Token));
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("irplayer", 0, 3), Token));
        await Live(
            client.PutAsJsonAsync("/api/v1/live/blocks/irplayer1/assets/ir", new AssetRequest(asset.Ref), Token)
        );
        await Live(
            client.PutAsJsonAsync("/api/v1/live/blocks/irplayer1/assets/ir2", new AssetRequest(asset.Ref), Token)
        );
        await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Verse", true), Token));
        await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Chorus", true), Token));

        var usage = await Json<AssetUsage[]>(client.GetAsync("/api/v1/assets/usage", Token));

        Assert.Equal(["Chorus", "Verse"], Assert.Single(usage, u => u.Ref == asset.Ref).Presets); // each preset once
    }

    [Fact]
    public async Task Reset_amp_blocks_keeps_the_front_panel_and_resets_the_expert_settings() {
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("circuitamp", 0, 3), Token));
        // Model 0 (a blackface) has Gain on its panel and no Cut; Bias is an expert setting.
        await Live(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/circuitamp1/params",
                new Dictionary<string, float> { ["gain"] = 7, ["cut"] = 3, ["bias"] = 80, ["level"] = -3 },
                Token
            )
        );
        var saved = await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Amp"), Token));

        var result = await Json<BatchResult>(
            client.PostAsJsonAsync(
                "/api/v1/presets/reset-amps",
                new PresetBatchRequest([saved.Preset.Id, "missing"]),
                Token
            )
        );
        var stored = await Json<Preset>(client.GetAsync($"/api/v1/presets/{saved.Preset.Id}", Token));

        var values = stored.Blocks.Single(static b => b.Id == "circuitamp1").Channels["A"];
        Assert.Equal([saved.Preset.Id], result.Changed);
        Assert.Equal(["missing"], result.Skipped);
        Assert.Equal(7, values["gain"]);
        Assert.Equal(-3, values["level"]);
        Assert.False(values.ContainsKey("cut"));
        Assert.False(values.ContainsKey("bias"));
    }

    [Fact]
    public async Task Batch_set_writes_one_parameter_into_the_matching_block_of_each_preset() {
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token));
        var first = await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("One", true), Token));
        var second = await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Two", true), Token));
        await Live(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        var plain = await Live(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Plain", true), Token));

        var result = await Json<BatchResult>(
            client.PostAsJsonAsync(
                "/api/v1/presets/batch-set",
                new BatchSetRequest(
                    [first.Preset.Id, second.Preset.Id, plain.Preset.Id],
                    "delay1",
                    "delay",
                    "time",
                    432,
                    "B"
                ),
                Token
            )
        );
        var stored = await Json<Preset>(client.GetAsync($"/api/v1/presets/{second.Preset.Id}", Token));
        using var unknown = await client.PostAsJsonAsync(
            "/api/v1/presets/batch-set",
            new BatchSetRequest([first.Preset.Id], "delay1", "delay", "nope", 1),
            Token
        );

        Assert.Equal([first.Preset.Id, second.Preset.Id], result.Changed);
        Assert.Equal([plain.Preset.Id], result.Skipped);
        Assert.Equal(432, stored.Blocks.Single(static b => b.Id == "delay1").Channels["B"]["time"]);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Whole_block_entries_recall_every_channel_with_modifiers_and_travel_as_files() {
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("chorus", 0, 2), Token));
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("chorus", 1, 2), Token));
        var entry = new BlockPreset {
            Type = "chorus",
            Name = "Wide",
            Folder = "Mod",
            Values = { ["rate"] = 1 },
            Channels = new() { ["A"] = new() { ["rate"] = 1 }, ["C"] = new() { ["rate"] = 3 } },
            Modifiers = [new() { Param = "mix", Source = "lfo1", Min = 0, Max = 100 }]
        };
        var stored = await Json<BlockPreset>(client.PutAsJsonAsync("/api/v1/block-presets/wide", entry, Token));

        var recalled = await Live(
            client.PostAsJsonAsync(
                "/api/v1/live/blocks/chorus2/preset",
                new ApplyBlockPresetRequest("wide", AllChannels: true),
                Token
            )
        );
        var file = await Json<BlockPresetFile>(
            client.PostAsJsonAsync("/api/v1/block-presets/export", new BlockPresetExportRequest(["wide"]), Token)
        );
        var imported = await Json<BlockPreset[]>(client.PostAsJsonAsync("/api/v1/block-presets/import", file, Token));
        var all = await Json<BlockPreset[]>(
            client.PostAsJsonAsync("/api/v1/live/block-presets", new SaveAllBlocksRequest("Rig"), Token)
        );

        var chorus = recalled.Preset.Blocks.Single(static b => b.Id == "chorus2");
        Assert.Equal("Mod", stored.Folder);
        Assert.Equal(3, chorus.Channels["C"]["rate"]);
        Assert.Empty(chorus.Channels["B"]);
        Assert.Contains(recalled.Preset.Modifiers, static m => m is { Block: "chorus2", Param: "mix", Source: "lfo1" });
        Assert.Equal(BlockPresetFile.FormatName, file.Format);
        Assert.NotEqual("wide", Assert.Single(imported).Id);
        Assert.Equal(2, all.Length);
        Assert.All(all, static p => Assert.Equal("Rig", p.Folder));
        Assert.All(all, static p => Assert.NotNull(p.Channels));
    }

    [Fact]
    public async Task Performance_pages_live_in_the_preset_and_in_global() {
        await Live(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token));

        var live = await Live(
            client.PutAsJsonAsync(
                "/api/v1/live/perform",
                new List<PerformControl> { new("delay1", "mix", "Echo") },
                Token
            )
        );
        using var bad = await client.PutAsJsonAsync(
            "/api/v1/live/perform",
            new List<PerformControl> { new("delay1", "nope") },
            Token
        );
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.Perform = [new("amp1", "gain")];
        var stored = await Json<GlobalSettings>(client.PutAsJsonAsync("/api/v1/global", global, Token));

        Assert.Equal(new("delay1", "mix", "Echo"), Assert.Single(live.Preset.Perform));
        Assert.True(live.CanUndo);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(new("amp1", "gain"), Assert.Single(stored.Perform));
    }

    [Fact]
    public async Task Footswitch_views_links_and_preset_overrides_change_what_a_switch_does() {
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.Footswitches = [
            new(
                "main",
                "Main",
                [
                    new(1, "Scene 2", "cc", 1, 40, new("scene", Value: 1)),
                    new(1, "Scene 4", "cc", 1, 40, new("scene", Value: 3), View: 1),
                    new(2, "Page", "cc", 1, 41, new("viewNext")),
                    new(3, "To FX", "cc", 1, 42, new("scene", Value: 5), View: 1, Link: "fx")
                ],
                ["Scenes", "More"]
            ),
            new("fx", "FX", [new(1, "Back", "cc", 1, 40, new("layout", "main"))])
        ];
        global.ActiveLayout = "main";
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
        var router = server.Services.GetRequiredService<MidiRouter>();

        router.OnMidi([new(0xB0, 40, 127, 0)]);
        await ServerFixture.Eventually(async () => (await Live(client.GetAsync("/api/v1/live", Token))).ActiveScene == 1
        );
        router.OnMidi([new(0xB0, 41, 127, 0)]); // next view
        await ServerFixture.Eventually(async () => (await Global()).ActiveView == 1);
        router.OnMidi([new(0xB0, 40, 127, 0)]); // the same switch on view 2
        await ServerFixture.Eventually(async () => (await Live(client.GetAsync("/api/v1/live", Token))).ActiveScene == 3
        );

        await Live(
            client.PutAsJsonAsync(
                "/api/v1/live/footswitch-overrides",
                new List<FootswitchOverride> { new("main", 1, new("scene", Value: 6)) },
                Token
            )
        );
        router.OnMidi([new(0xB0, 40, 127, 0)]); // overridden by the preset
        await ServerFixture.Eventually(async () => (await Live(client.GetAsync("/api/v1/live", Token))).ActiveScene == 6
        );

        router.OnMidi([new(0xB0, 42, 127, 0)]); // on view 2 too: its action, then the jump to the FX layout
        await ServerFixture.Eventually(async () => await Global() is { ActiveLayout: "fx", ActiveView: 0 });
        Assert.Equal(5, (await Live(client.GetAsync("/api/v1/live", Token))).ActiveScene);

        global.Footswitches[0].Switches.Add(new(9, "Bad", "cc", 1, 50, new("tap"), View: 4));
        using var invalid = await client.PutAsJsonAsync("/api/v1/global", global, Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    /// <summary>A short mono 16-bit WAV.</summary>
    static byte[] Wav() {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        const int samples = 64;
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(48_000);
        writer.Write(96_000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) {
            writer.Write((short)(i == 0 ? 16_000 : 0));
        }

        writer.Flush();
        return stream.ToArray();
    }

    static Task<LiveState> Live(Task<HttpResponseMessage> call) => Json<LiveState>(call);

    static async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        await Succeeded(response);
    }

    static async Task<T> Json<T>(Task<HttpResponseMessage> call) {
        using var response = await call;
        await Succeeded(response);
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    static async Task Succeeded(HttpResponseMessage response) {
        if (!response.IsSuccessStatusCode) {
            Assert.Fail(
                $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
            );
        }
    }
}
