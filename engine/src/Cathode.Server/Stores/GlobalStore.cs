using Cathode.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cathode.Server.Stores;

/// <summary>
///     The global settings document (<c>&lt;data&gt;/global.json</c>), written atomically. Readers get a copy;
///     <see cref="Update" /> applies a change under a lock and raises <see cref="Changed" /> with the result. Every
///     change takes a new revision (an in-memory counter, like the live rig's), so a client that replaces the whole
///     document can say which one it changed (<see cref="UpdateIf" />).
/// </summary>
/// <remarks>
///     A value that changes many times a second while a pedal or a knob moves (Manual A-D, a linked global block's
///     parameters) is changed with <see cref="UpdateLive" />: in memory at once, on the disk and to the listeners of
///     <see cref="Changed" /> at most once per <see cref="LiveSaveDelay" />, and when the store is disposed.
/// </remarks>
public sealed class GlobalStore : IDisposable {
    readonly string path;
    readonly ILogger? logger;
    readonly Lock gate = new();

    /// <summary>Held for a write of the file, so the file only ever moves on to a later revision.</summary>
    readonly Lock writing = new();

    readonly Timer saver;
    GlobalSettings settings;
    long revision;

    /// <summary>The revision the file holds (under <see cref="writing" />).</summary>
    long written;

    /// <summary>Whether a live change waits for the timer to save and announce it (under <see cref="gate" />).</summary>
    bool unsaved;

    bool disposed;

    public GlobalStore(IOptions<ServerOptions> options, ILogger<GlobalStore>? logger = null) {
        Directory.CreateDirectory(options.Value.DataDirectory);
        path = Path.Combine(options.Value.DataDirectory, "global.json");
        this.logger = logger;
        settings = Read(logger) ?? new();
        saver = new(_ => SaveLive(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     How long after the first live change (<see cref="UpdateLive" />) it and those that followed are written and
    ///     announced.
    /// </summary>
    public TimeSpan LiveSaveDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public GlobalSettings Get() {
        lock (gate) {
            return Copy(settings);
        }
    }

    /// <summary>A copy of the settings and their revision, read together.</summary>
    public (GlobalSettings Settings, long Revision) Versioned() {
        lock (gate) {
            return (Copy(settings), revision);
        }
    }

    public GlobalSettings Update(Action<GlobalSettings> change) => UpdateIf(null, change)!.Value.Settings;

    /// <summary>
    ///     Applies <paramref name="change" /> when the settings are still at revision <paramref name="expected" /> (or
    ///     whatever their revision, when null); returns the result and its revision, or null (changing nothing) when
    ///     another change came first. The file is written before this returns.
    /// </summary>
    public (GlobalSettings Settings, long Revision)? UpdateIf(long? expected, Action<GlobalSettings> change) {
        (GlobalSettings Settings, long Revision) result;
        lock (gate) {
            if (expected is { } wanted && wanted != revision) {
                return null;
            }

            var next = Copy(settings);
            change(next);
            Write(JsonSerializer.Serialize(next, ContractsJsonContext.Default.GlobalSettings), revision + 1);
            settings = next;
            revision++;
            unsaved = false; // this write and this Changed carry the live changes before it too
            result = (Copy(next), revision);
        }

        Changed?.Invoke(result.Settings);
        return result;
    }

    /// <summary>
    ///     Applies a change to a value that moves continuously (a pedal on Manual A-D, a knob of a linked global
    ///     block). Readers see it at once, under a new revision; the file is written and <see cref="Changed" /> raised
    ///     <see cref="LiveSaveDelay" /> after the first such change, once for all of them up to then. The caller sends
    ///     the engine what changed itself.
    /// </summary>
    public void UpdateLive(Action<GlobalSettings> change) {
        lock (gate) {
            var next = Copy(settings);
            change(next);
            settings = next;
            revision++;
            if (!unsaved && !disposed) {
                unsaved = true;
                saver.Change(LiveSaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Writes the live changes that wait, if any, and stops the timer.</summary>
    public void Dispose() {
        lock (gate) {
            if (disposed) {
                return;
            }

            disposed = true;
        }

        saver.Dispose();
        Save();
    }

    /// <summary>The timer's turn: writes the live changes made since it was set and announces them.</summary>
    void SaveLive() {
        lock (gate) {
            if (!unsaved || disposed) {
                return; // a durable update wrote and announced them meanwhile
            }

            unsaved = false;
        }

        var current = Save();
        try {
            Changed?.Invoke(current);
        } catch (Exception e) when (e is not OutOfMemoryException) {
            // On the timer's thread nobody can be told; an exception left here would stop the Server.
            if (logger is not null) {
                StoreLog.GlobalChangeFailed(logger, e.Message);
            }
        }
    }

    /// <summary>
    ///     Writes the settings as they are now, outside the lock readers take, and returns a copy of them. A failed
    ///     write is logged; the next change or the shutdown writes everything again.
    /// </summary>
    GlobalSettings Save() {
        GlobalSettings current;
        string json;
        long at;
        lock (gate) {
            current = Copy(settings);
            json = JsonSerializer.Serialize(settings, ContractsJsonContext.Default.GlobalSettings);
            at = revision;
        }

        try {
            Write(json, at);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            if (logger is not null) {
                StoreLog.GlobalSaveFailed(logger, path, e.Message);
            }
        }

        return current;
    }

    /// <summary>Writes the file as of revision <paramref name="at" />, unless it already holds a later one.</summary>
    void Write(string json, long at) {
        lock (writing) {
            if (at > written) {
                AtomicRename.Write(path, json);
                written = at;
            }
        }
    }

    GlobalSettings? Read(ILogger? logger) {
        if (!File.Exists(path)) {
            return null;
        }

        try {
            var stored = UpgradeJson(JsonNode.Parse(File.ReadAllText(path)))
                ?.Deserialize(ContractsJsonContext.Default.GlobalSettings);
            if (stored is not null) {
                // Valid JSON of the right shape can still hold "eq": null or an id that is no id (a hand edit).
                GlobalShape.RepairStored(stored);
            }

            return Upgrade(stored);
        } catch (Exception e) when (StoreLog.IsDamage(e)) {
            // A damaged file falls back to defaults rather than stopping the Server; it is kept beside them.
            if (logger is not null) {
                StoreLog.Unreadable(logger, "global settings", path, e.Message);
            }

            try {
                File.Copy(path, path + ".damaged", true);
            } catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) {
                // the defaults still apply
            }

            return null;
        }
    }

    /// <summary>
    ///     The settings file's JSON taken to <see cref="GlobalSettings.CurrentSchemaVersion" />. Schema 2 (after
    ///     1.0.0-rc.3): a global Amp block as a preset's (<see cref="PresetMigrations.UpgradeAmpBlock" />). A file
    ///     without a version is schema 1.
    /// </summary>
    public static JsonNode? UpgradeJson(JsonNode? node) {
        if (node is not JsonObject settings
            || PresetMigrations.VersionOf(settings) is not { } version
            || version >= GlobalSettings.CurrentSchemaVersion) {
            return node;
        }

        foreach (var amp in PresetMigrations.Amps(settings["blocks"])) {
            PresetMigrations.UpgradeAmpBlock(amp);
        }

        settings["schemaVersion"] = GlobalSettings.CurrentSchemaVersion;
        return settings;
    }

    /// <summary>
    ///     Settings saved by an earlier Cathode, brought up to date: the output mode <c>dualMono</c> (up to
    ///     1.0.0-rc.3) did what <c>copyLR</c> does and is now that.
    /// </summary>
    public static GlobalSettings? Upgrade(GlobalSettings? settings) {
        foreach (var output in settings?.Outputs ?? []) {
            if (output.Mode == "dualMono") {
                output.Mode = "copyLR";
            }
        }

        return settings;
    }

    static GlobalSettings Copy(GlobalSettings value) =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(value, ContractsJsonContext.Default.GlobalSettings),
            ContractsJsonContext.Default.GlobalSettings
        )!;

    /// <summary>
    ///     Raised after every change, outside the lock; for live changes (<see cref="UpdateLive" />) once per
    ///     <see cref="LiveSaveDelay" />, from a timer's thread.
    /// </summary>
    public event Action<GlobalSettings>? Changed;
}

static partial class StoreLog {
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the global settings to {Path}: {Error}")]
    public static partial void GlobalSaveFailed(ILogger logger, string path, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Applying changed global settings failed: {Error}")]
    public static partial void GlobalChangeFailed(ILogger logger, string error);
}
