using System.IO.Compression;
using System.Text.Json;
using Cathode.Dsp.IO;

namespace Cathode.Dsp.Cabinets;

/// <summary>
///     A user's multi-position cab IR pack: one speaker and mic captured on a grid of positions (cap to edge) by
///     distances. Packs stay outside the repository, in the asset store, like captures.
/// </summary>
/// <remarks>
///     <para>
///         Formats (<see cref="Load" />): a <c>.cabgrid</c> (or <c>.zip</c>) archive with a <c>manifest.json</c>, a
///         folder holding a <c>manifest.json</c>, the manifest itself, or a single <c>.wav</c> (a one-point grid). The
///         manifest lists every grid point:
///         <code>
/// { "format": "cathode-cabgrid", "version": 1, "name": "4x12 V30 SM57",
///   "irs": [ { "position": 0, "distance": 0, "file": "cap_0in.wav" }, … ] }
///         </code>
///         <c>position</c> is in percent from the cap's centre (0) to the cone's edge (100), <c>distance</c> in inches
///         from the grille, as the block's own parameters. The points must form a full grid: every position at every
///         distance. Files are relative to the manifest.
///     </para>
///     <para>
///         Interpolation is bilinear in log magnitude, with the onset delay interpolated separately and the phase
///         rebuilt as minimum phase (<see cref="CabIrSynthesizer" />). Crossfading the IRs themselves would comb-filter:
///         neighbouring positions differ in arrival time and in phase above a few kHz, so their sum notches where they
///         disagree and a sweep between points would flange. Log-magnitude interpolation moves peaks and the treble
///         smoothly, and a close-miked cab is nearly minimum phase apart from its delay, so little is lost; what is lost
///         is excess phase such as a captured room reflection's exact timing.
///     </para>
/// </remarks>
public sealed class CabGrid : IDisposable {
    /// <summary>Largest grid accepted.</summary>
    public const int MaxPoints = 256;

    const string ManifestName = "manifest.json";
    const float FloorLog = -16f; // about -140 dB

    readonly NativeBuffer logMagnitude; // points × bins, natural log
    readonly double[] onsets; // seconds, per point
    readonly int bins;
    readonly int fftSize;
    readonly double fileRate;

    public string Name { get; }

    /// <summary>Positions on the grid, 0 (cap) … 1 (edge), ascending.</summary>
    public IReadOnlyList<double> Positions { get; }

    /// <summary>Distances on the grid in inches, ascending.</summary>
    public IReadOnlyList<double> Distances { get; }

    CabGrid(string name, double[] positions, double[] distances, AudioData[,] irs) {
        Name = name;
        Positions = positions;
        Distances = distances;
        fileRate = irs[0, 0].SampleRate;
        var window = (int)Math.Round(CabIrSynthesizer.ReferenceTaps * fileRate / CabIrSynthesizer.ReferenceRate);
        fftSize = 4 * (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)window);
        bins = fftSize / 2 + 1;
        onsets = new double[positions.Length * distances.Length];
        logMagnitude = new(onsets.Length * bins);
        using var fft = new NativeFft(fftSize);
        using var re = new NativeBuffer(fftSize);
        using var im = new NativeBuffer(fftSize);
        for (var d = 0; d < distances.Length; d++) {
            for (var p = 0; p < positions.Length; p++) {
                var audio = irs[p, d];
                if (audio.SampleRate != fileRate) {
                    throw new InvalidDataException("Every IR in a cab grid must have the same sample rate.");
                }

                var point = d * positions.Length + p;
                var samples = audio.Channels[0];
                var onset = Onset(samples);
                onsets[point] = onset / fileRate;

                // Analyse from just before the onset, for as long as the synthesised IR will be.
                var start = Math.Max(0, onset - (int)(0.0005 * fileRate));
                re.Clear();
                im.Clear();
                var span = re.Span;
                var length = Math.Min(window, samples.Length - start);
                for (var i = 0; i < length; i++) {
                    var fade = i >= length - 64 ? 0.5f * (1f + MathF.Cos(MathF.PI * (i - length + 65) / 64)) : 1f;
                    span[i] = samples[start + i] * fade;
                }

                fft.Forward(re.Span, im.Span);
                var target = logMagnitude.Span.Slice(point * bins, bins);
                for (var k = 0; k < bins; k++) {
                    var power = re[k] * re[k] + im[k] * im[k];
                    target[k] = Math.Max(FloorLog, 0.5f * MathF.Log(power + 1e-30f));
                }
            }
        }
    }

    /// <summary>Control thread: reads a pack (see the remarks for the formats).</summary>
    public static CabGrid Load(string path) {
        if (Directory.Exists(path)) {
            return FromManifest(Path.Combine(path, ManifestName));
        }

        if (!File.Exists(path)) {
            throw new FileNotFoundException($"Cab grid '{path}' was not found.", path);
        }

        return Path.GetExtension(path).ToLowerInvariant() switch {
            ".wav" => new(Path.GetFileNameWithoutExtension(path), [0], [0], new[,] { { WavFile.Read(path) } }),
            ".json" => FromManifest(path),
            ".cabgrid" or ".zip" => FromArchive(path),
            _ => throw new InvalidDataException($"'{path}' is not a cab grid: use a .cabgrid, a manifest.json or a .wav.")
        };
    }

    /// <summary>
    ///     Natural-log magnitude at <paramref name="position" /> (0 … 1) and <paramref name="distance" /> (inches) on the
    ///     bins of an FFT at <paramref name="sampleRate" /> with <c>destination.Length</c> = N/2 + 1. Returns the
    ///     interpolated onset delay in seconds.
    /// </summary>
    public double LogMagnitude(double position, double distance, double sampleRate, Span<float> destination) {
        var (p0, p1, tp) = Bracket(Positions, position);
        var (d0, d1, td) = Bracket(Distances, distance);
        var count = Positions.Count;
        var weights = (
            A: (1 - tp) * (1 - td), B: tp * (1 - td), C: (1 - tp) * td, D: tp * td
        );
        var all = logMagnitude.Span;
        var a = all.Slice((d0 * count + p0) * bins, bins);
        var b = all.Slice((d0 * count + p1) * bins, bins);
        var c = all.Slice((d1 * count + p0) * bins, bins);
        var d = all.Slice((d1 * count + p1) * bins, bins);
        var hzPerDestination = sampleRate / (2.0 * (destination.Length - 1));
        var binsPerHz = fftSize / fileRate;
        for (var k = 0; k < destination.Length; k++) {
            var x = k * hzPerDestination * binsPerHz;
            if (x >= bins - 1) {
                destination[k] = FloorLog;
                continue;
            }

            var i = (int)x;
            var f = (float)(x - i);
            var va = a[i] + (a[i + 1] - a[i]) * f;
            var vb = b[i] + (b[i + 1] - b[i]) * f;
            var vc = c[i] + (c[i + 1] - c[i]) * f;
            var vd = d[i] + (d[i + 1] - d[i]) * f;
            destination[k] = (float)(weights.A * va + weights.B * vb + weights.C * vc + weights.D * vd);
        }

        return weights.A * onsets[d0 * count + p0] + weights.B * onsets[d0 * count + p1]
            + weights.C * onsets[d1 * count + p0] + weights.D * onsets[d1 * count + p1];
    }

    public void Dispose() => logMagnitude.Dispose();

    /// <summary>First sample within 20 dB of the peak: the direct sound's arrival.</summary>
    static int Onset(float[] samples) {
        var peak = 0f;
        foreach (var s in samples) {
            peak = Math.Max(peak, Math.Abs(s));
        }

        if (peak == 0) {
            throw new InvalidDataException("A cab grid IR is silent.");
        }

        for (var i = 0; i < samples.Length; i++) {
            if (Math.Abs(samples[i]) >= 0.1f * peak) {
                return i;
            }
        }

        return 0;
    }

    static (int Low, int High, double Fraction) Bracket(IReadOnlyList<double> axis, double value) {
        if (axis.Count == 1 || value <= axis[0]) {
            return (0, 0, 0);
        }

        if (value >= axis[^1]) {
            return (axis.Count - 1, axis.Count - 1, 0);
        }

        var high = 1;
        while (axis[high] < value) {
            high++;
        }

        return (high - 1, high, (value - axis[high - 1]) / (axis[high] - axis[high - 1]));
    }

    static CabGrid FromManifest(string manifestPath) {
        var folder = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        using var stream = File.OpenRead(manifestPath);
        return Parse(
            stream,
            Path.GetFileName(folder),
            file => {
                var full = Path.GetFullPath(file, folder);
                if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
                    throw new InvalidDataException($"Cab grid file '{file}' is outside the pack.");
                }

                return WavFile.Read(full);
            }
        );
    }

    static CabGrid FromArchive(string archivePath) {
        using var archive = ZipFile.OpenRead(archivePath);
        var manifest = archive.Entries
                .Where(static e => e.Name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                .MinBy(static e => e.FullName.Length)
            ?? throw new InvalidDataException($"'{archivePath}' has no {ManifestName}.");
        var root = manifest.FullName[..^manifest.Name.Length];
        using var stream = manifest.Open();
        return Parse(
            stream,
            Path.GetFileNameWithoutExtension(archivePath),
            file => {
                var entry = archive.GetEntry(root + file.Replace('\\', '/'))
                    ?? throw new InvalidDataException($"Cab grid file '{file}' is missing from the archive.");
                using var content = entry.Open();
                using var buffer = new MemoryStream();
                content.CopyTo(buffer);
                buffer.Position = 0;
                return WavFile.Read(buffer);
            }
        );
    }

    static CabGrid Parse(Stream manifest, string fallbackName, Func<string, AudioData> read) {
        using var document = JsonDocument.Parse(manifest);
        var json = document.RootElement;
        if (json.TryGetProperty("format", out var format) && format.GetString() != "cathode-cabgrid") {
            throw new InvalidDataException("Not a cathode-cabgrid manifest.");
        }

        var name = json.TryGetProperty("name", out var named) ? named.GetString() ?? fallbackName : fallbackName;
        if (!json.TryGetProperty("irs", out var list) || list.ValueKind != JsonValueKind.Array) {
            throw new InvalidDataException("A cab grid manifest needs an \"irs\" array.");
        }

        var points = new List<(double Position, double Distance, string File)>();
        foreach (var item in list.EnumerateArray()) {
            points.Add((
                item.GetProperty("position").GetDouble(),
                item.GetProperty("distance").GetDouble(),
                item.GetProperty("file").GetString() ?? throw new InvalidDataException("A grid point has no file.")
            ));
        }

        var positions = points.Select(static p => p.Position).Distinct().Order().ToArray();
        var distances = points.Select(static p => p.Distance).Distinct().Order().ToArray();
        if (points.Count == 0 || points.Count > MaxPoints || points.Count != positions.Length * distances.Length) {
            throw new InvalidDataException(
                $"A cab grid needs every position at every distance (at most {MaxPoints} points); got {points.Count} points on {positions.Length} positions × {distances.Length} distances."
            );
        }

        if (positions[0] < 0 || positions[^1] > 100 || distances[0] < 0) {
            throw new InvalidDataException("Grid positions run 0-100 (% cap to edge) and distances from 0 (inches).");
        }

        var irs = new AudioData[positions.Length, distances.Length];
        foreach (var (position, distance, file) in points) {
            var p = Array.IndexOf(positions, position);
            var d = Array.IndexOf(distances, distance);
            if (irs[p, d] is not null) {
                throw new InvalidDataException($"Grid point {position} %, {distance} in appears twice.");
            }

            irs[p, d] = read(file);
            if (irs[p, d].Frames == 0) {
                throw new InvalidDataException($"Cab grid file '{file}' is empty.");
            }
        }

        return new(name, [..positions.Select(static p => p / 100)], distances, irs);
    }
}
