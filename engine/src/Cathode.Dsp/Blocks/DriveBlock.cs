using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Drive pedal with three voicings and antiderivative anti-aliased clippers:
///     <list type="bullet">
///         <item>
///             <b>Overdrive</b> (TS-style): the clipped path is high-passed around 720 Hz and summed with a clean path,
///             giving the mid hump and keeping the lows tight.
///         </item>
///         <item><b>Distortion</b> (Rat-style): high gain into a hard clipper.</item>
///         <item><b>Fuzz</b>: a biased, asymmetric soft clipper followed by a DC blocker.</item>
///     </list>
///     The whole nonlinear path (pre-filter, gain, clipper, clean-path sum, DC blocker) runs at 4x the sample rate
///     through IIR half-band oversampling, then a tone control (low-pass from 600 Hz to 8 kHz), output level and mix.
///     At partial mix the dry signal takes the same up/down filtering, so the two paths cannot comb.
/// </summary>
public sealed class DriveBlock : DspBlock {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Tone = 2;
    public const int Level = 3;
    public const int Mix = 4;

    const int Factor = 4;

    readonly Channel _left = new();
    readonly Channel _right = new();
    float _gain = 1f;
    float _outputGain = 1f;
    float _mix = 1f;
    float[] _dry = [];

    Oversampler4x _dryLeft = new(1);
    Oversampler4x _dryRight = new(1);

    public static BlockDescriptor Info { get; } = new(
        "drive",
        "Drive",
        "Drive",
        [
            ParamDescriptor.Choice("type", "Type", ["Overdrive", "Distortion", "Fuzz"]),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 5f),
            ParamDescriptor.Float("tone", "Tone", 0f, 10f, 5f),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%")
        ],
        maxInstances: 4
    );

    /// <summary>Disables antiderivative anti-aliasing, for measuring what it buys.</summary>
    public bool Antialias { get; set; } = true;

    /// <summary>Disables oversampling, for measuring what it buys. Set before <see cref="IDspBlock.Prepare" />.</summary>
    public bool Oversample { get; set; } = true;

    public DriveBlock()
        : base(Info) { }

    void ProcessChannel(
        Channel channel,
        Oversampler4x dryOversampler,
        Span<float> samples,
        Span<float> dry,
        bool blend,
        int type,
        ClipperShape shape,
        float gain,
        in SvfCoefficients pre,
        in SvfCoefficients tone,
        in SvfCoefficients dcBlock,
        float mix
    ) {
        if (blend) {
            samples.CopyTo(dry);
            if (Oversample) {
                dryOversampler.Up(dry);
                dryOversampler.Down(dry); // same phase response as the processed path
            }
        }

        channel.Process(samples, Oversample, type, shape, _gain, gain, pre, tone, dcBlock, Antialias);
        if (blend) {
            GainRamp.Mix(dry, samples, _mix, mix);
        }
    }

    /// <summary>Drive 0..10 mapped to a gain range per type, in dB.</summary>
    static float InputGain(int type, float drive) {
        var (minDb, maxDb) = type switch {
            0 => (0f, 40f),
            1 => (6f, 60f),
            _ => (10f, 50f)
        };
        return Decibels.ToGain(minDb + (maxDb - minDb) * drive / 10f);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        _dry = new float[spec.MaxBlockSize];
        _left.Allocate(spec.MaxBlockSize);
        _right.Allocate(spec.MaxBlockSize);
        _dryLeft = new(spec.MaxBlockSize);
        _dryRight = new(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        _left.Reset();
        _right.Reset();
        _dryLeft.Reset();
        _dryRight.Reset();
        _gain = InputGain((int)Params.Current(Type), Params.Current(Drive));
        _outputGain = Decibels.ToGain(Params.Current(Level));
        _mix = Params.Current(Mix) / 100f;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var innerRate = Oversample ? rate * Factor : rate;
        var type = (int)Params.Current(Type);
        var shape = type switch {
            0 => ClipperShape.Soft,
            1 => ClipperShape.Hard,
            _ => ClipperShape.Asymmetric
        };
        var gain = InputGain(type, Params.Advance(Drive, io.Length));
        var toneHz = 600f * MathF.Pow(8_000f / 600f, Params.Advance(Tone, io.Length) / 10f);
        var tone = SvfCoefficients.Design(BiquadType.LowPass, rate, toneHz, 0.7071);
        var pre = type switch {
            0 => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 720, 0.7071),
            1 => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 60, 0.7071),
            _ => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 30, 0.7071)
        };
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        var mix = Params.Advance(Mix, io.Length) / 100f;

        var dry = _dry.AsSpan(0, io.Length);
        var blend = _mix < 1f || mix < 1f;
        ProcessChannel(_left, _dryLeft, io.Left, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);
        ProcessChannel(_right, _dryRight, io.Right, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);
        _gain = gain;
        _mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    sealed class Channel {
        Oversampler4x _oversampler = new(1);
        SvfState _pre;
        SvfState _tone;
        SvfState _dc;
        AdaaClipper _clipper;

        public void Allocate(int maxBlockSize) => _oversampler = new(maxBlockSize);

        public void Reset() {
            _oversampler.Reset();
            _pre.Reset();
            _tone.Reset();
            _dc.Reset();
            _clipper.Reset();
        }

        public void Process(
            Span<float> samples,
            bool oversample,
            int type,
            ClipperShape shape,
            float fromGain,
            float toGain,
            in SvfCoefficients pre,
            in SvfCoefficients tone,
            in SvfCoefficients dcBlock,
            bool antialias
        ) {
            var inner = oversample ? _oversampler.Up(samples) : samples;
            var step = (toGain - fromGain) / inner.Length;
            for (var i = 0; i < inner.Length; i++) {
                var x = inner[i];
                var gain = fromGain + step * (i + 1);
                var driven = (float)_pre.Tick(pre, x) * gain;
                var y = _clipper.Process(shape, driven, antialias);
                inner[i] = type switch {
                    0 => 0.5f * (y + x), // TS: clipped mids plus the clean path
                    2 => (float)_dc.Tick(dcBlock, y), // fuzz: remove the bias offset
                    _ => y
                };
            }

            if (oversample) {
                _oversampler.Down(samples);
                _oversampler.FlushDenormals();
            }

            for (var i = 0; i < samples.Length; i++) {
                samples[i] = (float)_tone.Tick(tone, samples[i]);
            }

            _pre.FlushDenormals();
            _tone.FlushDenormals();
            _dc.FlushDenormals();
        }
    }
}
