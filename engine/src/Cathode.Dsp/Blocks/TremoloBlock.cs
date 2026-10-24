using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Tremolo (amplitude modulation) or auto-panner, with the tube-amp variants:
///     <list type="bullet">
///         <item>Tremolo: gain from 1 down to 1 − depth at the LFO's trough.</item>
///         <item>
///             Panner: places the mono sum with a constant-power law, scaled so the centre position is unity on each side;
///             Pan Center shifts the middle of the swing.
///         </item>
///         <item>Bias Trem: the tremolo gain ahead of a gentle tanh stage, so loud notes saturate as a biased tube would.</item>
///         <item>
///             Harmonic Trem: a complementary one-pole split at the crossover frequency, low and high bands modulated in
///             antiphase, as in the brown-panel amps' circuit. With no depth the bands sum back exactly.
///         </item>
///         <item>
///             Optical Trem: the LFO lights a lamp read by a photocell that brightens in 5 ms and darkens in 35 ms, which
///             gives the familiar lopsided pulse.
///         </item>
///     </list>
///     The right channel's LFO is offset by LFO Phase (tremolo types only), and Tempo locks the rate to the rig tempo.
/// </summary>
public sealed class TremoloBlock() : DspBlock(Info), IEngageAware {
    public const int Type = 0;
    public const int Rate = 1;
    public const int Depth = 2;
    public const int Shape = 3;
    public const int Level = 4;
    public const int Balance = 5;
    public const int Tempo = 6;
    public const int Duty = 7;
    public const int Phase = 8;
    public const int PanCenter = 9;
    public const int Crossover = 10;
    public const int StartPhase = 11;

    public const int Tremolo = 0;
    public const int Panner = 1;
    public const int BiasTrem = 2;
    public const int HarmonicTrem = 3;
    public const int OpticalTrem = 4;

    const float BiasDrive = 1.5f;
    const double PhotocellAttackMs = 5;
    const double PhotocellReleaseMs = 35;

    Lfo lfo;
    float lightLeft = 1f;
    float lightRight = 1f;
    float lowLeft;
    float lowRight;
    float attack;
    float release;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "tremolo",
        "Tremolo/Panner",
        "Modulation",
        [
            ParamDescriptor.Choice("type", "Type", ["Tremolo", "Panner", "Bias Trem", "Harmonic Trem", "Optical Trem"]),
            ParamDescriptor.Float("rate", "Rate", 0.1f, 20f, 4f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("depth", "Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Choice("shape", "LFO Type", Lfo.ShapeNames),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("duty", "LFO Duty", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("phase", "LFO Phase", 0f, 180f, 0f, "°"),
            ParamDescriptor.Float("panCenter", "Pan Center", -100f, 100f, 0f, "%"),
            ParamDescriptor.Float("crossover", "Crossover Freq", 100f, 3_000f, 700f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("startPhase", "Start Phase", 0f, 360f, 0f, "°")
        ],
        maxInstances: 2
    );

    protected override void OnPrepare(in ProcessSpec spec) {
        attack = (float)(1 - Math.Exp(-1 / (PhotocellAttackMs * 0.001 * spec.SampleRate)));
        release = (float)(1 - Math.Exp(-1 / (PhotocellReleaseMs * 0.001 * spec.SampleRate)));
    }

    /// <summary>Start Phase: engaging the block starts the LFO at this phase, so the first pulse lands where it should.</summary>
    public void Engage() => lfo.Reset(Params.GetTarget(StartPhase) / 360.0);

    protected override void OnReset() {
        lfo.Reset(Params.Current(StartPhase) / 360.0);
        lightLeft = 1f;
        lightRight = 1f;
        lowLeft = 0;
        lowRight = 0;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var type = (int)Params.Current(Type);
        var shape = (LfoShape)(int)Params.Current(Shape);
        var lfoRate = TempoDivisions.Rate((int)Params.Current(Tempo), context.Tempo, Params.Advance(Rate, io.Length));
        var depth = Params.Advance(Depth, io.Length) / 100f;
        var duty = Params.Advance(Duty, io.Length) / 100.0;
        var phase = Params.Advance(Phase, io.Length) / 360.0;
        var center = Params.Advance(PanCenter, io.Length) / 100f;
        var split = 1f - MathF.Exp(-2f * MathF.PI * Params.Advance(Crossover, io.Length) / (float)rate);

        for (var i = 0; i < io.Length; i++) {
            var lfoLeft = lfo.Value(shape, 0, duty);
            var lfoRight = phase == 0 ? lfoLeft : lfo.Value(shape, phase, duty);
            lfo.Advance(lfoRate, rate);
            var left = io.Left[i];
            var right = io.Right[i];
            switch (type) {
                case Panner: {
                    var angle = Math.Clamp(depth * lfoLeft + center + 1, 0f, 2f) * MathF.PI / 4; // 0 = left, pi/2 = right
                    var mono = 0.5f * (left + right);
                    io.Left[i] = mono * MathF.Sqrt(2) * MathF.Cos(angle);
                    io.Right[i] = mono * MathF.Sqrt(2) * MathF.Sin(angle);
                    break;
                }

                case BiasTrem:
                    io.Left[i] = MathF.Tanh(BiasDrive * left * Gain(depth, lfoLeft)) / BiasDrive;
                    io.Right[i] = MathF.Tanh(BiasDrive * right * Gain(depth, lfoRight)) / BiasDrive;
                    break;
                case HarmonicTrem:
                    lowLeft += split * (left - lowLeft);
                    lowRight += split * (right - lowRight);
                    io.Left[i] = lowLeft * Gain(depth, lfoLeft) + (left - lowLeft) * Gain(depth, -lfoLeft);
                    io.Right[i] = lowRight * Gain(depth, lfoRight) + (right - lowRight) * Gain(depth, -lfoRight);
                    break;
                case OpticalTrem:
                    lightLeft = Photocell(lightLeft, 0.5f * (1 + lfoLeft));
                    lightRight = Photocell(lightRight, 0.5f * (1 + lfoRight));
                    io.Left[i] = left * (1 - depth * (1 - lightLeft));
                    io.Right[i] = right * (1 - depth * (1 - lightRight));
                    break;
                default:
                    io.Left[i] = left * Gain(depth, lfoLeft);
                    io.Right[i] = right * Gain(depth, lfoRight);
                    break;
            }
        }

        if (MathF.Abs(lowLeft) < 1e-20f) {
            lowLeft = 0;
        }

        if (MathF.Abs(lowRight) < 1e-20f) {
            lowRight = 0;
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    /// <summary>1 at the LFO's peak, 1 − depth at its trough.</summary>
    static float Gain(float depth, float lfo) => 1 - depth * 0.5f * (1 - lfo);

    /// <summary>The photocell follows the lamp quickly as it brightens and slowly as it darkens.</summary>
    float Photocell(float light, float lamp) => light + (lamp > light ? attack : release) * (lamp - light);
}
