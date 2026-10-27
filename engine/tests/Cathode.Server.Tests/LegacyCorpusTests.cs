using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Ipc;
using Cathode.Server.Rig;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>
///     The legacy corpus (docs/03-testing-strategy.md §9): <c>tests/assets/legacy/&lt;version&gt;/</c> holds a data
///     folder written by each released Cathode, through its own API: two factory presets duplicated into the library,
///     a user's edit saved as a new preset (a Tiny Terror with its Cut on channel A, a 5881 on channel B, the amp made
///     a global block), a user block preset of a Hot Cat and a <c>global.json</c> with a setting changed. Every
///     release since must read all of it, upgraded, and render every distinct scene of every preset finite, the same
///     twice, and at the loudness recorded in <c>levels.json</c>. A deliberate change of sound (an amp model refit)
///     moves those levels: rewrite them with <c>CATHODE_UPDATE_LEGACY_LEVELS=1</c> and say why in the commit.
/// </summary>
/// <remarks>
///     To add a release: start its Server (<c>Cathode:Engine:Mode=inprocess</c> on 1.0.0-rc.*) on an empty data
///     folder, make the same files through its API, copy <c>presets</c>, <c>block-presets</c> and
///     <c>global.json</c> into a folder named after the version, and record the levels.
/// </remarks>
public sealed class LegacyCorpusTests {
    /// <summary>Loudness tolerance, LU: tight enough to catch a migration that changes a value.</summary>
    const double Tolerance = 0.5;

    static readonly string Corpus = Path.Combine(EngineRoot(), "tests", "assets", "legacy");
    static readonly string LevelsPath = Path.Combine(Corpus, "levels.json");

    CancellationToken Token => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The releases in the corpus, oldest first.</summary>
    static readonly string[] Releases = [
        ..Directory.EnumerateDirectories(Corpus).Select(static d => Path.GetFileName(d)).Order(StringComparer.Ordinal)
    ];

    public static TheoryData<string> Versions => [..Releases];

    [Fact]
    public void The_corpus_holds_every_release_since_the_first_and_only_schema_1_files() {
        Assert.Equal(["1.0.0-rc.1", "1.0.0-rc.2", "1.0.0-rc.3"], Releases);
        foreach (var file in Directory.EnumerateFiles(Corpus, "*.json", SearchOption.AllDirectories)) {
            if (file != LevelsPath) {
                var document = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                Assert.True(
                    document["schemaVersion"] is null || document["schemaVersion"]!.GetValue<int>() == 1,
                    $"{file} is not a schema-1 file"
                );
            }
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_releases_data_folder_loads_upgraded(string version) {
        await using var server = new ServerFixture();
        Copy(Path.Combine(Corpus, version), server.DataDirectory);
        using var client = await server.ConnectedClientAsync();

        var presets = await Presets(client, version);
        Assert.Equal(3, presets.Count);
        var catalog = BlockRegistry.CreateDefault();
        foreach (var preset in presets) {
            Assert.Equal(Preset.CurrentSchemaVersion, preset.SchemaVersion);
            foreach (var block in preset.Blocks.Where(static b => b.Type != "shunt")) {
                // Every stored value still names a parameter: none disappeared without a migration.
                var descriptor = catalog.GetDescriptor(block.Type);
                foreach (var (channel, values) in block.Channels) {
                    var unknown = values.Keys.Where(k => descriptor.IndexOf(k) < 0).ToList();
                    Assert.True(
                        unknown.Count == 0,
                        $"{version} {preset.Name} {block.Id} {channel}: {string.Join(", ", unknown)}"
                    );
                }
            }
        }

        // The edited preset, its global amp and the block preset come through schema 2's Amp changes.
        var edits = presets.Single(static p => p.Name == "Legacy Edits");
        var amp = edits.Blocks.Single(static b => b.Type == "circuitamp");
        AssertSchema1AmpUpgraded(amp.Channels["A"], amp.Channels["B"]);
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        Assert.False(global.PresetSpillover);
        var shared = global.Blocks.Single(g => g.Id == amp.Global);
        AssertSchema1AmpUpgraded(shared.Channels["A"], shared.Channels["B"]);
        var hotCat = (await client.GetFromJsonAsync<BlockPreset[]>("/api/v1/block-presets?type=circuitamp", Token))!
            .Single(static p => p.Id == "legacy-hot-cat");
        Assert.Equal(10 - 7, hotCat.Values["presence"]);
        Assert.Equal(1, hotCat.Values["powertube"]); // the 5881 is the 6L6GC
        Assert.False(hotCat.Values.ContainsKey("cut"));

        // Loading one plays it: the live rig takes it and the engine reports no error.
        using var load = await client.PutAsync($"/api/v1/live/preset/{edits.Id}", null, Token);
        Assert.Equal(HttpStatusCode.OK, load.StatusCode);
        await ServerFixture.Eventually(async () => (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!
            .Engine.LastError is null
        );
        // No file was skipped as damaged.
        Assert.DoesNotContain("Skipped the unreadable", server.Log, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Every distinct scene of every preset in the corpus, read through the Server's store, rendered with the
    ///     factory leveling's guitar phrase: finite, bit-identical when rendered again, and at its recorded loudness.
    /// </summary>
    [Fact]
    public async Task Every_legacy_scene_renders_deterministically_at_its_recorded_level() {
        var input = FactoryPresetLevelTests.Phrase();
        var measured = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var version in Releases) {
            await using var server = new ServerFixture();
            Copy(Path.Combine(Corpus, version), server.DataDirectory);
            using var client = server.CreateClient();
            foreach (var preset in await Presets(client, version)) {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var s = 0; s < preset.Scenes.Count; s++) {
                    var graph = JsonSerializer.Serialize(
                        PresetGraph.Build(preset, s, null!),
                        GraphJsonContext.Default.GraphDefinition
                    );
                    if (!seen.Add(graph + preset.Scenes[s].Level.ToString(CultureInfo.InvariantCulture))) {
                        continue; // the same sound as an earlier scene
                    }

                    var name = $"{version}/{preset.Name}/{s + 1}";
                    var (left, right) = FactoryPresetLevelTests.RenderOutput(preset, s, input);
                    var (left2, right2) = FactoryPresetLevelTests.RenderOutput(preset, s, input);
                    Assert.True(left.SequenceEqual(left2) && right.SequenceEqual(right2), $"{name}: not deterministic");
                    Assert.True(left.All(float.IsFinite) && right.All(float.IsFinite), $"{name}: NaN or infinity");
                    var peak = Math.Max(left.Max(MathF.Abs), right.Max(MathF.Abs));
                    measured[name] = [
                        Math.Round(FactoryPresetLevelTests.Loudness(left, right), 2),
                        Math.Round(20 * Math.Log10(Math.Max(peak, 1e-9)), 2)
                    ];
                }
            }
        }

        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_LEGACY_LEVELS") == "1") {
            File.WriteAllText(
                LevelsPath,
                JsonSerializer.Serialize(measured, Indented) + "\n"
            );
            return;
        }

        var recorded = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(LevelsPath))!;
        var failures = new List<string>();
        foreach (var (name, values) in measured) {
            if (!recorded.TryGetValue(name, out var expected)) {
                failures.Add($"{name}: no recorded level");
            } else if (Math.Abs(values[0] - expected[0]) > Tolerance) {
                failures.Add($"{name}: {values[0]:0.00} LUFS, recorded {expected[0]:0.00}");
            }
        }

        failures.AddRange(recorded.Keys.Except(measured.Keys).Select(static n => $"{n}: recorded but not rendered"));
        Assert.True(
            failures.Count == 0,
            string.Join("\n", failures)
            + "\nAfter a deliberate change of sound, rewrite them: CATHODE_UPDATE_LEGACY_LEVELS=1 dotnet test "
            + "--project tests/Cathode.Server.Tests --filter-class Cathode.Server.Tests.LegacyCorpusTests"
        );
    }

    /// <summary>A schema-1 Tiny Terror with Cut 3 (channel A) and a 5881 (channel B), upgraded.</summary>
    static void AssertSchema1AmpUpgraded(Dictionary<string, float> a, Dictionary<string, float> b) {
        Assert.Equal(63, a["type"]);
        Assert.Equal(10 - 3, a["presence"]); // the Cut is the Presence knob's Tone, turning the other way
        Assert.False(a.ContainsKey("cut"));
        Assert.Equal(10, a["master"]); // schema 1's default, stored so it sounds the same
        Assert.Equal(1, b["powertube"]);
        Assert.False(b.ContainsKey("t1plate"));
    }

    async Task<List<Preset>> Presets(HttpClient client, string version) {
        var ids = Directory.EnumerateFiles(Path.Combine(Corpus, version, "presets"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);
        var presets = new List<Preset>();
        foreach (var id in ids) {
            presets.Add((await client.GetFromJsonAsync<Preset>($"/api/v1/presets/{id}", Token))!);
        }

        return presets;
    }

    static void Copy(string from, string to) {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder.");
    }
}
