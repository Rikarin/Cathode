using Cathode.Contracts;
using Cathode.Dsp.Looping;
using Cathode.Engine.Graph;
using Cathode.Engine.Rig;
using Cathode.Server.Engine;
using Cathode.Server.Midi;
using Cathode.Server.Realtime;
using Cathode.Server.Rig;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>Phase 5 through the API: bypass modes, modifiers, controllers, tempo, scenes, globals, looper, MIDI.</summary>
public sealed class ControlsApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Bypass_modes_and_modifiers_reach_the_compiled_graph() {
        await Put("/api/v1/live/blocks/delay1/bypass-mode", new BypassModeRequest("muteFxIn"));
        var live = await Put(
            "/api/v1/live/modifiers",
            new List<PresetModifier> {
                new() {
                    Block = "delay1",
                    Param = "mix",
                    Source = "lfo1",
                    Min = 10,
                    Max = 60
                }
            }
        );
        using var invalid = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> { new() { Block = "delay1", Param = "mix", Source = "lfo9" } },
            Token
        );

        Assert.Single(live.Preset.Modifiers);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await ServerFixture.Eventually(() => server.Graph?.Modifiers.Count == 1);
        Assert.Equal(BypassKind.MuteFxIn, server.Graph!.BypassModeOf(server.Graph.SlotOf("delay1")));
        Assert.Equal(ModSource.Lfo1, server.Graph.Modifiers[0].Source);
    }

    [Fact]
    public async Task A_dragged_scene_level_or_scene_controller_is_one_undo_step() {
        await Patch("/api/v1/live/scenes/0", new SceneUpdateRequest("Verse"));
        for (var level = -1f; level >= -10f; level -= 1f) {
            await Patch("/api/v1/live/scenes/0", new SceneUpdateRequest(Level: level));
        }

        for (var value = 0.1f; value <= 0.5f; value += 0.1f) {
            await Patch("/api/v1/live/scenes/0", new SceneUpdateRequest(Controllers: [value, 0, 0, 0]));
        }

        var controllersUndone = await Read(await client.PostAsync("/api/v1/live/undo", null, Token));
        var levelUndone = await Read(await client.PostAsync("/api/v1/live/undo", null, Token));

        Assert.Equal(-10f, controllersUndone.Preset.Scenes[0].Level);
        Assert.Equal(0f, controllersUndone.Preset.Scenes[0].Controllers[0]);
        Assert.Equal(0f, levelUndone.Preset.Scenes[0].Level);
        Assert.Equal("Verse", levelUndone.Preset.Scenes[0].Name); // the rename is a step of its own
    }

    [Fact]
    public async Task Controllers_scene_level_and_scene_controllers_reach_the_rig() {
        await Patch("/api/v1/live/controllers", new Dictionary<string, float> { ["lfo1Rate"] = 3.5f });
        await Patch("/api/v1/live/scenes/2", new SceneUpdateRequest(Level: -6, Controllers: [0.25f, 0, 0, 1]));
        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(2));
        using var notController = await client.PatchAsJsonAsync(
            "/api/v1/live/controllers",
            new Dictionary<string, float> { ["tempo"] = 90 },
            Token
        );

        Assert.Equal(HttpStatusCode.BadRequest, notController.StatusCode);
        await ServerFixture.Eventually(() => Rig("level") == -6f);
        Assert.Equal(3.5f, Rig("lfo1Rate"));
        Assert.Equal(0.25f, Rig("scene1"));
        Assert.Equal(1f, Rig("scene4"));
    }

    [Fact]
    public async Task Tempo_is_stored_with_the_preset_and_sent_to_the_engine() {
        var host = server.Host;
        var live = await Put("/api/v1/live/tempo", new TempoRequest(97));

        Assert.Equal(97, live.Preset.Tempo);
        await ServerFixture.Eventually(
            () => Rig("tempo") == 97f,
            () => $"engine tempo {Rig("tempo")} (same engine: {ReferenceEquals(host, server.Host)}), "
                + $"status {server.Services.GetRequiredService<EngineConnection>().Status()}\n{server.Log}"
        );
    }

    [Fact]
    public async Task Midi_taps_set_the_tempo_sample_accurately_and_the_preset_follows() {
        await PutGlobal(static g => g.MidiMappings.Add(new("tap", "cc", 0, 80, "tap")));
        var router = server.Services.GetRequiredService<MidiRouter>();

        // Four taps exactly half a second apart on the engine's clock: 120 BPM, however late they are delivered.
        for (var i = 0; i < 4; i++) {
            router.OnMidi([new(0xB0, 80, 127, 1_000 + i * 24_000)]);
        }

        await ServerFixture.Eventually(() => Math.Abs(server.Host.Engine.Tempo - 120) < 0.01);
        await ServerFixture.Eventually(async () => (await Live()).Preset.Tempo == 120);
    }

    [Fact]
    public async Task Midi_mappings_change_scenes_toggle_bypass_and_drive_external_controllers() {
        await PutGlobal(static g => {
                g.MidiMappings.Add(new("s", "cc", 0, 20, "scene"));
                g.MidiMappings.Add(new("b", "note", 1, 60, "bypass", "delay1"));
                g.MidiMappings.Add(new("x", "cc", 0, 11, "external", Value: 2));
            }
        );
        var router = server.Services.GetRequiredService<MidiRouter>();

        router.OnMidi([new(0xB0, 20, 3, 0), new(0x90, 60, 100, 0), new(0xB0, 11, 127, 0)]);

        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 3);
        await ServerFixture.Eventually(() => server.IsBypassed("delay1"));
        await ServerFixture.Eventually(() => Rig("ext2") == 1f);
    }

    [Fact]
    public async Task Footswitches_tap_hold_and_light_their_leds() {
        var sent = new List<byte[]>();
        server.Host.NativeMidiSend = (_, bytes) => {
            lock (sent) {
                sent.Add(bytes);
            }

            return true;
        };
        await PutGlobal(static g => {
                g.Footswitches.Add(
                    new(
                        "board",
                        "Board",
                        [new(1, "Scene 2", "cc", 1, 30, new("scene", Value: 1), new("scene", Value: 4))]
                    )
                );
                g.ActiveLayout = "board";
            }
        );
        var router = server.Services.GetRequiredService<MidiRouter>();

        router.OnMidi([new(0xB0, 30, 127, 0)]);
        router.OnMidi([new(0xB0, 30, 0, 0)]); // a quick tap
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 1);
        await ServerFixture.Eventually(() => Sent(sent, [0xB0, 30, 127])); // its LED: scene 2 is active

        // Held: the hold action fires once the switch has been down for the hold time. Waited for, not slept past: on a
        // busy machine the hold timer can run late, and a release before it fires is a tap.
        router.OnMidi([new(0xB0, 30, 127, 0)]);
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 4);
        router.OnMidi([new(0xB0, 30, 0, 0)]); // nothing on release
        await ServerFixture.Eventually(() => Sent(sent, [0xB0, 30, 0]));
        await Task.Delay(300, Token);
        Assert.Equal(4, (await Live()).ActiveScene); // a tap would have gone back to scene 2
    }

    [Fact]
    public async Task A_repeated_down_is_one_press_with_one_hold() {
        await PutGlobal(static g => {
                g.Footswitches.Add(
                    new(
                        "board",
                        "Board",
                        [new(1, "Next", "cc", 1, 31, new("scene", Value: 6), new("sceneNext"))]
                    )
                );
                g.ActiveLayout = "board";
            }
        );
        var router = server.Services.GetRequiredService<MidiRouter>();

        // A CC pedal held past 64 keeps sending "down": one hold action, not one per message.
        for (var i = 0; i < 5; i++) {
            router.OnMidi([new(0xB0, 31, 100 + i, 0)]);
        }

        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 1);
        router.OnMidi([new(0xB0, 31, 127, 0)]); // still held, after the hold fired
        await Task.Delay(MidiRouter.HoldTime * 3, Token);
        Assert.Equal(1, (await Live()).ActiveScene);

        router.OnMidi([new(0xB0, 31, 0, 0)]); // the release after a hold does nothing
        router.OnMidi([new(0xB0, 31, 127, 0)]);
        router.OnMidi([new(0xB0, 31, 0, 0)]); // and the next press is a fresh one: a tap
        await ServerFixture.Eventually(async () => (await Live()).ActiveScene == 6);
    }

    [Fact]
    public async Task Mute_actions_run_in_order() {
        await PutGlobal(static g => g.MidiMappings.Add(new("m", "note", 0, 85, "mute")));
        var router = server.Services.GetRequiredService<MidiRouter>();

        // Three toggles: each must see the one before it, or two would read the same state.
        router.OnMidi([new(0x90, 85, 127, 0), new(0x90, 85, 127, 0), new(0x90, 85, 127, 0)]);

        await ServerFixture.Eventually(async () => (await Live()).Muted);
        await Task.Delay(300, Token);
        Assert.True((await Live()).Muted);
    }

    [Fact]
    public async Task The_tuner_has_one_state_for_rest_midi_and_its_led() {
        var sent = new List<byte[]>();
        server.Host.NativeMidiSend = (_, bytes) => {
            lock (sent) {
                sent.Add(bytes);
            }

            return true;
        };
        await PutGlobal(static g => {
                g.Footswitches.Add(new("pedalboard", "Pedalboard", [new(1, "Tuner", "cc", 1, 32, new("tuner"))]));
                g.ActiveLayout = "pedalboard";
            }
        );
        var rig = server.Services.GetRequiredService<LiveRig>();
        var events = new List<RigEvent>();
        rig.Changes += e => {
            lock (events) {
                events.Add(e);
            }
        };
        var router = server.Services.GetRequiredService<MidiRouter>();

        // The editor turns it on: the LED follows, and an event tells the other clients.
        await Ok(client.PutAsJsonAsync("/api/v1/tuner", new BypassRequest(false), Token));
        Assert.True(rig.TunerOn);
        await ServerFixture.Eventually(() => Sent(sent, [0xB0, 32, 127]));
        lock (events) {
            Assert.Contains(events, static e => e is { Type: "tunerChanged", Value: 1 });
        }

        // The footswitch toggles what the editor set: off, not on again.
        lock (sent) {
            sent.Clear();
        }

        router.OnMidi([new(0xB0, 32, 127, 0)]);
        await ServerFixture.Eventually(() => {
                lock (events) {
                    return events.Exists(static e => e is { Type: "tunerChanged", Value: 0 });
                }
            }
        );
        Assert.False(rig.TunerOn);
        await ServerFixture.Eventually(() => Sent(sent, [0xB0, 32, 0]));
    }

    [Fact]
    public async Task Global_settings_changed_from_midi_publish_an_event() {
        await PutGlobal(static g => {
                g.MidiMappings.Add(new("metro", "note", 0, 86, "metronome"));
                g.MidiMappings.Add(new("manual", "cc", 0, 87, "manual", Value: 2));
            }
        );
        using var socket = await server.Server.CreateWebSocketClient()
            .ConnectAsync(new(server.Server.BaseAddress, "ws"), Token);
        await NextEvent(socket, "connected"); // registered: from here on it sees every change
        var router = server.Services.GetRequiredService<MidiRouter>();

        router.OnMidi([new(0x90, 86, 127, 0)]); // the metronome, from a footswitch
        await NextEvent(socket, "globalChanged");
        router.OnMidi([new(0xB0, 87, 127, 0)]); // Manual C, from a pedal
        await NextEvent(socket, "globalChanged");

        var global = await Global();
        Assert.True(global.Metronome);
        Assert.Equal(1f, global.Manual[2]);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
    }

    /// <summary>Reads WebSocket messages until one of type <paramref name="type" /> arrives.</summary>
    static async Task NextEvent(WebSocket socket, string type) {
        var buffer = new byte[256 * 1024];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); // generous: a busy machine
        while (true) {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (message?["type"]?.GetValue<string>() == type) {
                return;
            }
        }
    }

    [Fact]
    public async Task Selecting_a_scene_sends_its_midi_messages() {
        var sent = new List<byte[]>();
        server.Host.NativeMidiSend = (_, bytes) => {
            lock (sent) {
                sent.Add(bytes);
            }

            return true;
        };
        server.Services.GetRequiredService<MidiRouter>();
        var live = await Patch(
            "/api/v1/live/scenes/2",
            new SceneUpdateRequest(Midi: [new("pc", 2, 5), new("cc", 1, 7, 100)])
        );
        using var invalid = await client.PatchAsJsonAsync(
            "/api/v1/live/scenes/2",
            new SceneUpdateRequest(Midi: [new("nrpn", 1, 7, 100)]),
            Token
        );

        await Put("/api/v1/live/scenes/active", new SceneSelectRequest(2));

        Assert.Equal(2, live.Preset.Scenes[2].Midi.Count);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await ServerFixture.Eventually(() => Sent(sent, [0xC1, 5]) && Sent(sent, [0xB0, 7, 100]));
    }

    [Fact]
    public async Task The_rta_takes_its_resolution_decay_and_window() {
        using var ok = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(true, 12, 500, "flatTop"), Token);
        using var bands = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(true, 5), Token);
        using var window = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(true, Window: "kaiser"), Token);
        using var off = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(false), Token);

        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bands.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, window.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.False(server.Services.GetRequiredService<EngineConnection>().RtaEnabled);
    }

    [Fact]
    public async Task The_tuner_scope_and_rta_run_while_any_client_has_them_on() {
        var engine = server.Services.GetRequiredService<EngineConnection>();
        var analysis = server.Services.GetRequiredService<AnalysisSubscriptions>();

        async Task Set(string who, bool on) {
            using var tuner = await client.PutAsJsonAsync($"/api/v1/tuner?client={who}", new BypassRequest(!on), Token);
            using var scope = await client.PutAsJsonAsync($"/api/v1/scope?client={who}", new ScopeRequest(on), Token);
            using var rta = await client.PutAsJsonAsync($"/api/v1/rta?client={who}", new RtaRequest(on), Token);
            Assert.Equal(HttpStatusCode.NoContent, tuner.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, scope.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, rta.StatusCode);
        }

        await Set("desktop", true);
        await Set("tablet", true);
        await Set("desktop", false); // the desktop closes its dialog; the tablet still uses it
        Assert.True(analysis.TunerOn);
        Assert.True(engine.ScopeEnabled);
        Assert.True(engine.RtaEnabled);

        await Set("tablet", false);
        Assert.False(analysis.TunerOn);
        Assert.False(engine.ScopeEnabled);
        Assert.False(engine.RtaEnabled);

        // A caller that does not say who it is turns them off for everyone, as before.
        await Set("tablet", true);
        using var off = await client.PutAsJsonAsync("/api/v1/scope", new ScopeRequest(false), Token);
        Assert.False(engine.ScopeEnabled);
    }

    [Fact]
    public async Task What_a_client_left_on_is_released_with_its_last_socket() {
        var engine = server.Services.GetRequiredService<EngineConnection>();
        var url = new Uri(server.Server.BaseAddress, "ws?client=tablet");
        using var first = await server.Server.CreateWebSocketClient().ConnectAsync(url, Token);
        using var second = await server.Server.CreateWebSocketClient().ConnectAsync(url, Token);
        using var on = await client.PutAsJsonAsync("/api/v1/scope?client=tablet", new ScopeRequest(true), Token);
        Assert.True(engine.ScopeEnabled);

        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
        await Task.Delay(200, Token);
        Assert.True(engine.ScopeEnabled); // a reconnect overlapping the old socket keeps it

        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Token);
        await ServerFixture.Eventually(() => !engine.ScopeEnabled);
    }

    [Fact]
    public async Task A_modifier_can_engage_a_block_but_not_the_input() {
        var live = await Put(
            "/api/v1/live/modifiers",
            new List<PresetModifier> { new() { Block = "delay1", Param = "engage", Source = "manualB" } }
        );
        using var input = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> { new() { Block = "in1", Param = "engage", Source = "manualB" } },
            Token
        );

        Assert.Single(live.Preset.Modifiers);
        Assert.Equal(HttpStatusCode.BadRequest, input.StatusCode);
        await ServerFixture.Eventually(() => server.Graph!.Modifiers.Any(static m =>
                m.ParamIndex == ModifierBinding.Engage
            )
        );
    }

    [Fact]
    public async Task Midi_learn_binds_the_next_controller_moved() {
        await Ok(client.PutAsJsonAsync("/api/v1/midi/learn", new MidiLearnRequest("tuner"), Token));
        server.Services.GetRequiredService<MidiRouter>().OnMidi([new(0xB2, 64, 127, 0)]);

        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

        Assert.Contains(
            global.MidiMappings,
            static m => m is { Message: "cc", Number: 64, Channel: 3, Action: "tuner" }
        );
        Assert.Null(server.Services.GetRequiredService<MidiRouter>().Learning);
    }

    [Fact]
    public async Task Global_settings_drive_the_output_eq_metronome_and_manual_and_are_validated() {
        await PutGlobal(static g => {
                g.Outputs[0].Eq["g1k"] = 6;
                g.Metronome = true;
            }
        );
        await Ok(client.PutAsJsonAsync("/api/v1/live/manual/1", new ValueRequest(0.75f), Token));
        var bad = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        bad.TempoMode = "whenever";
        using var rejected = await client.PutAsJsonAsync("/api/v1/global", bad, Token);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var eq = server.Host.Engine.GlobalEq;
        await ServerFixture.Eventually(() => eq.Params.GetTarget(eq.Descriptor.IndexOf("g1k")) == 6f);
        Assert.Equal(1f, Rig("metronome"));
        Assert.Equal(0.75f, Rig("manualB"));
    }

    [Fact]
    public async Task The_looper_records_and_exports_a_wav() {
        await Ok(client.PostAsync("/api/v1/looper/record", null, Token));
        // Stop once the audio thread has recorded something, rather than after a fixed time: on a busy machine the null
        // backend can take both commands in one callback, which records nothing.
        await ServerFixture.Eventually(() => server.Host.Engine.Looper!.Length > 0);
        await Ok(client.PostAsync("/api/v1/looper/stop", null, Token));
        await ServerFixture.Eventually(() => server.Host.Engine.Looper!.State != LooperState.Recording);

        using var export = await client.GetAsync("/api/v1/looper/export", Token);
        var wav = await export.Content.ReadAsByteArrayAsync(Token);

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        using var unknown = await client.PostAsync("/api/v1/looper/rewind", null, Token);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Global_blocks_share_settings_and_channels_copy() {
        await Patch("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 444 });
        var linked = await Post("/api/v1/live/blocks/delay1/global", new LinkGlobalRequest("House delay"));
        await Patch("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["feedback"] = 55 });
        var shared = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!.Blocks.Single();

        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 1, 2));
        var used = await Put($"/api/v1/live/blocks/delay2/global/{shared.Id}", new { });
        var copied = await Post("/api/v1/live/blocks/delay2/channels/copy", new CopyChannelRequest("A", "C"));

        Assert.Equal(shared.Id, linked.Preset.Blocks.Single(static b => b.Id == "delay1").Global);
        Assert.Equal(55f, shared.Channels["A"]["feedback"]);
        Assert.Equal(444f, used.Preset.Blocks.Single(static b => b.Id == "delay2").Channels["A"]["time"]);
        Assert.Equal(444f, copied.Preset.Blocks.Single(static b => b.Id == "delay2").Channels["C"]["time"]);
    }

    [Fact]
    public async Task A_global_block_is_made_once_and_deleting_it_leaves_the_linked_blocks_their_settings() {
        await Patch("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 444 });
        await Post("/api/v1/live/blocks/delay1/global", new LinkGlobalRequest("House delay"));

        // Making it global again is refused: no second copy in the library.
        using var again = await client.PostAsJsonAsync(
            "/api/v1/live/blocks/delay1/global",
            new LinkGlobalRequest("House delay"),
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var shared = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!.Blocks.Single();

        using var response = await client.DeleteAsync($"/api/v1/live/globals/{shared.Id}", Token);
        var deleted = await Read(response);
        var delay = deleted.Preset.Blocks.Single(static b => b.Id == "delay1");
        Assert.Null(delay.Global);
        Assert.Equal(444f, delay.Channels["A"]["time"]);
        Assert.Empty((await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!.Blocks);

        using var missing = await client.DeleteAsync($"/api/v1/live/globals/{shared.Id}", Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Mute_silences_the_rig_live_without_touching_the_preset() {
        var before = await Live();
        var muted = await Put("/api/v1/live/mute", new MuteRequest(true));
        Assert.True(muted.Muted);
        Assert.Equal(before.Dirty, muted.Dirty);
        await ServerFixture.Eventually(() => Rig("mute") == 1f);

        var unmuted = await Put("/api/v1/live/mute", new MuteRequest(false));
        Assert.False(unmuted.Muted);
        await ServerFixture.Eventually(() => Rig("mute") == 0f);
    }

    float Rig(string id) => server.Host.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf(id));

    static bool Sent(List<byte[]> sent, byte[] message) {
        lock (sent) {
            return sent.Any(m => m.SequenceEqual(message));
        }
    }

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task PutGlobal(Action<GlobalSettings> change) {
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        change(global);
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
    }

    async Task<LiveState> Live() => (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

    async Task<LiveState> Post(string url, object body) => await Read(await client.PostAsJsonAsync(url, body, Token));

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
