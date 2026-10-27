using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Rotary speaker (Leslie 122/147 style). The mono input (Input Select) goes through an optional tube-style drive,
///     then a Linkwitz–Riley crossover splits it between the treble horn and the bass drum. Each rotor is a rotating
///     source heard by two microphones, placed HF or LF Mic Spacing degrees apart: the changing path length to each mic
///     is a modulated delay (Doppler), and the rotor's directivity is an amplitude modulation. The speed switch moves
///     each rotor towards its new speed with its own acceleration and deceleration inertia, as the real motors and
///     belts do; Brake stops (parks) both rotors.
/// </summary>
/// <remarks>
///     <para>
///         Rotor speed approaches its target exponentially; the acceleration and deceleration times are the time to
///         come within 5 % of the new speed (three time constants). With Tempo set, the horn turns once per division
///         at Fast, and every other speed keeps its ratio to Horn Fast: Slow stays as much slower as Horn Slow is,
///         and the drum keeps the ratio its own rates have at that speed. Low Rate Multiplier scales the drum against
///         the horn: every drum rate (its Slow, Fast and tempo-locked speeds) is multiplied by it, 1 keeping the rates
///         as set.
///     </para>
///     <para>
///         At 100 % Mic Distance the mics are far away: the path length varies as the cosine of the rotor angle, and
///         only the rotor's directivity changes the level. Closer, the geometry is exact: the path from a mouth on a
///         circle to a mic <c>k</c> radii from its axis is <c>√(k² + 1 − 2k cos θ)</c> radii, with the level falling
///         as one over it, so the sweep turns lopsided and the near pass louder. Rotor Length scales the horn's radius
///         (and so its Doppler).
///     </para>
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
    public const int Tempo = 20;
    public const int RotorLength = 21;
    public const int DrumSpread = 22;
    public const int MicDistance = 23;
    public const int StereoSpread = 24;
    public const int InputSelect = 25;
    public const int LowRateMultiplier = 26;

    public const int Slow = 0;
    public const int Fast = 1;
    public const int Brake = 2;

    /// <summary>Peak Doppler path-length change of the horn, in ms (a ~17 cm horn radius).</summary>
    public const float MaxHornExcursionMs = 0.5f;

    /// <summary>Peak Doppler path-length change of the drum, in ms.</summary>
    public const float MaxDrumExcursionMs = 0.6f;

    /// <summary>Deepest amplitude dip of the horn as it faces away from a mic, at 100 % AM depth.</summary>
    public const float HornAmScale = 0.9f;

    /// <summary>The drum's baffle is far less directional than the horn.</summary>
    public const float DrumAmScale = 0.5f;

    const float MaxRotorLength = 2f;

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
            ParamDescriptor.Choice("speed", "Speed", ["Slow", "Fast", "Brake"]),
            ParamDescriptor.Float("hornSlow", "Horn Slow", 0.1f, 3f, 0.83f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("hornFast", "Horn Fast", 2f, 10f, 6.7f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("drumSlow", "Drum Slow", 0.1f, 3f, 0.67f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("drumFast", "Drum Fast", 2f, 10f, 5.8f, "Hz", ParamTaper.Log),
            SpeedChange("hornAccel", "Horn Accel", 0.8f),
            SpeedChange("hornDecel", "Horn Decel", 1.2f),
            SpeedChange("drumAccel", "Drum Accel", 4f),
            SpeedChange("drumDecel", "Drum Decel", 5f),
            ParamDescriptor.Float("crossover", "Crossover", 200f, 2_000f, 800f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("hornDepth", "Horn Doppler", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("drumDepth", "Drum Doppler", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("amDepth", "AM Depth", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("spread", "HF Mic Spacing", 0f, 180f, 120f, "°"),
            ParamDescriptor.Float("hornLevel", "Horn Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("drumLevel", "Drum Level", Decibels.SilenceDb, 12f, 0f, "dB"),
            ParamDescriptor.Float("drive", "Drive", 0f, 100f, 0f, "%"),
            BlockParams.Mix(100f),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Choice("tempo", "Tempo", TempoDivisions.Names),
            ParamDescriptor.Float("rotorLength", "Rotor Length", 50f, 100f * MaxRotorLength, 100f, "%"),
            ParamDescriptor.Float("drumSpread", "LF Mic Spacing", 0f, 180f, 120f, "°"),
            ParamDescriptor.Float("micDistance", "Mic Distance", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("stereoSpread", "Stereo Spread", -100f, 100f, 100f, "%"),
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            ParamDescriptor.Float("lowRateMult", "Low Rate Multiplier", 0.25f, 4f, 1f, "×", ParamTaper.Log)
        ],
        maxInstances: 2,
        pages: [
            new(
                "Basic",
                [
                    "speed", "tempo", "hornDepth", "drumDepth", "amDepth", "hornLevel", "drumLevel", "drive", "spread",
                    "drumSpread"
                ]
            ),
            new(
                "Rotors",
                [
                    "hornSlow", "hornFast", "hornAccel", "hornDecel", "drumSlow", "drumFast", "drumAccel", "drumDecel",
                    "lowRateMult"
                ]
            ),
            new("Advanced", ["rotorLength", "crossover", "micDistance", "stereoSpread", "inputSelect"])
        ]
    );

    /// <summary>A rotor's time to change speed (a speed-up or a slow-down), 0.05 to 10 s.</summary>
    static ParamDescriptor SpeedChange(string id, string name, float seconds) =>
        ParamDescriptor.Float(id, name, 0.05f, 10f, seconds, "s", ParamTaper.Log);

    /// <summary>
    ///     The near-field path of a rotor to a mic <paramref name="k" /> radii from its axis, at
    ///     <paramref name="cosAngle" />: the change in path length in radii (−1 facing, +1 away) and the
    ///     inverse-distance level relative to facing it.
    /// </summary>
    public static (float Path, float Level) NearField(float k, float cosAngle) {
        var distance = MathF.Sqrt(k * k + 1 - 2 * k * cosAngle);
        return (distance - k, (k - 1) / distance);
    }

    /// <summary>Per-sample smoothing coefficient that settles within 5 % in <paramref name="seconds" />.</summary>
    static double Inertia(float seconds, double sampleRate) => 1 - Math.Exp(-3 / (seconds * sampleRate));

    /// <summary>Rotor gain towards a mic: 1 facing it, 1 - <paramref name="depth" /> facing away.</summary>
    static float Directivity(float depth, float cosAngle) => 1 - depth * 0.5f * (1 - cosAngle);

    /// <summary>
    ///     The mic's distance in rotor radii for Mic Distance below 100 % (0 % is 1.2 radii, 99 % about 10).
    /// </summary>
    static float Radii(float distance) => 1.2f + 8.8f * distance;

    /// <summary>One rotor heard by one mic in the near field.</summary>
    static float Near(FractionalDelayLine line, float centre, float excursion, float am, float radii, float cosAngle) {
        var (path, level) = NearField(radii, cosAngle);
        return line.Read(centre + excursion * path) * level * Directivity(am, cosAngle);
    }

    /// <summary>Fourth-order Linkwitz–Riley split: two cascaded Butterworth sections per side.</summary>
    void SetCrossover(float frequency) {
        crossover = frequency;
        lowPass = SvfCoefficients.Design(BiquadType.LowPass, Spec.SampleRate, frequency, ButterworthQ);
        highPass = SvfCoefficients.Design(BiquadType.HighPass, Spec.SampleRate, frequency, ButterworthQ);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var maxExcursion = Math.Max(MaxRotorLength * MaxHornExcursionMs, MaxDrumExcursionMs) * 0.001 * spec.SampleRate;
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
        var speed = (int)Params.Current(Speed);
        var fast = speed == Fast;
        var (hornParam, drumParam) = fast ? (HornFast, DrumFast) : (HornSlow, DrumSlow);
        hornRate = speed == Brake ? 0 : Params.Current(hornParam);
        drumRate = speed == Brake ? 0 : Params.Current(drumParam) * Params.Current(LowRateMultiplier);
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
        var speed = (int)Params.Current(Speed);
        var fast = speed == Fast;
        double hornTarget = Params.Advance(fast ? HornFast : HornSlow, n);
        var drumTarget = (double)Params.Advance(fast ? DrumFast : DrumSlow, n)
            * Params.Advance(LowRateMultiplier, n);
        Params.Advance(fast ? HornSlow : HornFast, n);
        Params.Advance(fast ? DrumSlow : DrumFast, n);
        var tempo = (int)Params.Current(Tempo);
        if (tempo > 0 && context.Tempo > 0) {
            // The fast horn turns once per division; every other rate keeps its ratio to Horn Fast, so Slow and Fast
            // still switch.
            double hornFast = Params.Current(HornFast);
            var locked = TempoDivisions.Rate(tempo, context.Tempo, hornFast) / hornFast;
            hornTarget *= locked;
            drumTarget *= locked;
        }

        if (speed == Brake) {
            (hornTarget, drumTarget) = (0, 0);
        }

        var hornUp = Inertia(Params.Advance(HornAccel, n), rate);
        var hornDown = Inertia(Params.Advance(HornDecel, n), rate);
        var drumUp = Inertia(Params.Advance(DrumAccel, n), rate);
        var drumDown = Inertia(Params.Advance(DrumDecel, n), rate);
        var crossover = Params.Advance(Crossover, n);
        if (crossover != this.crossover) {
            SetCrossover(crossover);
        }

        // A longer horn needs more room either side of the centre; up to the standard length the centre stays put.
        var length = Params.Advance(RotorLength, n) / 100f;
        var hornExcursion = Params.Advance(HornDepth, n) / 100f * MaxHornExcursionMs * 0.001f * (float)rate * length;
        var drumExcursion = Params.Advance(DrumDepth, n) / 100f * MaxDrumExcursionMs * 0.001f * (float)rate;
        var hornCentre = Math.Max(1f, length) * MaxHornExcursionMs * 0.001f * (float)rate
            + FractionalDelayLine.MinDelay;
        var drumCentre = MaxDrumExcursionMs * 0.001f * (float)rate + FractionalDelayLine.MinDelay;
        var am = Params.Advance(AmDepth, n) / 100f;
        var hornAm = am * HornAmScale;
        var drumAm = am * DrumAmScale;
        var micOffset = Params.Advance(Spread, n) / 720.0; // each mic sits half the spacing off axis, in cycles
        var drumOffset = Params.Advance(DrumSpread, n) / 720.0;
        var distance = Params.Advance(MicDistance, n) / 100f;
        var far = distance >= 1f;
        var radii = Radii(distance);
        var stereoSpread = Params.Advance(StereoSpread, n) / 100f;
        var select = (int)Params.Current(InputSelect);
        var drive = Params.Advance(Drive, n) / 100f;

        var hornGain = Decibels.ToGain(Params.Advance(HornLevel, n));
        var drumGain = Decibels.ToGain(Params.Advance(DrumLevel, n));
        var hornGainStep = (hornGain - this.hornGain) / n;
        var drumGainStep = (drumGain - this.drumGain) / n;
        var currentHornGain = this.hornGain;
        var currentDrumGain = this.drumGain;
        var mix = Params.Advance(Mix, n) / 100f;
        var gains = new MixRamp(MixLaw.ConstantPower, this.mix, mix, n);

        for (var i = 0; i < n; i++) {
            var left = io.Left[i];
            var right = io.Right[i];
            double input = SoftDrive.Apply(Stereo.Select(select, left, right), drive);
            var bass = low2.Tick(lowPass, low1.Tick(lowPass, input));
            var treble = high2.Tick(highPass, high1.Tick(highPass, input));

            // Angles between each rotor and each mic, in radians.
            var hornLeft = (float)(2 * Math.PI * (hornPhase + micOffset));
            var hornRight = (float)(2 * Math.PI * (hornPhase - micOffset));
            var drumLeft = (float)(2 * Math.PI * (drumPhase + drumOffset));
            var drumRight = (float)(2 * Math.PI * (drumPhase - drumOffset));
            var hornCosLeft = MathF.Cos(hornLeft);
            var hornCosRight = MathF.Cos(hornRight);
            var drumCosLeft = MathF.Cos(drumLeft);
            var drumCosRight = MathF.Cos(drumRight);

            // A rotor facing the mic is nearest (shortest delay) and loudest.
            float hornL, hornR, drumL, drumR;
            if (far) {
                hornL = hornLine.Read(hornCentre - hornExcursion * hornCosLeft) * Directivity(hornAm, hornCosLeft);
                hornR = hornLine.Read(hornCentre - hornExcursion * hornCosRight) * Directivity(hornAm, hornCosRight);
                drumL = drumLine.Read(drumCentre - drumExcursion * drumCosLeft) * Directivity(drumAm, drumCosLeft);
                drumR = drumLine.Read(drumCentre - drumExcursion * drumCosRight) * Directivity(drumAm, drumCosRight);
            } else {
                hornL = Near(hornLine, hornCentre, hornExcursion, hornAm, radii, hornCosLeft);
                hornR = Near(hornLine, hornCentre, hornExcursion, hornAm, radii, hornCosRight);
                drumL = Near(drumLine, drumCentre, drumExcursion, drumAm, radii, drumCosLeft);
                drumR = Near(drumLine, drumCentre, drumExcursion, drumAm, radii, drumCosRight);
            }

            hornLine.Write((float)treble);
            drumLine.Write((float)bass);

            currentHornGain += hornGainStep;
            currentDrumGain += drumGainStep;
            gains.Advance();
            var wetLeft = hornL * currentHornGain + drumL * currentDrumGain;
            var wetRight = hornR * currentHornGain + drumR * currentDrumGain;
            Stereo.Spread(ref wetLeft, ref wetRight, stereoSpread);
            io.Left[i] = left * gains.Dry + wetLeft * gains.Wet;
            io.Right[i] = right * gains.Dry + wetRight * gains.Wet;

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
}
