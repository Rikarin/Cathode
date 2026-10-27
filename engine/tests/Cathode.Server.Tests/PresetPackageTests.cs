using Cathode.Contracts;
using Cathode.Dsp.IO;
using Cathode.Testing;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

public sealed class PresetPackageTests : IAsyncLifetime {
    readonly ServerFixture source = new();
    readonly ServerFixture target = new();
    HttpClient from = null!;
    HttpClient to = null!;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        from = await source.ConnectedClientAsync();
        to = await target.ConnectedClientAsync();
    }

    public async ValueTask DisposeAsync() {
        from.Dispose();
        to.Dispose();
        await source.DisposeAsync();
        await target.DisposeAsync();
    }

    [Fact]
    public async Task A_preset_with_its_ir_moves_to_another_library_and_runs() {
        var id = await SaveRigWithCab(from, "Travel Rig");

        var package = await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token);
        var imported = await Import(to, package);
        var loaded = await to.PutAsJsonAsync($"/api/v1/live/preset/{imported.Preset.Id}", new { }, Token);

        Assert.Equal("Travel Rig", imported.Preset.Name);
        Assert.Equal(1, imported.AssetsAdded);
        Assert.False(imported.Renamed);
        Assert.True(loaded.IsSuccessStatusCode, await loaded.Content.ReadAsStringAsync(Token));
        await ServerFixture.Eventually(() => target.Graph?.SlotOf("cab1") >= 0); // the engine loaded the IR
        Assert.Single((await to.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!);
    }

    [Fact]
    public async Task A_preset_with_an_ir_pack_moves_to_another_library() {
        await using var pack = new MemoryStream();
        await using (var zip = new ZipArchive(pack, ZipArchiveMode.Create, true)) {
            await using (var wav = await zip.CreateEntry("a.wav").OpenAsync(Token)) {
                WavFile.Write(wav, new(48_000, [Signals.Noise(256, 5)]));
            }

            await using var manifest = new StreamWriter(await zip.CreateEntry("manifest.json").OpenAsync(Token));
            await manifest.WriteAsync("""{ "irs": [{ "position": 0, "distance": 1, "file": "a.wav" }] }""");
        }

        using var upload = await from.PostAsync(
            "/api/v1/assets?name=my-412.cabgrid",
            new ByteArrayContent(pack.ToArray()),
            Token
        );
        var asset = (await upload.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
        await Ensure(from.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        await Ensure(from.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("cab", 0, 2), Token));
        await Ensure(from.PutAsJsonAsync("/api/v1/live/blocks/cab1/assets/slot2", new AssetRequest(asset.Ref), Token));
        using var saved = await from.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("Pack Rig", true), Token);
        var id = (await saved.Content.ReadFromJsonAsync<LiveState>(Token))!.Preset.Id;

        var imported = await Import(to, await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token));

        Assert.Equal(1, imported.AssetsAdded);
        var stored = Assert.Single((await to.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!);
        Assert.Equal((asset.Ref, "cabgrid"), (stored.Ref, stored.Kind));
    }

    [Fact]
    public async Task Importing_into_the_same_library_never_overwrites() {
        var id = await SaveRigWithCab(from, "Original");
        var package = await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token);

        var imported = await Import(from, package);

        Assert.True(imported.Renamed);
        Assert.NotEqual(id, imported.Preset.Id);
        Assert.Equal(0, imported.AssetsAdded); // the IR was already there
    }

    [Fact]
    public async Task Tampered_assets_are_rejected_before_they_reach_the_store() {
        var id = await SaveRigWithCab(from, "Tampered");
        var package = Rewrite(
            await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token),
            static entry => entry.StartsWith("assets/", StringComparison.Ordinal),
            [1, 2, 3, 4]
        );

        var (status, body) = await TryImport(to, package);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("corrupt", body, StringComparison.Ordinal);
        Assert.Empty((await to.GetFromJsonAsync<AssetInfo[]>("/api/v1/assets", Token))!);
    }

    [Theory]
    [InlineData("../../evil.json")]
    [InlineData("assets/../preset.json")]
    [InlineData("assets/NOT-A-HASH.wav")]
    [InlineData("readme.txt")]
    [InlineData("assets/0000000000000000000000000000000000000000000000000000000000000000.json")]
    [InlineData("assets/0000000000000000000000000000000000000000000000000000000000000000.exe")]
    public async Task Unexpected_entry_names_are_rejected(string name) {
        var id = await SaveRigWithCab(from, "Sneaky");
        var package = AddEntry(
            await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token),
            name,
            [.."x"u8]
        );

        var (status, body) = await TryImport(to, package);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("unexpected files", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "name": "x", "schemaVersion": 1.5 }""")]
    [InlineData("""{ "name": "x", "blocks": [ { "id": "a", "type": 5 } ] }""")]
    [InlineData("""{ "name": "x", "blocks": {} }""")]
    public async Task A_preset_of_the_wrong_shape_is_refused_as_invalid(string json) {
        var id = await SaveRigWithCab(from, "Odd");
        var package = Rewrite(
            await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token),
            static e => e == "preset.json",
            System.Text.Encoding.UTF8.GetBytes(json)
        );

        var (status, body) = await TryImport(to, package);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("preset.json is not valid", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_entries_are_stopped_while_reading() {
        var id = await SaveRigWithCab(from, "Bomb");
        // 100 MB of zeros compresses to about 100 KB: the classic zip bomb shape.
        var package = Rewrite(
            await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token),
            static e => e == "preset.json",
            new byte[100 * 1024 * 1024]
        );

        var (status, _) = await TryImport(to, package);

        Assert.Equal((HttpStatusCode)413, status);
    }

    [Fact]
    public async Task Missing_assets_and_non_zips_are_rejected() {
        var id = await SaveRigWithCab(from, "Incomplete");
        var withoutAssets = Remove(
            await from.GetByteArrayAsync($"/api/v1/presets/{id}/export", Token),
            static e => e.StartsWith("assets/", StringComparison.Ordinal)
        );

        var (missingStatus, missingBody) = await TryImport(to, withoutAssets);
        var (garbageStatus, _) = await TryImport(to, [.."definitely not a zip"u8]);

        Assert.Equal(HttpStatusCode.BadRequest, missingStatus);
        Assert.Contains("neither in the package nor in this library", missingBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, garbageStatus);
    }

    async Task<string> SaveRigWithCab(HttpClient client, string name) {
        await using var ir = new MemoryStream();
        WavFile.Write(
            ir,
            new(48_000, [Signals.Noise(400, 3).Select(static (v, i) => v * MathF.Exp(-i / 60f)).ToArray()])
        );
        using var upload = await client.PostAsync(
            "/api/v1/assets?name=room.wav",
            new ByteArrayContent(ir.ToArray()),
            Token
        );
        var asset = (await upload.Content.ReadFromJsonAsync<AssetInfo>(Token))!;
        await Ensure(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        await Ensure(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("cab", 0, 2), Token));
        await Ensure(
            client.PutAsJsonAsync("/api/v1/live/blocks/cab1/assets/slot1", new AssetRequest(asset.Ref), Token)
        );
        using var saved = await client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(name, true), Token);
        return (await saved.Content.ReadFromJsonAsync<LiveState>(Token))!.Preset.Id;
    }

    async Task<ImportResult> Import(HttpClient client, byte[] package) {
        using var response = await client.PostAsync("/api/v1/presets/import", new ByteArrayContent(package), Token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ImportResult>(Token))!;
    }

    async Task<(HttpStatusCode Status, string Body)> TryImport(HttpClient client, byte[] package) {
        using var response = await client.PostAsync("/api/v1/presets/import", new ByteArrayContent(package), Token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    async Task Ensure(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    static byte[] Rewrite(byte[] package, Func<string, bool> which, byte[] content) =>
        Transform(
            package,
            which,
            (zip, name) => {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        );

    static byte[] Remove(byte[] package, Func<string, bool> which) => Transform(package, which, static (_, _) => { });

    static byte[] AddEntry(byte[] package, string name, byte[] content) {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true)) {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(content);
        }

        return buffer.ToArray();
    }

    /// <summary>Copies the package, handing entries matching <paramref name="which" /> to <paramref name="replace" />.</summary>
    static byte[] Transform(byte[] package, Func<string, bool> which, Action<ZipArchive, string> replace) {
        using var output = new MemoryStream();
        using (var original = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read)) {
            using var copy = new ZipArchive(output, ZipArchiveMode.Create, true);
            foreach (var entry in original.Entries) {
                if (which(entry.FullName)) {
                    replace(copy, entry.FullName);
                    continue;
                }

                using var source = entry.Open();
                using var target = copy.CreateEntry(entry.FullName).Open();
                source.CopyTo(target);
            }
        }

        return output.ToArray();
    }
}
