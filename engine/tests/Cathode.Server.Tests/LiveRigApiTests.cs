using Cathode.Contracts;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.IO;
using Cathode.Testing;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;

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

        Assert.Contains(blocks!, static b => b.TypeId == "nam" && b.AssetSlots.Contains("model"));
        Assert.Contains(
            blocks!,
            static b => b.TypeId == "cab" && b.AssetSlots.SequenceEqual(["slot1", "slot2", "slot3", "slot4"])
        );
        Assert.DoesNotContain(blocks!, static b => b.TypeId == "dynacab");
        Assert.Contains(
            blocks!.Single(static b => b.TypeId == "delay").Params,
            static p => p.Id == "time" && p.Unit == "ms"
        );
    }

    [Fact]
    public async Task Dropping_a_block_on_a_shunt_replaces_it_and_keeps_the_signal_path() {
        var state = await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 3));

        Assert.DoesNotContain(state.Preset.Blocks, static b => b is { Row: 2, Column: 3, Type: "shunt" });
        Assert.Contains(state.Preset.Cables, static c => c.To == "drive1");
        Assert.Contains(state.Preset.Cables, static c => c.From == "drive1");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("drive1") >= 0);
    }

    [Fact]
    public async Task Parameters_reach_the_engine_only_for_the_active_channel() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 2));

        await Patch("/api/v1/live/blocks/delay1/params", new Dictionary<string, float> { ["time"] = 500 });
        await Patch("/api/v1/live/blocks/delay1/params?channel=B", new Dictionary<string, float> { ["time"] = 120 });

        await ServerFixture.Eventually(() => server.Target("delay1", "time") == 500);
        var state = await Live();
        Assert.Equal(500, state.Preset.Blocks.Single(static b => b.Id == "delay1").Channels["A"]["time"]);
        Assert.Equal(120, state.Preset.Blocks.Single(static b => b.Id == "delay1").Channels["B"]["time"]);
        await Task.Delay(100, Token);
        Assert.Equal(500, server.Target("delay1", "time")); // channel B's edit did not leak into the running sound
    }

    [Fact]
    public async Task The_input_and_output_take_parameters_and_channels_but_no_bypass() {
        await Patch(
            "/api/v1/live/blocks/in1/params",
            new Dictionary<string, float> { ["type"] = 1, ["threshold"] = -50 }
        );
        await Patch("/api/v1/live/blocks/out1/params?channel=B", new Dictionary<string, float> { ["level1"] = -6 });
        await ServerFixture.Eventually(() => server.Target("in1", "threshold") == -50);

        await Put("/api/v1/live/blocks/out1/channel", new ChannelRequest("B"));
        await ServerFixture.Eventually(() => server.Target("out1", "level1") == -6);
        var bypass = await client.PutAsJsonAsync("/api/v1/live/blocks/in1/bypass", new BypassRequest(true), Token);
        Assert.Equal(HttpStatusCode.BadRequest, bypass.StatusCode);
    }

    [Fact]
    public async Task A_look_ahead_change_recompiles_so_the_latency_is_compensated() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("compressor", 2, 2));
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("compressor1") >= 0);
        var generation = server.Graph!.Generation;

        await Patch(
            "/api/v1/live/blocks/compressor1/params",
            new Dictionary<string, float> { ["lookAhead"] = 5 }
        ); // 5 ms

        await ServerFixture.Eventually(() => server.Graph!.Generation > generation && server.Graph.LatencySamples == 240
        );
        generation = server.Graph!.Generation;
        await Patch("/api/v1/live/blocks/compressor1/params", new Dictionary<string, float> { ["threshold"] = -10 });
        await ServerFixture.Eventually(() => server.Target("compressor1", "threshold") == -10);
        Assert.Equal(generation, server.Graph!.Generation); // an ordinary knob does not
    }

    [Fact]
    public async Task Scenes_switch_channels_and_bypass_without_recompiling() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 1));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 2));
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
        await Post("/api/v1/live/blocks", new AddBlockRequest("reverb", 2, 5));

        var undone = await Post("/api/v1/live/undo");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("reverb1") < 0);
        var redone = await Post("/api/v1/live/redo");

        Assert.DoesNotContain(undone.Preset.Blocks, static b => b.Id == "reverb1");
        Assert.Contains(redone.Preset.Blocks, static b => b.Id == "reverb1");
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("reverb1") >= 0);
    }

    [Fact]
    public async Task Knob_drags_coalesce_into_one_undo_step() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", 2, 1));
        var before = await Live();
        for (var level = -1f; level >= -10f; level -= 1f) {
            await Patch("/api/v1/live/blocks/volpan1/params", new Dictionary<string, float> { ["level"] = level });
        }

        var undone = await Post("/api/v1/live/undo");

        Assert.Equal(
            before.Preset.Blocks.Single(static b => b.Id == "volpan1").Channels.Count,
            undone.Preset.Blocks.Single(static b => b.Id == "volpan1").Channels.Count
        );
        Assert.True(undone.CanUndo); // the add-block step is still there
    }

    [Fact]
    public async Task Stale_revisions_are_rejected_with_412() {
        var state = await Live();
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 1));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/live/blocks");
        request.Content = JsonContent.Create(new AddBlockRequest("delay", 2, 2));
        request.Headers.IfMatch.Add(new($"\"{state.Revision}\""));
        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_edits_return_problem_details() {
        var badCable = await client.PostAsJsonAsync(
            "/api/v1/live/cables",
            new CableRequest("shunt-r2c3", "shunt-r2c1"),
            Token
        );
        var unknownParam = await client.PatchAsJsonAsync(
            "/api/v1/live/blocks/shunt-r2c1/params",
            new Dictionary<string, float> { ["x"] = 1 },
            Token
        );
        var badChannel = await client.PutAsJsonAsync("/api/v1/live/blocks/in1/channel", new ChannelRequest("E"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, badCable.StatusCode);
        var problem = await badCable.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("later column", problem!.Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknownParam.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badChannel.StatusCode);
    }

    [Fact]
    public async Task Assets_upload_by_content_and_a_broken_model_is_rolled_back() {
        var ir = new MemoryStream();
        WavFile.Write(
            ir,
            new(48_000, [Signals.Noise(512, 1).Select(static (v, i) => v * MathF.Exp(-i / 80f)).ToArray()])
        );
        var irAsset = await Upload("cab.wav", ir.ToArray());
        var again = await Upload("same-cab.wav", ir.ToArray());
        var badModel = await Upload("broken.nam", [.."{ \"not\": \"a model\" }"u8]);
        await Post("/api/v1/live/blocks", new AddBlockRequest("cab", 2, 4));
        await Post("/api/v1/live/blocks", new AddBlockRequest("nam", 2, 3));

        var withIr = await Put("/api/v1/live/blocks/cab1/assets/slot1", new AssetRequest(irAsset.Ref));
        using var rejected = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/nam1/assets/model",
            new AssetRequest(badModel.Ref),
            Token
        );

        var content = await client.GetByteArrayAsync($"/api/v1/assets/{irAsset.Ref}/content", Token);
        Assert.Equal(ir.ToArray(), content);
        Assert.Equal(irAsset.Ref, again.Ref); // stored once
        Assert.StartsWith("sha256:", irAsset.Ref, StringComparison.Ordinal);
        Assert.Equal(irAsset.Ref, withIr.Preset.Blocks.Single(static b => b.Id == "cab1").Assets["slot1"]);
        Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
        Assert.Empty((await Live()).Preset.Blocks.Single(static b => b.Id == "nam1").Assets);
    }

    [Fact]
    public async Task A_cab_grid_pack_uploads_into_a_cab_slot_and_plays_as_its_ir_pack() {
        var ir = new MemoryStream();
        WavFile.Write(
            ir,
            new(48_000, [Signals.Noise(512, 1).Select(static (v, i) => v * MathF.Exp(-i / 80f)).ToArray()])
        );
        var pack = new MemoryStream();
        await using (var zip = new ZipArchive(
                         pack,
                         ZipArchiveMode.Create,
                         true
                     )) {
            await using (var wav = await zip.CreateEntry("a.wav").OpenAsync(Token)) {
                await wav.WriteAsync(ir.ToArray(), Token);
            }

            await using var manifest = new StreamWriter(await zip.CreateEntry("manifest.json").OpenAsync(Token));
            await manifest.WriteAsync("""{ "irs": [{ "position": 0, "distance": 1, "file": "a.wav" }] }""");
        }

        var asset = await Upload("my-412.cabgrid", pack.ToArray());
        await Post("/api/v1/live/blocks", new AddBlockRequest("cab", 2, 4));
        await Post("/api/v1/live/blocks", new AddBlockRequest("irplayer", 0, 4));

        var live = await Put("/api/v1/live/blocks/cab1/assets/slot2", new AssetRequest(asset.Ref));
        await Patch(
            "/api/v1/live/blocks/cab1/params",
            new Dictionary<string, float> { ["m2source"] = CabBlock.SourcePack, ["m2mute"] = 0 }
        );
        using var rejected = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/irplayer1/assets/ir",
            new AssetRequest(asset.Ref),
            Token
        );

        Assert.Equal("cabgrid", asset.Kind);
        Assert.Equal(asset.Ref, live.Preset.Blocks.Single(static b => b.Id == "cab1").Assets["slot2"]);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await ServerFixture.Eventually(() => (server.Block("cab1") as CabBlock)?.Grid(1)
            ?.Distances.Count
            == 1
            && server.Target("cab1", "m2source") == CabBlock.SourcePack
        );
    }

    [Fact]
    public async Task Moving_a_cab_mic_reaches_the_engine_and_changes_its_ir() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("cab", 2, 4));
        await ServerFixture.Eventually(() => server.Graph?.SlotOf("cab1") >= 0);
        var cab = (CabBlock)server.Block("cab1");
        var before = cab.ImpulseResponse().Left;

        var state = await Patch(
            "/api/v1/live/blocks/cab1/params",
            new Dictionary<string, float> { ["m1position"] = 80, ["m1distance"] = 6 }
        );

        Assert.Equal(80, state.Preset.Blocks.Single(static b => b.Id == "cab1").Channels["A"]["m1position"]);
        await ServerFixture.Eventually(() =>
            server.Target("cab1", "m1position") == 80 && server.Target("cab1", "m1distance") == 6
        );
        Assert.Same(cab, server.Block("cab1")); // a parameter, not a rebuild of the graph
        var after = cab.ImpulseResponse().Left;
        var change = Analysis.NullResidualDb(before, after) - Analysis.NullResidualDb(before, new float[before.Length]);
        Assert.True(change > -20, $"the mic moved: the IR should change (by {change:0.0} dB)");
    }

    [Fact]
    public async Task Changing_the_amp_type_starts_the_new_model_from_its_defaults() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("circuitamp", 2, 4));
        await Patch(
            "/api/v1/live/blocks/circuitamp1/params",
            new Dictionary<string, float> {
                ["gain"] = 9,
                ["switch1"] = 1,
                ["bias"] = 80,
                ["level"] = -6,
                ["inputsel"] = 2
            }
        );

        var state = await Patch(
            "/api/v1/live/blocks/circuitamp1/params",
            new Dictionary<string, float> { ["type"] = 3, ["treble"] = 7 }
        );

        var stored = state.Preset.Blocks.Single(static b => b.Id == "circuitamp1").Channels["A"];
        var info = CircuitAmpBlock.Info;
        Assert.Equal(info.Params[info.IndexOf("gain")].Default, stored["gain"]);
        Assert.Equal(0, stored["switch1"]); // a control the new model may not even have
        Assert.Equal(info.Params[info.IndexOf("bias")].Default, stored["bias"]);
        Assert.Equal(7, stored["treble"]); // sent with the type: kept
        Assert.Equal(-6, stored["level"]); // the block's own output and routing carry over
        Assert.Equal(2, stored["inputsel"]);
        await ServerFixture.Eventually(() => server.Target("circuitamp1", "gain") == stored["gain"]);

        // Setting the same type again is not a change.
        state = await Patch(
            "/api/v1/live/blocks/circuitamp1/params",
            new Dictionary<string, float> { ["gain"] = 8 }
        );
        state = await Patch("/api/v1/live/blocks/circuitamp1/params", new Dictionary<string, float> { ["type"] = 3 });
        Assert.Equal(8, state.Preset.Blocks.Single(static b => b.Id == "circuitamp1").Channels["A"]["gain"]);
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
        var asset = await Upload("crunch.json", Encoding.UTF8.GetBytes(model));
        await Post("/api/v1/live/blocks", new AddBlockRequest("nam", 2, 3));

        var live = await Put("/api/v1/live/blocks/nam1/assets/model", new AssetRequest(asset.Ref));

        Assert.Equal("aidax", asset.Kind);
        Assert.Equal(asset.Ref, live.Preset.Blocks.Single(static b => b.Id == "nam1").Assets["model"]);
        await ServerFixture.Eventually(() => (server.Block("nam1") as CaptureAmpBlock)?.CaptureModel
            ?.Format
            == "AIDA-X"
        );
    }

    [Fact]
    public async Task Presets_save_list_and_load() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("chorus", 2, 6));
        var saved = await Post("/api/v1/live/save", new SaveRequest("Clean Chorus"));
        await Post("/api/v1/live/new");

        var list = await client.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token);
        var loaded = await Put($"/api/v1/live/preset/{saved.Preset.Id}", new { });

        Assert.Contains(list!, static p => p.Name == "Clean Chorus");
        Assert.False(saved.Dirty);
        Assert.Contains(loaded.Preset.Blocks, static b => b.Id == "chorus1");
        Assert.False(loaded.CanUndo);
        Assert.False(loaded.Dirty);
        Assert.False((await Live()).Dirty);
    }

    [Fact]
    public async Task A_new_or_loaded_preset_is_clean_until_it_is_edited() {
        Assert.False((await Live()).Dirty);

        var edited = await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 2));
        Assert.True(edited.Dirty);

        var saved = await Post("/api/v1/live/save", new SaveRequest("Crunch"));
        var renewed = await Post("/api/v1/live/new");
        Assert.False(renewed.Dirty);

        var loaded = await Put($"/api/v1/live/preset/{saved.Preset.Id}", new { });
        Assert.False(loaded.Dirty);
        var changed = await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 2, 3));
        Assert.True(changed.Dirty);
    }

    [Fact]
    public async Task Removing_a_block_in_the_signal_path_leaves_a_shunt_that_keeps_it_whole() {
        // Dropping a drive onto the main row takes over shunt-r2c2 and its cables.
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 2, 2));

        var removed = await Delete("/api/v1/live/blocks/drive1");

        var shunt = Assert.Single(removed.Preset.Blocks, static b => b.Row == 2 && b.Column == 2);
        Assert.Equal(BuiltInTypes.Shunt, shunt.Type);
        Assert.Contains(removed.Preset.Cables, c => c.From == "shunt-r2c1" && c.To == shunt.Id);
        Assert.Contains(removed.Preset.Cables, c => c.From == shunt.Id && c.To == "shunt-r2c3");
        Assert.DoesNotContain(removed.Preset.Cables, static c => c.From == "drive1" || c.To == "drive1");
        Assert.Equal(13, removed.Preset.Cables.Count); // in to out, unbroken

        var undone = await Post("/api/v1/live/undo");
        Assert.Contains(undone.Preset.Blocks, static b => b.Id == "drive1" && b.Row == 2 && b.Column == 2);
        Assert.DoesNotContain(
            undone.Preset.Blocks,
            static b => b.Row == 2 && b.Column == 2 && b.Type == BuiltInTypes.Shunt
        );
    }

    [Fact]
    public async Task A_cable_across_empty_cells_is_carried_by_shunts_and_a_block_in_the_way_stops_it() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 2));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 1, 5));

        // Row 0 is empty between them: shunts fill columns 3 and 4 there, then the cable drops to the delay's row.
        var wired = await Post("/api/v1/live/cables", new CableRequest("drive1", "delay1"));
        Assert.Contains(wired.Preset.Blocks, static b => b.Id == "shunt-r0c3" && b.Type == BuiltInTypes.Shunt);
        Assert.Contains(wired.Preset.Blocks, static b => b.Id == "shunt-r0c4" && b.Type == BuiltInTypes.Shunt);
        Assert.Contains(wired.Preset.Cables, static c => c.From == "drive1" && c.To == "shunt-r0c3");
        Assert.Contains(wired.Preset.Cables, static c => c.From == "shunt-r0c3" && c.To == "shunt-r0c4");
        Assert.Contains(wired.Preset.Cables, static c => c.From == "shunt-r0c4" && c.To == "delay1");

        // One undo takes the shunts and the cables back.
        var undone = await Post("/api/v1/live/undo");
        Assert.DoesNotContain(undone.Preset.Blocks, static b => b.Row == 0 && b.Column is 3 or 4);
        Assert.DoesNotContain(undone.Preset.Cables, static c => c.From == "drive1");

        // A block on both rows in between: refused, and nothing changes.
        await Post("/api/v1/live/blocks", new AddBlockRequest("chorus", 0, 3));
        await Post("/api/v1/live/blocks", new AddBlockRequest("phaser", 1, 4));
        using var blocked = await client.PostAsJsonAsync(
            "/api/v1/live/cables",
            new CableRequest("drive1", "delay1"),
            Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.DoesNotContain((await Live()).Preset.Cables, static c => c.From == "drive1");
    }

    [Fact]
    public async Task Removing_a_branch_end_or_a_shunt_takes_its_cables_with_it() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 2));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 3));
        await Post("/api/v1/live/cables", new CableRequest("drive1", "delay1"));

        // The delay only takes signal in: it goes, and nothing takes its place.
        var withoutDelay = await Delete("/api/v1/live/blocks/delay1");
        Assert.DoesNotContain(withoutDelay.Preset.Blocks, static b => b.Row == 0 && b.Column == 3);
        Assert.DoesNotContain(withoutDelay.Preset.Cables, static c => c.To == "delay1");

        // Deleting a shunt on purpose breaks the path there.
        var withoutShunt = await Delete("/api/v1/live/blocks/shunt-r2c5");
        Assert.DoesNotContain(withoutShunt.Preset.Blocks, static b => b.Row == 2 && b.Column == 5);
        Assert.DoesNotContain(withoutShunt.Preset.Cables, static c => c.From == "shunt-r2c5" || c.To == "shunt-r2c5");
    }

    [Fact]
    public async Task Moving_a_block_onto_a_shunt_inserts_it_and_moving_it_out_leaves_the_path_whole() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 3));

        // Onto the main row's shunt: the drive takes its place and its cables.
        var inserted = await Put("/api/v1/live/blocks/drive1/position", new MoveBlockRequest(2, 3));
        Assert.DoesNotContain(inserted.Preset.Blocks, static b => b.Id == "shunt-r2c3");
        Assert.Contains(inserted.Preset.Cables, static c => c.From == "shunt-r2c2" && c.To == "drive1");
        Assert.Contains(inserted.Preset.Cables, static c => c.From == "drive1" && c.To == "shunt-r2c4");
        Assert.Equal(13, inserted.Preset.Cables.Count);

        // Along the row: a shunt fills the cell it left, and it takes over the next one.
        var along = await Put("/api/v1/live/blocks/drive1/position", new MoveBlockRequest(2, 5));
        Assert.Contains(along.Preset.Cables, static c => c.From == "shunt-r2c4" && c.To == "drive1");
        Assert.Contains(along.Preset.Cables, static c => c.From == "drive1" && c.To == "shunt-r2c6");
        Assert.Equal(BuiltInTypes.Shunt, along.Preset.Blocks.Single(static b => b.Row == 2 && b.Column == 3).Type);
        Assert.Equal(13, along.Preset.Cables.Count);

        // Out of the path: the row stays connected, and the drive keeps no cables.
        var outside = await Put("/api/v1/live/blocks/drive1/position", new MoveBlockRequest(0, 5));
        Assert.Equal(BuiltInTypes.Shunt, outside.Preset.Blocks.Single(static b => b.Row == 2 && b.Column == 5).Type);
        Assert.DoesNotContain(outside.Preset.Cables, static c => c.From == "drive1" || c.To == "drive1");
        Assert.Equal(13, outside.Preset.Cables.Count);
    }

    [Fact]
    public async Task A_block_cannot_be_moved_onto_another_block() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", 0, 3));
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 4));

        using var response = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/drive1/position",
            new MoveBlockRequest(0, 4),
            Token
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Each_hardware_input_feeds_one_input_block_and_a_preset_has_one_output_1() {
        var added = await Post("/api/v1/live/blocks", new AddBlockRequest(BuiltInTypes.Input, 0, 0));
        var second = added.Preset.Blocks.Single(static b => b.Id == "input1");
        Assert.All(
            Preset.ChannelNames,
            c => Assert.Equal(1, second.Channels[c]["source"])
        ); // Input 2, on every channel

        using var taken = await client.PatchAsJsonAsync(
            "/api/v1/live/blocks/in1/params",
            new Dictionary<string, float> { ["source"] = 1 },
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        var moved = await Patch(
            "/api/v1/live/blocks/input1/params?channel=B",
            new Dictionary<string, float> { ["source"] = 4 }
        );
        Assert.All(
            Preset.ChannelNames,
            c => Assert.Equal(4, moved.Preset.Blocks.Single(static b => b.Id == "input1").Channels[c]["source"])
        );

        foreach (var row in new[] { 1, 3, 4, 5 }) { // row 2 holds in1
            await Post("/api/v1/live/blocks", new AddBlockRequest(BuiltInTypes.Input, row, 0));
        }

        using var seventh = await client.PostAsJsonAsync(
            "/api/v1/live/blocks",
            new AddBlockRequest(BuiltInTypes.Input, 0, 1),
            Token
        );
        using var output = await client.PostAsJsonAsync(
            "/api/v1/live/blocks",
            new AddBlockRequest(BuiltInTypes.Output, 0, 13),
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, seventh.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, output.StatusCode);
        Assert.Equal(6, (await Live()).Preset.Blocks.Count(static b => b.Type == BuiltInTypes.Input));
    }

    [Fact]
    public async Task Ab_compare_keeps_each_versions_modifiers_controllers_perform_page_and_overrides() {
        var a = (await Live()).Preset;
        await Post("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2));
        await Put(
            "/api/v1/live/modifiers",
            new List<PresetModifier> { new() { Block = "delay1", Param = "time", Source = "lfo1" } }
        );
        await Patch("/api/v1/live/controllers", new Dictionary<string, float> { ["lfo1Rate"] = 3 });
        await Put("/api/v1/live/perform", new List<PerformControl> { new("delay1", "time", "Time") });
        await Put(
            "/api/v1/live/footswitch-overrides",
            new List<FootswitchOverride> { new("board", 1, new("bypass", "delay1")) }
        );
        var b = (await Live()).Preset;

        var backToA = await Put("/api/v1/live", a);
        Assert.Empty(backToA.Preset.Modifiers);
        Assert.Empty(backToA.Preset.Controllers);
        Assert.Empty(backToA.Preset.Perform);
        Assert.Empty(backToA.Preset.FootswitchOverrides);

        var backToB = await Put("/api/v1/live", b);
        Assert.Equal(a.Id, backToB.Preset.Id);
        Assert.Equal("time", Assert.Single(backToB.Preset.Modifiers).Param);
        Assert.Equal(3, backToB.Preset.Controllers["lfo1Rate"]);
        Assert.Equal("Time", Assert.Single(backToB.Preset.Perform).Label);
        Assert.Equal("delay1", Assert.Single(backToB.Preset.FootswitchOverrides).Tap.Target);
        await ServerFixture.Eventually(() => server.Graph?.Modifiers.Count == 1);

        // Validated as their own endpoints validate them.
        b.Modifiers.Add(new() { Block = "ghost", Param = "time" });
        using var invalid = await client.PutAsJsonAsync("/api/v1/live", b, Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        b.Modifiers.RemoveAt(1);
        b.Controllers["level"] = 1;
        using var notController = await client.PutAsJsonAsync("/api/v1/live", b, Token);
        Assert.Equal(HttpStatusCode.BadRequest, notController.StatusCode);
    }

    async Task<LiveState> Delete(string url) => await Read(await client.DeleteAsync(url, Token));

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
