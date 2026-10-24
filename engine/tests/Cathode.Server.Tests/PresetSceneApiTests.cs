using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Rig;
using Cathode.Server.Midi;
using Cathode.Server.Rig;
using Microsoft.Extensions.DependencyInjection;

namespace Cathode.Server.Tests;

/// <summary>
///     Preset and scene features through the API (docs/08-blocks-parity.md, presets and scenes): Scene Ignore, scene
///     recall settings, scene and channel tools, MIDI actions, modifier scope, spillover, telemetry and the list export.
/// </summary>
public sealed class PresetSceneApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Post("/api/v1/live/new");
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));
        await Patch("/api/v1/live/blocks/delay1/params?channel=A", new Dictionary<string, float> { ["time"] = 500 });
        await Patch("/api/v1/live/blocks/delay1/params?channel=B", new Dictionary<string, float> { ["time"] = 120 });
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task The_name_author_and_tags_are_set_as_one_undoable_edit_that_survives_save() {
        var info = await Patch("/api/v1/live/info", new PresetInfoRequest(" Lead ", " Jiu ", ["solo", " ", "solo", "lead"]));
        Assert.Equal(("Lead", "Jiu"), (info.Preset.Name, info.Preset.Author));
        Assert.Equal(["solo", "lead"], info.Preset.Tags);
        Assert.True(info.Dirty);

        var saved = await Post("/api/v1/live/save", new SaveRequest(AsNew: true));
        Assert.Equal("Jiu", saved.Preset.Author);
        var cleared = await Patch("/api/v1/live/info", new PresetInfoRequest(Author: ""));
        Assert.Null(cleared.Preset.Author);
        Assert.Equal("Lead", cleared.Preset.Name); // a null field is left as it is

        var undone = await Post("/api/v1/live/undo");
        Assert.Equal("Jiu", undone.Preset.Author);
    }

    [Fact]
    public async Task Scene_ignore_holds_a_block_across_scenes_until_its_channel_stops_ignoring() {
        await Put("/api/v1/live/scenes/1/blocks/delay1", new SceneBlockState(true, "B"));
        var ignoring = await Put(
            "/api/v1/live/blocks/delay1/channels/A/scene-ignore",
            new SceneIgnoreRequest(true)
        );
        Assert.Equal(["A"], ignoring.Preset.Blocks.Single(b => b.Id == "delay1").SceneIgnore);

        var held = await Put("/api/v1/live/scenes/active", new SceneSelectRequest(1));

        Assert.Equal(new SceneBlockState(false, "A"), held.Held!["delay1"]);
        Assert.Equal(new SceneBlockState(true, "B"), held.Preset.Scenes[1].StateOf("delay1")); // the scene is untouched
        await Task.Delay(100, Token);
        Assert.False(server.IsBypassed("delay1"));
        Assert.Equal(500, server.Target("delay1", "time"));

        // Manual control still works while held, and a channel without Scene Ignore makes it follow scenes again.
        await Put("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true));
        await ServerFixture.Eventually(() => server.IsBypassed("delay1"));
        var following = await Put("/api/v1/live/blocks/delay1/channel", new ChannelRequest("B"));
        Assert.Null(following.Held);
        await ServerFixture.Eventually(() => server.Target("delay1", "time") == 120);
        var next = await Put("/api/v1/live/scenes/active", new SceneSelectRequest(2));
        Assert.Null(next.Held);
        await ServerFixture.Eventually(() => !server.IsBypassed("delay1") && server.Target("delay1", "time") == 500);
    }

    [Fact]
    public async Task Scene_revert_recalls_the_saved_scene_and_off_keeps_changes_made_while_playing() {
        await Post("/api/v1/live/save", new SaveRequest("Revert", true));
        await Put("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true));
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(1));
        var kept = await Put("/api/v1/live/scenes/active", new SceneSelectRequest(0));
        Assert.True(kept.Preset.Scenes[0].StateOf("delay1").Bypassed);

        await PutGlobal(g => g.SceneRevert = true);
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(1));
        var reverted = await Put("/api/v1/live/scenes/active", new SceneSelectRequest(0));

        Assert.False(reverted.Preset.Scenes[0].StateOf("delay1").Bypassed);
        await ServerFixture.Eventually(() => !server.IsBypassed("delay1"));
    }

    [Fact]
    public async Task A_preset_loads_on_the_scene_it_was_saved_on_or_on_the_global_default_scene() {
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(3));
        var saved = await Post("/api/v1/live/save", new SaveRequest("Scenes", true));
        Assert.Equal(3, saved.Preset.SavedScene);
        await Post("/api/v1/live/new");

        var asSaved = await Put($"/api/v1/live/preset/{saved.Preset.Id}", new { });
        await PutGlobal(g => g.DefaultScene = 5);
        var fixedScene = await Put($"/api/v1/live/preset/{saved.Preset.Id}", new { });

        Assert.Equal(3, asSaved.ActiveScene);
        Assert.Equal(5, fixedScene.ActiveScene);
    }

    [Fact]
    public async Task The_default_scene_is_validated() {
        var global = await Global();
        global.DefaultScene = 8;
        using var response = await client.PutAsJsonAsync("/api/v1/global", global, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_startup_preset_loads_when_the_server_starts() {
        var saved = await Post("/api/v1/live/save", new SaveRequest("Startup", true));
        await PutGlobal(g => g.StartupPreset = saved.Preset.Id);
        await Post("/api/v1/live/new");

        await server.Services.GetRequiredService<LiveRig>().LoadStartupAsync();

        Assert.Equal(saved.Preset.Id, (await Live()).Preset.Id);
        Assert.Equal(saved.Preset.Id, (await Global()).StartupPreset);
    }

    [Fact]
    public async Task Ignore_redundant_pc_leaves_the_loaded_preset_alone() {
        var saved = await Post("/api/v1/live/save", new SaveRequest("PC", true));
        var bank = new Bank { Name = "Gig", Slots = [saved.Preset.Id] };
        await Ok(client.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));
        await PutGlobal(g => {
                g.MidiMappings.Add(new("pc", "pc", 0, 0, "presetSlot", bank.Id));
                g.IgnoreRedundantPc = true;
            }
        );
        var router = server.Services.GetRequiredService<MidiRouter>();
        await Put("/api/v1/live/blocks/delay1/bypass", new BypassRequest(true)); // an unsaved edit

        router.OnMidi([new(0xC0, 0, 0, 0)]);
        await Task.Delay(300, Token);
        Assert.True((await Live()).Dirty); // not reloaded

        await PutGlobal(g => g.IgnoreRedundantPc = false);
        router.OnMidi([new(0xC0, 0, 0, 0)]);
        await ServerFixture.Eventually(async () => !(await Live()).Dirty); // reloaded: the edit is gone
    }

    [Fact]
    public async Task Scenes_copy_and_swap_and_the_scene_manager_edits_any_scene_with_undo() {
        await Patch("/api/v1/live/scenes/2", new SceneUpdateRequest("Lead", 3f, [0.5f, 0, 0, 0]));
        await Put("/api/v1/live/scenes/2/blocks/delay1", new SceneBlockState(true, "B"));

        var copied = await Post("/api/v1/live/scenes/copy", new ScenePairRequest(2, 5));
        Assert.Equal("Lead", copied.Preset.Scenes[5].Name);
        Assert.Equal(3f, copied.Preset.Scenes[5].Level);
        Assert.Equal(new SceneBlockState(true, "B"), copied.Preset.Scenes[5].StateOf("delay1"));

        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(2));
        var swapped = await Post("/api/v1/live/scenes/swap", new ScenePairRequest(2, 0));
        Assert.Equal("Lead", swapped.Preset.Scenes[0].Name);
        Assert.Equal("Scene 1", swapped.Preset.Scenes[2].Name);
        Assert.Equal(0, swapped.ActiveScene); // the rig follows its scene, so nothing audible changes

        // The active scene takes a Scene Manager edit at once.
        await Put("/api/v1/live/scenes/0/blocks/delay1", new SceneBlockState(false, "B"));
        await ServerFixture.Eventually(() => !server.IsBypassed("delay1"));
        var undone = await Post("/api/v1/live/undo");
        Assert.True(undone.Preset.Scenes[0].StateOf("delay1").Bypassed);
        await ServerFixture.Eventually(() => server.IsBypassed("delay1"));
    }

    [Fact]
    public async Task Channels_swap_and_reset_and_a_block_resets_to_its_defaults() {
        await Put("/api/v1/live/blocks/delay1/channels/A/scene-ignore", new SceneIgnoreRequest(true));

        var swapped = await Post("/api/v1/live/blocks/delay1/channels/swap", new CopyChannelRequest("A", "B"));
        var block = swapped.Preset.Blocks.Single(b => b.Id == "delay1");
        Assert.Equal(120, block.Channels["A"]["time"]);
        Assert.Equal(500, block.Channels["B"]["time"]);
        Assert.Equal(["B"], block.SceneIgnore);
        await ServerFixture.Eventually(() => server.Target("delay1", "time") == 120);

        var reset = await Post("/api/v1/live/blocks/delay1/channels/A/reset");
        Assert.False(reset.Preset.Blocks.Single(b => b.Id == "delay1").Channels.ContainsKey("A"));
        var defaultTime = DelayBlock.Info.Params[DelayBlock.Time].Default;
        await ServerFixture.Eventually(() => server.Target("delay1", "time") == defaultTime);

        var all = await Post("/api/v1/live/blocks/delay1/reset");
        Assert.Empty(all.Preset.Blocks.Single(b => b.Id == "delay1").Channels);
        Assert.Empty(all.Preset.Blocks.Single(b => b.Id == "delay1").SceneIgnore);
        var undone = await Post("/api/v1/live/undo");
        Assert.Equal(500, undone.Preset.Blocks.Single(b => b.Id == "delay1").Channels["B"]["time"]);
    }

    [Fact]
    public async Task Resetting_a_drive_keeps_the_running_graph_unless_its_oversampling_changes() {
        // A Drive has a parameter only a recompile applies (Oversampling). Resetting one used to rebuild the graph
        // regardless, which starts every block of the preset afresh: the reset of a drive nowhere near the signal
        // path restarted the delay (and, in the reported preset, an arpeggiating pitch block) mid-note. drive1 is
        // that drive: on row 2, without cables.
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 5));
        await Patch("/api/v1/live/blocks/drive1/params?channel=A", new Dictionary<string, float> { ["drive"] = 8 });
        await ServerFixture.Eventually(() => server.Target("drive1", "drive") == 8);
        var running = server.Graph;
        var delay = server.Block("delay1");

        await Post("/api/v1/live/blocks/drive1/reset");
        var defaultDrive = DriveBlock.Info.Params[DriveBlock.Info.IndexOf("drive")].Default;
        await ServerFixture.Eventually(() => server.Target("drive1", "drive") == defaultDrive);
        await Post("/api/v1/live/blocks/drive1/channels/A/reset");
        await Post("/api/v1/live/blocks/drive1/channels/swap", new CopyChannelRequest("A", "B"));
        await Task.Delay(100, Token);
        Assert.Same(running, server.Graph);
        Assert.Same(delay, server.Block("delay1"));

        // A changed oversampling still reaches the compiler.
        var fourX = new Dictionary<string, float> { ["oversampling"] = 3 };
        await Patch("/api/v1/live/blocks/drive1/params?channel=A", fourX);
        await ServerFixture.Eventually(() => server.Graph != running && server.Target("drive1", "oversampling") == 3);
        var rebuilt = server.Graph;
        await Post("/api/v1/live/blocks/drive1/reset");
        await ServerFixture.Eventually(() => server.Graph != rebuilt && server.Target("drive1", "oversampling") == 0);
    }

    [Fact]
    public async Task Midi_steps_the_scene_level_and_saves_and_a_high_scene_cc_steps_to_the_next_scene() {
        var saved = await Post("/api/v1/live/save", new SaveRequest("Levels", true));
        await PutGlobal(g => {
                g.MidiMappings.Add(new("up", "cc", 0, 40, "sceneLevelUp"));
                g.MidiMappings.Add(new("down", "cc", 0, 41, "sceneLevelDown"));
                g.MidiMappings.Add(new("save", "cc", 0, 42, "save"));
                g.MidiMappings.Add(new("scene", "cc", 0, 43, "scene"));
            }
        );
        var router = server.Services.GetRequiredService<MidiRouter>();
        var presets = server.Services.GetRequiredService<Stores.PresetStore>();

        router.OnMidi([new(0xB0, 40, 127, 0), new(0xB0, 40, 127, 0), new(0xB0, 41, 127, 0)]);
        await ServerFixture.Eventually(() => presets.Get(saved.Preset.Id)!.Scenes[0].Level == 1f);
        Assert.False((await Live()).Dirty);
        await ServerFixture.Eventually(() => Rig("level") == 1f);

        await Patch("/api/v1/live/scenes/0", new SceneUpdateRequest("Renamed"));
        router.OnMidi([new(0xB0, 42, 127, 0)]);
        await ServerFixture.Eventually(() => presets.Get(saved.Preset.Id)!.Scenes[0].Name == "Renamed");

        router.OnMidi([new(0xB0, 43, 7, 0)]);
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 7);
        router.OnMidi([new(0xB0, 43, 127, 0)]); // above 63: the next scene, wrapping
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 0);
        router.OnMidi([new(0xB0, 43, 100, 0)]);
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 1);
    }

    [Fact]
    public async Task A_modifier_scoped_to_one_channel_drives_its_parameter_only_there() {
        await Put(
            "/api/v1/live/modifiers",
            new List<PresetModifier> {
                new() { Block = "delay1", Param = "feedback", Source = "manualA", Min = 80, Max = 80, Channel = "B" }
            }
        );
        await Task.Delay(150, Token);
        var stock = DelayBlock.Info.Params[DelayBlock.Feedback].Default;
        Assert.Equal(stock, server.Target("delay1", "feedback")); // on A the channel's own value

        await Put("/api/v1/live/blocks/delay1/channel", new ChannelRequest("B"));
        await ServerFixture.Eventually(() => server.Target("delay1", "feedback") == 80);
        await Put("/api/v1/live/blocks/delay1/channel", new ChannelRequest("A"));
        await ServerFixture.Eventually(() => server.Target("delay1", "feedback") == stock);

        using var bad = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> { new() { Block = "delay1", Param = "feedback", Channel = "E" } },
            Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task The_spillover_setting_reaches_the_rig() {
        Assert.Equal(1f, Rig("spillover"));
        await PutGlobal(g => g.PresetSpillover = false);
        await ServerFixture.Eventually(() => Rig("spillover") == 0f);
        Assert.False((await Global()).PresetSpillover);
    }

    [Fact]
    public async Task Telemetry_carries_block_meters_by_id_and_the_output_loudness() {
        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        var buffer = new byte[256 * 1024];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        JsonNode? blocks = null;
        JsonNode? frame = null;
        while (blocks?["delay1"] is null) {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            frame = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
            if (frame["type"]!.GetValue<string>() == "telemetry") {
                blocks = frame["blocks"];
            }
        }

        Assert.NotNull(blocks["in1"]);
        Assert.Null(blocks["shunt-r0c1"]); // shunts have no meter
        Assert.NotNull(frame!["loudnessMomentary"]);
        Assert.NotNull(frame["loudnessShortTerm"]);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
    }

    [Fact]
    public async Task The_preset_list_exports_as_text_with_scene_names() {
        await Patch("/api/v1/live/scenes/1", new SceneUpdateRequest("Solo"));
        await Post("/api/v1/live/save", new SaveRequest("Zebra", true));
        await Post("/api/v1/live/save", new SaveRequest("Alpha", true));

        var text = await client.GetStringAsync("/api/v1/presets/list.txt", Token);

        Assert.StartsWith("Cathode presets (2)", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Alpha", StringComparison.Ordinal) < text.IndexOf("Zebra", StringComparison.Ordinal));
        Assert.Contains("Scene 2: Solo", text, StringComparison.Ordinal);
    }

    float Rig(string id) => server.Host.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf(id));

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task PutGlobal(Action<GlobalSettings> change) {
        var global = await Global();
        change(global);
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
    }

    async Task<LiveState> Live() => (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

    async Task<LiveState> Post(string url, object? body = null) =>
        await Read(await client.PostAsJsonAsync(url, body ?? new { }, Token));

    async Task<LiveState> Put(string url, object body) => await Read(await client.PutAsJsonAsync(url, body, Token));

    async Task<LiveState> Patch(string url, object body) => await Read(await client.PatchAsJsonAsync(url, body, Token));

    static async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        if (!response.IsSuccessStatusCode) {
            Assert.Fail(
                $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
            );
        }
    }

    static async Task<LiveState> Read(HttpResponseMessage response) {
        using (response) {
            if (!response.IsSuccessStatusCode) {
                Assert.Fail(
                    $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
                );
            }

            return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
        }
    }
}
