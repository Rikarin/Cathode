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
}
