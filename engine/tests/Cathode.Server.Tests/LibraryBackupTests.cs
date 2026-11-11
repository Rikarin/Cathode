using Cathode.Contracts;
using Cathode.Dsp.IO;
using Cathode.Server.Stores;
using Cathode.Testing;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Cathode.Server.Tests;

/// <summary>The whole library as one file: <c>GET /library/backup</c> and <c>POST /library/restore</c>.</summary>
public sealed class LibraryBackupTests : IAsyncLifetime {
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
    public async Task A_whole_library_moves_to_another_server() {
        var rig = await SaveRigWithCab(from, "Travel Rig");
        var asset = Assert.Single((await from.UserAssetsAsync(Token)));
        await Ensure(
            from.PatchAsJsonAsync(
                $"/api/v1/assets/{asset.Ref}/metadata",
                new AssetMetadataRequest("My room.wav", "red", true),
                Token
            )
        );
        await Ensure(from.PostAsJsonAsync("/api/v1/live/snapshots", new SnapshotRequest("Before the gig"), Token));
        await Ensure(from.PostAsJsonAsync("/api/v1/live/templates", new TemplateRequest("Two cabs"), Token));
        var blockPreset = new BlockPreset { Type = "chorus", Name = "Wide", Folder = "Mine" };
        await Ensure(from.PutAsJsonAsync($"/api/v1/block-presets/{blockPreset.Id}", blockPreset, Token));
        var bank = new Bank { Name = "Gig", Slots = [rig, null] };
        await Ensure(from.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));
        var setlist = new Setlist { Name = "Friday", Entries = [new(rig, 2, "Opener")] };
        await Ensure(from.PutAsJsonAsync($"/api/v1/setlists/{setlist.Id}", setlist, Token));
        var factory = (await from.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!
            .First(static p => p.Factory == true)
            .Id;
        await Ensure(
            from.PatchAsJsonAsync(
                $"/api/v1/presets/{factory}/metadata",
                new PresetMetadataRequest(Color: "blue", Favorite: true),
                Token
            )
        );
        var global = (await from.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.Tempo = 97;
        global.StartupPreset = rig;
        await Ensure(from.PutAsJsonAsync("/api/v1/global", global, Token));

        using var download = await from.GetAsync("/api/v1/library/backup", Token);
        var result = await Restore(to, await download.Content.ReadAsByteArrayAsync(Token));

        Assert.EndsWith(".cthbackup", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(
            (1, 1, 1, 1, 1, 1, 1, true),
            (result.Presets, result.BlockPresets, result.Templates, result.Snapshots, result.Setlists, result.Banks,
                result.Assets, result.Settings)
        );
        Assert.Empty(result.Skipped);
        var presets = (await to.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!;
        Assert.Equal("Travel Rig", presets.Single(p => p.Id == rig).Name);
        Assert.Equal(
            ("blue", true),
            (presets.Single(p => p.Id == factory).Color, presets.Single(p => p.Id == factory).Favorite)
        );
        var restored = Assert.Single((await to.UserAssetsAsync(Token)));
        Assert.Equal(
            (asset.Ref, "My room.wav", "red", true),
            (restored.Ref, restored.Name, restored.Color, restored.Favorite)
        );
        Assert.Equal(
            "Before the gig",
            Assert.Single((await to.GetFromJsonAsync<SnapshotSummary[]>("/api/v1/snapshots", Token))!).Name
        );
        Assert.Equal(
            "Two cabs",
            Assert.Single((await to.GetFromJsonAsync<PresetSummary[]>("/api/v1/templates", Token))!).Name
        );
        var block = (await to.GetFromJsonAsync<BlockPreset[]>("/api/v1/block-presets?type=chorus", Token))!
            .Single(p => p.Id == blockPreset.Id);
        Assert.Equal(("Wide", "Mine"), (block.Name, block.Folder));
        Assert.Equal(
            [rig, null],
            (await to.GetFromJsonAsync<Bank[]>("/api/v1/banks", Token))!.Single(b => b.Id == bank.Id).Slots
        );
        Assert.Equal(
            new SetlistEntry(rig, 2, "Opener"),
            Assert.Single((await to.GetFromJsonAsync<Setlist>($"/api/v1/setlists/{setlist.Id}", Token))!.Entries)
        );
        var settings = (await to.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        Assert.Equal((97, rig), (settings.Tempo, settings.StartupPreset));
        // The restored preset runs, with its IR.
        await Ensure(to.PutAsJsonAsync($"/api/v1/live/preset/{rig}", new { }, Token));
        await ServerFixture.Eventually(() => target.Graph?.SlotOf("cab1") >= 0);
    }

    [Fact]
    public async Task A_restore_replaces_entries_with_the_same_id_and_keeps_the_rest() {
        var id = await SaveRig(from, "First take");
        var backup = await from.GetByteArrayAsync("/api/v1/library/backup", Token);
        await Ensure(
            from.PatchAsJsonAsync($"/api/v1/presets/{id}/metadata", new PresetMetadataRequest("Second take"), Token)
        );
        var later = await SaveRig(from, "Made later");

        var result = await Restore(from, backup);

        var presets = (await from.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!;
        Assert.Equal(1, result.Presets);
        Assert.Equal("First take", presets.Single(p => p.Id == id).Name);
        Assert.Contains(presets, p => p.Id == later);
    }

    [Fact]
    public async Task What_a_store_refuses_is_left_out_with_the_reason_and_the_rest_is_restored() {
        var id = await SaveRig(from, "Kept");
        var bank = new Bank { Name = "Gig", Slots = [id] };
        await Ensure(from.PutAsJsonAsync($"/api/v1/banks/{bank.Id}", bank, Token));
        var backup = Rewrite(
            await from.GetByteArrayAsync("/api/v1/library/backup", Token),
            $"banks/{bank.Id}.json",
            """{ "name": "Gig", "slots": ["gone"] }"""
        );

        var result = await Restore(to, backup);

        Assert.Equal((1, 0), (result.Presets, result.Banks));
        Assert.Contains("Bank 'Gig'", Assert.Single(result.Skipped), StringComparison.Ordinal);
        Assert.Contains("no preset 'gone'", result.Skipped[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backup_from_a_newer_Cathode_is_refused_and_changes_nothing() {
        var id = await SaveRig(from, "From the future");
        var backup = Rewrite(
            await from.GetByteArrayAsync("/api/v1/library/backup", Token),
            $"presets/{id}.json",
            """{ "name": "From the future", "schemaVersion": 9999 }"""
        );

        var (status, body) = await TryRestore(to, backup);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("newer version", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            (await to.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!,
            static p => p.Factory != true
        );
    }

    [Theory]
    [InlineData("../../evil.json")]
    [InlineData("presets/../global.json")]
    [InlineData("presets/a.b.json")]
    [InlineData("presets/sub/a.json")]
    [InlineData("assets/index.json")]
    [InlineData("assets/0000000000000000000000000000000000000000000000000000000000000000.exe")]
    [InlineData("readme.txt")]
    public async Task Unexpected_entry_names_are_refused(string name) {
        await SaveRig(from, "Sneaky");
        var backup = AddEntry(await from.GetByteArrayAsync("/api/v1/library/backup", Token), name, "x");

        var (status, body) = await TryRestore(to, backup);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("unexpected files", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_assets_and_files_that_are_no_backup_are_refused() {
        await SaveRigWithCab(from, "Tampered");
        var tampered = Transform(
            await from.GetByteArrayAsync("/api/v1/library/backup", Token),
            static name => name.StartsWith("assets/", StringComparison.Ordinal),
            static _ => [1, 2, 3, 4]
        );
        var package = await from.GetByteArrayAsync("/api/v1/live/export", Token);

        var (tamperedStatus, tamperedBody) = await TryRestore(to, tampered);
        var (packageStatus, _) = await TryRestore(to, package); // a .cthpreset: its preset.json is not a backup's
        var (garbageStatus, _) = await TryRestore(to, [.."definitely not a zip"u8]);

        Assert.Equal(HttpStatusCode.BadRequest, tamperedStatus);
        Assert.Contains("corrupt", tamperedBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, packageStatus);
        Assert.Equal(HttpStatusCode.BadRequest, garbageStatus);
        Assert.Empty((await to.UserAssetsAsync(Token)));
        Assert.DoesNotContain(
            (await to.GetFromJsonAsync<PresetSummary[]>("/api/v1/presets", Token))!,
            static p => p.Factory != true
        );
    }

    [Fact]
    public void The_backup_extension_is_not_a_preset_package() =>
        Assert.NotEqual(PresetPackage.Extension, LibraryBackup.Extension);

    async Task<string> SaveRig(HttpClient client, string name) {
        await Ensure(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
        await Ensure(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("chorus", 0, 2), Token));
        using var saved = await client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest(name, true), Token);
        return (await saved.Content.ReadFromJsonAsync<LiveState>(Token))!.Preset.Id;
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

    async Task<RestoreResult> Restore(HttpClient client, byte[] backup) {
        using var response = await client.PostAsync("/api/v1/library/restore", new ByteArrayContent(backup), Token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<RestoreResult>(Token))!;
    }

    async Task<(HttpStatusCode Status, string Body)> TryRestore(HttpClient client, byte[] backup) {
        using var response = await client.PostAsync("/api/v1/library/restore", new ByteArrayContent(backup), Token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    async Task Ensure(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    static byte[] Rewrite(byte[] backup, string entry, string json) {
        var found = false;
        var rewritten = Transform(
            backup,
            name => name == entry && (found = true),
            _ => Encoding.UTF8.GetBytes(json)
        );
        Assert.True(found, $"The backup has no {entry}.");
        return rewritten;
    }

    static byte[] AddEntry(byte[] backup, string name, string content) {
        using var buffer = new MemoryStream();
        buffer.Write(backup);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, true)) {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return buffer.ToArray();
    }

    /// <summary>Copies the backup, with the content of entries matching <paramref name="which" /> replaced.</summary>
    static byte[] Transform(byte[] backup, Func<string, bool> which, Func<string, byte[]> replacement) {
        using var output = new MemoryStream();
        using (var original = new ZipArchive(new MemoryStream(backup), ZipArchiveMode.Read)) {
            using var copy = new ZipArchive(output, ZipArchiveMode.Create, true);
            foreach (var entry in original.Entries) {
                using var target = copy.CreateEntry(entry.FullName).Open();
                if (which(entry.FullName)) {
                    target.Write(replacement(entry.FullName));
                } else {
                    using var content = entry.Open();
                    content.CopyTo(target);
                }
            }
        }

        return output.ToArray();
    }
}
