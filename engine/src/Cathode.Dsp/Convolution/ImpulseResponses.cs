using Cathode.Dsp.IO;

namespace Cathode.Dsp.Convolution;

/// <summary>
///     Impulse response files shared by every block that convolves (Cab, IR Player, Tone Match). Files are cached by
///     path, size and write time, so a preset change back to an IR already heard reads, resamples and allocates nothing
///     for it.
/// </summary>
public static class ImpulseResponses {
    /// <summary>IR files kept decoded, most recently used first.</summary>
    const int CachedFiles = 24;

    static readonly List<ImpulseResponseFile> Cache = [];

    /// <summary>The cached IR for <paramref name="path" />, read again only when the file's size or time changed.</summary>
    public static ImpulseResponseFile Load(string path) {
        var file = new FileInfo(path);
        if (!file.Exists) {
            throw new FileNotFoundException($"Impulse response '{path}' was not found.", path);
        }

        lock (Cache) {
            var index = Cache.FindIndex(c => c.Path == file.FullName
                && c.Length == file.Length
                && c.Written == file.LastWriteTimeUtc
            );
            if (index >= 0) {
                var hit = Cache[index];
                Cache.RemoveAt(index);
                Cache.Insert(0, hit);
                return hit;
            }
        }

        var audio = WavFile.Read(path);
        if (audio.Frames == 0 || audio.Channels.Length == 0) {
            throw new InvalidDataException($"Impulse response '{path}' is empty.");
        }

        var entry = new ImpulseResponseFile(file.FullName, file.Length, file.LastWriteTimeUtc, audio);
        lock (Cache) {
            Cache.Insert(0, entry);
            if (Cache.Count > CachedFiles) {
                Cache.RemoveAt(Cache.Count - 1);
            }
        }

        return entry;
    }
}

/// <summary>How an IR is shaped for convolution.</summary>
/// <param name="MaxLength">Longest IR kept, in samples at the engine rate; longer ones fade out over 64 samples.</param>
/// <param name="UnitEnergy">Scale to unit energy (cabs), or keep the file's gain (Tone Match's correction filters).</param>
public readonly record struct IrShape(int MaxLength, bool UnitEnergy = true);

/// <summary>A decoded IR file and its prepared forms. Prepared channels are shared: read only.</summary>
public sealed class ImpulseResponseFile(string path, long length, DateTime written, AudioData source) {
    const int FadeLength = 64;

    readonly Dictionary<(double Rate, IrShape Shape), float[][]> prepared = [];

    public string Path { get; } = path;

    public long Length { get; } = length;

    public DateTime Written { get; } = written;

    public AudioData Source { get; } = source;

    /// <summary>Resampled to <paramref name="sampleRate" />, truncated and (optionally) normalised; at most two channels.</summary>
    public float[][] Prepare(double sampleRate, IrShape shape) {
        lock (prepared) {
            if (prepared.TryGetValue((sampleRate, shape), out var hit)) {
                return hit;
            }

            var channels = Source.Channels.Take(2)
                .Select(c => Truncate(Resampler.Resample(c, Source.SampleRate, sampleRate), shape.MaxLength))
                .ToArray();
            if (shape.UnitEnergy) {
                var energy = channels.Average(static float (float[] c) => c.Sum(static float (float v) => v * v));
                var norm = 1f / MathF.Sqrt(energy);
                foreach (var channel in channels) {
                    for (var i = 0; i < channel.Length; i++) {
                        channel[i] *= norm;
                    }
                }
            }

            prepared[(sampleRate, shape)] = channels;
            return channels;
        }
    }

    /// <summary>Truncates with a short raised-cosine fade so the cut does not click.</summary>
    static float[] Truncate(float[] ir, int maxLength) {
        if (ir.Length <= maxLength) {
            return ir;
        }

        var shaped = ir[..maxLength];
        for (var i = 0; i < FadeLength; i++) {
            shaped[maxLength - FadeLength + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / FadeLength));
        }

        return shaped;
    }
}

/// <summary>
///     A loaded IR as a pair of convolvers: stereo IRs process left and right separately, mono IRs both. Built on the
///     control thread (<see cref="Build" />); <see cref="Process" /> and <see cref="Reset" /> run on the audio thread.
/// </summary>
public sealed class StereoConvolver(IrShape shape) : IDisposable {
    Convolver? left;
    Convolver? right;

    public ImpulseResponseFile? Source { get; set; }

    public float[][]? Prepared { get; private set; }

    public bool IsLoaded => left is not null;

    public void Build(double sampleRate) {
        if (Source is null) {
            return;
        }

        Dispose();
        Prepared = Source.Prepare(sampleRate, shape);
        left = new(Prepared[0]);
        right = new(Prepared[^1]);
    }

    public void Reset() {
        left?.Reset();
        right?.Reset();
    }

    public void Process(Span<float> left, Span<float> right) {
        this.left!.Process(left);
        this.right!.Process(right);
    }

    public void Dispose() {
        left?.Dispose();
        right?.Dispose();
        left = null;
        right = null;
    }
}
