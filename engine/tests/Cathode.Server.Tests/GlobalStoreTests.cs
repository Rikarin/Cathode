using Cathode.Server.Stores;
using Microsoft.Extensions.Options;

namespace Cathode.Server.Tests;

/// <summary>The global settings file as earlier releases wrote it.</summary>
public sealed class GlobalStoreTests : IDisposable {
    readonly string folder = Directory.CreateTempSubdirectory("cathode-global-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void An_update_replaces_the_file_while_a_reader_holds_it() {
        var store = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder }));
        store.Update(static g => g.Tempo = 100);
        var path = Path.Combine(folder, "global.json");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            store.Update(static g => g.Tempo = 120); // a plain File.Move is refused on Windows while it is open
        }

        Assert.Equal(120, new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get().Tempo);
        Assert.Equal(["global.json"], Directory.EnumerateFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Live_changes_are_read_at_once_and_written_and_announced_once_for_a_burst() {
        var path = Path.Combine(folder, "global.json");
        using var store = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })) {
            LiveSaveDelay = TimeSpan.FromMilliseconds(400)
        };
        store.Update(static g => g.Tempo = 100);
        var written = File.GetLastWriteTimeUtc(path);
        var (_, before) = store.Versioned();
        var announced = new List<float>();
        store.Changed += g => {
            lock (announced) {
                announced.Add(g.Manual[1]);
            }
        };

        // A pedal on Manual B: a hundred messages, well inside the delay.
        for (var step = 1; step <= 100; step++) {
            var value = step / 100f;
            store.UpdateLive(g => g.Manual[1] = value);
        }

        var (now, revision) = store.Versioned();
        Assert.Equal(1f, now.Manual[1]);
        Assert.Equal(before + 100, revision); // a stale PUT /global is still refused
        lock (announced) {
            Assert.Empty(announced);
        }

        Assert.Equal(written, File.GetLastWriteTimeUtc(path)); // nothing written per message

        await ServerFixture.Eventually(() => {
                lock (announced) {
                    return announced.Count > 0;
                }
            }
        );
        Assert.Equal([1f], announced);
        using var reread = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder }));
        Assert.Equal(1f, reread.Get().Manual[1]);
    }

    [Fact]
    public void A_live_change_still_waiting_is_written_when_the_store_is_disposed() {
        var options = Options.Create(new ServerOptions { DataDirectory = folder });
        using (var store = new GlobalStore(options) { LiveSaveDelay = TimeSpan.FromMinutes(10) }) {
            store.UpdateLive(static g => g.Manual[3] = 0.25f);
        }

        using var reread = new GlobalStore(options);
        Assert.Equal(0.25f, reread.Get().Manual[3]);
    }

    [Fact]
    public void A_durable_update_writes_the_live_changes_before_it_and_announces_them_once() {
        var options = Options.Create(new ServerOptions { DataDirectory = folder });
        using var store = new GlobalStore(options) { LiveSaveDelay = TimeSpan.FromMinutes(10) };
        var announced = 0;
        store.Changed += _ => announced++;
        store.UpdateLive(static g => g.Manual[0] = 0.5f);
        store.Update(static g => g.Metronome = true);

        Assert.Equal(1, announced);
        using var reread = new GlobalStore(options);
        Assert.Equal(0.5f, reread.Get().Manual[0]);
        Assert.True(reread.Get().Metronome);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2.5 }""")]
    [InlineData("""{ "blocks": {} }""")]
    [InlineData("""{ "blocks": [ { "type": 5 } ] }""")]
    [InlineData("""{ "tempo": "fast" }""")]
    [InlineData("not json")]
    public void A_damaged_file_falls_back_to_the_defaults_and_is_kept(string json) {
        File.WriteAllText(Path.Combine(folder, "global.json"), json);

        var settings = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get();

        Assert.Equal(120, settings.Tempo);
        Assert.Equal(json, File.ReadAllText(Path.Combine(folder, "global.json.damaged")));
    }

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
              { "id": "g1", "type": "circuitamp",
                "channels": { "A": { "powertube": 8, "gain": 6, "mvcap": 50 }, "B": { "powertube": 2 } } },
              { "id": "g2", "type": "drive", "channels": { "A": { "powertube": 8 } } },
              { "id": "g3", "type": "circuitamp", "channels": { "A": { "type": 86 } } }
            ] }
            """
        );

        var settings = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get();

        Assert.Equal(1, settings.Blocks[0].Channels["A"]["powertube"]);
        Assert.Equal(6, settings.Blocks[0].Channels["A"]["gain"]);
        Assert.False(settings.Blocks[0].Channels["A"].ContainsKey("mvcap"));
        Assert.Equal(10, settings.Blocks[0].Channels["A"]["master"]); // the default up to 1.0.0-rc.3
        Assert.Equal(0, settings.Blocks[0].Channels["D"]["speaker"]);
        Assert.Equal(2, settings.SchemaVersion);
        Assert.Equal(2, settings.Blocks[0].Channels["B"]["powertube"]);
        Assert.Equal(8, settings.Blocks[1].Channels["A"]["powertube"]);
        Assert.Equal(5, settings.Blocks[2].Channels["A"]["control1"]); // the Quick Rod's Solo Boost up to rc.3
    }

    [Fact]
    public void A_schema_2_global_amp_keeps_the_new_defaults() {
        File.WriteAllText(
            Path.Combine(folder, "global.json"),
            """
            { "schemaVersion": 2,
              "blocks": [ { "id": "g1", "type": "circuitamp", "channels": { "A": { "gain": 6 } } } ] }
            """
        );

        var settings = new GlobalStore(Options.Create(new ServerOptions { DataDirectory = folder })).Get();

        Assert.Equal(["gain"], settings.Blocks[0].Channels["A"].Keys);
    }
}
