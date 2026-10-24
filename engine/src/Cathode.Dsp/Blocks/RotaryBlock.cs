using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Rotary speaker (Leslie 122/147 style). The mono input goes through an optional tube-style drive, then a
///     Linkwitz–Riley crossover splits it between the treble horn and the bass drum. Each rotor is a rotating source
///     heard by two microphones placed <c>spread</c> degrees apart: the changing path length to each mic is a modulated
///     delay (Doppler), and the rotor's directivity is an amplitude modulation. The slow/fast switch moves each rotor
///     towards its new speed with its own acceleration and deceleration inertia, as the real motors and belts do.
/// </summary>
/// <remarks>
///     Rotor speed approaches its target exponentially; the acceleration and deceleration times are the time to
///     come within 5 % of the new speed (three time constants).
/// </remarks>
public sealed class RotaryBlock() : DspBlock(Info) {
    public const int Speed = 0;
    public const int HornSlow = 1;
    public const int HornFast = 2;
    public const int DrumSlow = 3;
    public const int DrumFast = 4;
    public const int HornAccel = 5;
    public const int HornDecel = 6;
    public const int DrumAccel = 7;
    public const int DrumDecel = 8;
    public const int Crossover = 9;
    public const int HornDepth = 10;
    public const int DrumDepth = 11;
    public const int AmDepth = 12;
    public const int Spread = 13;
    public const int HornLevel = 14;
    public const int DrumLevel = 15;
    public const int Drive = 16;
    public const int Mix = 17;
    public const int Level = 18;
    public const int Balance = 19;
    public const int InputSelect = 20;

    /// <summary>Peak Doppler path-length change of the horn, in ms (a ~17 cm horn radius).</summary>
    public const float MaxHornExcursionMs = 0.5f;

    /// <summary>Peak Doppler path-length change of the drum, in ms.</summary>
    public const float MaxDrumExcursionMs = 0.6f;

    /// <summary>Deepest amplitude dip of the horn as it faces away from a mic, at 100 % AM depth.</summary>
    public const float HornAmScale = 0.9f;

    /// <summary>The drum's baffle is far less directional than the horn.</summary>
    public const float DrumAmScale = 0.5f;

    /// <summary>Starting angle of the drum, in cycles, so the rotors are not in lockstep.</summary>
    const double DrumStartPhase = 0.25;

    const double ButterworthQ = 0.70710678118654752;

    readonly FractionalDelayLine hornLine = new();
    readonly FractionalDelayLine drumLine = new();
    SvfCoefficients lowPass;
    SvfCoefficients highPass;
    SvfState low1;
    SvfState low2;
    SvfState high1;
    SvfState high2;
    float crossover;
    double hornRate;
    double drumRate;
    double hornPhase;
    double drumPhase;
    float mix;
    float hornGain;
    float drumGain;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "rotary",
        "Rotary",
        "Modulation",
        [
            ParamDescriptor.Choice("speed", "Speed", ["Slow", "Fast"]),
            ParamDescriptor.Float("hornSlow", "Horn Slow", 0.1f, 3f, 0.83f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("hornFast", "Horn Fast", 2f, 10f, 6.7f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("drumSlow", "Drum Slow", 0.1f, 3f, 0.67f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("drumFast", "Drum Fast", 2f, 10f, 5.8f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("hornAccel", "Horn Accel", 0.05f, 10f, 0.8f, "s", ParamTaper.Log),
            ParamDescriptor.Float("hornDecel", "Horn Decel", 0.05f, 10f, 1.2f, "s", ParamTaper.Log),
            ParamDescriptor.Float("drumAccel", "Drum Accel", 0.05f, 10f, 4f, "s", ParamTaper.Log),
            ParamDescriptor.Float("drumDecel", "Drum Decel", 0.05f, 10f, 5f, "s", ParamTaper.Log),
            ParamDescriptor.Float("crossover", "Crossover", 200f, 2_000f, 800f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("hornDepth", "Horn Doppler", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("drumDepth", "Drum Doppler", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("amDepth", "AM Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "Mic Spread", 0f, 180f, 120f, "°"),
            ParamDescriptor.Float("hornLevel", "Horn Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("drumLevel", "Drum Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("drive", "Drive", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions)
        ],
        maxInstances: 2
    );

    /// <summary>Per-sample smoothing coefficient that settles within 5 % in <paramref name="seconds" />.</summary>
    static double Inertia(float seconds, double sampleRate) => 1 - Math.Exp(-3 / (seconds * sampleRate));

    /// <summary>Rotor gain towards a mic: 1 facing it, 1 - <paramref name="depth" /> facing away.</summary>
    static float Directivity(float depth, float cosAngle) => 1 - depth * 0.5f * (1 - cosAngle);

    /// <summary>
    ///     Soft tube-style saturation, blended in with <paramref name="drive" /> (0..1). At 0 it is an exact
    ///     bypass; at full drive small signals gain up to 24 dB and peaks round off.
    /// </summary>
    static float Saturate(float x, float drive) {
        if (drive <= 0) {
            return x;
        }

        var gain = 1 + 15 * drive;
        return (1 - drive) * x + drive * MathF.Tanh(gain * x) / MathF.Tanh(gain);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var maxExcursion = Math.Max(MaxHornExcursionMs, MaxDrumExcursionMs) * 0.001 * spec.SampleRate;
        var capacity = (int)Math.Ceiling(2 * maxExcursion) + 16;
        hornLine.Allocate(capacity);
        drumLine.Allocate(capacity);
    }

    protected override void OnReset() {
        hornLine.Reset();
        drumLine.Reset();
        low1.Reset();
        low2.Reset();
        high1.Reset();
        high2.Reset();
        SetCrossover(Params.Current(Crossover));
        var fast = Params.Current(Speed) >= 0.5f;
        hornRate = Params.Current(fast ? HornFast : HornSlow);
        drumRate = Params.Current(fast ? DrumFast : DrumSlow);
        hornPhase = 0;
        drumPhase = DrumStartPhase;
        mix = Params.Current(Mix) / 100f;
        hornGain = Decibels.ToGain(Params.Current(HornLevel));
        drumGain = Decibels.ToGain(Params.Current(DrumLevel));
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            hornLine.Dispose();
            drumLine.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var n = io.Length;
        var fast = Params.Current(Speed) >= 0.5f;
        var hornTarget = (double)Params.Advance(fast ? HornFast : HornSlow, n);
        var drumTarget = (double)Params.Advance(fast ? DrumFast : DrumSlow, n);
        Params.Advance(fast ? HornSlow : HornFast, n);
        Params.Advance(fast ? DrumSlow : DrumFast, n);
        var hornUp = Inertia(Params.Advance(HornAccel, n), rate);
        var hornDown = Inertia(Params.Advance(HornDecel, n), rate);
        var drumUp = Inertia(Params.Advance(DrumAccel, n), rate);
        var drumDown = Inertia(Params.Advance(DrumDecel, n), rate);
        var crossover = Params.Advance(Crossover, n);
        if (crossover != this.crossover) {
            SetCrossover(crossover);
        }

        var hornExcursion = Params.Advance(HornDepth, n) / 100f * MaxHornExcursionMs * 0.001f * (float)rate;
        var drumExcursion = Params.Advance(DrumDepth, n) / 100f * MaxDrumExcursionMs * 0.001f * (float)rate;
        var hornCentre = MaxHornExcursionMs * 0.001f * (float)rate + FractionalDelayLine.MinDelay;
        var drumCentre = MaxDrumExcursionMs * 0.001f * (float)rate + FractionalDelayLine.MinDelay;
        var am = Params.Advance(AmDepth, n) / 100f;
        var hornAm = am * HornAmScale;
        var drumAm = am * DrumAmScale;
        var micOffset = Params.Advance(Spread, n) / 720.0; // each mic sits half the spread off axis, in cycles
        var drive = Params.Advance(Drive, n) / 100f;

        var hornGain = Decibels.ToGain(Params.Advance(HornLevel, n));
        var drumGain = Decibels.ToGain(Params.Advance(DrumLevel, n));
        var hornGainStep = (hornGain - this.hornGain) / n;
        var drumGainStep = (drumGain - this.drumGain) / n;
        var currentHornGain = this.hornGain;
        var currentDrumGain = this.drumGain;
        var mix = Params.Advance(Mix, n) / 100f;
        var mixStep = (mix - this.mix) / n;
        var currentMix = this.mix;
        var inputSelect = (int)Params.Current(InputSelect);

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            var input = (double)Saturate(Stereo.Select(inputSelect, left, right), drive);
            var bass = low2.Tick(lowPass, low1.Tick(lowPass, input));
            var treble = high2.Tick(highPass, high1.Tick(highPass, input));

            // Angles between each rotor and each mic, in radians.
            var hornLeft = (float)(2 * Math.PI * (hornPhase + micOffset));
            var hornRight = (float)(2 * Math.PI * (hornPhase - micOffset));
            var drumLeft = (float)(2 * Math.PI * (drumPhase + micOffset));
            var drumRight = (float)(2 * Math.PI * (drumPhase - micOffset));
            var hornCosLeft = MathF.Cos(hornLeft);
            var hornCosRight = MathF.Cos(hornRight);
            var drumCosLeft = MathF.Cos(drumLeft);
            var drumCosRight = MathF.Cos(drumRight);

            // A rotor facing the mic is nearest (shortest delay) and loudest.
            var hornL = hornLine.Read(hornCentre - hornExcursion * hornCosLeft) * Directivity(hornAm, hornCosLeft);
            var hornR = hornLine.Read(hornCentre - hornExcursion * hornCosRight) * Directivity(hornAm, hornCosRight);
            var drumL = drumLine.Read(drumCentre - drumExcursion * drumCosLeft) * Directivity(drumAm, drumCosLeft);
            var drumR = drumLine.Read(drumCentre - drumExcursion * drumCosRight) * Directivity(drumAm, drumCosRight);
            hornLine.Write((float)treble);
            drumLine.Write((float)bass);

            currentHornGain += hornGainStep;
            currentDrumGain += drumGainStep;
            currentMix += mixStep;
            var wetLeft = hornL * currentHornGain + drumL * currentDrumGain;
            var wetRight = hornR * currentHornGain + drumR * currentDrumGain;
            io.Left[i] = left + (wetLeft - left) * currentMix;
            io.Right[i] = right + (wetRight - right) * currentMix;

            // Motor inertia, then rotation. The horn and drum spin in opposite directions.
            hornRate += (hornTarget - hornRate) * (hornTarget > hornRate ? hornUp : hornDown);
            drumRate += (drumTarget - drumRate) * (drumTarget > drumRate ? drumUp : drumDown);
            hornPhase += hornRate / rate;
            hornPhase -= Math.Floor(hornPhase);
            drumPhase -= drumRate / rate;
            drumPhase -= Math.Floor(drumPhase);
        }

        low1.FlushDenormals();
        low2.FlushDenormals();
        high1.FlushDenormals();
        high2.FlushDenormals();
        this.hornGain = hornGain;
        this.drumGain = drumGain;
        this.mix = mix;
        output.Apply(io, Params.Advance(Level, n), Params.Advance(Balance, n));
    }

    /// <summary>Fourth-order Linkwitz–Riley split: two cascaded Butterworth sections per side.</summary>
    void SetCrossover(float frequency) {
        crossover = frequency;
        lowPass = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, ButterworthQ);
        highPass = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, frequency, ButterworthQ);
    }
}
