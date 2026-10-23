using System.Globalization;

namespace Cathode.Soak;

sealed class Options {
    readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public static Options Parse(string[] args) {
        var options = new Options();
        for (var i = 0; i + 1 < args.Length; i += 2) {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) {
                options.values[args[i][2..]] = args[i + 1];
            }
        }

        return options;
    }

    public string Text(string name, string fallback) => values.GetValueOrDefault(name, fallback);

    public double Double(string name, double fallback) =>
        values.TryGetValue(name, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
