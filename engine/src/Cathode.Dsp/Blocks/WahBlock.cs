using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Wah: a resonant band-pass swept exponentially over the voicing's range by the pedal position, with some low-pass
///     "body" mixed in as in the inductor circuit. Coefficients follow the smoothed position every
///     <see cref="SubBlock" /> samples, so an expression pedal or modifier can drive it smoothly.
/// </summary>
public sealed class WahBlock : DspBlock {
    public const int Type = 0;
    public const int Position = 1;
    public const int Mix = 2;
    public const int Level = 3;

    const int SubBlock = 16;
    const float PeakGain = 2f;
    const float Body = 0.15f;

    /// <summary>Heel-down frequency, toe-down frequency and Q per voicing.</summary>
    static readonly (float Low, float High, float Q)[] Voicings = [
        (350f, 2_200f, 5f), // Classic (Cry Baby style)
        (450f, 2_000f, 4.5f), // Vox style
        (300f, 3_000f, 6f) // Wide
    ];

    SvfState bandLeft, bandRight, lowLeft, lowRight;
    float mix = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "wah",
        "Wah",
        "Filter",
        [
            ParamDescriptor.Choice("type", "Type", ["Classic", "Vox", "Wide"]),
            new(
                "position",
                "Position",
                ParamKind.Float,
                0f,
                100f,
                50f,
                "%",
                Smoothing: ParamSmoothing.Linear,
                SmoothingMs: 10f
            ),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 2
    );

    public WahBlock()
        : base(Info) { }

    /// <summary>Resonant frequency for a pedal position (0..100 %) and voicing.</summary>
    public static float Frequency(int type, float position) {
        var (low, high, _) = Voicings[type];
        return low * MathF.Pow(high / low, Math.Clamp(position, 0f, 100f) / 100f);
    }

    static float Voice(
        float x,
        in SvfCoefficients band,
        in SvfCoefficients low,
        ref SvfState bandState,
        ref SvfState lowState,
        float mix
    ) {
        var wet = (float)(PeakGain * bandState.Tick(band, x) + Body * lowState.Tick(low, x));
        return x * (1 - mix) + wet * mix;
    }

    protected override void OnReset() {
        bandLeft.Reset();
        bandRight.Reset();
        lowLeft.Reset();
        lowRight.Reset();
        this.mix = Params.Current(Mix) / 100f;
        this.outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        var q = Voicings[type].Q;
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var currentMix = this.mix;

        for (var start = 0; start < io.Length; start += SubBlock) {
            var length = Math.Min(SubBlock, io.Length - start);
            var frequency = Frequency(type, Params.Advance(Position, length));
            var band = SvfCoefficients.Design(BiquadType.BandPass, Spec.SampleRate, frequency, q);
            var low = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, 0.7071);
            for (var i = start; i < start + length; i++) {
                currentMix += mixStep;
                io.Left[i] = Voice(io.Left[i], band, low, ref bandLeft, ref lowLeft, currentMix);
                io.Right[i] = Voice(io.Right[i], band, low, ref bandRight, ref lowRight, currentMix);
            }
        }

        bandLeft.FlushDenormals();
        bandRight.FlushDenormals();
        lowLeft.FlushDenormals();
        lowRight.FlushDenormals();
        this.mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }
}
