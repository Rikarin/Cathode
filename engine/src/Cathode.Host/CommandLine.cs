using System.Globalization;

namespace Cathode.Host;

/// <summary>
///     Minimal parser: the first <c>--name</c> is the command; later <c>--key value</c> pairs and bare flags are
///     options.
/// </summary>
sealed class CommandLine {
    static readonly HashSet<string> Commands =
        ["version", "self-test", "devices", "audio-test", "latency-test", "bench-nam", "help"];

    readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);

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
            result._options[name] = hasValue ? args[++i] : null;
        }

        return result;
    }

    public bool Flag(string name) => _options.ContainsKey(name);

    public string? Text(string name) => _options.GetValueOrDefault(name);

    public int Int(string name, int fallback) =>
        _options.TryGetValue(name, out var v) && v is not null ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) =>
        _options.TryGetValue(name, out var v) && v is not null
            ? double.Parse(v, CultureInfo.InvariantCulture)
            : fallback;
}
