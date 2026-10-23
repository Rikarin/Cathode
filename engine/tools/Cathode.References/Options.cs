using System.Globalization;

namespace Cathode.References;

/// <summary>Command-line options: <c>--name value</c> pairs (repeatable) and bare <c>--flag</c>s.</summary>
sealed class Options {
    readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
    readonly HashSet<string> flags = new(StringComparer.Ordinal);

    public Options(IReadOnlyList<string> args, IReadOnlySet<string> flagNames) {
        for (var i = 0; i < args.Count; i++) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }

            var name = args[i][2..];
            if (flagNames.Contains(name)) {
                flags.Add(name);
                continue;
            }

            if (i + 1 >= args.Count) {
                throw new ArgumentException($"Option --{name} needs a value.");
            }

            if (!values.TryGetValue(name, out var list)) {
                values[name] = list = [];
            }

            list.Add(args[++i]);
        }
    }

    public bool Flag(string name) => flags.Contains(name);

    public string? Value(string name) => values.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> All(string name) => values.TryGetValue(name, out var list) ? list : [];

    public double? Number(string name) =>
        Value(name) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : null;

    /// <summary><c>--set gain=6 --set bright=1</c> as a dictionary.</summary>
    public Dictionary<string, double> Assignments(string name) {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in All(name)) {
            var parts = item.Split('=', 2);
            if (parts.Length != 2) {
                throw new ArgumentException($"Expected --{name} id=value, got '{item}'.");
            }

            result[parts[0]] = double.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return result;
    }
}
