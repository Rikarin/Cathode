using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cathode.Server.Tests;

/// <summary>Block presets as earlier releases wrote them.</summary>
public sealed class BlockPresetStoreTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-block-presets-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void An_amp_block_preset_with_the_5881_from_an_earlier_release_reads_as_the_6L6GC() {
        var presets = Directory.CreateDirectory(Path.Combine(folder, "block-presets")).FullName;
        File.WriteAllText(
            Path.Combine(presets, "b1.json"),
            """
            {"id":"b1","type":"circuitamp","name":"Old","values":{"powertube":8,"gain":6,"t1plate":200},
             "channels":{"A":{"powertube":8},"B":{"powertube":3}}}
            """
        );

        var store = new BlockPresetStore(
            Options.Create(new ServerOptions { DataDirectory = folder }),
            new(BlockRegistry.CreateDefault())
        );
        var preset = store.Get("b1")!;

        Assert.Equal(1, preset.Values["powertube"]);
        Assert.Equal(6, preset.Values["gain"]);
        Assert.False(preset.Values.ContainsKey("t1plate"));
        Assert.Equal(10, preset.Values["master"]); // the default up to 1.0.0-rc.3
        Assert.Equal(10, preset.Channels!["C"]["master"]);
        Assert.Equal(1, preset.Channels!["A"]["powertube"]);
        Assert.Equal(3, preset.Channels["B"]["powertube"]);
    }

    [Fact]
    public void A_quick_rod_block_preset_from_an_earlier_release_keeps_its_solo_boost() {
        var presets = Directory.CreateDirectory(Path.Combine(folder, "block-presets")).FullName;
        File.WriteAllText(
            Path.Combine(presets, "b2.json"),
            """
            {"id":"b2","type":"circuitamp","name":"Rod","values":{"type":86,"gain":6},
             "channels":{"A":{"type":86},"B":{"type":86,"control1":7},"C":{"type":1}}}
            """
        );

        var store = new BlockPresetStore(
            Options.Create(new ServerOptions { DataDirectory = folder }),
            new(BlockRegistry.CreateDefault())
        );
        var preset = store.Get("b2")!;

        Assert.Equal(5, preset.Values["control1"]); // the block's default up to 1.0.0-rc.3
        Assert.Equal(5, preset.Channels!["A"]["control1"]);
        Assert.Equal(7, preset.Channels["B"]["control1"]);
        Assert.False(preset.Channels["C"].ContainsKey("control1"));
    }

    BlockPresetStore Store() =>
        new(Options.Create(new ServerOptions { DataDirectory = folder }), new(BlockRegistry.CreateDefault()));

    [Fact]
    public void An_imported_file_from_an_earlier_release_is_upgraded_like_a_stored_one() {
        // As 1.0.0-rc.3 exported them: no schemaVersion, and the Amp parameters and defaults of schema 1.
        var imported = Store()
            .Import(
                JsonNode.Parse(
                    """
                    {"format":"cathode-block-presets","version":1,"presets":[
                     {"id":"b1","type":"circuitamp","name":"Old","factory":false,
                      "values":{"powertube":8,"gain":6,"t1plate":200,"mvcap":1}},
                     {"id":"b2","type":"circuitamp","name":"Cat","factory":false,"values":{"type":28,"cut":7}},
                     {"id":"b3","type":"circuitamp","name":"Rod","factory":false,"values":{"type":86},
                      "channels":{"A":{"type":86},"B":{"type":86,"control1":7}}},
                     {"id":"b4","type":"chorus","name":"Slow","factory":false,"values":{"rate":1}}]}
                    """
                )
            );

        Assert.Equal(4, imported.Length);
        Assert.All(imported, static p => Assert.Equal(BlockPreset.CurrentSchemaVersion, p.SchemaVersion));
        var old = imported[0].Values;
        Assert.Equal(1, old["powertube"]); // the 5881 is the 6L6GC
        Assert.Equal(6, old["gain"]);
        Assert.False(old.ContainsKey("t1plate"));
        Assert.False(old.ContainsKey("mvcap"));
        Assert.Equal(10, old["master"]); // the defaults up to 1.0.0-rc.3
        Assert.Equal(0, old["speaker"]);
        Assert.Equal(10 - 7, imported[1].Values["presence"]); // the Hot Cat's Cut, turning the other way
        Assert.False(imported[1].Values.ContainsKey("cut"));
        Assert.Equal(5, imported[2].Values["control1"]); // the Quick Rod's Solo Boost
        Assert.Equal(7, imported[2].Channels!["B"]["control1"]);
        Assert.Equal(10, imported[2].Channels!["C"]["master"]);
        Assert.Equal(1, Assert.Single(imported[3].Values).Value);
    }

    [Fact]
    public void An_exported_file_of_this_release_is_imported_as_it_is() {
        var store = Store();
        store.Save("amp", new() { Type = "circuitamp", Name = "New", Values = { ["type"] = 28, ["cut"] = 4 } });
        var file = JsonSerializer.SerializeToNode(store.Export(["amp"]), ContractsJsonContext.Default.BlockPresetFile);

        var imported = Assert.Single(store.Import(file));

        // Not upgraded a second time: the Cut is this schema's, and Master stays at its default.
        Assert.Equal(4, imported.Values["cut"]);
        Assert.False(imported.Values.ContainsKey("presence"));
        Assert.False(imported.Values.ContainsKey("master"));
        Assert.NotEqual("amp", imported.Id);
    }

    [Fact]
    public void A_file_that_cannot_be_imported_is_refused_whole_with_a_reason() {
        var store = Store();
        string[] refused = [
            "null",
            "[]",
            """{"format":"something-else","version":1,"presets":[]}""",
            """{"format":"cathode-block-presets","version":1}""",
            """{"format":"cathode-block-presets","version":1,"presets":[null]}""",
            """{"format":"cathode-block-presets","version":1,"presets":[{"type":"chorus","name":"A","values":7}]}""",
            // The second entry is refused, so the first is not added either.
            """
            {"format":"cathode-block-presets","version":1,"presets":[
             {"schemaVersion":2,"type":"chorus","name":"A","values":{"rate":1}},
             {"schemaVersion":2,"type":"chorus","name":"B","values":{"nothing":1}}]}
            """
        ];

        foreach (var json in refused) {
            Assert.Equal(400, Assert.Throws<RigException>(() => store.Import(JsonNode.Parse(json))).Status);
        }

        var newer = Assert.Throws<RigException>(() => store.Import(
                JsonNode.Parse(
                    """
                    {"format":"cathode-block-presets","version":1,"presets":[
                     {"schemaVersion":99,"type":"chorus","name":"Future","values":{}}]}
                    """
                )
            )
        );
        Assert.Equal(409, newer.Status);
        Assert.Contains("newer version", newer.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(store.List(), static p => !p.Factory);
    }
}
