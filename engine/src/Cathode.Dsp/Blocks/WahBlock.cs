using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
/// Wah: a resonant band-pass swept exponentially over the voicing's range by the pedal position, with some low-pass
/// "body" mixed in as in the inductor circuit. Coefficients follow the smoothed position every
/// <see cref="SubBlock"/> samples, so an expression pedal or modifier can drive it smoothly.
/// </summary>
public sealed class WahBlock : DspBlock
{
    public const int Type = 0;
    public const int Position = 1;
    public const int Mix = 2;
    public const int Level = 3;

    private const int SubBlock = 16;
    private const float PeakGain = 2f;
    private const float Body = 0.15f;

    /// <summary>Heel-down frequency, toe-down frequency and Q per voicing.</summary>
    private static readonly (float Low, float High, float Q)[] Voicings =
    [
        (350f, 2_200f, 5f),   // Classic (Cry Baby style)
        (450f, 2_000f, 4.5f), // Vox style
        (300f, 3_000f, 6f),   // Wide
    ];

    public static BlockDescriptor Info { get; } = new(
        "wah",
        "Wah",
        "Filter",
        [
            ParamDescriptor.Choice("type", "Type", ["Classic", "Vox", "Wide"]),
            new("position", "Position", ParamKind.Float, 0f, 100f, 50f, "%", Smoothing: ParamSmoothing.Linear, SmoothingMs: 10f),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
        ],
        maxInstances: 2);

    private SvfState _bandLeft, _bandRight, _lowLeft, _lowRight;
    private float _mix = 1f;
    private float _outputGain = 1f;

    public WahBlock()
        : base(Info)
    {
    }

    /// <summary>Resonant frequency for a pedal position (0..100 %) and voicing.</summary>
    public static float Frequency(int type, float position)
    {
        var (low, high, _) = Voicings[type];
        return low * MathF.Pow(high / low, Math.Clamp(position, 0f, 100f) / 100f);
    }

    protected override void OnReset()
    {
        _bandLeft.Reset();
        _bandRight.Reset();
        _lowLeft.Reset();
        _lowRight.Reset();
        _mix = Params.Current(Mix) / 100f;
        _outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context)
    {
        var type = (int)Params.Current(Type);
        var q = Voicings[type].Q;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - _mix) / io.Length;
        var currentMix = _mix;

        for (var start = 0; start < io.Length; start += SubBlock)
        {
            var length = Math.Min(SubBlock, io.Length - start);
            var frequency = Frequency(type, Params.Advance(Position, length));
            var band = SvfCoefficients.Design(BiquadType.BandPass, Spec.SampleRate, frequency, q);
            var low = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, 0.7071);
            for (var i = start; i < start + length; i++)
            {
                currentMix += mixStep;
                io.Left[i] = Voice(io.Left[i], band, low, ref _bandLeft, ref _lowLeft, currentMix);
                io.Right[i] = Voice(io.Right[i], band, low, ref _bandRight, ref _lowRight, currentMix);
            }
        }

        _bandLeft.FlushDenormals();
        _bandRight.FlushDenormals();
        _lowLeft.FlushDenormals();
        _lowRight.FlushDenormals();
        _mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, _outputGain, outputGain);
        GainRamp.Apply(io.Right, _outputGain, outputGain);
        _outputGain = outputGain;
    }

    private static float Voice(float x, in SvfCoefficients band, in SvfCoefficients low, ref SvfState bandState, ref SvfState lowState, float mix)
    {
        var wet = (float)(PeakGain * bandState.Tick(band, x) + Body * lowState.Tick(low, x));
        return x * (1 - mix) + wet * mix;
    }
}
