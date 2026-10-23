using System.Text.Json.Serialization;
using Cathode.Engine.Graph;

namespace Cathode.Ipc;

/// <summary>Message types. Requests get a reply with the same <see cref="IpcMessage.Id"/>; events have id 0.</summary>
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
    public string Type { get; set; } = "";

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

public sealed record HostInfo(string Version, string Backend, double SampleRate, int BufferFrames);

public sealed record HostTelemetry(
    float InputPeak,
    float OutputPeakLeft,
    float OutputPeakRight,
    double CpuP999,
    double CpuMax,
    long Overloads,
    HostTunerReading? Tuner);

public sealed record HostTunerReading(double Frequency, string Note, int Octave, double Cents);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcMessage))]
public sealed partial class IpcJsonContext : JsonSerializerContext;
