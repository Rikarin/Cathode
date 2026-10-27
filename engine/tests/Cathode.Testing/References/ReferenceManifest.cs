using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cathode.Testing.References;

/// <summary>
///     The committed list of amp references (docs/06 §4): captures of real amps made by other people, which Cathode's
///     circuit amps are matched against. The files themselves live in the <see cref="ReferenceCache" />, never in the
///     repository; the manifest records where each comes from, its licence, the amp settings it was captured at, and
///     its SHA-256.
/// </summary>
public sealed record ReferenceManifest(int Version, IReadOnlyList<ReferenceEntry> References) {
    /// <summary>The manifest's path in the repository, relative to the engine folder.</summary>
    public const string RelativePath = "tests/assets/references/manifest.json";

    public static ReferenceManifest Load(string path) {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, ReferenceJsonContext.Default.ReferenceManifest)
            ?? throw new InvalidDataException($"Empty reference manifest '{path}'.");
    }

    /// <summary>
    ///     Finds the manifest by walking up from <paramref name="start" /> (default: this assembly's folder).
    /// </summary>
    public static string Locate(string? start = null) {
        for (var directory = new DirectoryInfo(start ?? AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent) {
            foreach (var candidate in new[] {
                         Path.Combine(directory.FullName, RelativePath),
                         Path.Combine(directory.FullName, "engine", RelativePath)
                     }) {
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException($"Could not find {RelativePath} above '{start ?? AppContext.BaseDirectory}'.");
    }

    public ReferenceEntry Find(string id) =>
        References.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"No reference '{id}' in the manifest.");
}

/// <summary>One reference capture.</summary>
/// <param name="Id">Stable identifier; also the file name in the cache (plus the format's extension).</param>
/// <param name="Amp">The real amp, as precisely as the source states it.</param>
/// <param name="Family">
///     The Phase 7 stream that uses it: <c>fender</c>, <c>marshall</c>, <c>class-a</c>, <c>high-gain</c>.
/// </param>
/// <param name="Format">
///     <c>nam</c>, <c>guitarml-lstm</c> (Proteus/NeuralPi JSON) or <c>measured-response</c> (CSV).
/// </param>
/// <param name="Url">
///     Direct download URL (redistributable files) or the page to fetch it from by hand (local-only).
/// </param>
/// <param name="Author">The capture's author as the source names them.</param>
/// <param name="Licence">SPDX id (e.g. <c>GPL-3.0</c>) or <c>T3K</c>.</param>
/// <param name="Redistributable">
///     True only for GPL-compatible licences: the fetch tool may download it. False: the user must fetch it by hand
///     (the T3K terms forbid scripted downloads) and it must never be committed.
/// </param>
/// <param name="SourceName">
///     The file or model name at the source, so a hand-fetched file can be found and renamed.
/// </param>
/// <param name="Settings">The amp's knobs and switches as the author states them.</param>
/// <param name="Conditioning">Knob values (0–1) fed to a conditioned model's extra inputs.</param>
/// <param name="Rig">How it was captured.</param>
/// <param name="SampleRate">
///     The rate the model runs at; null: the format's default (48 kHz NAM, 44.1 kHz GuitarML).
/// </param>
/// <param name="Sha256">Hex SHA-256 of the file; null for local-only files not yet seen.</param>
/// <param name="Size">File size in bytes, when known.</param>
/// <param name="Circuit">The circuit amp type and settings to compare with; null if no type matches yet.</param>
/// <param name="Targets">Pass thresholds for the reference test; the family streams tighten them.</param>
/// <param name="Notes">Caveats: what the capture is not, how the circuit settings were chosen.</param>
public sealed record ReferenceEntry(
    string Id,
    string Amp,
    string Family,
    string Format,
    string Url,
    string Author,
    string Licence,
    bool Redistributable,
    string? SourceName,
    IReadOnlyDictionary<string, string>? Settings,
    IReadOnlyList<double>? Conditioning,
    ReferenceRig Rig,
    double? SampleRate,
    string? Sha256,
    long? Size,
    CircuitSettings? Circuit,
    MatchTargets? Targets,
    string? Notes
) {
    /// <summary>The file name in the cache.</summary>
    public string FileName =>
        Id
        + Format switch {
            "nam" => ".nam",
            "guitarml-lstm" => ".json",
            "measured-response" => ".csv",
            _ => throw new InvalidDataException($"Unknown reference format '{Format}' ({Id}).")
        };

    public double EffectiveSampleRate => SampleRate ?? (Format == "guitarml-lstm" ? 44_100 : 48_000);
}

/// <param name="Output"><c>direct</c> (amp output into a load), <c>mic</c>, <c>preamp</c> or <c>unknown</c>.</param>
/// <param name="Load">Load box or speaker, when stated.</param>
/// <param name="InputDbu">
///     Interface calibration: the RMS level in dBu of a sine peaking at 0 dBFS at the capture's input. Null when not
///     stated; the matcher then fits it.
/// </param>
/// <param name="Notes">Anything else about the capture chain.</param>
public sealed record ReferenceRig(string Output, string? Load, double? InputDbu, string? Notes);

/// <summary>The circuit amp configuration matched against a reference: the block's parameters by id.</summary>
/// <param name="Type">The circuit amp's Type option name, e.g. <c>Plexi</c>.</param>
/// <param name="Params">
///     Other parameters by id (e.g. <c>gain</c>, <c>bright</c>); unset ones keep their defaults.
/// </param>
public sealed record CircuitSettings(string Type, IReadOnlyDictionary<string, double>? Params);

/// <param name="Esr">Largest passing pre-emphasised ESR.</param>
/// <param name="Mrstft">Largest passing multi-resolution STFT distance.</param>
public sealed record MatchTargets(double Esr, double Mrstft);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
)]
[JsonSerializable(typeof(ReferenceManifest))]
[JsonSerializable(typeof(MatchReport))]
[JsonSerializable(typeof(List<MatchReport>))]
public sealed partial class ReferenceJsonContext : JsonSerializerContext;
