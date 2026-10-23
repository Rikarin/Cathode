namespace Cathode.Contracts;

/// <summary>The rig as it is running: the working copy, which scene is active, and whether it differs from the library.</summary>
public sealed record LiveState(Preset Preset, int ActiveScene, long Revision, bool Dirty, bool CanUndo, bool CanRedo);

public sealed record PresetSummary(string Id, string Name, string? Author, IReadOnlyList<string> Tags, DateTimeOffset Modified);

public sealed record AssetInfo(string Ref, string Name, string Kind, long Size, DateTimeOffset Added);

/// <summary>
///     Engine health. CPU and native figures cover the last 5 s window (native timing is measured around the whole
///     driver callback); overloads and the host's GC counts are cumulative since the host started.
/// </summary>
public sealed record EngineStatus(
    bool Connected,
    string Mode,
    string? Backend,
    double SampleRate,
    int BufferFrames,
    double CpuP999,
    double CpuMax,
    long Overloads,
    string? LastError,
    long DriverOverloads = 0,
    double NativeP999 = 0,
    double NativeMax = 0,
    int HostGen0 = 0,
    int HostGen1 = 0,
    int HostGen2 = 0,
    double HostGcPauseMs = 0);

public sealed record SystemInfo(string Version, EngineStatus Engine);

public sealed record ParameterInfo(
    string Id,
    string Name,
    string Kind,
    float Min,
    float Max,
    float Default,
    string Unit,
    string Taper,
    IReadOnlyList<string>? Options);

public sealed record BlockTypeInfo(
    string TypeId,
    string Name,
    string Category,
    int MaxInstances,
    int Channels,
    IReadOnlyList<string> AssetSlots,
    IReadOnlyList<ParameterInfo> Params);

// Request bodies.
public sealed record AddBlockRequest(string Type, int Row, int Column, string? Id = null);

public sealed record MoveBlockRequest(int Row, int Column);

public sealed record CableRequest(string From, string To);

public sealed record BypassRequest(bool Bypassed);

public sealed record ChannelRequest(string Channel);

public sealed record AssetRequest(string Asset);

public sealed record SceneSelectRequest(int Scene);

public sealed record SceneUpdateRequest(string? Name = null, float? Level = null);

public sealed record SaveRequest(string? Name = null, bool AsNew = false);

/// <summary>Pushed over the WebSocket after every change, whatever its source (REST, MIDI later, another editor).</summary>
public sealed record RigEvent(string Type, long Revision, string? Block = null, string? Channel = null, string? Param = null, float? Value = null, int? Scene = null);

/// <summary>Pushed over the WebSocket about 30 times a second.</summary>
public sealed record Telemetry(
    string Type,
    float InputPeak,
    float OutputPeakLeft,
    float OutputPeakRight,
    double CpuP999,
    long Overloads,
    TunerInfo? Tuner);

public sealed record TunerInfo(double Frequency, string Note, int Octave, double Cents);

/// <summary><c>manifest.json</c> of a .cthpreset package.</summary>
public sealed record PackageManifest(string Format, int Version, string PresetId, string PresetName, IReadOnlyList<AssetInfo> Assets);

public sealed record ImportResult(PresetSummary Preset, int AssetsAdded, bool Renamed);

