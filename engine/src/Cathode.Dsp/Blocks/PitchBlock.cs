using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;

namespace Cathode.Dsp.Blocks;

/// <summary>The algorithms of <see cref="PitchBlock" />, in the order of its Type parameter.</summary>
public enum PitchType {
    Detune,
    Chromatic,
    Diatonic,
    Whammy,
    OctaveDivider,
    Crystals,
    Arpeggiator,
    VirtualCapo
}

/// <summary>
///     Pitch block with the Axe-Fx III's Type switch: one block, several algorithms sharing a monophonic
///     <see cref="PitchTracker" /> (YIN, always running so a type change never waits for it to settle).
/// </summary>
/// <remarks>
///     <para>
///         Detune: two voices at +/- the detune amount through <see cref="SpliceShifter" />s. Chromatic and Diatonic:
///         two PSOLA voices (<see cref="PsolaShifter" />), at fixed semitone shifts or at diatonic intervals in a key and
///         scale chosen from the tracked note; each voice has level, pan and delay. Whammy: one splice-shifted voice
///         between a start and an end shift, positioned by the Pedal parameter (0-100 %, the one to drive with a
///         modifier). Octave Divider: the OC-2 style analog divider (<see cref="OctaveDivider" />). Crystals: reversed,
///         pitch-shifted grains (<see cref="GrainShifter" />) on the output of a feedback delay, so every repeat climbs
///         again. Arpeggiator: a tempo-synced sequence of up to eight semitone steps. Virtual Capo: a single shifted
///         voice of up to +/- 12 semitones.
///     </para>
///     <para>
///         Whammy and Virtual Capo are always 100 % wet (the shifter's few milliseconds of delay would comb against the
///         dry signal at unison); the others use Mix (the Axe mix law: 50 % = -6 dB each). The wet voices are computed
///         from the mono sum; the dry signal stays stereo.
///     </para>
///     <para>
///         <see cref="IDspBlock.LatencySamples" /> is 0 for every type, so the graph compiler's compensation never
///         changes with Type: the dry path is undelayed and each type's wet delay is part of its sound. Wet path delays
///         at 48 kHz: Octave Divider none; Detune, Whammy, Arpeggiator and Virtual Capo 5-15 ms on average (a few to 50 ms,
///         see <see cref="SpliceShifter" />); Chromatic and Diatonic a fixed ~29 ms plus the voice delay; Crystals up to 100 ms of
///         grain plus the delay time.
///     </para>
///     <para>
///         Not modelled: polyphonic tracking (the Axe's polyphonic capo; here chords fall back to unsynchronised
///         splices), formant correction and shift, custom scales, more than two harmony voices, per-step arpeggiator
///         levels and the Kill Dry flag (Mix 100 % does the same).
///     </para>
/// </remarks>
public sealed class PitchBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Mix = 1;
    public const int Level = 2;
    public const int Voice1Shift = 3;
    public const int Voice2Shift = 4;
    public const int Voice1Harmony = 5;
    public const int Voice2Harmony = 6;
    public const int Key = 7;
    public const int Scale = 8;
    public const int Voice1Level = 9;
    public const int Voice2Level = 10;
    public const int Voice1Pan = 11;
    public const int Voice2Pan = 12;
    public const int Voice1Delay = 13;
    public const int Voice2Delay = 14;
    public const int Detune = 15;
    public const int WhammyStart = 16;
    public const int WhammyEnd = 17;
    public const int Pedal = 18;
    public const int Octave1Level = 19;
    public const int Octave2Level = 20;
    public const int CrystalsShift = 21;
    public const int CrystalsTime = 22;
    public const int CrystalsFeedback = 23;
    public const int ArpTempo = 24;
    public const int ArpSteps = 25;
    public const int ArpStep1 = 26;
    public const int Capo = ArpStep1 + MaxArpSteps;

    public const int MaxArpSteps = 8;
    public const float MaxVoiceDelayMs = 1_000f;
    public const float MaxCrystalsTimeMs = 2_000f;

    /// <summary>Harmony choices run from two octaves down to two octaves up, in scale steps.</summary>
    public const int MaxHarmonySteps = 14;

    const double CrystalsGrainSeconds = 0.1;
    const double VoicedHoldSeconds = 0.1;
    const double NoteHysteresis = 0.15;

    /// <summary>Arpeggiator step lengths in beats (quarter notes).</summary>
    static readonly double[] ArpDivisions = [1, 0.5, 1 / 3.0, 0.25, 1 / 6.0, 0.125];

    static readonly float[] DefaultArpSteps = [0, 7, 12, 7, 0, 5, 12, 5];

    readonly PitchTracker tracker = new();
    readonly SpliceShifter shifterOne = new();
    readonly SpliceShifter shifterTwo = new();
    readonly PsolaShifter psola = new(2);
    readonly GrainShifter grains = new();
    readonly FractionalDelayLine delayOne = new();
    readonly FractionalDelayLine delayTwo = new();
    readonly FractionalDelayLine crystalsLine = new();
    readonly double[] arpSpeeds = new double[MaxArpSteps];
    OctaveDivider divider;
    SvfCoefficients crystalsLowPass;
    SvfCoefficients crystalsHighPass;
    SvfState crystalsLow;
    SvfState crystalsHigh;
    PitchType activeType;
    int voicedHold;
    int sinceVoiced;
    double lastPeriod;
    int harmonyNote;
    double harmonyOne;
    double harmonyTwo;
    double arpPhase;
    int arpStep;
    float whammySemitones;
    float crystalsDelay;
    float mix;
    float gainOneLeft;
    float gainOneRight;
    float gainTwoLeft;
    float gainTwoRight;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "pitch",
        "Pitch",
        "Pitch",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                [
                    "Detune",
                    "Chromatic",
                    "Diatonic",
                    "Whammy",
                    "Octave Divider",
                    "Crystals",
                    "Arpeggiator",
                    "Virtual Capo"
                ]
            ),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 50f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("voice1Shift", "Voice 1 Shift", -24f, 24f, 12f, "st"),
            ParamDescriptor.Float("voice2Shift", "Voice 2 Shift", -24f, 24f, -12f, "st"),
            ParamDescriptor.Choice("voice1Harmony", "Voice 1 Harmony", HarmonyNames(), MaxHarmonySteps + 2),
            ParamDescriptor.Choice("voice2Harmony", "Voice 2 Harmony", HarmonyNames(), MaxHarmonySteps + 4),
            ParamDescriptor.Choice("key", "Key", Scales.Keys),
            ParamDescriptor.Choice("scale", "Scale", Scales.Names),
            ParamDescriptor.Float("voice1Level", "Voice 1 Level", Decibels.SilenceDb, 6f, 0f, "dB"),
            ParamDescriptor.Float("voice2Level", "Voice 2 Level", Decibels.SilenceDb, 6f, 0f, "dB"),
            ParamDescriptor.Float("voice1Pan", "Voice 1 Pan", -100f, 100f, -50f, "%"),
            ParamDescriptor.Float("voice2Pan", "Voice 2 Pan", -100f, 100f, 50f, "%"),
            new(
                "voice1Delay",
                "Voice 1 Delay",
                ParamKind.Float,
                0f,
                MaxVoiceDelayMs,
                0f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ),
            new(
                "voice2Delay",
                "Voice 2 Delay",
                ParamKind.Float,
                0f,
                MaxVoiceDelayMs,
                0f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ),
            ParamDescriptor.Float("detune", "Detune", 0f, 50f, 10f, "cents"),
            ParamDescriptor.Float("whammyStart", "Whammy Start", -24f, 24f, 0f, "st"),
            ParamDescriptor.Float("whammyEnd", "Whammy End", -24f, 24f, 12f, "st"),
            ParamDescriptor.Float("pedal", "Pedal", 0f, 100f, 0f, "%", smoothingMs: 10f),
            ParamDescriptor.Float("octave1Level", "Octave 1 Level", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("octave2Level", "Octave 2 Level", 0f, 100f, 0f, "%"),
            ParamDescriptor.Float("crystalsShift", "Crystals Shift", -24f, 24f, 12f, "st"),
            new(
                "crystalsTime",
                "Crystals Time",
                ParamKind.Float,
                20f,
                MaxCrystalsTimeMs,
                300f,
                "ms",
                ParamTaper.Log,
                ParamSmoothing.Linear,
                60f
            ),
            ParamDescriptor.Float("crystalsFeedback", "Crystals Feedback", 0f, 90f, 30f, "%"),
            ParamDescriptor.Choice("arpTempo", "Arp Tempo", ["1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32"], 3),
            ParamDescriptor.Choice("arpSteps", "Arp Steps", ["1", "2", "3", "4", "5", "6", "7", "8"], 3),
            ArpStep(1),
            ArpStep(2),
            ArpStep(3),
            ArpStep(4),
            ArpStep(5),
            ArpStep(6),
            ArpStep(7),
            ArpStep(8),
            ParamDescriptor.Choice("capo", "Capo", CapoNames(), 12)
        ],
        maxInstances: 2
    );

    static ParamDescriptor ArpStep(int step) =>
        new(
            $"arpStep{step}",
            $"Arp Step {step}",
            ParamKind.Float,
            -24f,
            24f,
            DefaultArpSteps[step - 1],
            "st",
            Smoothing: ParamSmoothing.Step
        );

    /// <summary>"-2 Oct" .. "Unison" .. "+2 Oct": index - <see cref="MaxHarmonySteps" /> is the step count.</summary>
    static string[] HarmonyNames() {
        string[] intervals = [
            "Unison",
            "2nd",
            "3rd",
            "4th",
            "5th",
            "6th",
            "7th",
            "Oct",
            "9th",
            "10th",
            "11th",
            "12th",
            "13th",
            "14th",
            "2 Oct"
        ];
        return [
            ..Enumerable.Range(-MaxHarmonySteps, 2 * MaxHarmonySteps + 1)
                .Select(steps => steps switch {
                        0 => intervals[0],
                        < 0 => "-" + intervals[-steps],
                        _ => "+" + intervals[steps]
                    }
                )
        ];
    }

    static string[] CapoNames() => [..Enumerable.Range(-12, 25).Select(static s => s > 0 ? $"+{s}" : $"{s}")];

    static double Ratio(double semitones) => Math.Pow(2, semitones / 12);

    static (float Left, float Right) Pan(float gain, float pan) {
        var angle = (pan / 100f + 1) * MathF.PI / 4; // constant power, unity per side at the centre
        return (gain * MathF.Sqrt(2) * MathF.Cos(angle), gain * MathF.Sqrt(2) * MathF.Sin(angle));
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        tracker.Prepare(rate);
        shifterOne.Prepare(rate);
        shifterTwo.Prepare(rate);
        psola.Prepare(rate);
        grains.Prepare(rate, CrystalsGrainSeconds);
        var voiceCapacity = (int)(MaxVoiceDelayMs * 0.001 * rate) + 8;
        delayOne.Allocate(voiceCapacity);
        delayTwo.Allocate(voiceCapacity);
        crystalsLine.Allocate((int)(MaxCrystalsTimeMs * 0.001 * rate) + 8);
        divider.Prepare(rate);
        crystalsLowPass = SvfCoefficients.Design(BiquadType.LowPass, rate, 6_000, 0.7071);
        crystalsHighPass = SvfCoefficients.Design(BiquadType.HighPass, rate, 80, 0.7071);
        voicedHold = (int)(VoicedHoldSeconds * rate);
    }

    protected override void OnReset() {
        tracker.Reset();
        ResetEngines();
        activeType = (PitchType)(int)Params.Current(Type);
        sinceVoiced = int.MaxValue;
        lastPeriod = 0;
        harmonyNote = int.MinValue;
        harmonyOne = 0;
        harmonyTwo = 0;
        whammySemitones = WhammySemitones();
        crystalsDelay = CrystalsDelaySamples();
        mix = MixFor(activeType, Params.Current(Mix));
        (gainOneLeft, gainOneRight, gainTwoLeft, gainTwoRight) = VoiceGains(activeType);
        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            tracker.Dispose();
            shifterOne.Dispose();
            shifterTwo.Dispose();
            psola.Dispose();
            grains.Dispose();
            delayOne.Dispose();
            delayTwo.Dispose();
            crystalsLine.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var frames = io.Length;
        var type = (PitchType)(int)Params.Current(Type);
        if (type != activeType) {
            ResetEngines(); // a type switch starts the new algorithm from silence instead of stale buffers
            activeType = type;
        }

        // Smoothed values read through Params.Current further down.
        foreach (var index in (ReadOnlySpan<int>)[
                     Voice1Level,
                     Voice2Level,
                     Voice1Pan,
                     Voice2Pan,
                     WhammyStart,
                     WhammyEnd,
                     Pedal,
                     CrystalsTime
                 ]) {
            Params.Advance(index, frames);
        }

        var rate = Spec.SampleRate;
        var mix = MixFor(type, Params.Advance(Mix, frames));
        var (oneLeft, oneRight, twoLeft, twoRight) = VoiceGains(type);
        var delayOneTarget = Params.Advance(Voice1Delay, frames) * 0.001f * (float)rate;
        var delayTwoTarget = Params.Advance(Voice2Delay, frames) * 0.001f * (float)rate;

        var detune = Params.Advance(Detune, frames) / 1_200.0;
        var detuneUp = Math.Pow(2, detune);
        var detuneDown = 1 / detuneUp;
        var shiftOne = Ratio(Params.Advance(Voice1Shift, frames));
        var shiftTwo = Ratio(Params.Advance(Voice2Shift, frames));
        UpdateHarmony();
        var whammy = WhammySemitones();
        var whammyStep = (whammy - whammySemitones) / frames;
        var capo = Ratio(Params.Current(Capo) - 12);
        var octaveOne = Params.Advance(Octave1Level, frames) / 100f;
        var octaveTwo = Params.Advance(Octave2Level, frames) / 100f;
        var crystalsSpeed = -Ratio(Params.Advance(CrystalsShift, frames));
        var crystalsTarget = CrystalsDelaySamples();
        var crystalsStep = (crystalsTarget - crystalsDelay) / frames;
        var feedback = Params.Advance(CrystalsFeedback, frames) / 100f;

        var arpCount = (int)Params.Current(ArpSteps) + 1;
        var tempo = context.Tempo > 0 ? context.Tempo : 120;
        var arpRate = tempo / 60 / rate / ArpDivisions[(int)Params.Current(ArpTempo)];
        for (var s = 0; s < MaxArpSteps; s++) {
            arpSpeeds[s] = Ratio(Params.Current(ArpStep1 + s));
        }

        var mixStep = (mix - this.mix) / frames;
        var oneLeftStep = (oneLeft - gainOneLeft) / frames;
        var oneRightStep = (oneRight - gainOneRight) / frames;
        var twoLeftStep = (twoLeft - gainTwoLeft) / frames;
        var twoRightStep = (twoRight - gainTwoRight) / frames;
        for (var i = 0; i < frames; i++) {
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var mono = 0.5f * (dryLeft + dryRight);
            Track(mono);
            var period = sinceVoiced < voicedHold ? lastPeriod : 0;

            float one, two = 0f;
            switch (type) {
                case PitchType.Detune:
                    one = Delayed(delayOne, shifterOne.Process(mono, detuneUp, period), delayOneTarget);
                    two = Delayed(delayTwo, shifterTwo.Process(mono, detuneDown, period), delayTwoTarget);
                    break;
                case PitchType.Chromatic:
                case PitchType.Diatonic:
                    psola.Write(mono);
                    var diatonic = type == PitchType.Diatonic;
                    one = psola.Read(0, diatonic ? harmonyOne : shiftOne, period);
                    two = psola.Read(1, diatonic ? harmonyTwo : shiftTwo, period);
                    one = Delayed(delayOne, one, delayOneTarget);
                    two = Delayed(delayTwo, two, delayTwoTarget);
                    break;
                case PitchType.Whammy:
                    whammySemitones += whammyStep;
                    one = shifterOne.Process(mono, Ratio(whammySemitones), period);
                    break;
                case PitchType.OctaveDivider:
                    var (down, twoDown) = divider.Process(mono);
                    one = octaveOne * down + octaveTwo * twoDown;
                    break;
                case PitchType.Crystals:
                    crystalsDelay += crystalsStep;
                    var repeat = crystalsLine.Read(crystalsDelay);
                    one = grains.Process(repeat, crystalsSpeed);
                    var fed = (float)crystalsHigh.Tick(crystalsHighPass, crystalsLow.Tick(crystalsLowPass, one));
                    crystalsLine.Write(mono + feedback * fed);
                    break;
                case PitchType.Arpeggiator:
                    arpPhase += arpRate;
                    if (arpPhase >= 1) {
                        arpPhase -= 1;
                        arpStep++;
                    }

                    arpStep %= arpCount;
                    one = shifterOne.Process(mono, arpSpeeds[arpStep], period);
                    break;
                default:
                    one = shifterOne.Process(mono, capo, period);
                    break;
            }

            this.mix += mixStep;
            gainOneLeft += oneLeftStep;
            gainOneRight += oneRightStep;
            gainTwoLeft += twoLeftStep;
            gainTwoRight += twoRightStep;
            io.Left[i] = dryLeft + (one * gainOneLeft + two * gainTwoLeft - dryLeft) * this.mix;
            io.Right[i] = dryRight + (one * gainOneRight + two * gainTwoRight - dryRight) * this.mix;
        }

        // Land exactly on the targets, so rounding in the ramps never accumulates across blocks.
        this.mix = mix;
        (gainOneLeft, gainOneRight, gainTwoLeft, gainTwoRight) = (oneLeft, oneRight, twoLeft, twoRight);
        whammySemitones = whammy;
        crystalsDelay = crystalsTarget;
        divider.FlushDenormals();
        crystalsLow.FlushDenormals();
        crystalsHigh.FlushDenormals();

        var outputGain = Decibels.ToGain(Params.Advance(Level, frames));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    static float MixFor(PitchType type, float mix) =>
        type is PitchType.Whammy or PitchType.VirtualCapo ? 1f : mix / 100f;

    static float Delayed(FractionalDelayLine line, float input, float delay) {
        var output = line.Read(delay); // read before writing, so a delay of N is exactly N samples
        line.Write(input);
        return output;
    }

    void Track(float mono) {
        if (sinceVoiced < int.MaxValue) {
            sinceVoiced++;
        }

        if (!tracker.Push(mono) || !tracker.Voiced) {
            return;
        }

        sinceVoiced = 0;
        lastPeriod = tracker.Period;
        UpdateHarmony();
    }

    /// <summary>Picks the diatonic intervals for the tracked note; the note changes only past a small hysteresis.</summary>
    void UpdateHarmony() {
        if (tracker.Frequency > 0) {
            var midi = Scales.MidiNote(tracker.Frequency);
            if (Math.Abs(midi - harmonyNote) > 0.5 + NoteHysteresis) {
                harmonyNote = (int)Math.Round(midi);
            }
        }

        var note = harmonyNote == int.MinValue ? 60 : harmonyNote;
        var key = (int)Params.Current(Key);
        var scale = (int)Params.Current(Scale);
        harmonyOne = Ratio(
            Scales.HarmonySemitones(note, key, scale, (int)Params.Current(Voice1Harmony) - MaxHarmonySteps)
        );
        harmonyTwo = Ratio(
            Scales.HarmonySemitones(note, key, scale, (int)Params.Current(Voice2Harmony) - MaxHarmonySteps)
        );
    }

    float WhammySemitones() {
        var start = Params.Current(WhammyStart);
        return start + (Params.Current(WhammyEnd) - start) * Params.Current(Pedal) / 100f;
    }

    float CrystalsDelaySamples() => Params.Current(CrystalsTime) * 0.001f * (float)Spec.SampleRate;

    (float, float, float, float) VoiceGains(PitchType type) {
        if (type is not (PitchType.Detune or PitchType.Chromatic or PitchType.Diatonic)) {
            return (1f, 1f, 0f, 0f); // single centred voice
        }

        var (oneLeft, oneRight) = Pan(Decibels.ToGain(Params.Current(Voice1Level)), Params.Current(Voice1Pan));
        var (twoLeft, twoRight) = Pan(Decibels.ToGain(Params.Current(Voice2Level)), Params.Current(Voice2Pan));
        return (oneLeft, oneRight, twoLeft, twoRight);
    }

    void ResetEngines() {
        shifterOne.Reset();
        shifterTwo.Reset();
        psola.Reset();
        grains.Reset();
        delayOne.Reset();
        delayTwo.Reset();
        crystalsLine.Reset();
        divider.Reset();
        crystalsLow.Reset();
        crystalsHigh.Reset();
        arpPhase = 0;
        arpStep = 0;
    }
}
