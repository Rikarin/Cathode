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
    public async Task The_cab_align_page_reports_each_slot_onset_and_delays() {
        var ir = await Upload("late.wav", Impulse(48, 1f)); // onset at 1 ms
        (await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("cab", 0, 6), Token))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/live/blocks/cab1/assets/slot2", new AssetRequest(ir.Ref), Token))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync(
                "/api/v1/live/blocks/cab1/params",
                new Dictionary<string, float> { ["m2source"] = 0, ["m2mute"] = 0, ["m2delay"] = 0.5f },
                Token
            ))
            .EnsureSuccessStatusCode();

        var alignment = (await client.GetFromJsonAsync<CabAlignment>("/api/v1/live/blocks/cab1/cab/align", Token))!;
        using var wrongBlock = await client.GetAsync("/api/v1/live/blocks/in1/cab/align", Token);

        Assert.Equal(48_000, alignment.SampleRate);
        Assert.Equal(4, alignment.Slots.Count);
        var slot = alignment.Slots[1];
        Assert.True(slot.Plays && slot.Audible);
        Assert.Equal(1.0, slot.OnsetMs, 3);
        Assert.Equal(0.5, slot.DelayMs, 3);
        Assert.Equal(256, slot.Head.Count); // a 10 ms head, cut to the 256-sample IR
        Assert.True(alignment.Slots[0].Plays); // the default DynaCab
        Assert.Equal(HttpStatusCode.BadRequest, wrongBlock.StatusCode);
    }

    static byte[] Impulse(int position, float amplitude) {
        var samples = new float[256];
        samples[position] = amplitude;
        using var stream = new MemoryStream();
        WavFile.Write(stream, new(48_000, [samples]));
        return stream.ToArray();
    }

    async Task<AssetInfo> Upload(string name, byte[] content) {
        using var response =
            await client.PostAsync($"/api/v1/assets?name={name}", new ByteArrayContent(content), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
    }
}
