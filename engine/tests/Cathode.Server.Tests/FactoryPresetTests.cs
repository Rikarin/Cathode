using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Dsp.Blocks;
using Cathode.Engine.Graph;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Cathode.Testing.References;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     The factory bank (<see cref="FactoryPresets" />): every preset is well formed, uses real blocks and parameters
///     with values in range, fits the CPU budget, compiles and loads into the live rig; and the bank is read-only,
///     while Save, Duplicate and Export give the user a copy of their own. Its levels are checked in
///     <see cref="FactoryPresetLevelTests" />.
/// </summary>
public sealed class FactoryPresetTests : IAsyncLifetime {
    static readonly BlockCatalog Catalog = new(BlockRegistry.CreateDefault());
    static readonly ProcessSpec Spec = new(48_000, 64);

    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    static List<Preset> Presets => FactoryPresets.Build(Catalog).Presets;

    public async ValueTask InitializeAsync() => client = await server.ConnectedClientAsync();

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public void Every_recipe_builds_from_known_blocks_and_in_range_values() {
        var (presets, problems) = FactoryPresets.Build(Catalog);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.InRange(presets.Count, 60, 100);
        Assert.Equal(presets.Count, presets.Select(static p => p.Id).Distinct().Count());
        Assert.Equal(
            presets.Count,
            presets.Select(static p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        );
        Assert.All(
            presets,
            static p => {
                Assert.True(StoreIds.IsValid(p.Id), p.Id);
                Assert.StartsWith(FactoryPresets.IdPrefix, p.Id, StringComparison.Ordinal);
                Assert.Equal(FactoryPresets.Author, p.Author);
                Assert.True(p.Factory);
                Assert.False(string.IsNullOrWhiteSpace(p.Description), p.Name);
                Assert.NotEmpty(p.Tags);
                Assert.Single(p.Blocks, static b => b.Type == "circuitamp");
                Assert.Single(p.Blocks, static b => b.Type == "cab");
            }
        );
    }

    [Fact]
    public void Every_preset_has_the_rigs_shape_and_compiles() {
        var compiler = new GraphCompiler(BlockRegistry.CreateDefault());
        foreach (var preset in Presets) {
            Assert.Equal(6, preset.Rows);
            Assert.Equal(14, preset.Columns);
            Assert.Equal(Preset.SceneCount, preset.Scenes.Count);
            Assert.Equal(preset.Blocks.Count, preset.Blocks.Select(static b => b.Id).Distinct().Count());
            Assert.Equal(preset.Blocks.Count, preset.Blocks.Select(static b => (b.Row, b.Column)).Distinct().Count());
            Assert.All(
                preset.Blocks,
                static b => Assert.True(b.Row is >= 0 and < 6 && b.Column is >= 0 and < 14, b.Id)
            );
            Assert.All(
                preset.Blocks.GroupBy(static b => b.Type),
                g => Assert.True(g.Count() <= Catalog.Info(g.Key)!.MaxInstances, $"{preset.Name}: too many {g.Key}")
            );

            // The main row runs Input 1 to Output 1 through every column, block or shunt.
            var row = preset.Blocks.Where(static b => b.Row == FactoryRecipe.MainRow)
                .OrderBy(static b => b.Column)
                .ToList();
            Assert.Equal(14, row.Count);
            Assert.Equal(BuiltInTypes.Input, row[0].Type);
            Assert.Equal(BuiltInTypes.Output, row[^1].Type);
            Assert.Equal(
                row.Zip(row.Skip(1), static (a, b) => (a.Id, b.Id)),
                preset.Cables.Select(static c => (c.From, c.To))
            );

            var effects = preset.Blocks.Where(static b => !BuiltInTypes.IsBuiltIn(b.Type)).Select(static b => b.Id);
            for (var s = 0; s < Preset.SceneCount; s++) {
                Assert.False(string.IsNullOrWhiteSpace(preset.Scenes[s].Name));
                Assert.Equal(effects.Order(), preset.Scenes[s].Blocks.Keys.Order());
                using var graph = compiler.Compile(PresetGraph.Build(preset, s, null!), Spec, 1);
            }
        }
    }

    [Fact]
    public void Pedals_go_in_front_of_the_amp() {
        // Drives, fuzzes, boosts and wahs are stompboxes between the guitar and the amp, not after the cabinet.
        var late = Presets
            .SelectMany(static p => {
                    var row = p.Blocks.Where(b => b.Row == p.Blocks.Single(static i => i.Id == "in1").Row)
                        .OrderBy(static b => b.Column)
                        .ToList();
                    var amp = row.FindIndex(static b => b.Type == CircuitAmpBlock.Info.TypeId);
                    return row.Skip(amp + 1)
                        .Where(static b => b.Type is "drive" or "wah")
                        .Select(b => $"{p.Name}: {b.Id}");
                }
            )
            .ToList();
        Assert.Empty(late);
    }

    [Fact]
    public void Clean_scenes_play_clean_at_a_hot_pickups_level() {
        // A Clean scene (or a Clean preset's first scene) must not break up with a hot pickup: a 0.5 V peak sine
        // through its amp channel, power amp included, stays under 8 % THD, which leaves a tube input stage's own
        // few percent (a tweed's, a Marshall's), and under 1 % on the solid-state JC-120, whose power amp clips hard
        // (at volume 4 it does near 0.4 V, which the levels' 0.35 V test signal never showed).
        using var amp = new CircuitAmpRenderer(Rate);
        var types = amp.Types.ToList();
        var dirty = new List<string>();
        foreach (var preset in Presets) {
            if (preset.Blocks.Find(static b => b.Type == "circuitamp") is not { } block) {
                continue;
            }

            for (var s = 0; s < preset.Scenes.Count; s++) {
                var scene = preset.Scenes[s];
                var clean = scene.Name.StartsWith("Clean", StringComparison.Ordinal)
                    || s == 0 && preset.Tags.Contains("Clean");
                if (!clean) {
                    continue;
                }

                var values = block.Channels.GetValueOrDefault(scene.StateOf(block.Id).Channel) ?? [];
                var type = types[(int)values.GetValueOrDefault("type", block.Channel("A").GetValueOrDefault("type"))];
                var settings = values.Where(static v => v.Key != "type")
                    .ToDictionary(static v => v.Key, static v => (double)v.Value);
                var thd = Thd(amp.Render(Sine(0.5), new(type, settings)));
                if (thd > (type == "JC-120" ? 1 : 8)) {
                    dirty.Add($"{preset.Name} / {scene.Name} ({type}): {thd:0.0} % THD");
                }
            }
        }

        Assert.Empty(dirty);
    }

    [Fact]
    public void Every_preset_fits_the_cpu_budget() {
        var over = Presets.Select(static p => (p.Name, Cost: p.Blocks.Sum(static b => BlockCosts.Of(b.Type) ?? 0)))
            .Where(static p => p.Cost > FactoryPresets.CpuBudget)
            .Select(static p => $"{p.Name}: {p.Cost:0.0} %")
            .ToList();

        Assert.True(over.Count == 0, string.Join("\n", over));
    }

    [Fact]
    public async Task Every_preset_loads_into_the_live_rig() {
        foreach (var summary in (await Json<PresetSummary[]>(
                     client.GetAsync("/api/v1/presets", Token)
                 )).Where(static p => p.Factory == true)) {
            var state = await Json<LiveState>(
                client.PutAsJsonAsync($"/api/v1/live/preset/{summary.Id}", new { }, Token)
            );

            Assert.Equal(summary.Id, state.Preset.Id);
            Assert.True(state.Preset.Factory);
            Assert.False(state.Dirty);
            await ServerFixture.Eventually(
                () => Task.FromResult(server.Graph?.SlotOf("circuitamp1") >= 0),
                describe: () => $"{summary.Name} did not compile: {server.Log}"
            );
        }
    }

    [Fact]
    public async Task The_factory_bank_lists_first_and_is_read_only() {
        var presets = await Json<PresetSummary[]>(client.GetAsync("/api/v1/presets", Token));
        var banks = await Json<Bank[]>(client.GetAsync("/api/v1/banks", Token));
        var factory = presets.Where(static p => p.Factory == true).ToList();
        var id = factory[0].Id;
        var preset = await Json<Preset>(client.GetAsync($"/api/v1/presets/{id}", Token));

        Assert.Equal(Presets.Count, factory.Count);
        Assert.Equal(FactoryPresets.BankId, banks[0].Id);
        Assert.True(banks[0].Factory);
        Assert.Equal(factory.Select(static p => p.Id), banks[0].Slots);
        Assert.Equal(
            HttpStatusCode.Conflict,
            await Status(client.PutAsJsonAsync($"/api/v1/presets/{id}", preset, Token))
        );
        Assert.Equal(HttpStatusCode.Conflict, await Status(client.DeleteAsync($"/api/v1/presets/{id}", Token)));
        Assert.Equal(
            HttpStatusCode.Conflict,
            await Status(
                client.PatchAsJsonAsync($"/api/v1/presets/{id}/metadata", new PresetMetadataRequest("Mine"), Token)
            )
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            await Status(client.PutAsJsonAsync($"/api/v1/banks/{FactoryPresets.BankId}", banks[0], Token))
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            await Status(client.DeleteAsync($"/api/v1/banks/{FactoryPresets.BankId}", Token))
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            await Status(client.PutAsJsonAsync($"/api/v1/presets/{FactoryPresets.IdPrefix}mine", preset, Token))
        );
    }

    [Fact]
    public async Task Factory_presets_take_a_colour_and_a_favourite_and_go_into_user_banks() {
        var id = Presets[0].Id;

        await Ok(
            client.PatchAsJsonAsync(
                $"/api/v1/presets/{id}/metadata",
                new PresetMetadataRequest(null, "red", true),
                Token
            )
        );
        var bank = new Bank { Name = "Gig", Slots = [null, id] };
        await Ok(client.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));
        var state = await Json<LiveState>(
            client.PutAsJsonAsync($"/api/v1/live/banks/{bank.Id}/slots/1", new { }, Token)
        );
        var listed = (await Json<PresetSummary[]>(client.GetAsync("/api/v1/presets", Token))).Single(p => p.Id == id);

        Assert.Equal(id, state.Preset.Id);
        Assert.Equal("red", listed.Color);
        Assert.True(listed.Favorite);
        Assert.True(state.Preset.Favorite);
    }

    [Fact]
    public async Task Saving_a_factory_preset_stores_the_users_own_copy() {
        var original = Presets[0];
        await Ok(client.PutAsJsonAsync($"/api/v1/live/preset/{original.Id}", new { }, Token));
        await Ok(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/circuitamp1/params",
                new Dictionary<string, float> { ["gain"] = 2 },
                Token
            )
        );

        var saved = await Json<LiveState>(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(), Token));
        var again = await Json<LiveState>(client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(), Token));
        var copy = await Json<Preset>(client.GetAsync($"/api/v1/presets/{saved.Preset.Id}", Token));
        var factory = await Json<Preset>(client.GetAsync($"/api/v1/presets/{original.Id}", Token));
        var duplicate = await Json<PresetSummary>(
            client.PostAsJsonAsync($"/api/v1/presets/{original.Id}/duplicate", new DuplicatePresetRequest(), Token)
        );

        Assert.NotEqual(original.Id, saved.Preset.Id);
        Assert.False(FactoryPresets.IsFactoryId(saved.Preset.Id));
        Assert.Null(saved.Preset.Factory);
        Assert.False(saved.Dirty);
        Assert.Equal(saved.Preset.Id, again.Preset.Id);
        Assert.Equal(original.Name, copy.Name);
        Assert.Null(copy.Factory);
        Assert.Equal(2, copy.Blocks.Single(static b => b.Id == "circuitamp1").Channels["A"]["gain"]);
        Assert.NotEqual(2, factory.Blocks.Single(static b => b.Id == "circuitamp1").Channels["A"]["gain"]);
        Assert.Null(duplicate.Factory);
        Assert.False(FactoryPresets.IsFactoryId(duplicate.Id));
    }

    [Fact]
    public async Task An_exported_factory_preset_imports_as_the_users_own() {
        var id = Presets[1].Id;
        using var export = await client.GetAsync($"/api/v1/presets/{id}/export", Token);
        export.EnsureSuccessStatusCode();
        var package = await export.Content.ReadAsByteArrayAsync(Token);

        using var content = new ByteArrayContent(package);
        content.Headers.ContentType = new("application/zip");
        var imported = await Json<ImportResult>(client.PostAsync("/api/v1/presets/import", content, Token));

        Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);
        Assert.True(imported.Renamed);
        Assert.False(FactoryPresets.IsFactoryId(imported.Preset.Id));
        Assert.Null(imported.Preset.Factory);
        Assert.Equal(Presets[1].Description, imported.Preset.Description);
    }

    static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> call) {
        using var response = await call;
        return response.StatusCode;
    }

    static async Task Ok(Task<HttpResponseMessage> call) {
        using var response = await call;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    static async Task<T> Json<T>(Task<HttpResponseMessage> call) {
        using var response = await call;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    const double Rate = 48_000;
    const double Hz = 196;

    /// <summary>One second of G3 at <paramref name="peak" /> volts.</summary>
    static float[] Sine(double peak) =>
        [..Enumerable.Range(0, (int)Rate).Select(i => (float)(peak * Math.Sin(2 * Math.PI * Hz * i / Rate)))];

    /// <summary>
    ///     THD in % of a steady sine's second half: everything but the fundamental and DC, over the fundamental.
    /// </summary>
    static double Thd(float[] y) {
        var start = y.Length / 2;
        var count = y.Length - start;
        double c = 0, q = 0, mean = 0;
        for (var i = start; i < y.Length; i++) {
            var t = 2 * Math.PI * Hz * i / Rate;
            c += y[i] * Math.Cos(t);
            q += y[i] * Math.Sin(t);
            mean += y[i];
        }

        c *= 2.0 / count;
        q *= 2.0 / count;
        mean /= count;
        var rest = 0.0;
        for (var i = start; i < y.Length; i++) {
            var t = 2 * Math.PI * Hz * i / Rate;
            var r = y[i] - mean - c * Math.Cos(t) - q * Math.Sin(t);
            rest += r * r;
        }

        return 100 * Math.Sqrt(rest / count / ((c * c + q * q) / 2));
    }
}
