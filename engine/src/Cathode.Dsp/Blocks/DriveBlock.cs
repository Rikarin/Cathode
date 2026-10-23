using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
/// Drive pedal with three voicings and antiderivative anti-aliased clippers:
/// <list type="bullet">
/// <item><b>Overdrive</b> (TS-style): the clipped path is high-passed around 720 Hz and summed with a clean path,
/// giving the mid hump and keeping the lows tight.</item>
/// <item><b>Distortion</b> (Rat-style): high gain into a hard clipper.</item>
/// <item><b>Fuzz</b>: a biased, asymmetric soft clipper followed by a DC blocker.</item>
/// </list>
/// Then a tone control (low-pass from 600 Hz to 8 kHz), output level and mix.
/// </summary>
public sealed class DriveBlock : DspBlock
{
    public const int Type = 0;
    public const int Drive = 1;
    public const int Tone = 2;
    public const int Level = 3;
    public const int Mix = 4;

    public static BlockDescriptor Info { get; } = new(
        "drive",
        "Drive",
        "Drive",
        [
            ParamDescriptor.Choice("type", "Type", ["Overdrive", "Distortion", "Fuzz"]),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 5f),
            ParamDescriptor.Float("tone", "Tone", 0f, 10f, 5f),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
        ],
        maxInstances: 4);

    private readonly Channel _left = new();
    private readonly Channel _right = new();
    private float _gain = 1f;
    private float _outputGain = 1f;
    private float _mix = 1f;
    private float[] _dry = [];

    public DriveBlock()
        : base(Info)
    {
    }

    /// <summary>Disables anti-aliasing, for measuring what it buys.</summary>
    public bool Antialias { get; set; } = true;

    protected override void OnPrepare(in ProcessSpec spec) => _dry = new float[spec.MaxBlockSize];

    protected override void OnReset()
    {
        _left.Reset();
        _right.Reset();
        _gain = InputGain((int)Params.Current(Type), Params.Current(Drive));
        _outputGain = Decibels.ToGain(Params.Current(Level));
        _mix = Params.Current(Mix) / 100f;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context)
    {
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var shape = type switch { 0 => ClipperShape.Soft, 1 => ClipperShape.Hard, _ => ClipperShape.Asymmetric };
        var gain = InputGain(type, Params.Advance(Drive, io.Length));
        var toneHz = 600f * MathF.Pow(8_000f / 600f, Params.Advance(Tone, io.Length) / 10f);
        var tone = SvfCoefficients.Design(BiquadType.LowPass, rate, toneHz, 0.7071);
        var pre = type switch
        {
            0 => SvfCoefficients.Design(BiquadType.HighPass, rate, 720, 0.7071),
            1 => SvfCoefficients.Design(BiquadType.HighPass, rate, 60, 0.7071),
            _ => SvfCoefficients.Design(BiquadType.HighPass, rate, 30, 0.7071),
        };
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, rate, 10, 0.7071);
        var mix = Params.Advance(Mix, io.Length) / 100f;

        var dry = _dry.AsSpan(0, io.Length);
        io.Left.CopyTo(dry);
        _left.Process(io.Left, type, shape, _gain, gain, pre, tone, dcBlock, Antialias);
        GainRamp.Mix(dry, io.Left, _mix, mix);
        io.Right.CopyTo(dry);
        _right.Process(io.Right, type, shape, _gain, gain, pre, tone, dcBlock, Antialias);
        GainRamp.Mix(dry, io.Right, _mix, mix);
        _gain = gain;
        _mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    /// <summary>Drive 0..10 mapped to a gain range per type, in dB.</summary>
    private static float InputGain(int type, float drive)
    {
        var (minDb, maxDb) = type switch { 0 => (0f, 40f), 1 => (6f, 60f), _ => (10f, 50f) };
        return Decibels.ToGain(minDb + (maxDb - minDb) * drive / 10f);
    }

    private sealed class Channel
    {
        private SvfState _pre;
        private SvfState _tone;
        private SvfState _dc;
        private AdaaClipper _clipper;

        public void Reset()
        {
            _pre.Reset();
            _tone.Reset();
            _dc.Reset();
            _clipper.Reset();
        }

        public void Process(
            Span<float> samples,
            int type,
            ClipperShape shape,
            float fromGain,
            float toGain,
            in SvfCoefficients pre,
            in SvfCoefficients tone,
            in SvfCoefficients dcBlock,
            bool antialias)
        {
            var step = (toGain - fromGain) / samples.Length;
            for (var i = 0; i < samples.Length; i++)
            {
                var x = samples[i];
                var gain = fromGain + step * (i + 1);
                var driven = (float)_pre.Tick(pre, x) * gain;
                var y = _clipper.Process(shape, driven, antialias);
                y = type switch
                {
                    0 => 0.5f * (y + x),                // TS: clipped mids plus the clean path
                    2 => (float)_dc.Tick(dcBlock, y),   // fuzz: remove the bias offset
                    _ => y,
                };
                samples[i] = (float)_tone.Tick(tone, y);
            }

            _pre.FlushDenormals();
            _tone.FlushDenormals();
            _dc.FlushDenormals();
        }
    }
}
