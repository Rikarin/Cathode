using Cathode.Contracts;
using Cathode.Engine.Rig;
using Cathode.Server.Rig;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     A modifier's per-source scales, subtract, update rate, off-above engage and PC Reset, through the API to the
///     engine and the preset.
/// </summary>
public sealed class ModifierSurfaceApiTests : IAsyncLifetime {
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

    async Task<LiveState> PutModifiers(params PresetModifier[] modifiers) {
        using var response = await client.PutAsJsonAsync("/api/v1/live/modifiers", modifiers.ToList(), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LiveState>(Token))!;
    }

    [Fact]
    public async Task Source_scales_subtract_update_rate_and_off_above_reach_the_engine_and_the_preset() {
        var live = await PutModifiers(
            new PresetModifier {
                Block = "delay1",
                Param = "mix",
                Source = "ext1",
                Source2 = "lfo1",
                Operation = "subtract",
                Scale1 = 0.5f,
                Scale2 = 0.25f,
                UpdateRate = "slow",
                Max = 100
            },
            new PresetModifier {
                Block = "delay1",
                Param = "engage",
                Source = "ext2",
                Threshold = 0.9f,
                OffAbove = true
            }
        );

        var stored = live.Preset.Modifiers[0];
        Assert.Equal(
            ("subtract", 0.5f, 0.25f, "slow"),
            (stored.Operation, stored.Scale1, stored.Scale2, stored.UpdateRate)
        );
        Assert.True(live.Preset.Modifiers[1].OffAbove);
        await ServerFixture.Eventually(() => server.Graph?.Modifiers.Count == 2);
        var bound = server.Graph!.Modifiers[0];
        Assert.Equal(
            (ModOperation.Subtract, 0.5f, 0.25f, ModNames.SlowUpdateMs),
            (bound.Operation, bound.Scale1, bound.Scale2, bound.UpdateMs)
        );
        var engage = server.Graph.Modifiers[1];
        Assert.True(engage.OffAbove);
        Assert.Equal(0.1f, engage.Switch.Threshold, 5); // the switch runs on the mirrored value
    }

    [Theory]
    [InlineData(1.5f, null, null)]
    [InlineData(null, -0.1f, null)]
    [InlineData(null, null, "warp")]
    public async Task A_source_scale_out_of_range_or_an_unknown_update_rate_is_refused(
        float? scale1,
        float? scale2,
        string? updateRate
    ) {
        using var response = await client.PutAsJsonAsync(
            "/api/v1/live/modifiers",
            new List<PresetModifier> {
                new() {
                    Block = "delay1",
                    Param = "mix",
                    Source = "ext1",
                    Scale1 = scale1,
                    Scale2 = scale2,
                    UpdateRate = updateRate
                }
            },
            Token
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pc_reset_holds_the_stored_value_from_a_preset_load_until_the_controller_sends_one() {
        var rig = server.Services.GetRequiredService<LiveRig>();
        await PutModifiers(
            new PresetModifier {
                Block = "delay1",
                Param = "mix",
                Source = "ext1",
                Max = 100,
                PcReset = true
            }
        );
        using var pedal = await client.PutAsJsonAsync("/api/v1/live/external/0", new ValueRequest(0.75f), Token);
        pedal.EnsureSuccessStatusCode();

        // Added to the loaded preset, the modifier follows the pedal at once: PC Reset is about a preset load.
        await ServerFixture.Eventually(() => server.Graph!.Modifiers is [{ Enabled: true }]
            && Math.Abs(server.Graph.ModifierValues[0] - 75f) < 0.5f
        );
        using var saving = await client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Held", true), Token);
        var saved = (await saving.Content.ReadFromJsonAsync<LiveState>(Token))!;
        var stored = Cathode.Dsp.Blocks.DelayBlock.Info.Params.First(static p => p.Id == "mix").Default;

        await rig.LoadAsync(saved.Preset.Id);

        // The pedal still sits at 0.75, but the loaded preset plays its stored mix.
        await ServerFixture.Eventually(
            () => server.Graph!.Modifiers is [{ Enabled: false }] && server.Target("delay1", "mix") == stored,
            () => $"mix {server.Target("delay1", "mix")}, stored {stored}"
        );

        // An edit that rebuilds the graph does not end the wait.
        using var added = await client.PostAsJsonAsync(
            "/api/v1/live/blocks",
            new AddBlockRequest("chorus", 0, 3),
            Token
        );
        added.EnsureSuccessStatusCode();
        await ServerFixture.Eventually(() => server.Graph!.SlotOf("chorus1") >= 0);
        Assert.False(server.Graph!.Modifiers[0].Enabled);

        // Another controller does not either; the modifier's own does.
        using var other = await client.PutAsJsonAsync("/api/v1/live/external/1", new ValueRequest(1f), Token);
        using var moved = await client.PutAsJsonAsync("/api/v1/live/external/0", new ValueRequest(0.5f), Token);
        moved.EnsureSuccessStatusCode();
        await ServerFixture.Eventually(
            () => server.Graph!.Modifiers is [{ Enabled: true }]
                && Math.Abs(server.Graph.ModifierValues[0] - 50f) < 0.5f,
            () => $"the modifier's value: {server.Graph!.ModifierValues[0]}"
        );
    }

    [Fact]
    public void A_modifier_without_the_settings_stores_none_of_them() {
        var json = JsonSerializer.Serialize(
            [new() { Block = "delay1", Param = "mix", Source = "ext1" }],
            ContractsJsonContext.Default.ListPresetModifier
        );
        var read = JsonSerializer.Deserialize(
            """[{"block":"delay1","param":"mix","source":"ext1"}]""",
            ContractsJsonContext.Default.ListPresetModifier
        )!;

        foreach (var name in new[] { "scale1", "scale2", "updateRate", "pcReset", "offAbove" }) {
            Assert.DoesNotContain(name, json, StringComparison.Ordinal);
        }

        Assert.Equal(
            (null, null, null, null, null),
            (read[0].Scale1, read[0].Scale2, read[0].UpdateRate, read[0].PcReset, read[0].OffAbove)
        );
    }

    [Fact]
    public void A_copy_for_another_block_keeps_every_setting_but_the_channel() {
        var modifier = new PresetModifier {
            Block = "delay1",
            Param = "mix",
            Source = "ext1",
            Min = 1,
            Max = 2,
            Curve = "log",
            Source2 = "lfo1",
            Operation = "subtract",
            Scale1 = 0.1f,
            Scale2 = 0.2f,
            Start = 0.3f,
            Mid = 0.4f,
            End = 0.6f,
            Slope = 0.7f,
            Scale = 3f,
            Offset = 0.2f,
            AttackMs = 4f,
            ReleaseMs = 5f,
            UpdateRate = "medium",
            PcReset = true,
            Mode = "moving",
            Threshold = 0.8f,
            OffDelayMs = 9f,
            OffAbove = true,
            Channel = "B"
        };

        var copy = modifier.CopyFor("delay2");

        modifier.Block = "delay2";
        modifier.Channel = null;
        Assert.Equal(
            JsonSerializer.Serialize(modifier, ContractsJsonContext.Default.PresetModifier),
            JsonSerializer.Serialize(copy, ContractsJsonContext.Default.PresetModifier)
        );
    }
}
