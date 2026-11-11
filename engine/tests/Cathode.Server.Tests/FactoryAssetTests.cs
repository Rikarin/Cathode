using Cathode.Contracts;
using Cathode.Dsp.Cabinets;
using Cathode.Dsp.IO;
using Cathode.Server.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>
///     The factory assets (<see cref="FactoryAssets" />, issue #355): impulse responses rendered from Cathode's own
///     cab model, built into the Server and put into every library read-only. This file is also where they are
///     made: <see cref="Rendered" /> names each one's speaker, cabinet, microphone and placement, and
///     <c>CATHODE_UPDATE_FACTORY_ASSETS=1</c> writes them to <c>src/Cathode.Server/Factory/assets</c>.
/// </summary>
/// <remarks>
///     A stored file is not rewritten without that variable, even when the model has since changed: presets name an
///     asset by the hash of its bytes, so a rewritten file is another asset, and the one before it stops being a
///     factory asset in libraries that have it (it stays, as the user's).
/// </remarks>
public sealed class FactoryAssetTests {
    const double Rate = CabIrSynthesizer.ReferenceRate;
    const double Inch = 0.0254;

    /// <summary>One factory IR: a speaker in a cabinet under a microphone, by the catalogue's names.</summary>
    public sealed record Rendered(
        string Name,
        string Speaker,
        string Enclosure,
        string Mic,
        double Position,
        double Inches
    );

    /// <summary>
    ///     The set: the cabinets the factory presets' amps are usually heard through, each close-miked the usual way
    ///     (a dynamic a quarter of the way from the cap to the edge, an inch from the cloth), and two of them again
    ///     under a ribbon further back. No room.
    /// </summary>
    static readonly Rendered[] Set = [
        new(
            "Cathode 1x12 Open Alnico Blue - Dynamic 57.wav",
            "Celestion Alnico Blue",
            "1x12 Open Back",
            "Dynamic 57 (SM57)",
            0.25,
            1
        ),
        new("Cathode 2x12 Open C12N - Dynamic 57.wav", "Jensen C12N", "2x12 Open Back", "Dynamic 57 (SM57)", 0.25, 1),
        new("Cathode 2x12 Open C12N - Ribbon 121.wav", "Jensen C12N", "2x12 Open Back", "Ribbon 121 (R-121)", 0.4, 4),
        new("Cathode 4x10 Open P10R - Dynamic 57.wav", "Jensen P10R", "4x10 Open Back", "Dynamic 57 (SM57)", 0.25, 1),
        new(
            "Cathode 4x12 Closed Greenback - Dynamic 57.wav",
            "Celestion Greenback (G12M)",
            "4x12 Closed Back",
            "Dynamic 57 (SM57)",
            0.25,
            1
        ),
        new(
            "Cathode 4x12 Closed G12T-75 - Dynamic 421.wav",
            "Celestion G12T-75",
            "4x12 Closed Back",
            "Dynamic 421 (MD 421)",
            0.3,
            1
        ),
        new(
            "Cathode 4x12 Closed Vintage 30 - Dynamic 57.wav",
            "Celestion Vintage 30",
            "4x12 Closed Back",
            "Dynamic 57 (SM57)",
            0.25,
            1
        ),
        new(
            "Cathode 4x12 Closed Vintage 30 - Ribbon 121.wav",
            "Celestion Vintage 30",
            "4x12 Closed Back",
            "Ribbon 121 (R-121)",
            0.4,
            4
        )
    ];

    static string Folder => Path.Combine(FactoryFiles.Folder, "assets");

    CancellationToken Token => TestContext.Current.CancellationToken;

    static float[] Render(Rendered ir) {
        using var synthesizer = new CabIrSynthesizer(Rate);
        var taps = new float[synthesizer.Taps];
        synthesizer.Synthesize(
            new(
                CabCatalogue.Speakers.Single(s => s.Name == ir.Speaker),
                CabCatalogue.Enclosures.Single(e => e.Name == ir.Enclosure),
                CabCatalogue.Mics.Single(m => m.Name == ir.Mic),
                ir.Position,
                ir.Inches * Inch,
                0,
                true,
                0
            ),
            taps
        );
        return taps;
    }

    [Fact]
    public void The_factory_IRs_are_the_ones_this_file_describes() {
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_FACTORY_ASSETS") == "1") {
            Directory.CreateDirectory(Folder);
            foreach (var ir in Set) {
                WavFile.Write(Path.Combine(Folder, ir.Name), new((int)Rate, [Render(ir)]));
            }
        }

        var shipped = FactoryAssets.All;
        Assert.Equal(
            Set.Select(static ir => ir.Name).Order(StringComparer.Ordinal),
            shipped.Select(static a => a.Name)
        );
        foreach (var asset in shipped) {
            Assert.Equal("ir", asset.Kind);
            using var content = new MemoryStream(asset.Content);
            var audio = WavFile.Read(content);
            Assert.Equal(
                ((int)Rate, 1, CabIrSynthesizer.ReferenceTaps),
                (audio.SampleRate, audio.Channels.Length, audio.Frames)
            );

            // What the model renders now. Close, not equal: the file was rendered once, perhaps on another CPU, and
            // the model's later refinements are small beside an IR's peak (about 0.1 to 1).
            var now = Render(Set.Single(ir => ir.Name == asset.Name));
            var stored = audio.Channels[0];
            var worst = Enumerable.Range(0, now.Length).Max(i => Math.Abs(now[i] - stored[i]));
            Assert.True(worst < 0.02, $"{asset.Name} is {worst:0.0000} away from the cab model's render.");
            Assert.True(stored.Max(Math.Abs) is > 0.01f and < 4f, $"{asset.Name} has no usable level.");
        }
    }

    [Fact]
    public async Task Every_library_has_them_and_they_cannot_be_renamed_or_deleted() {
        await using var server = new ServerFixture();
        using var client = await server.ConnectedClientAsync();

        var listed = (await client.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!;
        Assert.Equal(Set.Length, listed.Length);
        Assert.All(listed, static a => Assert.True(a.Factory));
        var asset = listed[0];
        var path = $"/api/v1/assets/{asset.Ref}";

        // It plays: an IR Player takes it like an uploaded file.
        using var content = await client.GetAsync($"{path}/content", Token);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(asset.Size, (await content.Content.ReadAsByteArrayAsync(Token)).Length);

        using var deleted = await client.DeleteAsync(path, Token);
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        using var renamed = await client.PatchAsJsonAsync(
            $"{path}/metadata",
            new AssetMetadataRequest("Mine.wav", null, null),
            Token
        );
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        // The colour tag and the favourite flag are the user's.
        using var tagged = await client.PatchAsJsonAsync(
            $"{path}/metadata",
            new AssetMetadataRequest(null, "red", true),
            Token
        );
        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        var after = (await client.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!.Single(a =>
            a.Ref == asset.Ref
        );
        Assert.Equal((asset.Name, "red", true, true), (after.Name, after.Color, after.Favorite, after.Factory == true));
    }

    [Fact]
    public async Task A_library_from_before_them_gets_them_and_keeps_its_own_assets() {
        await using var server = new ServerFixture();
        var folder = Path.Combine(server.DataDirectory, "assets");
        Directory.CreateDirectory(folder);
        // An asset an earlier Cathode shipped and this one does not: it stays, as the user's.
        var retired = new AssetInfo(
            "sha256:" + new string('a', 64),
            "Old factory.wav",
            "ir",
            4,
            DateTimeOffset.UtcNow
        ) { Factory = true };
        File.WriteAllBytes(Path.Combine(folder, new string('a', 64) + ".wav"), [1, 2, 3, 4]);
        File.WriteAllText(
            Path.Combine(folder, "index.json"),
            System.Text.Json.JsonSerializer.Serialize([retired], ContractsJsonContext.Default.AssetInfoArray)
        );

        var store = server.Services.GetRequiredService<AssetStore>();

        var listed = store.List();
        Assert.Equal(Set.Length + 1, listed.Count);
        Assert.Null(listed.Single(a => a.Ref == retired.Ref).Factory);
        Assert.True(store.Delete(retired.Ref));
        Assert.All(store.List(), a => Assert.True(File.Exists(store.PathOf(a.Ref))));
    }

    [Fact]
    public async Task A_backup_leaves_them_out() {
        await using var server = new ServerFixture();
        using var client = await server.ConnectedClientAsync();

        await using var backup = await client.GetStreamAsync("/api/v1/library/backup", Token);
        await using var copy = new MemoryStream();
        await backup.CopyToAsync(copy, Token);
        await using var zip = new System.IO.Compression.ZipArchive(copy);

        Assert.DoesNotContain(zip.Entries, static e => e.FullName.StartsWith("assets/", StringComparison.Ordinal));
    }
}

static class AssetListing {
    /// <summary>
    ///     The assets the user put into the library: the listing without the factory ones every library has, for
    ///     tests that count what an upload, an import or a restore added.
    /// </summary>
    public static async Task<AssetInfo[]> UserAssetsAsync(this HttpClient client, CancellationToken token) =>
        [..(await client.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", token))!.Where(static a => a.Factory != true)];
}
