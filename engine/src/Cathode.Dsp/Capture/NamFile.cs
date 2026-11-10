using System.Text.Json;

namespace Cathode.Dsp.Capture;

/// <summary>
///     What a Neural Amp Modeler file (.nam, a JSON document) says about itself, read without the native core: the
///     Server and the editor need it for files the engine host has not loaded.
/// </summary>
public static class NamFile {
    /// <summary>The one slimming method NeuralAmpModelerCore v0.6.0 builds a slimmable WaveNet for.</summary>
    const string SlimmableMethod = "slice_channels_uniform";

    /// <summary>
    ///     Whether the file holds more than one size of its model (NAM A2: Full and Lite), by the rule the core loads
    ///     it with (NeuralAmpModelerCore v0.6.0): its architecture is <c>SlimmableContainer</c> (NAM/container.cpp), or
    ///     it is a <c>WaveNet</c> one of whose layer arrays carries a <c>slimmable</c> object with the method
    ///     <c>slice_channels_uniform</c> (<c>config_is_slimmable_wavenet</c> in NAM/wavenet/model.cpp). False for
    ///     every other file, one that is not JSON included.
    /// </summary>
    public static bool IsSlimmable(string path) {
        try {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return IsSlimmable(document.RootElement);
        } catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    static bool IsSlimmable(JsonElement root) {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("architecture", out var architecture)
            || architecture.ValueKind != JsonValueKind.String) {
            return false;
        }

        if (architecture.ValueEquals("SlimmableContainer")) {
            return true;
        }

        if (!architecture.ValueEquals("WaveNet")
            || !root.TryGetProperty("config", out var config)
            || config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty("layers", out var layers)
            || layers.ValueKind != JsonValueKind.Array) {
            return false;
        }

        foreach (var layer in layers.EnumerateArray()) {
            if (layer.ValueKind == JsonValueKind.Object
                && layer.TryGetProperty("slimmable", out var slimmable)
                && slimmable.ValueKind == JsonValueKind.Object
                && slimmable.TryGetProperty("method", out var method)
                && method.ValueKind == JsonValueKind.String
                && method.ValueEquals(SlimmableMethod)) {
                return true;
            }
        }

        return false;
    }
}
