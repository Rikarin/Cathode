using System.Net;
using System.Net.Http.Json;
using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Server.Stores;

namespace Cathode.Server.Tests;

/// <summary>The Block Library: factory type presets and the user's block presets.</summary>
public sealed class BlockLibraryTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Read(await client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        await Read(await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("chorus", 0, 2), Token));
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public void Every_factory_preset_names_real_parameters_with_values_in_range() {
        var registry = BlockRegistry.CreateDefault();
        var problems = new List<string>();
        foreach (var preset in FactoryBlockPresets.All) {
            if (!registry.Contains(preset.Type)) {
                problems.Add($"{preset.Id}: no block type '{preset.Type}'");
                continue;
            }

            var descriptor = registry.GetDescriptor(preset.Type);
            foreach (var (id, value) in preset.Values) {
                var index = descriptor.IndexOf(id);
                if (index < 0) {
                    problems.Add($"{preset.Id}: no parameter '{id}'");
                } else if (descriptor.Params[index].Sanitize(value) != value) {
                    problems.Add($"{preset.Id}: {id} = {value} is out of range");
                }
            }

            if (!StoreIds.IsValid(preset.Id)) {
                problems.Add($"{preset.Id}: not a valid id");
            }
        }

        Assert.Empty(problems);
        Assert.Equal(FactoryBlockPresets.All.Count, FactoryBlockPresets.All.Select(static p => p.Id).Distinct().Count());
        foreach (var type in new[] {
                     "chorus", "flanger", "phaser", "delay", "multidelay", "megatap", "reverb", "compressor", "gate",
                     "filter", "geq", "wah", "pitch", "tremolo", "ringmod", "dyndist", "drive"
                 }) {
            Assert.True(FactoryBlockPresets.All.Count(p => p.Type == type) >= 4, $"{type} has fewer than four presets");
        }
    }

    [Fact]
    public async Task A_factory_preset_sets_the_channel_and_the_running_block() {
        var presets = (await client.GetFromJsonAsync<BlockPreset[]>("/api/v1/block-presets?type=chorus", Token))!;
        var analog = presets.Single(p => p.Name == "Analog Stereo");

        await Read(
            await client.PatchAsJsonAsync("/api/v1/live/blocks/chorus1/params", new Dictionary<string, float> { ["mix"] = 80 }, Token)
        );
        var live = await Read(
            await client.PostAsJsonAsync("/api/v1/live/blocks/chorus1/preset", new ApplyBlockPresetRequest(analog.Id), Token)
        );

        Assert.All(presets, static p => Assert.Equal("chorus", p.Type));
        Assert.Equal(analog.Values, live.Preset.Blocks.Single(b => b.Id == "chorus1").Channels["A"]);
        await ServerFixture.Eventually(() => server.Target("chorus1", "highCut") == 6_000 && server.Target("chorus1", "mix") == 50);
    }

    [Fact]
    public async Task User_presets_are_saved_listed_and_deleted_but_factory_ones_are_fixed() {
        var saved = await client.PutAsJsonAsync(
            "/api/v1/block-presets/my-chorus",
            new BlockPreset { Type = "chorus", Name = "My Chorus", Values = { ["rate"] = 99, ["depth"] = 20 } },
            Token
        );
        var unknown = await client.PutAsJsonAsync(
            "/api/v1/block-presets/bad",
            new BlockPreset { Type = "chorus", Name = "Bad", Values = { ["nope"] = 1 } },
            Token
        );
        var factory = FactoryBlockPresets.All.First(static p => p.Type == "chorus").Id;
        using var deleteFactory = await client.DeleteAsync($"/api/v1/block-presets/{factory}", Token);

        var stored = (await saved.Content.ReadFromJsonAsync<BlockPreset>(Token))!;
        var listed = (await client.GetFromJsonAsync<BlockPreset[]>("/api/v1/block-presets?type=chorus", Token))!;
        Assert.Equal(10f, stored.Values["rate"]); // clamped to the parameter's range
        Assert.Contains(listed, static p => p is { Id: "my-chorus", Factory: false });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteFactory.StatusCode);

        using var deleted = await client.DeleteAsync("/api/v1/block-presets/my-chorus", Token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    static async Task<LiveState> Read(HttpResponseMessage response) {
        using (response) {
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
        }
    }
}
