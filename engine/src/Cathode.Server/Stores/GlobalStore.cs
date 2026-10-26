using Cathode.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Cathode.Server.Stores;

/// <summary>
///     The global settings document (<c>&lt;data&gt;/global.json</c>), written atomically. Readers get a copy;
///     <see cref="Update" /> applies a change under a lock and raises <see cref="Changed" /> with the result.
/// </summary>
public sealed class GlobalStore {
    readonly string path;
    readonly Lock gate = new();
    GlobalSettings settings;

    public GlobalStore(IOptions<ServerOptions> options) {
        Directory.CreateDirectory(options.Value.DataDirectory);
        path = Path.Combine(options.Value.DataDirectory, "global.json");
        settings = Read() ?? new();
    }

    public GlobalSettings Get() {
        lock (gate) {
            return Copy(settings);
        }
    }

    public GlobalSettings Update(Action<GlobalSettings> change) {
        GlobalSettings result;
        lock (gate) {
            var next = Copy(settings);
            change(next);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, ContractsJsonContext.Default.GlobalSettings));
            File.Move(temporary, path, true);
            settings = next;
            result = Copy(next);
        }

        Changed?.Invoke(result);
        return result;
    }

    GlobalSettings? Read() {
        try {
            return File.Exists(path)
                ? Upgrade(
                    JsonSerializer.Deserialize(File.ReadAllText(path), ContractsJsonContext.Default.GlobalSettings)
                )
                : null;
        } catch (JsonException) {
            return null; // a damaged file falls back to defaults rather than stopping the Server
        }
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
