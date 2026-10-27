using Cathode.Contracts;
using System.Globalization;
using System.Text;

namespace Cathode.Server.Stores;

/// <summary>
///     The preset list as plain text, for printing or a set list: name, author, tags and the scene names.
/// </summary>
public static class PresetListText {
    public static string Build(PresetStore store) => Build(store.User().Select(s => store.Get(s.Id)).OfType<Preset>());

    public static string Build(IEnumerable<Preset> presets) {
        var sorted = presets.OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Cathode presets ({sorted.Count})\n");
        var number = 0;
        foreach (var preset in sorted) {
            text.Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"{++number,3}. {preset.Name}");
            if (!string.IsNullOrWhiteSpace(preset.Author)) {
                text.Append(CultureInfo.InvariantCulture, $" ({preset.Author})");
            }

            if (preset.Tags.Count > 0) {
                text.Append(CultureInfo.InvariantCulture, $" [{string.Join(", ", preset.Tags)}]");
            }

            text.Append('\n');
            for (var i = 0; i < preset.Scenes.Count; i++) {
                text.Append(CultureInfo.InvariantCulture, $"     Scene {i + 1}: {preset.Scenes[i].Name}\n");
            }
        }

        return text.ToString();
    }
}
