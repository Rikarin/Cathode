using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Cathode.Contracts;

namespace Cathode.Server.Realtime;

/// <summary>
///     How the meter, scope and RTA frames are written. Their numbers come from the audio path, where one unstable
///     filter or damaged capture can produce NaN or an infinity; JSON has no such numbers and the serialiser throws on
///     them. Here every non-finite number is written as 0 and counted, so one bad sample costs one wrong reading
///     instead of the frame.
/// </summary>
public static class FrameJson {
    static readonly JsonSerializerOptions Options = Create();

    static long replaced;

    public static JsonTypeInfo<Telemetry> Telemetry { get; } = Info<Telemetry>();

    public static JsonTypeInfo<ScopeFrame> ScopeFrame { get; } = Info<ScopeFrame>();

    public static JsonTypeInfo<RtaFrame> RtaFrame { get; } = Info<RtaFrame>();

    /// <summary>How many non-finite numbers have been replaced since the Server started.</summary>
    public static long Replaced => Interlocked.Read(ref replaced);

    static JsonSerializerOptions Create() {
        var options = new JsonSerializerOptions(ContractsJsonContext.Default.Options) {
            TypeInfoResolver = ContractsJsonContext.Default
        };
        options.Converters.Add(new FiniteSingle());
        options.Converters.Add(new FiniteDouble());
        return options;
    }

    static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    sealed class FiniteSingle : JsonConverter<float> {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetSingle();

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) {
            if (float.IsFinite(value)) {
                writer.WriteNumberValue(value);
            } else {
                Interlocked.Increment(ref replaced);
                writer.WriteNumberValue(0);
            }
        }
    }

    sealed class FiniteDouble : JsonConverter<double> {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) {
            if (double.IsFinite(value)) {
                writer.WriteNumberValue(value);
            } else {
                Interlocked.Increment(ref replaced);
                writer.WriteNumberValue(0);
            }
        }
    }
}
