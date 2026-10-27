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
public sealed class GlobalStore {
    readonly string path;
    readonly Lock gate = new();
    GlobalSettings settings;
    long revision;

    public GlobalStore(IOptions<ServerOptions> options, ILogger<GlobalStore>? logger = null) {
        Directory.CreateDirectory(options.Value.DataDirectory);
        path = Path.Combine(options.Value.DataDirectory, "global.json");
        settings = Read(logger) ?? new();
    }

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
    ///     another change came first.
    /// </summary>
    public (GlobalSettings Settings, long Revision)? UpdateIf(long? expected, Action<GlobalSettings> change) {
        (GlobalSettings Settings, long Revision) result;
        lock (gate) {
            if (expected is { } wanted && wanted != revision) {
                return null;
            }

            var next = Copy(settings);
            change(next);
            AtomicRename.Write(path, JsonSerializer.Serialize(next, ContractsJsonContext.Default.GlobalSettings));
            settings = next;
            revision++;
            result = (Copy(next), revision);
        }

        Changed?.Invoke(result.Settings);
        return result;
    }

    GlobalSettings? Read(ILogger? logger) {
        if (!File.Exists(path)) {
            return null;
        }

        try {
            return Upgrade(
                UpgradeJson(JsonNode.Parse(File.ReadAllText(path)))
                    ?.Deserialize(ContractsJsonContext.Default.GlobalSettings)
            );
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

    /// <summary>Raised after every change, outside the lock.</summary>
    public event Action<GlobalSettings>? Changed;
}
