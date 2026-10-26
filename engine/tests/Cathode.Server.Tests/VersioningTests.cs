using Cathode.Contracts;
using Cathode.Server.Api;
using Cathode.Server.Stores;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>
///     The release version (one for the engine and the editor) and the preset schema's upgrade path
///     (<see cref="PresetMigrations" />).
/// </summary>
public sealed class VersioningTests {
    [Fact]
    public void The_engine_and_the_editor_have_the_same_version() {
        // A tag build sets both from CATHODE_VERSION; a local build takes the defaults, which must agree.
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CATHODE_VERSION") is { Length: > 0 }, "A tag build.");
        var engine = typeof(CathodeApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        var package = Path.Combine(EngineRoot(), "..", "editor", "package.json");
        var editor = JsonNode.Parse(File.ReadAllText(package))!["version"]!.GetValue<string>();
        Assert.Equal(editor, engine);
    }

    [Fact]
    public void A_preset_of_the_current_schema_reads_unchanged() {
        var json = $$"""{"schemaVersion":{{Preset.CurrentSchemaVersion}},"name":"New","blocks":[]}""";
        var upgraded = PresetMigrations.Upgrade(JsonNode.Parse(json))!;

        Assert.Equal(json, upgraded.ToJsonString());
    }

    [Fact]
    public void A_preset_without_a_schema_is_schema_1_and_upgrades_to_the_current_one() {
        var upgraded = PresetMigrations.Upgrade(JsonNode.Parse("""{"name":"Old","blocks":[]}"""))!;
        var preset = upgraded.Deserialize(ContractsJsonContext.Default.Preset)!;

        Assert.Equal(Preset.CurrentSchemaVersion, preset.SchemaVersion);
        Assert.Equal("Old", preset.Name);
    }

    [Fact]
    public void Schema_2_turns_the_amps_5881_into_the_6L6GC_it_always_was() {
        var upgraded = PresetMigrations.Upgrade(
            JsonNode.Parse(
                """
                {"schemaVersion":1,"blocks":[
                  {"id":"amp1","type":"circuitamp","channels":{"A":{"powertube":8,"gain":6},"B":{"powertube":7},"C":{}}},
                  {"id":"drive1","type":"drive","channels":{"A":{"powertube":8}}}
                ]}
                """
            )
        )!;
        var preset = upgraded.Deserialize(ContractsJsonContext.Default.Preset)!;

        Assert.Equal(2, preset.SchemaVersion);
        Assert.Equal(1, preset.Blocks[0].Channels["A"]["powertube"]); // 5881 → 6L6GC
        Assert.Equal(6, preset.Blocks[0].Channels["A"]["gain"]);
        Assert.Equal(7, preset.Blocks[0].Channels["B"]["powertube"]); // 6550 stays
        Assert.Empty(preset.Blocks[0].Channels["C"]);
        Assert.Equal(8, preset.Blocks[1].Channels["A"]["powertube"]); // not an amp
    }

    [Fact]
    public void Steps_run_in_order_from_the_files_schema_and_a_newer_schema_is_left_alone() {
        Func<JsonObject, JsonObject>[] steps = [
            static p => {
                p["name"] = p["title"]!.GetValue<string>(); // schema 2 renamed "title" to "name"
                p.Remove("title");
                return p;
            },
            static p => {
                p["tempo"] = p["tempo"]!.GetValue<double>() * 2; // schema 3 doubled the tempo's meaning
                return p;
            }
        ];

        var fromOne = (JsonObject)PresetMigrations.Upgrade(
            JsonNode.Parse("""{"schemaVersion":1,"title":"Old","tempo":60}"""),
            steps,
            3
        )!;
        var fromTwo = (JsonObject)PresetMigrations.Upgrade(
            JsonNode.Parse("""{"schemaVersion":2,"name":"Mid","tempo":60}"""),
            steps,
            3
        )!;
        var newer = PresetMigrations.Upgrade(JsonNode.Parse("""{"schemaVersion":4,"tempo":60}"""), steps, 3)!;

        Assert.Equal(3, fromOne["schemaVersion"]!.GetValue<int>());
        Assert.Equal("Old", fromOne["name"]!.GetValue<string>());
        Assert.Equal(120, fromOne["tempo"]!.GetValue<double>());
        Assert.Equal(120, fromTwo["tempo"]!.GetValue<double>());
        Assert.Equal(4, newer["schemaVersion"]!.GetValue<int>());
        Assert.Equal(60, newer["tempo"]!.GetValue<double>());
    }

    [Fact]
    public void A_stored_preset_passes_through_the_upgrade_when_read() {
        var folder = Directory.CreateTempSubdirectory("cathode-migrate").FullName;
        try {
            File.WriteAllText(Path.Combine(folder, "p1.json"), """{"id":"p1","name":"Stored"}""");
            var seen = 0;
            var store = new JsonFileStore<Preset>(
                folder,
                "preset",
                ContractsJsonContext.Default.Preset,
                static p => p.Id,
                node => {
                    seen++;
                    return PresetMigrations.Upgrade(node);
                }
            );

            Assert.Equal("Stored", store.Get("p1")!.Name);
            Assert.Equal(1, seen);
        } finally {
            Directory.Delete(folder, true);
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
