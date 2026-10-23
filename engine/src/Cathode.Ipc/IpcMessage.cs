using System.Text.Json.Serialization;
using Cathode.Engine.Graph;

namespace Cathode.Ipc;

/// <summary>Message types. Requests get a reply with the same <see cref="IpcMessage.Id" />; events have id 0.</summary>
public static class IpcTypes {
    public const string Hello = "hello";
    public const string LoadGraph = "loadGraph";
    public const string SetParams = "setParams";
    public const string SetBypass = "setBypass";
    public const string SetTuner = "setTuner";
    public const string Status = "status";
    public const string Tap = "tap";
    public const string Looper = "looper";
    public const string ExportLooper = "exportLooper";
    public const string SetMidi = "setMidi";
    public const string MidiOut = "midiOut";

    public const string Ok = "ok";
    public const string Error = "error";
    public const string Telemetry = "telemetry";

    /// <summary>Host to Server: MIDI input, as it arrives (id 0).</summary>
    public const string Midi = "midi";
}

/// <summary>Reserved block ids for parameters that are not in the graph.</summary>
public static class IpcBlocks {
    /// <summary>The rig parameters (Cathode.Engine.Rig.RigControls): scene level, tempo, controllers, sources.</summary>
    public const string Rig = "rig";

    /// <summary>The global output EQ.</summary>
    public const string Global = "global";
}

/// <summary>One envelope for every message, so the protocol stays a single source-generated type.</summary>
public sealed class IpcMessage {
    public string Type { get; set; } = string.Empty;

    public long Id { get; set; }

    public int Generation { get; set; }

    public GraphDefinition? Graph { get; set; }

    public List<ParamValue>? Params { get; set; }

    public string? Block { get; set; }

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

    /// <summary>midiOut: destination port uid, or null for every output.</summary>
    public string? Destination { get; set; }

    /// <summary>midi: messages received since the previous push.</summary>
    public List<MidiEventMessage>? Midi { get; set; }

    public static IpcMessage Ok(long id) => new() { Type = IpcTypes.Ok, Id = id };

    public static IpcMessage Fail(long id, string error, List<string>? errors = null) =>
        new() { Type = IpcTypes.Error, Id = id, Error = error, Errors = errors };
}

public sealed record ParamValue(string Block, string Param, float Value);

/// <summary>
///     The host's answer to <c>hello</c>. <c>TelemetryPath</c> names its shared-memory telemetry file (meters, CPU
///     histogram, xruns) for the Server to map read-only; it is null from hosts that predate it.
/// </summary>
public sealed record HostInfo(
    string Version,
    string Backend,
    double SampleRate,
    int BufferFrames,
    string? TelemetryPath = null);

/// <summary>
///     Host health. CPU figures cover the current 5 s window; overloads and GC counts are cumulative. Native timing is
///     measured in the audio shim around the whole callback, so it includes any wait to enter the runtime (GC
///     suspension); it covers the previous complete window and is 0 without a device.
/// </summary>
public sealed record HostTelemetry(
    float InputPeak,
    float OutputPeakLeft,
    float OutputPeakRight,
    double CpuP999,
    double CpuMax,
    long Overloads,
    HostTunerReading? Tuner,
    long DriverOverloads = 0,
    double NativeP999 = 0,
    double NativeMax = 0,
    int Gen0 = 0,
    int Gen1 = 0,
    int Gen2 = 0,
    double GcPauseMs = 0,
    long AllocatedBytes = 0,
    double Tempo = 120,
    LooperStatus? Looper = null);

/// <summary>A MIDI message as the host forwards it, timed on the engine's sample clock.</summary>
public sealed record MidiEventMessage(int Status, int Data1, int Data2, long SampleTime, string? Port = null);

/// <summary>The looper's transport, for the editor.</summary>
public sealed record LooperStatus(string State, double LengthSeconds, double PositionSeconds, bool Reversed, bool HalfSpeed);

public sealed record HostTunerReading(double Frequency, string Note, int Octave, double Cents);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(IpcMessage))]
public sealed partial class IpcJsonContext : JsonSerializerContext;
