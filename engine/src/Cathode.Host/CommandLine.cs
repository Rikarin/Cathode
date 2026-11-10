using Cathode.Engine.Graph;
using Cathode.Ipc;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Cathode.Host;

/// <summary>
///     Minimal parser: the first <c>--name</c> is the command; later <c>--key value</c> pairs and bare flags are
///     options.
/// </summary>
sealed class CommandLine {
    /// <summary>
    ///     Every command <c>Program.cs</c> dispatches on; a name missing here is parsed as an option
    ///     instead.
    /// </summary>
    public static readonly FrozenSet<string> Commands = new[] {
        "version", "self-test", "devices", "audio-test", "latency-test", "ir-capture", "bench-nam", "render", "serve",
        "help"
    }.ToFrozenSet(StringComparer.Ordinal);

    readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);

    public string? Command { get; private set; }

    public static CommandLine Parse(string[] args) {
        var result = new CommandLine();
        for (var i = 0; i < args.Length; i++) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) {
                continue;
            }

            var name = args[i][2..];
            if (result.Command is null && Commands.Contains(name)) {
                result.Command = name;
                continue;
            }

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result.options[name] = hasValue ? args[++i] : null;
        }

        return result;
    }

    public bool Flag(string name) => options.ContainsKey(name);

    public string? Text(string name) => options.GetValueOrDefault(name);

    /// <summary>
    ///     A whole number from <paramref name="min" /> to <paramref name="max" />; anything else (text, a fraction, a
    ///     value out of range or too large for an int) is a <see cref="FormatException" /> that names the option.
    /// </summary>
    public int Int(string name, int fallback, int min, int max) {
        if (!options.TryGetValue(name, out var v) || v is null) {
            return fallback;
        }

        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value >= min
            && value <= max
                ? value
                : throw new FormatException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"--{name} must be a whole number from {min} to {max}, not '{v}'."
                    )
                );
    }

    /// <summary>
    ///     A number from <paramref name="min" /> to <paramref name="max" />; anything else (text, NaN, a value out of
    ///     range) is a <see cref="FormatException" /> that names the option.
    /// </summary>
    public double Double(string name, double fallback, double min, double max) {
        if (!options.TryGetValue(name, out var v) || v is null) {
            return fallback;
        }

        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value >= min
            && value <= max
                ? value
                : throw new FormatException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"--{name} must be a number from {min} to {max}, not '{v}'."
                    )
                );
    }

    /// <summary>The sample rate to ask the device for (<c>--rate</c>, 48 kHz by default).</summary>
    public double Rate() => Double("rate", 48_000, 8_000, 384_000);

    /// <summary>
    ///     The buffer size to ask the device for (<c>--buffer</c>, 64 frames by default), up to the shim's
    ///     <c>CTH_MAX_FRAMES</c>.
    /// </summary>
    public int Buffer() => Int("buffer", 64, 16, 4096);

    /// <summary>
    ///     Reads a graph file (<c>--graph</c>). A file that is not a graph is a <see cref="FormatException" /> that
    ///     names it; a missing or unreadable one is the <see cref="IOException" /> the file system gives.
    /// </summary>
    public static GraphDefinition ReadGraph(string path) {
        try {
            return JsonSerializer.Deserialize(File.ReadAllText(path), GraphJsonContext.Default.GraphDefinition)
                ?? throw new FormatException($"{path} holds no graph.");
        } catch (JsonException e) {
            throw new FormatException($"{path} is not a graph: {e.Message}", e);
        }
    }
}
