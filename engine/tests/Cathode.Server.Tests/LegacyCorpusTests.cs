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
///     a user's edit saved as a new preset (a Tiny Terror on channel A, another power tube on channel B, the amp made
///     a global block), a user block preset of a Hot Cat, that entry exported as <c>legacy-hot-cat.cthblock</c> and a
///     <c>global.json</c> with a setting changed; since 1.0.0-rc.4 also two modifiers on the edited preset and a
///     setlist. Up to 1.0.0-rc.3 (schema 1) the Tiny Terror has its Cut set and channel B a 5881, which schema 2
///     changed. Every release since must read all of it, upgraded, and render every distinct scene of every preset
///     finite, the same twice, and at the loudness recorded in <c>levels.json</c>. A deliberate change of sound (an
///     amp model refit) moves those levels: rewrite them with <c>CATHODE_UPDATE_LEGACY_LEVELS=1</c> and say why in
///     the commit.
/// </summary>
/// <remarks>
///     To add a release (a step of releasing, docs/10-releases.md): start its Server on an empty data folder and run
///     <c>tests/assets/legacy/make-corpus.mjs</c> against it, which makes the files through the API and copies them
///     into a folder named after the version; then record the levels. The script's header has the commands. A
///     release that changes a stored schema also gets its line in <see cref="Schemas" />.
/// </remarks>
public sealed class LegacyCorpusTests {
    /// <summary>Loudness tolerance, LU: tight enough to catch a migration that changes a value.</summary>
    const double Tolerance = 0.5;

    static readonly string Corpus = Path.Combine(EngineRoot(), "tests", "assets", "legacy");
    static readonly string LevelsPath = Path.Combine(Corpus, "levels.json");

    CancellationToken Token => TestContext.Current.CancellationToken;

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    ///     Every released version, oldest first: the headings of CHANGELOG.md but the one marked unreleased. A
    ///     release's section is written before it is tagged, so its folder must be in the corpus by then.
    /// </summary>
    static readonly string[] Released = [
        ..File.ReadLines(Path.Combine(EngineRoot(), "..", "CHANGELOG.md"))
            .Where(static line => line.StartsWith("## ", StringComparison.Ordinal)
                && !line.Contains("unreleased", StringComparison.OrdinalIgnoreCase)
            )
            .Select(static line => line[3..].Split(' ', 2)[0])
            .Reverse()
    ];

    /// <summary>The releases in the corpus, in the order they were released (one that never was comes last).</summary>
    static readonly string[] Releases = [
        ..Directory.EnumerateDirectories(Corpus)
            .Select(static d => Path.GetFileName(d))
            .OrderBy(static v => Array.IndexOf(Released, v) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(static v => v, StringComparer.Ordinal)
    ];

    /// <summary>
    ///     The schema each release stores, by the first release that wrote it. Presets, block presets and the global
    ///     settings have changed schema together so far; give them a column each when they stop.
    /// </summary>
    static readonly (string Since, int Schema)[] Schemas = [("1.0.0-rc.1", 1), ("1.0.0-rc.4", 2)];

    public static TheoryData<string> Versions => [..Releases];

    /// <summary>
    ///     The schema <paramref name="version" /> stores: that of the last entry of <see cref="Schemas" /> not after
    ///     it.
    /// </summary>
    static int SchemaOf(string version) =>
        Schemas.Last(s => Array.IndexOf(Released, s.Since) <= Array.IndexOf(Released, version)).Schema;

    [Fact]
    public void The_corpus_holds_every_release_and_each_folders_files_are_of_its_releases_schema() {
        Assert.True(
            Released.Length >= 5 && Released[0] == "1.0.0-rc.1",
            "CHANGELOG.md's release headings were not read"
        );
        Assert.True(
            Released.SequenceEqual(Releases),
            $"The corpus has {string.Join(", ", Releases)}; CHANGELOG.md lists the releases "
            + $"{string.Join(", ", Released)}. Add the missing release's folder: see "
            + "tests/assets/legacy/make-corpus.mjs."
        );
        Assert.All(Schemas, static s => Assert.Contains(s.Since, Released));
        Assert.True(Schemas.Select(static s => s.Schema).SequenceEqual(Enumerable.Range(1, Schemas.Length)));
        Assert.True(Schemas[^1].Schema <= Preset.CurrentSchemaVersion);

        foreach (var version in Releases) {
            var folder = Path.Combine(Corpus, version);
            string[] stored = [
                Path.Combine(folder, "global.json"),
                ..Directory.EnumerateFiles(Path.Combine(folder, "presets"), "*.json"),
                ..Directory.EnumerateFiles(Path.Combine(folder, "block-presets"), "*.json")
            ];
            var exported = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "legacy-hot-cat.cthblock")))!;
            JsonNode[] documents = [
                ..stored.Select(static f => JsonNode.Parse(File.ReadAllText(f))!),
                ..exported["presets"]!.AsArray().Select(static e => e!)
            ];
            Assert.True(documents.Length >= 6, $"{version}: files are missing");
            foreach (var document in documents) {
                // A file without a version is schema 1: 1.0.0-rc.3 and earlier wrote none into a block preset.
                var schema = document["schemaVersion"]?.GetValue<int>() ?? 1;
                var json = document.ToJsonString();
                Assert.True(
                    schema == SchemaOf(version),
                    $"{version}: a file is schema {schema}, but that release stored schema {SchemaOf(version)}:\n"
                    + json[..Math.Min(json.Length, 200)]
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

        // The edited preset, its global amp and the block preset: the same Tiny Terror and Hot Cat in every release.
        var edits = presets.Single(static p => p.Name == "Legacy Edits");
        var amp = edits.Blocks.Single(static b => b.Type == "circuitamp");
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        Assert.False(global.PresetSpillover);
        var shared = global.Blocks.Single(g => g.Id == amp.Global);
        var hotCat = (await client.GetFromJsonAsync<BlockPreset[]>("/api/v1/block-presets?type=circuitamp", Token))!
            .Single(static p => p.Id == "legacy-hot-cat");
        foreach (var channels in new[] { amp.Channels, shared.Channels }) {
            Assert.Equal(63, channels["A"]["type"]);
            Assert.Equal(7, channels["A"]["presence"]);
            Assert.Equal(10, channels["A"]["master"]);
            Assert.Equal(1, channels["B"]["powertube"]);
        }

        Assert.Equal(3, hotCat.Values["presence"]);
        Assert.Equal(1, hotCat.Values["powertube"]);
        if (SchemaOf(version) == 1) {
            // They come through schema 2's Amp changes.
            AssertSchema1AmpUpgraded(amp.Channels["A"], amp.Channels["B"]);
            AssertSchema1AmpUpgraded(shared.Channels["A"], shared.Channels["B"]);
            Assert.False(hotCat.Values.ContainsKey("cut")); // the Cut of 7 is the Presence knob's 3
        }

        if (SchemaOf(version) == Preset.CurrentSchemaVersion) {
            // Nothing to upgrade: every file reads back as that release wrote it. (A field added since may come
            // back beside what the file has.) When the schema is raised, these folders get their own checks above.
            await AssertReadsAsWritten(client, version);
        }

        // The same entry as that release exported it (a .cthblock file) comes through the import upgraded alike.
        using var exported = new ByteArrayContent(
            await File.ReadAllBytesAsync(Path.Combine(Corpus, version, "legacy-hot-cat.cthblock"), Token)
        );
        exported.Headers.ContentType = new("application/json");
        using var import = await client.PostAsync("/api/v1/block-presets/import", exported, Token);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var imported = Assert.Single((await import.Content.ReadFromJsonAsync<BlockPreset[]>(Token))!);
        Assert.Equal(hotCat.Values, imported.Values);

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

    /// <summary>
    ///     Every preset, block preset and setlist of a release's folder, and its global settings, as the API gives
    ///     them: all that the file holds, with the same values.
    /// </summary>
    async Task AssertReadsAsWritten(HttpClient client, string version) {
        var folder = Path.Combine(Corpus, version);
        foreach (var (directory, route) in new[] {
                     ("presets", "presets"), ("block-presets", "block-presets"), ("setlists", "setlists")
                 }) {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, directory), "*.json")) {
                var id = Path.GetFileNameWithoutExtension(file);
                var read = route == "block-presets"
                    ? (await client.GetFromJsonAsync<JsonArray>($"/api/v1/{route}", Token))!.Single(p =>
                        p!["id"]!.GetValue<string>() == id
                    )
                    : await client.GetFromJsonAsync<JsonNode>($"/api/v1/{route}/{id}", Token);
                AssertHolds(
                    JsonNode.Parse(await File.ReadAllTextAsync(file, Token)),
                    read,
                    $"{version}/{directory}/{id}"
                );
            }
        }

        AssertHolds(
            JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "global.json"), Token)),
            await client.GetFromJsonAsync<JsonNode>("/api/v1/global", Token),
            $"{version}/global"
        );
    }

    /// <summary>
    ///     <paramref name="read" /> holds everything <paramref name="written" /> does: each property of an object
    ///     (a null one may be missing), each element of an array in order, and equal values.
    /// </summary>
    static void AssertHolds(JsonNode? written, JsonNode? read, string path) {
        switch (written) {
            case null:
                Assert.True(read is null, $"{path}: null was written, {read?.ToJsonString()} is read");
                break;
            case JsonObject expected:
                Assert.True(
                    read is JsonObject,
                    $"{path}: an object was written, {read?.ToJsonString() ?? "null"} is read"
                );
                foreach (var (name, value) in expected) {
                    AssertHolds(value, read![name], $"{path}.{name}");
                }

                break;
            case JsonArray expected:
                Assert.True(
                    read is JsonArray actual && actual.Count == expected.Count,
                    $"{path}: {expected.ToJsonString()} was written, {read?.ToJsonString() ?? "null"} is read"
                );
                for (var i = 0; i < expected.Count; i++) {
                    AssertHolds(expected[i], read![i], $"{path}[{i}]");
                }

                break;
            default:
                var numbers = written.GetValueKind() == JsonValueKind.Number
                    && read?.GetValueKind() == JsonValueKind.Number;
                var same = numbers
                    ? Math.Abs(written.GetValue<double>() - read!.GetValue<double>()) < 1e-6
                    : read is not null && JsonNode.DeepEquals(written, read);
                Assert.True(
                    same,
                    $"{path}: {written.ToJsonString()} was written, {read?.ToJsonString() ?? "null"} is read"
                );
                break;
        }
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
