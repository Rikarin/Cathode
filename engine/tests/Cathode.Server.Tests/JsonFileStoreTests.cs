using Cathode.Contracts;
using Cathode.Server.Stores;

namespace Cathode.Server.Tests;

/// <summary>
///     The file store under concurrent access: a save must land while someone reads the same file (on Windows a rename
///     over an open file is refused unless the reader shares delete), or while another program holds it a moment.
/// </summary>
public sealed class JsonFileStoreTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-store-").FullName;
    readonly JsonFileStore<Preset> store;

    public JsonFileStoreTests() {
        store = new(folder, "preset", ContractsJsonContext.Default.Preset, static p => p.Id);
    }

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public async Task Saves_land_while_the_file_is_being_read() {
        var preset = Preset.CreateDefault();
        store.Save(preset);
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(
            () => {
                while (!stop.IsCancellationRequested) {
                    Assert.NotNull(store.Get(preset.Id));
                }
            },
            TestContext.Current.CancellationToken
        );

        for (var level = 1; level <= 200; level++) {
            preset.Scenes[0].Level = level % 12;
            store.Save(preset);
        }

        stop.Cancel();
        await reader;
        Assert.Equal(200 % 12, store.Get(preset.Id)!.Scenes[0].Level);
    }

    [Theory]
    [InlineData("""{ "id": "bad", "schemaVersion": 2.5 }""")]
    [InlineData("""{ "id": "bad", "schemaVersion": 1e30 }""")]
    [InlineData("""{ "id": "bad", "blocks": {} }""")]
    [InlineData("""{ "id": "bad", "blocks": [ { "id": "a", "type": 5 } ] }""")]
    [InlineData("""{ "id": "bad", "blocks": [{ "type": "circuitamp", "channels": { "A": { "powertube": "x" } } }] }""")]
    [InlineData("""[ 1, 2 ]""")]
    [InlineData("")]
    public void A_file_of_the_wrong_shape_is_skipped_and_the_rest_still_list(string json) {
        var presets = new JsonFileStore<Preset>(
            folder,
            "preset",
            ContractsJsonContext.Default.Preset,
            static p => p.Id,
            PresetMigrations.Upgrade
        );
        var good = Preset.CreateDefault();
        presets.Save(good);
        File.WriteAllText(Path.Combine(folder, "bad.json"), json);

        Assert.Equal([good.Id], presets.All().Select(static e => e.Item.Id));
        Assert.Null(presets.Get("bad"));
    }

    [Fact]
    public async Task Writers_of_one_id_never_rename_each_others_half_written_file() {
        var preset = Preset.CreateDefault();
        store.Save(preset);

        // Each writer saves its own copy: a temporary file they shared would be truncated under another's rename.
        await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(writer => Task.Run(
                        () => {
                            var copy = store.Get(preset.Id)!;
                            copy.Name = new((char)('a' + writer), 1_000);
                            for (var i = 0; i < 300; i++) {
                                store.Save(copy);
                            }
                        },
                        TestContext.Current.CancellationToken
                    )
                )
        );

        Assert.Matches(@"^([a-d])\1{999}$", store.Get(preset.Id)!.Name);
        Assert.Equal([preset.Id + ".json"], Directory.EnumerateFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_save_replaces_a_file_a_reader_holds_open_and_the_reader_keeps_the_old_content() {
        var preset = Preset.CreateDefault();
        preset.Name = "Before";
        store.Save(preset);
        var path = Path.Combine(folder, preset.Id + ".json");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        preset.Name = "After";
        store.Save(preset);

        Assert.Equal("After", store.Get(preset.Id)!.Name);
        Assert.Contains("Before", new StreamReader(reader).ReadToEnd());
    }

    [Fact]
    public void A_save_waits_out_a_short_lock_by_another_program() {
        var preset = Preset.CreateDefault();
        store.Save(preset);
        var path = Path.Combine(folder, preset.Id + ".json");
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); // no delete sharing
        // A thread of its own lets go after 100 ms: a thread-pool continuation can wait seconds on a busy runner,
        // longer than the store retries.
        var release = new Thread(() => {
                Thread.Sleep(100);
                held.Dispose();
            }
        );
        release.Start();

        preset.Name = "After";
        try {
            store.Save(preset);
        } finally {
            release.Join();
        }

        Assert.Equal("After", store.Get(preset.Id)!.Name);
    }
}
