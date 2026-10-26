using Cathode.Server.Stores;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Tests;

/// <summary>The global settings file as earlier releases wrote it.</summary>
public sealed class GlobalStoreTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-global-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Dual_mono_from_an_earlier_release_reads_as_copy_left_to_right() {
        File.WriteAllText(
            Path.Combine(folder, "global.json"),
            """{ "outputs": [ { "mode": "dualMono" }, { "mode": "sum" }, {}, { "mode": "dualMono" } ] }"""
        );

        var settings = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get();

        Assert.Equal(["copyLR", "sum", "stereo", "copyLR"], settings.Outputs.Select(static o => o.Mode));
    }

    [Fact]
    public void A_global_amp_with_the_5881_from_an_earlier_release_reads_as_the_6L6GC() {
        File.WriteAllText(
            Path.Combine(folder, "global.json"),
            """
            { "blocks": [
              { "id": "g1", "type": "circuitamp", "channels": { "A": { "powertube": 8, "gain": 6, "mvcap": 50 }, "B": { "powertube": 2 } } },
              { "id": "g2", "type": "drive", "channels": { "A": { "powertube": 8 } } }
            ] }
            """
        );

        var settings = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get();

        Assert.Equal(1, settings.Blocks[0].Channels["A"]["powertube"]);
        Assert.Equal(6, settings.Blocks[0].Channels["A"]["gain"]);
        Assert.False(settings.Blocks[0].Channels["A"].ContainsKey("mvcap"));
        Assert.Equal(2, settings.Blocks[0].Channels["B"]["powertube"]);
        Assert.Equal(8, settings.Blocks[1].Channels["A"]["powertube"]);
    }
}
