using Cathode.Contracts;
using System.Text.Json.Nodes;

namespace Cathode.Server.Stores;

/// <summary>
///     Upgrades a stored preset's JSON to <see cref="Preset.CurrentSchemaVersion" /> before it is read: library files,
///     snapshots, templates and <c>.cthpreset</c> imports all pass through here, so a preset saved by any earlier
///     Cathode since 1.0 keeps loading.
/// </summary>
/// <remarks>
///     <para>
///         Schema 1 is the format Cathode 1.0 shipped with; a preset without <c>schemaVersion</c> is schema 1. Parameter
///         values are stored sparsely, so a new parameter, block type or amp model needs no new schema: only a change
///         that would read an older file wrongly does (a parameter renamed, re-ranged or re-meaning, a field moved).
///     </para>
///     <para>
///         To make one: raise <see cref="Preset.CurrentSchemaVersion" />, append to <see cref="Steps" /> the step that
///         takes the previous schema's JSON to the new one, and add a test with a file of the previous schema. Steps are
///         never edited once released: each runs on presets that may be years old.
///     </para>
///     <para>
///         A preset from a newer Cathode (a schema above the current) is left as it is: loading or importing it is
///         refused with a message that says so, rather than reading it wrongly.
///     </para>
/// </remarks>
public static class PresetMigrations {
    const string VersionField = "schemaVersion";

    /// <summary>The upgrade steps: <c>Steps[n]</c> takes schema <c>n + 1</c> to <c>n + 2</c>.</summary>
    static readonly Func<JsonObject, JsonObject>[] Steps = [ToSchema2];

    /// <summary>The Amp's Power Tube value that was 5881 up to 1.0.0-rc.3 (schema 1).</summary>
    const float Schema1FiveEightEightOne = 8;

    /// <summary>The Amp's Power Tube value for the 6L6GC, which a 5881 is electrically.</summary>
    const float SixL6GC = 1;

    /// <summary>
    ///     Schema 2 (after 1.0.0-rc.3) drops the Amp's Power Tube choice 5881, which was the 6L6GC under another name:
    ///     every Amp channel set to it becomes 6L6GC, so it sounds the same.
    /// </summary>
    static JsonObject ToSchema2(JsonObject preset) {
        foreach (var block in preset["blocks"]?.AsArray() ?? []) {
            if (block?["type"]?.GetValue<string>() == "circuitamp" && block["channels"] is JsonObject channels) {
                foreach (var (_, channel) in channels) {
                    UpgradePowerTube(channel as JsonObject);
                }
            }
        }

        return preset;
    }

    /// <summary>
    ///     An Amp channel's values with a Power Tube of 5881 (up to 1.0.0-rc.3) set to the 6L6GC. Formats without a
    ///     schema version (block presets, global blocks) call it on every read; no current value is 8, so it is safe to
    ///     repeat.
    /// </summary>
    public static void UpgradePowerTube(JsonObject? channel) {
        if (channel?["powertube"] is JsonValue value && value.GetValue<float>() == Schema1FiveEightEightOne) {
            channel["powertube"] = SixL6GC;
        }
    }

    /// <summary>As <see cref="UpgradePowerTube(JsonObject?)" />, for values already read.</summary>
    public static void UpgradePowerTube(Dictionary<string, float>? channel) {
        if (channel is not null && channel.TryGetValue("powertube", out var tube) && tube == Schema1FiveEightEightOne) {
            channel["powertube"] = SixL6GC;
        }
    }

    /// <summary>The preset JSON at <paramref name="node" /> upgraded to the current schema.</summary>
    public static JsonNode? Upgrade(JsonNode? node) => Upgrade(node, Steps, Preset.CurrentSchemaVersion);

    /// <summary>
    ///     <paramref name="node" /> taken through <paramref name="steps" /> to schema <paramref name="current" />; one
    ///     from a newer schema, or that is not an object, comes back as it is.
    /// </summary>
    public static JsonNode? Upgrade(JsonNode? node, IReadOnlyList<Func<JsonObject, JsonObject>> steps, int current) {
        if (node is not JsonObject preset) {
            return node;
        }

        var version = preset[VersionField]?.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? preset[VersionField]!.GetValue<int>()
            : 1;
        if (version >= current || version < 1) {
            return preset;
        }

        for (var from = version; from < current; from++) {
            preset = steps[from - 1](preset);
            preset[VersionField] = from + 1;
        }

        return preset;
    }

    /// <summary>A snapshot's JSON with its preset upgraded.</summary>
    public static JsonNode? UpgradeSnapshot(JsonNode? node) {
        if (node is JsonObject snapshot && snapshot["preset"] is { } preset) {
            snapshot["preset"] = Upgrade(preset.DeepClone());
        }

        return node;
    }
}
