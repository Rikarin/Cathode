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
///     removes the asymmetric shape's offset (and the Bias's). At partial mix the dry signal takes the same up/down
///     filtering.
///     <para>
///         The frequency-dependent part: <b>Filter Type</b> (a low shelf, high shelf or peak at <b>Frequency</b>, with
///         <b>Q</b> and <b>Gain</b>) emphasises a band ahead of the clipper and the inverse filter takes it back out
///         after, so a quiet signal comes through flat while the emphasised band clips first and hardest. <b>Bias</b>
///         offsets the clipper for even harmonics; <b>Mono</b> sums the input and runs one channel to both outputs.
///     </para>
/// </summary>
public sealed class DynamicDistortionBlock() : DspBlock(Info), IOversampledBlock {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Dynamics = 2;
    public const int Threshold = 3;
    public const int Attack = 4;
    public const int Release = 5;
    public const int Mix = 6;
    public const int Level = 7;
    public const int Balance = 8;
    public const int FilterType = 9;
    public const int FilterFrequency = 10;
    public const int FilterQ = 11;
    public const int FilterGain = 12;
    public const int Bias = 13;
    public const int Mode = 14;

    // Values of Filter Type.
    public const int FilterNone = 0;
    public const int FilterLowShelf = 1;
    public const int FilterHighShelf = 2;
    public const int FilterPeak = 3;

    /// <summary>The clipper's input offset at a Bias of 100 %.</summary>
    public const float FullBias = 0.5f;

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
    float bias;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "dyndist",
        "Dynamic Distortion",
        "Drive",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                [
                    "Soft", "Hard", "Asymmetric", "Germanium", "LED", "Tube", "Op-Amp", "FET", "Rectifier", "Variable",
                    "HV Tube", "LV Tube", "Silicon", "4558 Diode"
                ]
            ),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 3f),
            ParamDescriptor.Float("dynamics", "Dynamics", -100f, 100f, 50f, "%"),
            ParamDescriptor.Float("threshold", "Threshold", -60f, 0f, -40f, "dB"),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 100f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 10f, 1_000f, 100f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("filterType", "Filter Type", ["None", "Low Shelf", "High Shelf", "Peaking"]),
            ParamDescriptor.Float("filterFreq", "Frequency", 50f, 10_000f, 1_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("filterQ", "Q", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log),
            ParamDescriptor.Float("filterGain", "Gain", -24f, 24f, 12f, "dB"),
            ParamDescriptor.Float("bias", "Bias", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("mode", "Mode", ["Stereo", "Mono"])
        ],
        maxInstances: 2
    );

    /// <summary>The oversamplers' filters (the global setting); set before <see cref="IDspBlock.Prepare" />.</summary>
    public OversamplingPhase OversamplingPhase { get; set; }

    public override int LatencySamples => dryLeft.LatencySamples;

    /// <summary>The emphasis filter's type for Filter Type <paramref name="filter" />.</summary>
    public static BiquadType EmphasisType(int filter) =>
        filter switch {
            FilterLowShelf => BiquadType.LowShelf,
            FilterHighShelf => BiquadType.HighShelf,
            _ => BiquadType.Peak
        };

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
        in Emphasis emphasis,
        float bias,
        float mix
    ) {
        if (blend) {
            samples.CopyTo(dry);
            dryOversampler.Up(dry);
            dryOversampler.Down(dry); // same phase response as the processed path
        }

        channel.Process(samples, gains, previousGain, shape, dcBlock, emphasis, this.bias, bias);
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
        if (dryLeft.Phase != OversamplingPhase) {
            left.SetPhase(OversamplingPhase);
            right.SetPhase(OversamplingPhase);
            dryLeft.SetPhase(OversamplingPhase);
            dryRight.SetPhase(OversamplingPhase);
        }
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
        bias = Params.Current(Bias) / 100f * FullBias;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var shape = (ClipperShape)(int)Params.Current(Type);
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate * Factor, 10, 0.7071);
        var mono = (int)Params.Current(Mode) == 1;
        if (mono) {
            for (var i = 0; i < io.Length; i++) {
                io.Left[i] = 0.5f * (io.Left[i] + io.Right[i]);
            }

            io.Left.CopyTo(io.Right);
        }

        var gains = this.gains.AsSpan(0, io.Length);
        ComputeGains(io, gains);

        var filter = (int)Params.Current(FilterType);
        var frequency = Params.Advance(FilterFrequency, io.Length);
        var q = Params.Advance(FilterQ, io.Length);
        var filterGain = Params.Advance(FilterGain, io.Length);
        var emphasis = filter == FilterNone || filterGain == 0f
            ? default
            : new Emphasis(
                true,
                SvfCoefficients.Design(EmphasisType(filter), Spec.SampleRate, frequency, q, filterGain),
                SvfCoefficients.Design(EmphasisType(filter), Spec.SampleRate, frequency, q, -filterGain)
            );
        var bias = Params.Advance(Bias, io.Length) / 100f * FullBias;

        var mix = Params.Advance(Mix, io.Length) / 100f;
        var dry = this.dry.AsSpan(0, io.Length);
        var blend = this.mix < 1f || mix < 1f;
        ProcessChannel(left, dryLeft, io.Left, dry, gains, blend, shape, dcBlock, emphasis, bias, mix);
        if (mono) {
            io.Left.CopyTo(io.Right);
        } else {
            ProcessChannel(right, dryRight, io.Right, dry, gains, blend, shape, dcBlock, emphasis, bias, mix);
        }

        previousGain = gains[^1];
        this.mix = mix;
        this.bias = bias;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>The emphasis filter ahead of the clipper and its inverse after it, or none.</summary>
    readonly record struct Emphasis(bool On, SvfCoefficients Pre, SvfCoefficients Post);

    sealed class Channel {
        readonly Oversampler4x oversampler = new(1);
        SvfState dc;
        SvfState pre;
        SvfState post;
        AdaaClipper clipper;

        public void Allocate(int maxBlockSize) => oversampler.Resize(maxBlockSize);

        public void SetPhase(OversamplingPhase phase) => oversampler.SetPhase(phase);

        public void Reset() {
            oversampler.Reset();
            dc.Reset();
            pre.Reset();
            post.Reset();
            clipper.Reset();
        }

        /// <summary>
        ///     Clips <paramref name="samples" /> at 4x, the drive moving linearly from one frame's gain to the next across
        ///     the oversampled samples between them, the bias from <paramref name="fromBias" /> to
        ///     <paramref name="toBias" /> across the block.
        /// </summary>
        public void Process(
            Span<float> samples,
            ReadOnlySpan<float> gains,
            float previousGain,
            ClipperShape shape,
            in SvfCoefficients dcBlock,
            in Emphasis emphasis,
            float fromBias,
            float toBias
        ) {
            if (emphasis.On) {
                pre.Process(emphasis.Pre, samples);
            } else {
                pre.Reset(); // a filter switched back in starts clean
                post.Reset();
            }

            var inner = oversampler.Up(samples);
            var from = previousGain;
            var biased = fromBias != 0f || toBias != 0f;
            var biasStep = (toBias - fromBias) / inner.Length;
            for (var i = 0; i < samples.Length; i++) {
                var to = gains[i];
                var step = (to - from) / Factor;
                for (var k = 0; k < Factor; k++) {
                    var n = i * Factor + k;
                    var x = inner[n] * (from + step * (k + 1));
                    if (biased) {
                        x += fromBias + biasStep * (n + 1);
                    }

                    var y = clipper.Process(shape, x);
                    inner[n] = (float)dc.Tick(dcBlock, y);
                }

                from = to;
            }

            oversampler.Down(samples);
            oversampler.FlushDenormals();
            dc.FlushDenormals();
            if (emphasis.On) {
                post.Process(emphasis.Post, samples);
            }
        }
    }
}
