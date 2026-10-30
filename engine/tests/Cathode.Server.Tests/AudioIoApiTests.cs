using Cathode.Contracts;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Filters;
using Cathode.Engine.Rig;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     Audio I/O through the API (docs/08-blocks-parity.md §2): outputs, inputs, oversampling, latency, test tone.
/// </summary>
public sealed class AudioIoApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Post("/api/v1/live/blocks", new AddBlockRequest("drive", Preset.DefaultRow, 2));
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Output_and_input_settings_reach_the_engine_and_are_validated() {
        await PutGlobal(static g => {
                g.Outputs[1].Mode = "sum";
                g.Outputs[1].BoostPad = 6;
                g.Outputs[2].Eq["g1k"] = 3;
                g.CopyOutput1To2 = true;
                g.Inputs[0].FullScaleVolts = 2;
                g.Inputs[0].Gain = 3;
            }
        );
        var bad = await Global();
        bad.Outputs[3].Mode = "quad";
        using var rejected = await client.PutAsJsonAsync("/api/v1/global", bad, Token);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var eq = server.Host.Engine.OutputEq(2);
        await ServerFixture.Eventually(() => eq.Params.GetTarget(eq.Descriptor.IndexOf("g1k")) == 3f);
        Assert.Equal(RigControls.SumLeftRight, Rig("out2Mode"));
        Assert.Equal(6f, Rig("out2Level"));
        Assert.Equal(1f, Rig("copyOut2"));
        Assert.Equal(3f, Rig("in1Gain"));
        Assert.Equal(20 * Math.Log10(2), Rig("in1Cal"), 3);
    }

    [Fact]
    public async Task Cab_modeling_off_is_stored_and_reaches_the_engine() {
        await PutGlobal(static g => g.CabModelingOff = true);

        Assert.True((await Global()).CabModelingOff);
        await ServerFixture.Eventually(() => Rig("cabsOff") == 1f);

        await PutGlobal(static g => g.CabModelingOff = false);

        Assert.False((await Global()).CabModelingOff);
        await ServerFixture.Eventually(() => Rig("cabsOff") == 0f);
    }

    [Fact]
    public async Task The_tuner_output_is_stored_validated_and_applied_while_the_tuner_is_on() {
        Assert.Equal("mute", (await Global()).TunerOutput);
        await PutGlobal(static g => g.TunerOutput = "bypass");

        Assert.Equal("bypass", (await Global()).TunerOutput);
        await ServerFixture.Eventually(() => Rig("tunerOut") == RigControls.TunerBypass);
        var bad = await Global();
        bad.TunerOutput = "loud";
        using var rejected = await client.PutAsJsonAsync("/api/v1/global", bad, Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("bypass", (await Global()).TunerOutput);

        // The engine applies the mode only while the tuner is on.
        Assert.Equal(0f, Rig("tuner"));
        await Ok(client.PutAsJsonAsync("/api/v1/tuner", new BypassRequest(false), Token));
        await ServerFixture.Eventually(() => Rig("tuner") == 1f);
        await PutGlobal(static g => g.TunerOutput = "passThrough");
        await ServerFixture.Eventually(() => Rig("tunerOut") == RigControls.TunerPassThrough);
        await Ok(client.PutAsJsonAsync("/api/v1/tuner", new BypassRequest(true), Token));
        await ServerFixture.Eventually(() => Rig("tuner") == 0f);
    }

    [Fact]
    public async Task The_tuner_reference_view_tuning_and_offsets_are_stored_and_validated() {
        var before = await Global();
        Assert.Equal(440.0, before.TunerReference);
        Assert.Equal("needle", before.TunerView);
        Assert.Equal("chromatic", before.Tuning);
        Assert.False(before.TunerOffsetsOn);
        Assert.Equal(8, before.TunerOffsets.Count);
        Assert.Equal(440f, Rig("tunerRef"));

        await PutGlobal(static g => {
                g.TunerReference = 432;
                g.TunerView = "strobe";
                g.Tuning = "dropD";
                g.TunerOffsetsOn = true;
                g.TunerOffsets = [-1.5f, 0, 2, 0, 0, 0];
            }
        );

        var after = await Global();
        Assert.Equal(432.0, after.TunerReference);
        Assert.Equal("strobe", after.TunerView);
        Assert.Equal("dropD", after.Tuning);
        Assert.True(after.TunerOffsetsOn);
        Assert.Equal([-1.5f, 0, 2, 0, 0, 0], after.TunerOffsets);
        await ServerFixture.Eventually(() => Rig("tunerRef") == 432f);

        Action<GlobalSettings>[] spoilers = [
            static g => g.TunerReference = 460,
            static g => g.TunerView = "dial",
            static g => g.Tuning = "drop d",
            static g => g.TunerOffsets = [25],
            static g => g.TunerOffsets = [0, 0, 0, 0, 0, 0, 0, 0, 0]
        ];
        foreach (var spoil in spoilers) {
            var bad = await Global();
            spoil(bad);
            using var rejected = await client.PutAsJsonAsync("/api/v1/global", bad, Token);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        Assert.Equal(432.0, (await Global()).TunerReference);
    }

    [Fact]
    public async Task Every_switch_in_the_global_settings_survives_a_put() {
        // Each bool flipped from what the server holds: a field the PUT handler forgets to copy springs back.
        var switches = typeof(GlobalSettings).GetProperties()
            .Where(static p => p.PropertyType == typeof(bool))
            .ToList();
        var before = await Global();
        await PutGlobal(g => switches.ForEach(p => p.SetValue(g, !(bool)p.GetValue(before)!)));

        var after = await Global();
        Assert.All(switches, p => Assert.Equal(!(bool)p.GetValue(before)!, (bool)p.GetValue(after)!));
    }

    [Fact]
    public async Task Linear_phase_oversampling_recompiles_and_the_latency_report_names_the_drive() {
        await PutGlobal(static g => g.Oversampling = "linear");

        await ServerFixture.Eventually(() => server.Graph?.LatencySamples > 0);
        var report = (await client.GetFromJsonAsync<LatencyInfo>("/api/v1/system/latency", Token))!;
        Assert.True(report.Connected);
        Assert.Equal(server.Graph!.LatencySamples, report.GraphFrames);
        Assert.Contains(report.Path, static s => s is { Block: "drive1", Frames: > 0 });
        Assert.Equal(report.InputFrames + report.OutputFrames + report.GraphFrames, report.TotalFrames);

        await PutGlobal(static g => g.Oversampling = "minimum");
        await ServerFixture.Eventually(() => server.Graph?.LatencySamples == 0);
    }

    [Fact]
    public async Task Output_2_is_a_grid_output_and_nothing_leaves_it() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("output2", Preset.DefaultRow + 1, 3));
        await Post("/api/v1/live/cables", new CableRequest("drive1", "out2"));

        await ServerFixture.Eventually(() => server.Graph?.HasOutput(1) == true);
        await Post("/api/v1/live/blocks", new AddBlockRequest("volpan", Preset.DefaultRow + 1, 4));
        using var outOfOutput = await client.PostAsJsonAsync(
            "/api/v1/live/cables",
            new CableRequest("out2", "volpan1"),
            Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, outOfOutput.StatusCode);
    }

    [Fact]
    public async Task The_test_tone_plays_for_a_block_without_touching_the_preset() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("circuitamp", Preset.DefaultRow, 3));
        var before = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

        await Ok(
            client.PutAsJsonAsync("/api/v1/live/blocks/circuitamp1/test-tone", new TestToneRequest(true, 110), Token)
        );
        await ServerFixture.Eventually(() => server.Host.Engine.TestTone.Enabled);
        using var missing = await client.PutAsJsonAsync(
            "/api/v1/live/blocks/ghost/test-tone",
            new TestToneRequest(true),
            Token
        );
        var resonance = (await client.GetFromJsonAsync<SpeakerResonanceInfo>(
            "/api/v1/live/blocks/circuitamp1/speaker-resonance",
            Token
        ))!;
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/circuitamp1/test-tone", new TestToneRequest(false), Token));
        var after = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await ServerFixture.Eventually(() => !server.Host.Engine.TestTone.Enabled);
        Assert.Equal(before.Revision, after.Revision);
        Assert.False(after.Dirty && !before.Dirty);
        Assert.Equal(100f, resonance.Percent);
        Assert.InRange(resonance.BaseHz, 40, 200);
        Assert.Equal(resonance.BaseHz, resonance.Hz, 6);
    }

    [Fact]
    public async Task The_speaker_resonance_follows_the_cab_after_the_amp() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("circuitamp", Preset.DefaultRow, 3));
        await Post("/api/v1/live/blocks", new AddBlockRequest("cab", Preset.DefaultRow, 4));
        var live = (await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!;
        if (!live.Preset.Cables.Exists(static c => c is { From: "circuitamp1", To: "cab1" })) {
            await Post("/api/v1/live/cables", new CableRequest("circuitamp1", "cab1"));
        }

        var cab = CabBlock.Info.Params;
        var closed = CabCatalogue.Enclosures.ToList().FindIndex(static e => e.Name == "4x12 Closed Back");
        await Patch("cab1", new() { [cab[CabBlock.SlotParam(0, CabBlock.Cab)].Id] = closed });
        await Patch("cab1", new() { [cab[CabBlock.SlotParam(0, CabBlock.Speaker)].Id] = 7 });
        await Patch("circuitamp1", new() { ["speaker"] = CircuitAmpBlock.SpeakerAuto });

        var followed = await Resonance();
        Assert.Equal(AutoSpeaker.For(closed, 7)!.Resonance, followed.BaseHz, 6);

        // A different speaker in the Cab moves the amp's load with it.
        await Patch("cab1", new() { [cab[CabBlock.SlotParam(0, CabBlock.Speaker)].Id] = 8 });
        Assert.Equal(AutoSpeaker.For(closed, 8)!.Resonance, (await Resonance()).BaseHz, 6);

        // Muted, the Cab synthesises nothing to follow: the model's own load.
        await Patch("cab1", new() { [cab[CabBlock.SlotParam(0, CabBlock.Mute)].Id] = 1 });
        var type = (int)CircuitAmpBlock.Info.Params[CircuitAmpBlock.Type].Default;
        Assert.Equal(CircuitAmpBlock.SpeakerResonanceHz(type, 0), (await Resonance()).BaseHz, 6);

        async Task<SpeakerResonanceInfo> Resonance() =>
            (await client.GetFromJsonAsync<SpeakerResonanceInfo>(
                "/api/v1/live/blocks/circuitamp1/speaker-resonance",
                Token
            ))!;

        Task Patch(string block, Dictionary<string, float> values) =>
            Ok(client.PatchAsJsonAsync($"/api/v1/live/blocks/{block}/params", values, Token));
    }

    [Fact]
    public async Task A_graphic_eqs_response_follows_its_active_channel() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("geq", Preset.DefaultRow, 3));
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/geq1/params",
                new Dictionary<string, float> { ["g1k"] = 6 },
                Token
            )
        );

        var boosted = await Response();
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/geq1/channel", new ChannelRequest("B"), Token));
        var flat = await Response();
        await Post("/api/v1/live/blocks", new AddBlockRequest("reverb", Preset.DefaultRow, 4));
        using var other = await client.GetAsync("/api/v1/live/blocks/reverb1/response", Token);
        using var missing = await client.GetAsync("/api/v1/live/blocks/ghost/response", Token);

        Assert.Equal(256, boosted.Frequencies.Length);
        Assert.Equal(boosted.Frequencies.Length, boosted.Db.Length);
        Assert.Equal(GraphicEqBlock.Centers.ToArray(), boosted.Centers);
        var nearest = boosted.Frequencies.Select(static (f, i) => (Distance: Math.Abs(Math.Log(f / 1_000)), i)).Min().i;
        Assert.Equal(6f, boosted.Db[nearest], 0.1f);
        Assert.All(flat.Db, static d => Assert.Equal(0f, d, 1e-4f));
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        async Task<FrequencyResponse> Response() =>
            (await client.GetFromJsonAsync<FrequencyResponse>("/api/v1/live/blocks/geq1/response", Token))!;
    }

    [Fact]
    public async Task A_drives_eq_2_response_follows_its_active_channel() {
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/drive1/params",
                new Dictionary<string, float> { ["eq2"] = DriveBlock.EqPost, ["geq7"] = 6 },
                Token
            )
        );

        var boosted = await Response("drive1");
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/drive1/channel", new ChannelRequest("B"), Token));
        var off = await Response("drive1");

        Assert.Equal(256, boosted.Db.Length);
        Assert.Equal(GraphicEqualizer.Layouts[DriveBlock.Eq2Layout].Centers, boosted.Centers);
        Assert.Equal(6f, boosted.Db[Nearest(boosted, 1_000)], 0.1f);
        Assert.All(off.Db, static d => Assert.Equal(0f, d));
        Assert.Empty(off.Centers);
    }

    [Fact]
    public async Task An_amps_output_eq_response_follows_its_active_channel() {
        await Post("/api/v1/live/blocks", new AddBlockRequest("circuitamp", Preset.DefaultRow, 3));
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/circuitamp1/params",
                new Dictionary<string, float> { ["outeq"] = 1, ["outeq6"] = -6 }, // 10 Band, 1 kHz
                Token
            )
        );

        var cut = await Response("circuitamp1");
        await Ok(client.PutAsJsonAsync("/api/v1/live/blocks/circuitamp1/channel", new ChannelRequest("B"), Token));
        var off = await Response("circuitamp1");

        Assert.Equal(GraphicEqualizer.Layouts[0].Centers, cut.Centers);
        Assert.Equal(-6f, cut.Db[Nearest(cut, 1_000)], 0.1f);
        Assert.All(off.Db, static d => Assert.Equal(0f, d));
        Assert.Empty(off.Centers);
    }

    async Task<FrequencyResponse> Response(string block) =>
        (await client.GetFromJsonAsync<FrequencyResponse>($"/api/v1/live/blocks/{block}/response", Token))!;

    static int Nearest(FrequencyResponse response, float hz) =>
        response.Frequencies.Select((f, i) => (Distance: Math.Abs(Math.Log(f / hz)), i)).Min().i;

    float Rig(string id) => server.Host.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf(id));

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task PutGlobal(Action<GlobalSettings> change) {
        var global = await Global();
        change(global);
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
    }

    Task Post(string url, object body) => Ok(client.PostAsJsonAsync(url, body, Token));

    static async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        if (!response.IsSuccessStatusCode) {
            Assert.Fail(
                $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} "
                + $"{await response.Content.ReadAsStringAsync(Token)}"
            );
        }
    }
}
