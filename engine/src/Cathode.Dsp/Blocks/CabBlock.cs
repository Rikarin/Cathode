using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.IO;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Cabinet simulation: up to two impulse responses (mic blend), each with its own level, plus low and high cut.
///     IRs are resampled to the engine rate, normalised to unit energy and truncated to <see cref="MaxIrLength" />.
///     Zero latency. Stereo IRs process left and right separately; mono IRs are used for both channels. Prepared IRs
///     are cached by file, so a preset change back to a cab already heard reads, resamples and allocates nothing for
///     it; the convolvers keep their state in native memory.
/// </summary>
public sealed class CabBlock() : DspBlock(Info), IAssetConsumer {
    public const int Level1 = 0;
    public const int Level2 = 1;
    public const int LowCut = 2;
    public const int HighCut = 3;
    public const int Level = 4;

    /// <summary>Longest IR kept, in samples at the engine rate (about 170 ms at 48 kHz).</summary>
    public const int MaxIrLength = 8_192;

    /// <summary>Low cut at or below this is off.</summary>
    public const float LowCutOff = 10f;

    /// <summary>High cut at or above this is off.</summary>
    public const float HighCutOff = 20_000f;

    const int FadeLength = 64;

    /// <summary>IR files kept prepared, most recently used first.</summary>
    const int CachedIrs = 16;

    static readonly List<CachedIr> Cache = [];

    readonly Slot[] slots = [new(), new()];
    SvfState lowCutLeft, lowCutRight, highCutLeft, highCutRight;
    NativeBuffer scratchLeft = NativeBuffer.Empty;
    NativeBuffer scratchRight = NativeBuffer.Empty;
    float gain1 = 1f, gain2 = 1f, outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "cab",
        "Cab",
        "Cab",
        [
            ParamDescriptor.Float("level1", "IR 1 Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("level2", "IR 2 Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("lowcut", "Low Cut", LowCutOff, 500f, LowCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("highcut", "High Cut", 1_000f, HighCutOff, HighCutOff, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public IReadOnlyList<string> AssetSlots { get; } = ["ir1", "ir2"];

    /// <summary>Control thread: load a WAV impulse response into <c>ir1</c> or <c>ir2</c>.</summary>
    public void LoadAsset(string slot, string path) {
        var index = slot switch {
            "ir1" => 0,
            "ir2" => 1,
            _ => throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot))
        };

        slots[index].Source = Load(path);
    }

    /// <summary>The IR as it will be convolved (resampled, truncated, normalised), for tests and the editor's plot.</summary>
    public float[]? PreparedImpulseResponse(int slot, int channel = 0) => slots[slot].Prepared?[channel];

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var slot in slots) {
                slot.Dispose();
            }

            scratchLeft.Dispose();
            scratchRight.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The cached IR for <paramref name="path" />, read again only when the file's size or time changed.</summary>
    static CachedIr Load(string path) {
        var file = new FileInfo(path);
        if (!file.Exists) {
            throw new FileNotFoundException($"Impulse response '{path}' was not found.", path);
        }

        lock (Cache) {
            var index = Cache.FindIndex(c => c.Path == file.FullName
                && c.Length == file.Length
                && c.Written == file.LastWriteTimeUtc);
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

        var entry = new CachedIr(file.FullName, file.Length, file.LastWriteTimeUtc, audio);
        lock (Cache) {
            Cache.Insert(0, entry);
            if (Cache.Count > CachedIrs) {
                Cache.RemoveAt(Cache.Count - 1);
            }
        }

        return entry;
    }

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        foreach (var slot in slots) {
            slot.Build(spec.SampleRate);
        }

        scratchLeft.Dispose();
        scratchRight.Dispose();
        scratchLeft = new(spec.MaxBlockSize);
        scratchRight = new(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        foreach (var slot in slots) {
            slot.Reset();
        }

        lowCutLeft.Reset();
        lowCutRight.Reset();
        highCutLeft.Reset();
        highCutRight.Reset();
        gain1 = Decibels.ToGain(Params.Current(Level1));
        gain2 = Decibels.ToGain(Params.Current(Level2));
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var gain1 = Decibels.ToGain(Params.Advance(Level1, io.Length));
        var gain2 = Decibels.ToGain(Params.Advance(Level2, io.Length));
        var first = slots[0];
        var second = slots[1];

        if (second.IsLoaded) {
            var left = scratchLeft.Span[..io.Length];
            var right = scratchRight.Span[..io.Length];
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            second.Process(left, right);
            GainRamp.Apply(left, this.gain2, gain2);
            GainRamp.Apply(right, this.gain2, gain2);

            if (first.IsLoaded) {
                first.Process(io.Left, io.Right);
                GainRamp.Apply(io.Left, this.gain1, gain1);
                GainRamp.Apply(io.Right, this.gain1, gain1);
                Accumulate(left, io.Left);
                Accumulate(right, io.Right);
            } else {
                left.CopyTo(io.Left);
                right.CopyTo(io.Right);
            }
        } else if (first.IsLoaded) {
            first.Process(io.Left, io.Right);
            GainRamp.Apply(io.Left, this.gain1, gain1);
            GainRamp.Apply(io.Right, this.gain1, gain1);
        }

        this.gain1 = gain1;
        this.gain2 = gain2;

        var lowCut = Params.Advance(LowCut, io.Length);
        if (lowCut > LowCutOff) {
            var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, lowCut, 0.7071);
            lowCutLeft.Process(c, io.Left);
            lowCutRight.Process(c, io.Right);
        }

        var highCut = Params.Advance(HighCut, io.Length);
        if (highCut < HighCutOff) {
            var c = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, highCut, 0.7071);
            highCutLeft.Process(c, io.Left);
            highCutRight.Process(c, io.Right);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    /// <summary>A decoded IR file and its prepared form at the last sample rate asked for.</summary>
    sealed class CachedIr(string path, long length, DateTime written, AudioData source) {
        (double Rate, float[][] Channels)? prepared;

        public string Path { get; } = path;
        public long Length { get; } = length;
        public DateTime Written { get; } = written;

        /// <summary>Resampled, truncated and normalised for <paramref name="sampleRate" />. Shared: read only.</summary>
        public float[][] Prepare(double sampleRate) {
            lock (this) {
                if (prepared is { } p && p.Rate == sampleRate) {
                    return p.Channels;
                }

                var channels = source.Channels.Take(2)
                    .Select(c => Shape(Resampler.Resample(c, source.SampleRate, sampleRate)))
                    .ToArray();
                var norm = 1f / MathF.Sqrt(channels.Average(static c => c.Sum(static v => v * v)));
                foreach (var channel in channels) {
                    for (var i = 0; i < channel.Length; i++) {
                        channel[i] *= norm;
                    }
                }

                prepared = (sampleRate, channels);
                return channels;
            }
        }

        /// <summary>Truncates to <see cref="MaxIrLength" /> with a short raised-cosine fade so the cut does not click.</summary>
        static float[] Shape(float[] ir) {
            if (ir.Length <= MaxIrLength) {
                return ir;
            }

            var shaped = ir[..MaxIrLength];
            for (var i = 0; i < FadeLength; i++) {
                shaped[MaxIrLength - FadeLength + i] *= 0.5f * (1f + MathF.Cos(MathF.PI * (i + 1) / FadeLength));
            }

            return shaped;
        }
    }

    sealed class Slot : IDisposable {
        Convolver? left;
        Convolver? right;

        public CachedIr? Source { get; set; }
        public float[][]? Prepared { get; private set; }
        public bool IsLoaded => left is not null;

        public void Build(double sampleRate) {
            if (Source is null) {
                return;
            }

            Dispose();
            Prepared = Source.Prepare(sampleRate);
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
}
