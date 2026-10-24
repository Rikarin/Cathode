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
    VirtualCapo,
    CustomShifter
}

/// <summary>Where the harmony types take their key from, in the order of <see cref="PitchBlock" />'s Key Source parameter.</summary>
public enum PitchKeySource {
    /// <summary>The Key and Scale parameters.</summary>
    Fixed,

    /// <summary>The chord held on the MIDI input: its lowest note is the root, its notes the chord tones.</summary>
    Midi,

    /// <summary>Estimated from the notes played (<see cref="KeyEstimator" />).</summary>
    Auto
}

/// <summary>
///     Pitch block with the Axe-Fx III's Type switch: one block, several algorithms sharing a monophonic
///     <see cref="PitchTracker" /> (YIN, always running so a type change never waits for it to settle).
/// </summary>
/// <remarks>
///     <para>
///         Detune: two voices at +/- the detune amount through <see cref="SpliceShifter" />s. Chromatic, Diatonic and
///         Custom Shifter: up to four PSOLA voices (<see cref="PsolaShifter" />), at fixed semitone shifts, at diatonic
///         intervals in a key and scale chosen from the tracked note, or at the shift a per-voice table gives each of
///         the twelve notes of the key (the Axe-Fx Custom Shifter: any mapping, diatonic or not); each voice has level,
///         pan and delay, and a voice at silence costs nothing. Whammy: one splice-shifted voice between a start and an
///         end shift, positioned by the Pedal parameter (0-100 %, the one to drive with a modifier). Octave Divider: the
///         OC-2 style analog divider (<see cref="OctaveDivider" />). Crystals: reversed, pitch-shifted grains
///         (<see cref="GrainShifter" />) on the output of a feedback delay, so every repeat climbs again. Arpeggiator: a
///         tempo-synced sequence of up to eight semitone steps. Virtual Capo: a single shifted voice of up to +/- 12
///         semitones.
///     </para>
///     <para>
///         Key Source sets where Diatonic and Custom Shifter take their key. Fixed: Key and Scale. MIDI: the chord held on
///         the rig's MIDI input (<see cref="BlockContext.Notes" />, any channel); its lowest note is the root, the scale is
///         Scale when that holds every chord tone and otherwise the first scale that does
///         (<see cref="Scales.ChordScale" />: Am gives Minor, G7 Mixolydian), and diatonic voices snap to the nearest
///         chord tone (<see cref="Scales.HarmonySemitones(int, int, int, int, int)" />). Releasing the keys keeps the
///         last chord. Auto: the key estimated from the notes played (<see cref="KeyEstimator" />, Krumhansl-Schmuckler),
///         with Scale when it is major or minor like the estimate, else Major or Minor. Until they have a key, both use
///         Key and Scale.
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
///         see <see cref="SpliceShifter" />); Chromatic, Diatonic and Custom Shifter a fixed ~29 ms plus the voice delay;
///         Crystals up to 100 ms of grain plus the delay time.
///     </para>
///     <para>
///         Not modelled: polyphonic tracking (the Axe's polyphonic capo; here chords fall back to unsynchronised
///         splices), formant correction and shift, per-step arpeggiator levels and the Kill Dry flag (Mix 100 % does
///         the same).
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
    public const int Voice3Shift = Capo + 1;
    public const int Voice4Shift = Voice3Shift + 1;
    public const int Voice3Harmony = Voice4Shift + 1;
    public const int Voice4Harmony = Voice3Harmony + 1;
    public const int Voice3Level = Voice4Harmony + 1;
    public const int Voice4Level = Voice3Level + 1;
    public const int Voice3Pan = Voice4Level + 1;
    public const int Voice4Pan = Voice3Pan + 1;
    public const int Voice3Delay = Voice4Pan + 1;
    public const int Voice4Delay = Voice3Delay + 1;
    public const int KeySource = Voice4Delay + 1;

    /// <summary>The Custom Shifter tables: voice v's (0-3) shift for note n of the key is at CustomTable + 12 v + n.</summary>
    public const int CustomTable = KeySource + 1;

    public const int Balance = CustomTable + 12 * 4;
    public const int GlobalMix = Balance + 1;
    public const int InputSelect = GlobalMix + 1;
    public const int InputGain = InputSelect + 1;

    public const int VoiceCount = 4;
    public const int MaxArpSteps = 8;
    public const float MaxVoiceDelayMs = 1_000f;
    public const float MaxCrystalsTimeMs = 2_000f;

    /// <summary>Harmony choices run from two octaves down to two octaves up, in scale steps.</summary>
    public const int MaxHarmonySteps = 14;

    const double CrystalsGrainSeconds = 0.1;
    const double VoicedHoldSeconds = 0.1;
    const double NoteHysteresis = 0.15;
    const int MajorScale = 0;
    const int MinorScale = 1;

    /// <summary>Arpeggiator step lengths in beats (quarter notes).</summary>
    static readonly double[] ArpDivisions = [1, 0.5, 1 / 3.0, 0.25, 1 / 6.0, 0.125];

    static readonly float[] DefaultArpSteps = [0, 7, 12, 7, 0, 5, 12, 5];

    /// <summary>The Custom Shifter's default tables, in steps of the major scale: a third, a fifth, an octave up and down.</summary>
    static readonly int[] DefaultCustomSteps = [2, 4, 7, -7];

    static readonly int[] ShiftParams = [Voice1Shift, Voice2Shift, Voice3Shift, Voice4Shift];
    static readonly int[] HarmonyParams = [Voice1Harmony, Voice2Harmony, Voice3Harmony, Voice4Harmony];
    static readonly int[] LevelParams = [Voice1Level, Voice2Level, Voice3Level, Voice4Level];
    static readonly int[] PanParams = [Voice1Pan, Voice2Pan, Voice3Pan, Voice4Pan];
    static readonly int[] DelayParams = [Voice1Delay, Voice2Delay, Voice3Delay, Voice4Delay];

    readonly PitchTracker tracker = new();
    readonly KeyEstimator keys = new();
    readonly SpliceShifter shifterOne = new();
    readonly SpliceShifter shifterTwo = new();
    readonly PsolaShifter psola = new(VoiceCount);
    readonly GrainShifter grains = new();
    readonly FractionalDelayLine[] delays = [new(), new(), new(), new()];
    readonly FractionalDelayLine crystalsLine = new();
    readonly double[] arpSpeeds = new double[MaxArpSteps];
    readonly double[] shifts = new double[VoiceCount];
    readonly double[] harmonies = new double[VoiceCount];
    readonly float[] delayTargets = new float[VoiceCount];
    readonly float[] gainsLeft = new float[VoiceCount];
    readonly float[] gainsRight = new float[VoiceCount];
    readonly float[] targetsLeft = new float[VoiceCount];
    readonly float[] targetsRight = new float[VoiceCount];
    readonly float[] stepsLeft = new float[VoiceCount];
    readonly float[] stepsRight = new float[VoiceCount];
    readonly bool[] active = new bool[VoiceCount];
    readonly bool[] skipped = new bool[VoiceCount];
    OctaveDivider divider;
    SvfCoefficients crystalsLowPass;
    SvfCoefficients crystalsHighPass;
    SvfState crystalsLow;
    SvfState crystalsHigh;
    PitchType activeType;
    int voicedHold;
    int sinceVoiced;
    double lastPeriod;
    double hopSeconds;
    int harmonyNote;
    int chordRoot;
    int chordTones;
    double arpPhase;
    int arpStep;
    float whammySemitones;
    float crystalsDelay;
    float mix;
    OutputStage output;

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
                    "Virtual Capo",
                    "Custom Shifter"
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
            VoiceDelay(1),
            VoiceDelay(2),
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
            ParamDescriptor.Choice("capo", "Capo", CapoNames(), 12),
            // Added later, so appended: presets store parameters by index. Voices 3 and 4 start silent.
            ParamDescriptor.Float("voice3Shift", "Voice 3 Shift", -24f, 24f, 7f, "st"),
            ParamDescriptor.Float("voice4Shift", "Voice 4 Shift", -24f, 24f, -5f, "st"),
            ParamDescriptor.Choice("voice3Harmony", "Voice 3 Harmony", HarmonyNames(), MaxHarmonySteps + 7),
            ParamDescriptor.Choice("voice4Harmony", "Voice 4 Harmony", HarmonyNames(), MaxHarmonySteps - 7),
            ParamDescriptor.Float("voice3Level", "Voice 3 Level", Decibels.SilenceDb, 6f, Decibels.SilenceDb, "dB"),
            ParamDescriptor.Float("voice4Level", "Voice 4 Level", Decibels.SilenceDb, 6f, Decibels.SilenceDb, "dB"),
            ParamDescriptor.Float("voice3Pan", "Voice 3 Pan", -100f, 100f, -25f, "%"),
            ParamDescriptor.Float("voice4Pan", "Voice 4 Pan", -100f, 100f, 25f, "%"),
            VoiceDelay(3),
            VoiceDelay(4),
            ParamDescriptor.Choice("keySource", "Key Source", ["Fixed", "MIDI", "Auto"]),
            ..CustomTableParams(),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            EffectsMix.Param,
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions),
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%")
        ],
        maxInstances: 2
    );

    /// <summary>The key (pitch class, 0 = C) the harmony voices currently use: Key, or the one from the Key Source.</summary>
    public int HarmonyKey { get; private set; }

    /// <summary>The scale (index into <see cref="Scales.Names" />) the harmony voices currently use.</summary>
    public int HarmonyScale { get; private set; }

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

    static ParamDescriptor VoiceDelay(int voice) =>
        new(
            $"voice{voice}Delay",
            $"Voice {voice} Delay",
            ParamKind.Float,
            0f,
            MaxVoiceDelayMs,
            0f,
            "ms",
            ParamTaper.Log,
            ParamSmoothing.Linear,
            60f
        );

    /// <summary>
    ///     "custom1C" .. "custom4B": voice V's shift for each note of the key, named as in C (C = the key's root, C# a
    ///     semitone above it, ...), so one table serves every key.
    /// </summary>
    static IEnumerable<ParamDescriptor> CustomTableParams() =>
        Enumerable.Range(0, VoiceCount * 12)
            .Select(static i => {
                    var (voice, note) = (i / 12, i % 12);
                    return new ParamDescriptor(
                        $"custom{voice + 1}{Scales.Keys[note].Replace('#', 's')}",
                        $"V{voice + 1} {Scales.Keys[note]}",
                        ParamKind.Float,
                        -24f,
                        24f,
                        Scales.HarmonySemitones(note, 0, MajorScale, DefaultCustomSteps[voice]),
                        "st",
                        Smoothing: ParamSmoothing.Step
                    );
                }
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

    static bool IsHarmony(PitchType type) => type is PitchType.Chromatic or PitchType.Diatonic or PitchType.CustomShifter;

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        tracker.Prepare(rate);
        shifterOne.Prepare(rate);
        shifterTwo.Prepare(rate);
        psola.Prepare(rate);
        grains.Prepare(rate, CrystalsGrainSeconds);
        var voiceCapacity = (int)(MaxVoiceDelayMs * 0.001 * rate) + 8;
        foreach (var delay in delays) {
            delay.Allocate(voiceCapacity);
        }

        crystalsLine.Allocate((int)(MaxCrystalsTimeMs * 0.001 * rate) + 8);
        divider.Prepare(rate);
        crystalsLowPass = SvfCoefficients.Design(BiquadType.LowPass, rate, 6_000, 0.7071);
        crystalsHighPass = SvfCoefficients.Design(BiquadType.HighPass, rate, 80, 0.7071);
        voicedHold = (int)(VoicedHoldSeconds * rate);
        hopSeconds = tracker.HopSamples / rate;
    }

    protected override void OnReset() {
        tracker.Reset();
        keys.Reset();
        ResetEngines();
        activeType = (PitchType)(int)Params.Current(Type);
        sinceVoiced = int.MaxValue;
        lastPeriod = 0;
        harmonyNote = int.MinValue;
        chordRoot = -1;
        chordTones = 0;
        Array.Clear(harmonies);
        whammySemitones = WhammySemitones();
        crystalsDelay = CrystalsDelaySamples();
        mix = MixFor(activeType, Params.Current(Mix));
        VoiceGains(activeType);
        targetsLeft.CopyTo(gainsLeft, 0);
        targetsRight.CopyTo(gainsRight, 0);
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            tracker.Dispose();
            shifterOne.Dispose();
            shifterTwo.Dispose();
            psola.Dispose();
            grains.Dispose();
            foreach (var delay in delays) {
                delay.Dispose();
            }

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
                     Voice3Level,
                     Voice4Level,
                     Voice1Pan,
                     Voice2Pan,
                     Voice3Pan,
                     Voice4Pan,
                     WhammyStart,
                     WhammyEnd,
                     Pedal,
                     CrystalsTime
                 ]) {
            Params.Advance(index, frames);
        }

        var rate = Spec.SampleRate;
        var mix = MixFor(type, EffectsMix.Apply(Params.Advance(Mix, frames), Params.Current(GlobalMix), context));
        VoiceGains(type);
        var harmony = IsHarmony(type);
        for (var v = 0; v < VoiceCount; v++) {
            delayTargets[v] = Params.Advance(DelayParams[v], frames) * 0.001f * (float)rate;
            shifts[v] = Ratio(Params.Advance(ShiftParams[v], frames));
            stepsLeft[v] = (targetsLeft[v] - gainsLeft[v]) / frames;
            stepsRight[v] = (targetsRight[v] - gainsRight[v]) / frames;

            // A harmony voice that stays silent through the block is skipped; it restarts from a clean state.
            var audible = gainsLeft[v] != 0 || gainsRight[v] != 0 || targetsLeft[v] != 0 || targetsRight[v] != 0;
            active[v] = harmony && audible;
            if (active[v] && skipped[v]) {
                psola.ResetVoice(v);
                delays[v].Reset();
            }

            skipped[v] |= harmony && !audible;
            skipped[v] &= !active[v];
        }

        var detune = Params.Advance(Detune, frames) / 1_200.0;
        var detuneUp = Math.Pow(2, detune);
        var detuneDown = 1 / detuneUp;
        FollowNotes(context.Notes);
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

        var ratios = type == PitchType.Chromatic ? shifts : harmonies;
        var mixStep = (mix - this.mix) / frames;
        var inputSelect = (int)Params.Current(InputSelect);
        var inputGain = Params.Advance(InputGain, frames) / 100f;
        for (var i = 0; i < frames; i++) {
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var mono = inputGain * Stereo.Select(inputSelect, dryLeft, dryRight);
            Track(mono);
            var period = sinceVoiced < voicedHold ? lastPeriod : 0;
            for (var v = 0; v < VoiceCount; v++) {
                gainsLeft[v] += stepsLeft[v];
                gainsRight[v] += stepsRight[v];
            }

            float one, two = 0f, wetLeft = 0f, wetRight = 0f;
            switch (type) {
                case PitchType.Detune:
                    one = Delayed(delays[0], shifterOne.Process(mono, detuneUp, period), delayTargets[0]);
                    two = Delayed(delays[1], shifterTwo.Process(mono, detuneDown, period), delayTargets[1]);
                    break;
                case PitchType.Chromatic:
                case PitchType.Diatonic:
                case PitchType.CustomShifter:
                    psola.Write(mono);
                    one = 0f;
                    for (var v = 0; v < VoiceCount; v++) {
                        if (active[v]) {
                            var voice = Delayed(delays[v], psola.Read(v, ratios[v], period), delayTargets[v]);
                            wetLeft += voice * gainsLeft[v];
                            wetRight += voice * gainsRight[v];
                        }
                    }

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

            if (!harmony) {
                wetLeft = one * gainsLeft[0] + two * gainsLeft[1];
                wetRight = one * gainsRight[0] + two * gainsRight[1];
            }

            this.mix += mixStep;
            io.Left[i] = dryLeft + (wetLeft - dryLeft) * this.mix;
            io.Right[i] = dryRight + (wetRight - dryRight) * this.mix;
        }

        // Land exactly on the targets, so rounding in the ramps never accumulates across blocks.
        this.mix = mix;
        targetsLeft.CopyTo(gainsLeft, 0);
        targetsRight.CopyTo(gainsRight, 0);
        whammySemitones = whammy;
        crystalsDelay = crystalsTarget;
        divider.FlushDenormals();
        crystalsLow.FlushDenormals();
        crystalsHigh.FlushDenormals();

        output.Apply(io, Params.Advance(Level, frames), Params.Advance(Balance, frames));
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

        // The note changes only past a small hysteresis, so a bend or vibrato does not flip the harmony.
        var midi = Scales.MidiNote(tracker.Frequency);
        if (Math.Abs(midi - harmonyNote) > 0.5 + NoteHysteresis) {
            harmonyNote = (int)Math.Round(midi);
        }

        keys.Add(Scales.PitchClass(harmonyNote), hopSeconds);
        UpdateHarmony();
    }

    /// <summary>Latches the chord held on the MIDI input; with every key released the last chord stays.</summary>
    void FollowNotes(in HeldNotes notes) {
        if (notes.IsEmpty) {
            return;
        }

        chordRoot = Scales.PitchClass(notes.Lowest);
        chordTones = notes.PitchClasses;
    }

    /// <summary>Picks the key and scale from the Key Source, then each harmony voice's ratio for the tracked note.</summary>
    void UpdateHarmony() {
        var key = (int)Params.Current(Key);
        var scale = (int)Params.Current(Scale);
        var chord = 0;
        switch ((PitchKeySource)(int)Params.Current(KeySource)) {
            case PitchKeySource.Midi when chordRoot >= 0:
                (key, chord) = (chordRoot, chordTones);
                scale = Scales.ChordScale(key, chord, scale);
                break;
            case PitchKeySource.Auto when keys.Key >= 0:
                key = keys.Key;
                if (Scales.IsMinor(scale) != keys.Minor) {
                    scale = keys.Minor ? MinorScale : MajorScale;
                }

                break;
        }

        (HarmonyKey, HarmonyScale) = (key, scale);
        if (activeType is not (PitchType.Diatonic or PitchType.CustomShifter)) {
            return;
        }

        var note = harmonyNote == int.MinValue ? 60 : harmonyNote;
        var degree = Scales.PitchClass(note - key);
        for (var v = 0; v < VoiceCount; v++) {
            var semitones = activeType == PitchType.CustomShifter
                ? Params.Current(CustomTable + 12 * v + degree)
                : Scales.HarmonySemitones(note, key, scale, (int)Params.Current(HarmonyParams[v]) - MaxHarmonySteps, chord);
            harmonies[v] = Ratio(semitones);
        }
    }

    float WhammySemitones() {
        var start = Params.Current(WhammyStart);
        return start + (Params.Current(WhammyEnd) - start) * Params.Current(Pedal) / 100f;
    }

    float CrystalsDelaySamples() => Params.Current(CrystalsTime) * 0.001f * (float)Spec.SampleRate;

    /// <summary>Sets each voice's target gains: four harmony voices, Detune's two, or one centred voice.</summary>
    void VoiceGains(PitchType type) {
        var voices = IsHarmony(type) ? VoiceCount : type == PitchType.Detune ? 2 : 0;
        for (var v = 0; v < VoiceCount; v++) {
            (targetsLeft[v], targetsRight[v]) = v < voices
                ? Pan(Decibels.ToGain(Params.Current(LevelParams[v])), Params.Current(PanParams[v]))
                : (0f, 0f);
        }

        if (voices == 0) {
            (targetsLeft[0], targetsRight[0]) = (1f, 1f);
        }
    }

    void ResetEngines() {
        shifterOne.Reset();
        shifterTwo.Reset();
        psola.Reset();
        grains.Reset();
        foreach (var delay in delays) {
            delay.Reset();
        }

        crystalsLine.Reset();
        divider.Reset();
        crystalsLow.Reset();
        crystalsHigh.Reset();
        Array.Clear(skipped);
        arpPhase = 0;
        arpStep = 0;
    }
}
