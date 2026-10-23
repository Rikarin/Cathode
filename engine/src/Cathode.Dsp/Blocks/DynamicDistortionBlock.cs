using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Dynamic distortion: a clipper whose drive follows the playing dynamics. A peak envelope follower (linked across
///     channels, with its own attack and release) measures how far the input is above <b>Threshold</b>; each dB above it
///     adds <b>Dynamics</b> % of a dB of drive (or removes it, for negative values, down to no drive), over at most
///     40 dB of envelope. Positive dynamics saturate harder as you dig in and stay clean when you play softly; negative
///     dynamics clean up hard picking.
///     The clipper is antiderivative anti-aliased and runs at 4x through the same half-band oversampling as
///     <see cref="DriveBlock" />, with the drive gain interpolated across the oversampled samples. A 10 Hz DC blocker
///     removes the asymmetric shape's offset. At partial mix the dry signal takes the same up/down filtering.
/// </summary>
public sealed class DynamicDistortionBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Dynamics = 2;
    public const int Threshold = 3;
    public const int Attack = 4;
    public const int Release = 5;
    public const int Mix = 6;
    public const int Level = 7;

    const int Factor = 4;
    const float MaxDriveDb = 40f;
    const float MaxDynamicDb = 40f;

    readonly Channel left = new();
    readonly Channel right = new();
    readonly Oversampler4x dryLeft = new(1);
    readonly Oversampler4x dryRight = new(1);
    float[] gains = [];
    float[] dry = [];
    float envelope;
    float driveDb;
    float previousGain = 1f;
    float mix = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "dyndist",
        "Dynamic Distortion",
        "Drive",
        [
            ParamDescriptor.Choice("type", "Type", ["Soft", "Hard", "Asymmetric"]),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 3f),
            ParamDescriptor.Float("dynamics", "Dynamics", -100f, 100f, 50f, "%"),
            ParamDescriptor.Float("threshold", "Threshold", -60f, 0f, -40f, "dB"),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 100f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 10f, 1_000f, 100f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    /// <summary>Drive 0..10 mapped to 0..40 dB.</summary>
    static float DriveDb(float drive) => drive * (MaxDriveDb / 10f);

    static float Coefficient(double milliseconds, double sampleRate) =>
        (float)Math.Exp(-1.0 / Math.Max(1e-6, milliseconds * 0.001 * sampleRate));

    /// <summary>Fills <see cref="gains" /> with the drive gain for each input frame, from the linked envelope.</summary>
    void ComputeGains(in StereoBuffer io, Span<float> gains) {
        var rate = Spec.SampleRate;
        var driveDb = DriveDb(Params.Advance(Drive, io.Length));
        var driveStep = (driveDb - this.driveDb) / io.Length;
        var dynamics = Params.Advance(Dynamics, io.Length) / 100f;
        var threshold = Params.Advance(Threshold, io.Length);
        var attack = Coefficient(Params.Advance(Attack, io.Length), rate);
        var release = Coefficient(Params.Advance(Release, io.Length), rate);
        var envelope = this.envelope;

        for (var i = 0; i < io.Length; i++) {
            var peak = MathF.Max(MathF.Abs(io.Left[i]), MathF.Abs(io.Right[i]));
            envelope = peak + (envelope - peak) * (peak > envelope ? attack : release);
            var levelDb = envelope > 1e-10f ? 20f * MathF.Log10(envelope) : -200f;
            var over = Math.Clamp(levelDb - threshold, 0f, MaxDynamicDb);
            var gainDb = Math.Max(0f, this.driveDb + driveStep * (i + 1) + dynamics * over);
            gains[i] = MathF.Pow(10f, gainDb / 20f);
        }

        this.envelope = envelope < 1e-20f ? 0 : envelope;
        this.driveDb = driveDb;
    }

    void ProcessChannel(
        Channel channel,
        Oversampler4x dryOversampler,
        Span<float> samples,
        Span<float> dry,
        ReadOnlySpan<float> gains,
        bool blend,
        ClipperShape shape,
        in SvfCoefficients dcBlock,
        float mix
    ) {
        if (blend) {
            samples.CopyTo(dry);
            dryOversampler.Up(dry);
            dryOversampler.Down(dry); // same phase response as the processed path
        }

        channel.Process(samples, gains, previousGain, shape, dcBlock);
        if (blend) {
            GainRamp.Mix(dry, samples, this.mix, mix);
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        if (gains.Length != spec.MaxBlockSize) {
            gains = new float[spec.MaxBlockSize];
            dry = new float[spec.MaxBlockSize];
        }

        left.Allocate(spec.MaxBlockSize);
        right.Allocate(spec.MaxBlockSize);
        dryLeft.Resize(spec.MaxBlockSize);
        dryRight.Resize(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        dryLeft.Reset();
        dryRight.Reset();
        envelope = 0;
        driveDb = DriveDb(Params.Current(Drive));
        previousGain = Decibels.ToGain(driveDb);
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var shape = (ClipperShape)(int)Params.Current(Type);
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate * Factor, 10, 0.7071);
        var gains = this.gains.AsSpan(0, io.Length);
        ComputeGains(io, gains);

        var mix = Params.Advance(Mix, io.Length) / 100f;
        var dry = this.dry.AsSpan(0, io.Length);
        var blend = this.mix < 1f || mix < 1f;
        ProcessChannel(left, dryLeft, io.Left, dry, gains, blend, shape, dcBlock, mix);
        ProcessChannel(right, dryRight, io.Right, dry, gains, blend, shape, dcBlock, mix);
        previousGain = gains[^1];
        this.mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    sealed class Channel {
        readonly Oversampler4x oversampler = new(1);
        SvfState dc;
        AdaaClipper clipper;

        public void Allocate(int maxBlockSize) => oversampler.Resize(maxBlockSize);

        public void Reset() {
            oversampler.Reset();
            dc.Reset();
            clipper.Reset();
        }

        /// <summary>
        ///     Clips <paramref name="samples" /> at 4x, the drive moving linearly from one frame's gain to the next across
        ///     the oversampled samples between them.
        /// </summary>
        public void Process(
            Span<float> samples,
            ReadOnlySpan<float> gains,
            float previousGain,
            ClipperShape shape,
            in SvfCoefficients dcBlock
        ) {
            var inner = oversampler.Up(samples);
            var from = previousGain;
            for (var i = 0; i < samples.Length; i++) {
                var to = gains[i];
                var step = (to - from) / Factor;
                for (var k = 0; k < Factor; k++) {
                    var n = i * Factor + k;
                    var y = clipper.Process(shape, inner[n] * (from + step * (k + 1)));
                    inner[n] = (float)dc.Tick(dcBlock, y);
                }

                from = to;
            }

            oversampler.Down(samples);
            oversampler.FlushDenormals();
            dc.FlushDenormals();
        }
    }
}
