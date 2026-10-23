using Cathode.Dsp.Convolution;
using Cathode.Dsp.Filters;
using Cathode.Dsp.IO;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Cabinet simulation: up to two impulse responses (mic blend), each with its own level, plus low and high cut.
///     IRs are resampled to the engine rate, normalised to unit energy and truncated to <see cref="MaxIrLength" />.
///     Zero latency. Stereo IRs process left and right separately; mono IRs are used for both channels.
/// </summary>
public sealed class CabBlock : DspBlock, IAssetConsumer {
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

    readonly Slot[] _slots = [new(), new()];
    SvfState _lowCutLeft, _lowCutRight, _highCutLeft, _highCutRight;
    float[] _scratchLeft = [];
    float[] _scratchRight = [];
    float _gain1 = 1f, _gain2 = 1f, _outputGain = 1f;

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

    public CabBlock()
        : base(Info) { }

    /// <summary>Control thread: load a WAV impulse response into <c>ir1</c> or <c>ir2</c>.</summary>
    public void LoadAsset(string slot, string path) {
        var index = slot switch {
            "ir1" => 0,
            "ir2" => 1,
            _ => throw new ArgumentException($"Unknown asset slot '{slot}'.", nameof(slot))
        };

        var audio = WavFile.Read(path);
        if (audio.Frames == 0 || audio.Channels.Length == 0) {
            throw new InvalidDataException($"Impulse response '{path}' is empty.");
        }

        _slots[index].Source = audio;
    }

    /// <summary>The IR as it will be convolved (resampled, truncated, normalised), for tests and the editor's plot.</summary>
    public float[]? PreparedImpulseResponse(int slot, int channel = 0) => _slots[slot].Prepared?[channel];

    static void Accumulate(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        foreach (var slot in _slots) {
            slot.Build(spec.SampleRate);
        }

        _scratchLeft = new float[spec.MaxBlockSize];
        _scratchRight = new float[spec.MaxBlockSize];
    }

    protected override void OnReset() {
        foreach (var slot in _slots) {
            slot.Reset();
        }

        _lowCutLeft.Reset();
        _lowCutRight.Reset();
        _highCutLeft.Reset();
        _highCutRight.Reset();
        _gain1 = Decibels.ToGain(Params.Current(Level1));
        _gain2 = Decibels.ToGain(Params.Current(Level2));
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var gain1 = Decibels.ToGain(Params.Advance(Level1, io.Length));
        var gain2 = Decibels.ToGain(Params.Advance(Level2, io.Length));
        var first = _slots[0];
        var second = _slots[1];

        if (second.IsLoaded) {
            var left = _scratchLeft.AsSpan(0, io.Length);
            var right = _scratchRight.AsSpan(0, io.Length);
            io.Left.CopyTo(left);
            io.Right.CopyTo(right);
            second.Process(left, right);
            GainRamp.Apply(left, _gain2, gain2);
            GainRamp.Apply(right, _gain2, gain2);

            if (first.IsLoaded) {
                first.Process(io.Left, io.Right);
                GainRamp.Apply(io.Left, _gain1, gain1);
                GainRamp.Apply(io.Right, _gain1, gain1);
                Accumulate(left, io.Left);
                Accumulate(right, io.Right);
            } else {
                left.CopyTo(io.Left);
                right.CopyTo(io.Right);
            }
        } else if (first.IsLoaded) {
            first.Process(io.Left, io.Right);
            GainRamp.Apply(io.Left, _gain1, gain1);
            GainRamp.Apply(io.Right, _gain1, gain1);
        }

        _gain1 = gain1;
        _gain2 = gain2;

        var lowCut = Params.Advance(LowCut, io.Length);
        if (lowCut > LowCutOff) {
            var c = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, lowCut, 0.7071);
            _lowCutLeft.Process(c, io.Left);
            _lowCutRight.Process(c, io.Right);
        }

        var highCut = Params.Advance(HighCut, io.Length);
        if (highCut < HighCutOff) {
            var c = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, highCut, 0.7071);
            _highCutLeft.Process(c, io.Left);
            _highCutRight.Process(c, io.Right);
        }

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    sealed class Slot {
        Convolver? _left;
        Convolver? _right;

        public AudioData? Source { get; set; }

        public float[][]? Prepared { get; private set; }

        public bool IsLoaded => _left is not null;

        public void Build(double sampleRate) {
            if (Source is null) {
                return;
            }

            var channels = Source.Channels.Take(2)
                .Select(c => Shape(Resampler.Resample(c, Source.SampleRate, sampleRate)))
                .ToArray();
            var norm = 1f / MathF.Sqrt(channels.Average(static c => c.Sum(static v => v * v)));
            foreach (var channel in channels) {
                for (var i = 0; i < channel.Length; i++) {
                    channel[i] *= norm;
                }
            }

            Prepared = channels;
            _left = new(channels[0]);
            _right = new(channels[^1]);
        }

        public void Reset() {
            _left?.Reset();
            _right?.Reset();
        }

        public void Process(Span<float> left, Span<float> right) {
            _left!.Process(left);
            _right!.Process(right);
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
}
