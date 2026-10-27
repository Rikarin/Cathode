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
///         Schema 1 is the format Cathode 1.0 shipped with; a preset without <c>schemaVersion</c> is schema 1.
///         Parameter values are stored sparsely, so a new parameter, block type or amp model needs no new schema: only
///         a change that would read an older file wrongly does (a parameter renamed, re-ranged or re-meaning, a field
///         moved).
///     </para>
///     <para>
///         To make one: raise <see cref="Preset.CurrentSchemaVersion" />, append to <see cref="Steps" /> the step that
///         takes the previous schema's JSON to the new one, and add a test with a file of the previous schema. Steps
///         are never edited once released: each runs on presets that may be years old.
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

    /// <summary>The Amp's parameters removed after 1.0.0-rc.3: no model had the parts they scaled.</summary>
    static readonly string[] RemovedAmpParams = ["t1plate", "mvcap"];

    /// <summary>
    ///     The Amp's defaults that changed after 1.0.0-rc.3, with their schema-1 values: a value left at its default is
    ///     not stored, so a schema-1 Amp gets them written in to sound the same (Master 10; Speaker load Stock).
    /// </summary>
    static readonly (string Id, float Value)[] Schema1AmpDefaults = [("master", 10), ("speaker", 0)];

    /// <summary>
    ///     The Amp types whose Cut became the Tone or Brilliance on the Presence knob after 1.0.0-rc.3, turning the
    ///     other way: Chieftain, Hot Cat 30 and Tiny Terror (their indices then and now).
    /// </summary>
    static readonly float[] Schema1BrillianceTypes = [27, 28, 63];

    /// <summary>The Amp's Cut knob's default in schema 1: 0, the brilliance's full track.</summary>
    const float Schema1CutDefault = 0;

    /// <summary>The top of the Amp's knobs (Cut, Presence): 10.</summary>
    const float KnobTop = 10;

    /// <summary>The Amp's Quick Rod type (its index then and now).</summary>
    public const float QuickRodType = 86;

    /// <summary>
    ///     The Quick Rod's Solo Boost (Control 1) in schema 1: the block's default, 5, as every model had; since, the
    ///     Quick Rod defaults to 0, the boost off.
    /// </summary>
    static readonly (string Id, float Value) Schema1QuickRodBoost = ("control1", 5);

    /// <summary>
    ///     Schema 2 (after 1.0.0-rc.3) changes the Amp: the Power Tube choice 5881, the 6L6GC under another name, is
    ///     gone; so are Triode 1 Plate Freq and Master Cap, which did nothing on any model; a new Amp starts at
    ///     Master 5 with its speaker load following the Cab; and the Chieftain's, Hot Cat's and Tiny Terror's Cut,
    ///     their Brilliance or Tone turned the other way, is that control on the Presence knob; the Quick Rod's Solo
    ///     Boost defaults to off rather than 5. Every Amp channel is brought over so it sounds the same
    ///     (<see cref="UpgradeAmpBlock" />).
    /// </summary>
    static JsonObject ToSchema2(JsonObject preset) {
        foreach (var amp in Amps(preset["blocks"])) {
            UpgradeAmpBlock(amp);
        }

        return preset;
    }

    /// <summary>
    ///     The Amp blocks in a stored <c>blocks</c> array. Nothing else is touched, and a document of the wrong shape
    ///     (no array, a block that is not an object, a type that is not a string) is not an error here: the reader
    ///     refuses it afterwards, as damaged.
    /// </summary>
    public static IEnumerable<JsonObject> Amps(JsonNode? blocks) =>
        (blocks as JsonArray)?.OfType<JsonObject>()
        .Where(static b => b["type"] is JsonValue type
            && type.TryGetValue<string>(out var name)
            && name == "circuitamp"
        )
        ?? [];

    /// <summary>
    ///     The schema version a stored document declares: 1 without a number; null for a number that is not a whole
    ///     one, which no step can take (the reader refuses the document as damaged).
    /// </summary>
    public static int? VersionOf(JsonObject document) {
        if (document[VersionField] is not JsonValue value
            || value.GetValueKind() != System.Text.Json.JsonValueKind.Number) {
            return 1;
        }

        return value.TryGetValue<int>(out var version) ? version : null;
    }

    /// <summary>
    ///     A schema-1 Amp block's JSON (a preset's, a global block, or a Block Library whole-block entry) brought
    ///     to schema 2: every channel, those at their defaults too, as <see cref="UpgradeAmpChannel" />.
    /// </summary>
    public static void UpgradeAmpBlock(JsonObject block) {
        if (block["channels"] is not JsonObject channels) {
            block["channels"] = channels = [];
        }

        foreach (var name in Preset.ChannelNames) {
            if (channels[name] is not JsonObject channel) {
                channels[name] = channel = [];
            }

            UpgradeAmpChannel(channel);
        }
    }

    /// <summary>
    ///     A schema-1 Amp channel's values brought to schema 2: a Power Tube of 5881 becomes the 6L6GC, the removed
    ///     parameters' values are dropped, the defaults that changed are written in at their old values, and on a
    ///     Chieftain, Hot Cat 30 or Tiny Terror the Cut (0 when absent) becomes the Presence knob's Tone or Brilliance
    ///     at 10 - Cut; a Quick Rod without a Solo Boost gets the 5 it played at. Run it once per schema-1 channel: a
    ///     schema-2 channel without a Master means Master 5.
    /// </summary>
    public static void UpgradeAmpChannel(JsonObject channel) {
        if (channel["powertube"] is JsonValue value
            && value.TryGetValue<float>(out var tube)
            && tube == Schema1FiveEightEightOne) {
            channel["powertube"] = SixL6GC;
        }

        foreach (var id in RemovedAmpParams) {
            channel.Remove(id);
        }

        foreach (var (id, old) in Schema1AmpDefaults) {
            if (!channel.ContainsKey(id)) {
                channel[id] = old;
            }
        }

        if (channel["type"] is JsonValue type
            && type.TryGetValue<float>(out var model)
            && Schema1BrillianceTypes.Contains(model)) {
            var cut = channel["cut"] is JsonValue stored && stored.TryGetValue<float>(out var c)
                ? c
                : Schema1CutDefault;
            channel["presence"] = KnobTop - Math.Clamp(cut, 0, KnobTop);
            channel.Remove("cut");
        }

        if (channel["type"] is JsonValue rod
            && rod.TryGetValue<float>(out var rodModel)
            && rodModel == QuickRodType) {
            var (id, old) = Schema1QuickRodBoost;
            if (!channel.ContainsKey(id)) {
                channel[id] = old;
            }
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

        if (VersionOf(preset) is not { } version || version >= current || version < 1) {
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
