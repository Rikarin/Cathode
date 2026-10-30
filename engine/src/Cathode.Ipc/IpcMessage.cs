using System.Text.Json.Serialization;

namespace Cathode.Ipc;

/// <summary>Message types. Requests get a reply with the same <see cref="IpcMessage.Id" />; events have id 0.</summary>
public static class IpcTypes {
    public const string Hello = "hello";
    public const string LoadGraph = "loadGraph";
    public const string SetParams = "setParams";
    public const string SetBypass = "setBypass";
    public const string SetTuner = "setTuner";
    public const string Tap = "tap";
    public const string Looper = "looper";
    public const string ExportLooper = "exportLooper";
    public const string SetMidi = "setMidi";
    public const string MidiOut = "midiOut";

    /// <summary>The host's MIDI endpoints (<see cref="IpcMessage.MidiPorts" />), for choosing a MIDI output.</summary>
    public const string MidiPorts = "midiPorts";

    /// <summary>The device's and the graph's latency (<see cref="LatencyReport" />).</summary>
    public const string Latency = "latency";

    /// <summary>
    ///     Starts, retunes or stops the test tone in place of a block's output (Block, Enabled, Frequency, Level).
    /// </summary>
    public const string TestTone = "testTone";

    /// <summary>
    ///     Turns the modifier on <c>Block</c>.<c>Param</c> on or off (<c>Enabled</c>): channel-scoped modifiers.
    ///     Turning it off restores the parameter to the value in <c>Params[0]</c>.
    /// </summary>
    public const string SetModifier = "setModifier";

    public const string Ok = "ok";
    public const string Error = "error";
    public const string Telemetry = "telemetry";

    /// <summary>Host to Server: MIDI input, as it arrives (id 0).</summary>
    public const string Midi = "midi";
}

/// <summary>Reserved block ids for parameters that are not in the graph.</summary>
public static class IpcBlocks {
    /// <summary>
    ///     The rig parameters (Cathode.Engine.Rig.RigControls): scene level, tempo, controllers, sources.
    /// </summary>
    public const string Rig = "rig";

    /// <summary>
    ///     Output 1's global EQ; Outputs 2-4 have <c>global2</c>..<c>global4</c> (<see cref="GlobalEq" />).
    /// </summary>
    public const string Global = "global";

    /// <summary>The block id of output <paramref name="output" />'s (0-3) global EQ.</summary>
    public static string GlobalEq(int output) => output == 0 ? Global : Global + (output + 1);

    /// <summary>Which output's global EQ <paramref name="block" /> names (0-3), or −1.</summary>
    public static int GlobalEqOutput(ReadOnlySpan<char> block) =>
        !block.StartsWith(Global) ? -1
        : block.Length == Global.Length ? 0
        : block.Length == Global.Length + 1 && block[^1] is >= '2' and <= '4' ? block[^1] - '1'
        : -1;

    /// <summary>A rig or global EQ id: parameters that need no graph.</summary>
    public static bool IsEngineWide(ReadOnlySpan<char> block) => block is Rig || GlobalEqOutput(block) >= 0;
}

/// <summary>One envelope for every message, so the protocol stays a single source-generated type.</summary>
public sealed class IpcMessage {
    public string Type { get; set; } = string.Empty;

    public long Id { get; set; }

    public int Generation { get; set; }

    public GraphDefinition? Graph { get; set; }

    public List<ParamValue>? Params { get; set; }

    public string? Block { get; set; }

    /// <summary>setModifier: the modified parameter.</summary>
    public string? Param { get; set; }

    /// <summary>The reply to loadGraph: the block id of each graph slot.</summary>
    public List<string>? SlotIds { get; set; }

    /// <summary>
    ///     The reply to loadGraph: the id of each block meter, in the order the telemetry region's block meters come
    ///     in: slot by slot, <c>peak</c> and then the block's own meters, so each <c>peak</c> starts the next slot's.
    /// </summary>
    public string[]? MeterIds { get; set; }

    /// <summary>
    ///     The reply to loadGraph: the parameter each modifier drives, in the order the telemetry region's modifier
    ///     values come in (an engage modifier's parameter is <c>engage</c>).
    /// </summary>
    public List<ModifierTarget>? ModifierTargets { get; set; }

    public bool? Bypassed { get; set; }

    public bool? Enabled { get; set; }

    public string? Error { get; set; }

    public List<string>? Errors { get; set; }

    public HostInfo? Host { get; set; }

    public HostTelemetry? Telemetry { get; set; }

    /// <summary>tap: the engine sample time of the tap (from a MIDI timestamp); absent means "now".</summary>
    public long? Time { get; set; }

    /// <summary>looper: record, play, stop, overdub, undo, reverse, halfSpeed or clear.</summary>
    public string? Action { get; set; }

    /// <summary>exportLooper: where to write the WAV file.</summary>
    public string? Path { get; set; }

    /// <summary>setMidi: whether the tempo follows incoming MIDI clock.</summary>
    public bool? FollowClock { get; set; }

    /// <summary>midiOut: the message bytes; midi: the received messages.</summary>
    public List<int>? Bytes { get; set; }

    /// <summary>
    ///     midiOut: the destination port's uid (<see cref="MidiPortInfo.Uid" />), or null for every output. The Server
    ///     sends the global MIDI output setting (Cathode.Contracts.GlobalSettings.MidiOutPort).
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>The reply to midiPorts: every MIDI endpoint the OS reports, inputs and outputs.</summary>
    public List<MidiPortInfo>? MidiPorts { get; set; }

    /// <summary>midi: messages received since the previous push.</summary>
    public List<MidiEventMessage>? Midi { get; set; }

    /// <summary>testTone: the sine's frequency in hertz.</summary>
    public double? Frequency { get; set; }

    /// <summary>testTone: the sine's level in dBFS.</summary>
    public float? Level { get; set; }

    /// <summary>latency: the reply.</summary>
    public LatencyReport? Latency { get; set; }

    public static IpcMessage Ok(long id) => new() { Type = IpcTypes.Ok, Id = id };

    public static IpcMessage Fail(long id, string error, List<string>? errors = null) =>
        new() { Type = IpcTypes.Error, Id = id, Error = error, Errors = errors };
}

public sealed record ParamValue(string Block, string Param, float Value);

/// <summary>A MIDI endpoint as the host's OS reports it; <c>Uid</c> is stable across runs.</summary>
public sealed record MidiPortInfo(string Uid, string Name, bool IsInput);

/// <summary>The block and parameter a modifier drives.</summary>
public sealed record ModifierTarget(string Block, string Param);

/// <summary>
///     The host's answer to <c>hello</c>. <c>TelemetryPath</c> names its shared-memory telemetry file (meters, CPU
///     histogram, xruns) for the Server to map read-only; it is null from hosts that predate it. <c>InputSources</c>
///     has one bit per hardware input source with device channels (bit 0 = Input 1, then Input 2–4, USB 5/6, USB 7/8).
/// </summary>
public sealed record HostInfo(
    string Version,
    string Backend,
    double SampleRate,
    int BufferFrames,
    string? TelemetryPath = null,
    int InputSources = 1
);

/// <summary>
///     Host health. CPU figures cover the current 5 s window; overloads and GC counts are cumulative. Native timing is
///     measured in the audio shim around the whole callback, so it includes any wait to enter the runtime (GC
///     suspension); it covers the previous complete window and is 0 without a device. Mutable so the host can refill
///     one instance for every push instead of allocating ten a second.
/// </summary>
public sealed class HostTelemetry {
    public float InputPeak { get; set; }

    public float OutputPeakLeft { get; set; }

    public float OutputPeakRight { get; set; }

    public double CpuP999 { get; set; }

    public double CpuMax { get; set; }

    public long Overloads { get; set; }

    public HostTunerReading? Tuner { get; set; }

    public long DriverOverloads { get; set; }

    /// <summary>Cumulative exceptions the engine threw on the audio thread; each one silenced a callback.</summary>
    public long CallbackExceptions { get; set; }

    /// <summary>
    ///     The audio device: <c>ok</c>, <c>changed</c> (another application changed its rate or buffer, or the default
    ///     device changed) or <c>lost</c> (unplugged, taken away, or no longer calling back).
    /// </summary>
    public string DeviceStatus { get; set; } = "ok";

    public double NativeP999 { get; set; }

    public double NativeMax { get; set; }

    public int Gen0 { get; set; }

    public int Gen1 { get; set; }

    public int Gen2 { get; set; }

    public double GcPauseMs { get; set; }

    public long AllocatedBytes { get; set; }

    public double Tempo { get; set; } = 120;

    public LooperStatus? Looper { get; set; }

    /// <summary>
    ///     Peak of each output since the previous push (Output 1, then Outputs 2-4; the larger of left and right, after
    ///     the global EQ and Boost/Pad). Refilled in place.
    /// </summary>
    public float[] OutputPeaks { get; set; } = new float[4];
}

/// <summary>
///     Where the time goes between the instrument and the speakers: the device's input and output latency as the
///     driver reports it (each includes one buffer), the buffer itself, and the graph's latency along its longest path
///     with the blocks on that path. Frames at <c>SampleRate</c>.
/// </summary>
public sealed record LatencyReport(
    double SampleRate,
    int BufferFrames,
    int InputLatencyFrames,
    int OutputLatencyFrames,
    int GraphLatencyFrames,
    List<LatencyStep> Path
);

/// <summary>A MIDI message as the host forwards it, timed on the engine's sample clock.</summary>
public sealed record MidiEventMessage(int Status, int Data1, int Data2, long SampleTime, string? Port = null);

/// <summary>
///     The looper's transport, for the editor; mutable for the same reason as <see cref="HostTelemetry" />.
/// </summary>
public sealed class LooperStatus {
    public string State { get; set; } = string.Empty;

    public double LengthSeconds { get; set; }

    public double PositionSeconds { get; set; }

    public bool Reversed { get; set; }

    public bool HalfSpeed { get; set; }

    /// <summary>Which looper the transport drives: <c>block</c>, the preset's Looper block, or <c>rig</c>.</summary>
    public string Source { get; set; } = "rig";
}

/// <summary>The tuner's latest reading; mutable for the same reason as <see cref="HostTelemetry" />.</summary>
public sealed class HostTunerReading {
    public double Frequency { get; set; }

    public string Note { get; set; } = string.Empty;

    public int Octave { get; set; }

    public double Cents { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(IpcMessage))]
public sealed partial class IpcJsonContext : JsonSerializerContext;
