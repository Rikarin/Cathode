using System.Net;
using System.Net.Http.Json;
using Cathode.Contracts;
using Cathode.Dsp.IO;
using Microsoft.AspNetCore.Mvc;

namespace Cathode.Server.Tests;

/// <summary>The IR mixer, Tone Match and RTA endpoints.</summary>
public sealed class IrToolsApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        (await client.PostAsync("/api/v1/live/new", null, Token)).EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Mixing_aligns_peaks_applies_levels_and_stores_a_new_ir() {
        var near = await Upload("near.wav", Impulse(10, 1f));
        var far = await Upload("far.wav", Impulse(30, 1f));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/assets/mix",
            new IrMixRequest(
                "Blend",
                [new(near.Ref), new(far.Ref, -6.0206, Invert: true)],
                LengthMs: 10,
                Normalize: false
            ),
            Token
        );
        response.EnsureSuccessStatusCode();
        var mixed = (await response.Content.ReadFromJsonAsync<AssetInfo>(Token))!;

        var audio = WavFile.Read(
            new MemoryStream(await client.GetByteArrayAsync($"/api/v1/assets/{mixed.Ref}/content", Token))
        );
        Assert.Equal("Blend.wav", mixed.Name);
        Assert.Equal(480, audio.Frames);
        Assert.Equal(0.5f, audio.Channels[0][30], 1e-4f); // both peaks moved to the later one: 1 - 0.5
        Assert.Equal(0f, audio.Channels[0][10], 1e-6f);
    }

    [Fact]
    public async Task Mixing_a_model_is_refused() {
        var model = await Upload("amp.nam", "{}"u8.ToArray());

        using var response = await client.PostAsJsonAsync(
            "/api/v1/assets/mix",
            new IrMixRequest("x", [new(model.Ref)]),
            Token
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tone_match_needs_a_tone_match_block_and_something_to_hear() {
        var reference = await Upload("reference.wav", Impulse(0, 0.5f));
        (await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("tonematch", 0, 6), Token))
            .EnsureSuccessStatusCode();

        using var wrongBlock = await client.PostAsJsonAsync(
            "/api/v1/live/blocks/shunt1/tonematch",
            new ToneMatchRequest(reference.Ref, 3),
            Token
        );
        using var silent = await client.PostAsJsonAsync(
            "/api/v1/live/blocks/tonematch1/tonematch",
            new ToneMatchRequest(reference.Ref, 3),
            Token
        );

        Assert.True(wrongBlock.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.Conflict, silent.StatusCode); // nothing is played into the test engine
        var problem = await silent.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("play while Tone Match listens", problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_rta_can_be_switched_on_and_off() {
        using var on = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(true), Token);
        using var off = await client.PutAsJsonAsync("/api/v1/rta", new RtaRequest(false), Token);

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
    }

    [Fact]
    public async Task An_ir_upload_can_be_trimmed_and_its_polarity_corrected() {
        var plain = await Upload("plain.wav", Impulse(40, -0.5f));
        var processed = await Upload("trimmed.wav", Impulse(40, -0.5f), "&processing=autoTrim&polarity=true");
        using var rejected = await client.PostAsync(
            "/api/v1/assets?name=bad.wav&processing=sideways",
            new ByteArrayContent(Impulse(40, 1f)),
            Token
        );

        Assert.NotEqual(plain.Ref, processed.Ref);
        var audio = WavFile.Read(
            new MemoryStream(await client.GetByteArrayAsync($"/api/v1/assets/{processed.Ref}/content", Token))
        );
        Assert.Equal(216, audio.Frames);
        Assert.Equal(0.5f, audio.Channels[0][0]);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task A_cab_exports_its_mixed_ir_and_saves_it_as_an_asset() {
        (await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("cab", 0, 4), Token))
            .EnsureSuccessStatusCode();

        var wav = WavFile.Read(new MemoryStream(await client.GetByteArrayAsync("/api/v1/live/blocks/cab1/ir", Token)));
        using var saved = await client.PostAsJsonAsync(
            "/api/v1/live/blocks/cab1/ir",
            new CabExportRequest("My Cab"),
            Token
        );
        using var notCab = await client.GetAsync("/api/v1/live/blocks/in1/ir", Token);

        Assert.Equal(2, wav.Channels.Length);
        Assert.InRange(wav.Frames, 1_000, 4_000); // the default DynaCab, trimmed
        Assert.Equal(wav.Channels[0], wav.Channels[1]);
        saved.EnsureSuccessStatusCode();
        var asset = (await saved.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
        Assert.Equal("My Cab.wav", asset.Name);
        Assert.Equal("ir", asset.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, notCab.StatusCode);
    }

    static byte[] Impulse(int position, float amplitude) {
        var samples = new float[256];
        samples[position] = amplitude;
        using var stream = new MemoryStream();
        WavFile.Write(stream, new(48_000, [samples]));
        return stream.ToArray();
    }

    async Task<AssetInfo> Upload(string name, byte[] content, string query = "") {
        using var response =
            await client.PostAsync($"/api/v1/assets?name={name}{query}", new ByteArrayContent(content), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
    }
}
