using Cathode.Contracts;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Cabinets;
using Cathode.Engine.Rig;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>Audio I/O through the API (docs/08-blocks-parity.md §2): outputs, inputs, oversampling, latency, test tone.</summary>
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
        if (!live.Preset.Cables.Any(static c => c is { From: "circuitamp1", To: "cab1" })) {
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

    float Rig(string id) => server.Host.Engine.Rig.GetTarget(RigControls.Descriptor.IndexOf(id));

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task PutGlobal(Action<GlobalSettings> change) {
        var global = await Global();
        change(global);
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
    }

    async Task Post(string url, object body) => await Ok(client.PostAsJsonAsync(url, body, Token));

    static async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        if (!response.IsSuccessStatusCode) {
            Assert.Fail(
                $"{response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}"
            );
        }
    }
}
