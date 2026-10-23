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

    public const string Ok = "ok";
    public const string Error = "error";
    public const string Telemetry = "telemetry";
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

    public double NativeP999 { get; set; }

    public double NativeMax { get; set; }

    public int Gen0 { get; set; }

    public int Gen1 { get; set; }

    public int Gen2 { get; set; }

    public double GcPauseMs { get; set; }

    public long AllocatedBytes { get; set; }
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
