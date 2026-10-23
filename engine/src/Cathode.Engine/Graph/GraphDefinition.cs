using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cathode.Engine.Graph;

/// <summary>Well-known node types handled by the graph compiler itself rather than by a DSP block.</summary>
public static class NodeTypes {
    public const string Input = "input";
    public const string Output = "output";
    public const string Shunt = "shunt";

    public static bool IsBuiltIn(string type) => type is Input or Output or Shunt;
}

/// <summary>
///     What a bypassed block does (docs/02-architecture.md §5.5). The two "in" modes keep the block running on a muted
///     input, so a delay or reverb bypassed by a scene change rings out instead of stopping dead.
/// </summary>
public static class BypassModes {
    /// <summary>The input passes straight through (the default).</summary>
    public const string Thru = "thru";

    /// <summary>Silence.</summary>
    public const string Mute = "mute";

    /// <summary>The block's input is muted; only its tails come out.</summary>
    public const string MuteIn = "muteIn";

    /// <summary>The dry signal passes and the block's tails are added: spillover for delays and reverbs.</summary>
    public const string MuteFxIn = "muteFxIn";

    public static bool IsValid(string mode) => mode is Thru or Mute or MuteIn or MuteFxIn;
}

/// <summary>
///     The editable grid: blocks placed in cells plus cables between adjacent columns, as on the Axe-Fx III.
///     This is the serialised form; <see cref="GraphCompiler" /> turns it into a <see cref="CompiledGraph" />.
/// </summary>
public sealed class GraphDefinition {
    public int SchemaVersion { get; set; } = 1;

    public int Rows { get; set; } = 6;

    public int Columns { get; set; } = 14;

    public List<NodeDefinition> Nodes { get; set; } = [];

    public List<CableDefinition> Cables { get; set; } = [];
}

public sealed class NodeDefinition {
    /// <summary>Stable instance id such as <c>amp1</c> or <c>delay2</c>. Never an index.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>A block type id from the registry, or one of <see cref="NodeTypes" />.</summary>
    public string Type { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    public bool Bypassed { get; set; }

    /// <summary>One of <see cref="BypassModes" />.</summary>
    public string BypassMode { get; set; } = BypassModes.Thru;

    /// <summary>Parameter values by parameter id. Missing parameters take their default.</summary>
    [JsonPropertyName("params")]
    public Dictionary<string, float> Parameters { get; set; } = [];

    /// <summary>Files the block needs, by asset slot (e.g. <c>"model": "captures/plexi.nam"</c>).</summary>
    public Dictionary<string, string> Assets { get; set; } = [];
}

/// <summary>A cable from a node in column <c>c</c> to a node in column <c>c + 1</c>.</summary>
public sealed class CableDefinition {
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(GraphDefinition))]
public sealed partial class GraphJsonContext : JsonSerializerContext;
