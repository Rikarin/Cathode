using System.Text.Json.Serialization;

namespace Cathode.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(LiveState))]
[JsonSerializable(typeof(PresetSummary[]))]
[JsonSerializable(typeof(AssetInfo))]
[JsonSerializable(typeof(AssetInfo[]))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(BlockTypeInfo))]
[JsonSerializable(typeof(BlockTypeInfo[]))]
[JsonSerializable(typeof(AddBlockRequest))]
[JsonSerializable(typeof(MoveBlockRequest))]
[JsonSerializable(typeof(CableRequest))]
[JsonSerializable(typeof(BypassRequest))]
[JsonSerializable(typeof(ChannelRequest))]
[JsonSerializable(typeof(AssetRequest))]
[JsonSerializable(typeof(SceneSelectRequest))]
[JsonSerializable(typeof(SceneUpdateRequest))]
[JsonSerializable(typeof(SaveRequest))]
[JsonSerializable(typeof(RigEvent))]
[JsonSerializable(typeof(Telemetry))]
[JsonSerializable(typeof(Dictionary<string, float>))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;
