namespace Cathode.Contracts;

/// <summary>
///     The rig as it is running: the working copy, which scene is active, and whether it differs from the library.
///     <c>Held</c> lists the blocks whose bypass and channel differ from the active scene's because Scene Ignore kept
///     them as they were: what they are running now.
/// </summary>
public sealed record LiveState(
    Preset Preset,
    int ActiveScene,
    long Revision,
    bool Dirty,
    bool CanUndo,
    bool CanRedo,
    Dictionary<string, SceneBlockState>? Held = null,
    bool Muted = false
);

public sealed record PresetSummary(
    string Id,
    string Name,
    string? Author,
    IReadOnlyList<string> Tags,
    DateTimeOffset Modified,
    string? Color = null,
    bool Favorite = false,
    string? Description = null,
    bool? Factory = null
);

public sealed record AssetInfo(
    string Ref,
    string Name,
    string Kind,
    long Size,
    DateTimeOffset Added,
    string? Color = null,
    bool Favorite = false
);

/// <summary>
///     Engine health. CPU and native figures cover the last 5 s window (native timing is measured around the whole
///     driver callback); overloads and the host's GC counts are cumulative since the host started. <c>Inputs</c> names
///     the hardware input sources the device maps (Input 1–4, USB 5/6, USB 7/8); the others are silent.
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
    double HostGcPauseMs = 0,
    long HostAllocatedBytes = 0,
    IReadOnlyList<string>? Inputs = null
);

public sealed record SystemInfo(string Version, EngineStatus Engine);

/// <summary>One block on the graph's longest path and the latency it adds, in frames.</summary>
public sealed record LatencyStepInfo(string Block, string Type, int Frames);

/// <summary>
///     Where the time goes, instrument to speaker: the device's input and output latency as its driver reports them
///     (each includes one buffer), the buffer itself, the graph's latency along its longest path (with the blocks on
///     it), and the total of the device and graph latencies.
/// </summary>
public sealed record LatencyInfo(
    bool Connected,
    double SampleRate,
    int BufferFrames,
    int InputFrames,
    int OutputFrames,
    int GraphFrames,
    IReadOnlyList<LatencyStepInfo> Path,
    int TotalFrames,
    double TotalMs
);

/// <summary>The cab resonance helper's sine, in place of a block's output: on or off, its pitch and level (dBFS).</summary>
public sealed record TestToneRequest(bool Enabled, double Frequency = 100, float Level = -20);

/// <summary>
///     A Circuit Amp's speaker load resonance: the model's (or the chosen load's; with Follow Cab, the followed Cab's
///     speaker's) frequency at Load resonance 100 %, the Load resonance setting, and the frequency it gives.
/// </summary>
public sealed record SpeakerResonanceInfo(double BaseHz, float Percent, double Hz);

/// <summary>
///     A block parameter. <c>TypeLabels</c>, where present, names the parameter per value of the block's <c>type</c>
///     parameter (an amp model's own name for a generic control), empty where that type lacks the control: hide it there.
/// </summary>
public sealed record ParameterInfo(
    string Id,
    string Name,
    string Kind,
    float Min,
    float Max,
    float Default,
    string Unit,
    string Taper,
    IReadOnlyList<string>? Options,
    IReadOnlyList<string>? TypeLabels = null,
    string? Page = null
);

/// <summary>
///     A block type the editor can place. <c>CpuCost</c> is its measured cost in percent of one core at 48 kHz
///     (64-frame blocks, its most expensive type), for the editor's CPU estimate; 0 for built-in cells. <c>Pages</c>
///     names the editor pages its parameters sit on (each <see cref="ParameterInfo.Page" />), in order; empty for one.
/// </summary>
public sealed record BlockTypeInfo(
    string TypeId,
    string Name,
    string Category,
    int MaxInstances,
    int Channels,
    IReadOnlyList<string> AssetSlots,
    IReadOnlyList<ParameterInfo> Params,
    double CpuCost = 0,
    IReadOnlyList<string>? Pages = null
);

// Request bodies.
public sealed record AddBlockRequest(string Type, int Row, int Column, string? Id = null);

public sealed record MoveBlockRequest(int Row, int Column);

public sealed record CableRequest(string From, string To);

public sealed record BypassRequest(bool Bypassed);

public sealed record ChannelRequest(string Channel);

public sealed record AssetRequest(string Asset);

public sealed record SceneSelectRequest(int Scene);

/// <summary>Changes to a scene; null leaves a field as it is. <c>Midi</c> replaces the scene's MIDI messages.</summary>
public sealed record SceneUpdateRequest(
    string? Name = null,
    float? Level = null,
    List<float>? Controllers = null,
    List<SceneMidiMessage>? Midi = null
);

/// <summary>Changes to the working copy's name, author, tags and description; null leaves a field as it is.</summary>
public sealed record PresetInfoRequest(
    string? Name = null,
    string? Author = null,
    List<string>? Tags = null,
    string? Description = null
);

public sealed record SaveRequest(string? Name = null, bool AsNew = false);

/// <summary>Pushed over the WebSocket after every change, whatever its source (REST, MIDI later, another editor).</summary>
public sealed record RigEvent(
    string Type,
    long Revision,
    string? Block = null,
    string? Channel = null,
    string? Param = null,
    float? Value = null,
    int? Scene = null
);

/// <summary>
///     Pushed over the WebSocket about 30 times a second. <c>Blocks</c> holds every block meter since the last frame,
///     by block id and then meter id: each grid block's <c>peak</c> (its output peak, linear) and the meters some blocks
///     keep of their own, in dB: the Cab's <c>preampIn</c> (the level into the mic preamp, 0 at the onset of clipping)
///     and <c>motorReduction</c> (Motor Drive's gain reduction). Each is the highest value since the previous frame. The
///     loudness is the output's ITU-R BS.1770 momentary (400 ms) and short-term (3 s) level in LUFS. Both come from the
///     shared-memory telemetry and are absent without it.
/// </summary>
public sealed record Telemetry(
    string Type,
    float InputPeak,
    float OutputPeakLeft,
    float OutputPeakRight,
    double CpuP999,
    long Overloads,
    TunerInfo? Tuner,
    double Tempo = 120,
    LooperInfo? Looper = null,
    Dictionary<string, Dictionary<string, float>>? Blocks = null,
    float? LoudnessMomentary = null,
    float? LoudnessShortTerm = null,
    float[]? OutputPeaks = null
);

/// <summary>
///     Pushed about 30 times a second while the scope is on (<c>PUT /api/v1/scope</c>): the most recent output samples
///     (left), decimated to <c>SampleRate</c>, oldest first.
/// </summary>
public sealed record ScopeFrame(string Type, double SampleRate, float[] Samples);

public sealed record ScopeRequest(bool Enabled);

public sealed record TunerInfo(double Frequency, string Note, int Octave, double Cents);

/// <summary><c>manifest.json</c> of a .cthpreset package.</summary>
public sealed record PackageManifest(
    string Format,
    int Version,
    string PresetId,
    string PresetName,
    IReadOnlyList<AssetInfo> Assets
);

public sealed record ImportResult(PresetSummary Preset, int AssetsAdded, bool Renamed);
