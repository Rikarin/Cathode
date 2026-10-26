using Cathode.Dsp;
using Cathode.Server.Rig;
using Cathode.Server.Stores;
using Microsoft.Extensions.Options;

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
            new BlockCatalog(BlockRegistry.CreateDefault())
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
}
