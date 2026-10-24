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

    public JsonFileStoreTests() =>
        store = new(folder, "preset", ContractsJsonContext.Default.Preset, static p => p.Id);

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public async Task Saves_land_while_the_file_is_being_read() {
        var preset = Preset.CreateDefault();
        store.Save(preset);
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() => {
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

    [Fact]
    public void A_save_waits_out_a_short_lock_by_another_program() {
        var preset = Preset.CreateDefault();
        store.Save(preset);
        var path = Path.Combine(folder, preset.Id + ".json");
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); // no delete sharing
        _ = Task.Delay(100, TestContext.Current.CancellationToken).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);

        preset.Name = "After";
        store.Save(preset);

        Assert.Equal("After", store.Get(preset.Id)!.Name);
    }
}
