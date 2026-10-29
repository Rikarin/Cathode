using Cathode.Contracts;
using Cathode.Engine.Rig;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cathode.Server.Tests;

/// <summary>An engage modifier's mode, threshold and off delay, through the API to the engine and the preset.</summary>
public sealed class EngageModifierApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        using var added = await client.PostAsJsonAsync(
            "/api/v1/live/blocks",
            new AddBlockRequest("delay", 0, 2),
            Token
        );
        added.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task The_settings_reach_the_engine_and_the_preset() {
        using var response = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> {
                new() {
                    Block = "delay1",
                    Param = "engage",
                    Source = "ext1",
                    Mode = "moving",
                    Threshold = 0.05f,
                    OffDelayMs = 300f
                }
            },
            Token
        );
        response.EnsureSuccessStatusCode();
        var live = (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;

        var stored = Assert.Single(live.Preset.Modifiers);
        Assert.Equal(("moving", 0.05f, 300f), (stored.Mode, stored.Threshold, stored.OffDelayMs));
        await ServerFixture.Eventually(() => server.Graph!.Modifiers.Any(static m =>
                m.Switch is { Mode: EngageMode.Moving, Threshold: 0.05f, OffDelayMs: 300f }
            )
        );
    }

    [Theory]
    [InlineData("sideways", null, null)]
    [InlineData(null, 1.5f, null)]
    [InlineData(null, -0.1f, null)]
    [InlineData(null, null, -1f)]
    [InlineData(null, null, 60_000f)]
    public async Task Settings_out_of_range_are_refused(string? mode, float? threshold, float? offDelayMs) {
        using var response = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> {
                new() {
                    Block = "delay1",
                    Param = "engage",
                    Source = "ext1",
                    Mode = mode,
                    Threshold = threshold,
                    OffDelayMs = offDelayMs
                }
            },
            Token
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void A_modifier_without_the_settings_stores_none_of_them() {
        var json = JsonSerializer.Serialize(
            [new() { Block = "wah1", Param = "engage", Source = "ext1" }],
            ContractsJsonContext.Default.ListPresetModifier
        );
        var read = JsonSerializer.Deserialize(
            """[{"block":"wah1","param":"engage","source":"ext1"}]""",
            ContractsJsonContext.Default.ListPresetModifier
        )!;

        Assert.DoesNotContain("mode", json, StringComparison.Ordinal);
        Assert.DoesNotContain("threshold", json, StringComparison.Ordinal);
        Assert.DoesNotContain("offDelayMs", json, StringComparison.Ordinal);
        Assert.Equal((null, null, null), (read[0].Mode, read[0].Threshold, read[0].OffDelayMs));
    }
}
