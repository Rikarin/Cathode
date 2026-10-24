using Cathode.Dsp.Filters;
using Cathode.Dsp.Modulation;
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
///         Detune: up to four voices, each detuned by its own amount through a <see cref="SpliceShifter" />, with an
///         optional LFO that sways the detune (the voices a quarter or half cycle apart). Chromatic, Diatonic and Custom
///         Shifter: up to four PSOLA voices (<see cref="PsolaShifter" />), at fixed semitone shifts, at diatonic intervals
///         in a key and scale chosen from the tracked note, or at the shift a per-voice table gives each of the twelve
///         notes of the key (the Axe-Fx Custom Shifter: any mapping, diatonic or not); each voice has level, pan, delay
///         (or a tempo division) and feedback, and a voice at silence costs nothing. Whammy: one shifted voice between a
///         start and an end shift, positioned by the Pedal parameter (0-100 %, the one to drive with a modifier); its
///         Tracking is Mono (period-synchronous splices), Off (free splices) or Poly, a phase vocoder
///         (<see cref="PhaseVocoderShifter" />) that shifts chords cleanly at the cost of a 43 ms delay. Octave Divider:
///         the OC-2 style analog divider (<see cref="OctaveDivider" />). Crystals: pitch-shifted grains
///         (<see cref="GrainShifter" />, reversed or forward, of a set splice length or tempo) on the output of a
///         feedback delay, whose repeats either climb again (shifted feedback) or each sound one shift up (unshifted),
///         with a diffuser in the loop. Arpeggiator: a tempo-synced sequence of up to sixteen semitone steps, each with
///         an amplitude shape and a pan from a pattern, running for ever or a number of times from each new note.
///         Virtual Capo: a single shifted voice of up to +/- 12 semitones plus a fine detune.
///     </para>
///     <para>
///         Key Source sets where Diatonic and Custom Shifter take their key. Fixed: Key and Scale. MIDI: the chord held on
///         the rig's MIDI input (<see cref="BlockContext.Notes" />, any channel); its lowest note is the root, the scale is
///         Scale when that holds every chord tone and otherwise the first scale that does
///         (<see cref="Scales.ChordScale" />: Am gives Minor, G7 Mixolydian), and diatonic voices snap to the nearest
///         chord tone (<see cref="Scales.HarmonySemitones(int, int, int, int, int)" />). Releasing the keys keeps the
///         last chord. Auto: the key estimated from the notes played (<see cref="KeyEstimator" />, Krumhansl-Schmuckler),
///         with Scale when it is major or minor like the estimate, else Major or Minor. Until they have a key, both use
///         Key and Scale. Temperament Just tunes their intervals to five-limit just ratios on the key
///         (<see cref="Scales.JustRatio" />), and Glide slides a voice to a new interval instead of jumping.
///     </para>
///     <para>
///         The detector: Pitch Tracking Smooth (the default) changes note only past a 0.15-semitone hysteresis, Fast
///         past 0.05 and with a shorter hold, and Off stops tracking (the splices run free and the harmony stays put).
///         Splice Length sets the shifters' minimum splice. Detector Source can take the tracked signal from another grid
///         row instead of the block input; the block then takes its inputs by row (<see cref="IRowInputBlock" />), the
///         chosen row feeding only the detector.
///     </para>
///     <para>
///         Feedback per voice: Delay mode repeats the voice through its own delay line (echoes at the same pitch), Shifter
///         mode feeds it back into the shifter's input, so every repeat is shifted again. Master Level, Pan, Feedback,
///         Pitch and Delay scale every voice's level, pan, feedback, shift (Detune and Chromatic) and delay. Input Mode
///         Stereo shifts the left input in voices 1 and 3 and the right in voices 2 and 4 (and keeps the sides apart
///         for the single-voice types); the detector always hears both. Input Gain sets the level into the shifters,
///         and a low and high cut shape the wet signal.
///     </para>
///     <para>
///         Whammy and Virtual Capo are always 100 % wet (the shifter's few milliseconds of delay would comb against the
///         dry signal at unison); the others use Mix (the Axe mix law: 50 % = -6 dB each). The dry signal stays stereo.
///     </para>
///     <para>
///         <see cref="IDspBlock.LatencySamples" /> is 0 for every type, so the graph compiler's compensation never
///         changes with Type: the dry path is undelayed and each type's wet delay is part of its sound. Wet path delays
///         at 48 kHz: Octave Divider none; Detune, Whammy, Arpeggiator and Virtual Capo 5-15 ms on average (a few to 50 ms,
///         see <see cref="SpliceShifter" />), polyphonic Whammy 43 ms; Chromatic, Diatonic and Custom Shifter a fixed
///         ~29 ms plus the voice delay; Crystals up to 200 ms of grain plus the delay time.
///     </para>
///     <para>Not modelled: formant correction and shift, and the Kill Dry flag (Mix 100 % does the same).</para>
/// </remarks>
public sealed class PitchBlock() : DspBlock(Info), IRowInputBlock {
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

    /// <summary>Voice 1's detune; voices 2 to 4 follow at <see cref="Detune2" />.</summary>
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
    public const int Detune2 = Balance + 1;
    public const int Detune3 = Detune2 + 1;
    public const int Detune4 = Detune3 + 1;
    public const int DetuneLfoRate = Detune4 + 1;
    public const int DetuneLfoTempo = DetuneLfoRate + 1;
    public const int DetuneLfoDepth = DetuneLfoTempo + 1;
    public const int MasterLevel = DetuneLfoDepth + 1;
    public const int MasterPan = MasterLevel + 1;
    public const int MasterFeedback = MasterPan + 1;
    public const int MasterPitch = MasterFeedback + 1;
    public const int MasterDelay = MasterPitch + 1;
    public const int InputMode = MasterDelay + 1;
    public const int Tracking = InputMode + 1;
    public const int Splice = Tracking + 1;
    public const int Temperament = Splice + 1;
    public const int FeedbackMode = Temperament + 1;

    /// <summary>Voice 1's feedback; voices 2 to 4 follow.</summary>
    public const int Feedback1 = FeedbackMode + 1;

    /// <summary>Voice 1's delay tempo; voices 2 to 4 follow.</summary>
    public const int DelayTempo1 = Feedback1 + VoiceCount;

    public const int Glide = DelayTempo1 + VoiceCount;
    public const int ArpRepeats = Glide + 1;
    public const int ArpAmpShape = ArpRepeats + 1;
    public const int ArpAmpAlpha = ArpAmpShape + 1;
    public const int ArpPanShape = ArpAmpAlpha + 1;
    public const int ArpPanAlpha = ArpPanShape + 1;
    public const int WhammyTracking = ArpPanAlpha + 1;
    public const int CrystalsSplice = WhammyTracking + 1;
    public const int CrystalsSpliceTempo = CrystalsSplice + 1;
    public const int CrystalsDirection = CrystalsSpliceTempo + 1;
    public const int CrystalsDiffusion = CrystalsDirection + 1;
    public const int CrystalsFeedbackType = CrystalsDiffusion + 1;
    public const int CapoDetune = CrystalsFeedbackType + 1;
    public const int InputGain = CapoDetune + 1;
    public const int LowCut = InputGain + 1;
    public const int HighCut = LowCut + 1;
    public const int DetectorSource = HighCut + 1;
    public const int GlobalMix = DetectorSource + 1;
    public const int InputSelect = GlobalMix + 1;

    public const int VoiceCount = 4;
    public const int MaxArpSteps = 16;
    public const float MaxVoiceDelayMs = 1_000f;
    public const float MaxCrystalsTimeMs = 2_000f;
    public const float MaxCrystalsSpliceMs = 200f;

    /// <summary>Harmony choices run from two octaves down to two octaves up, in scale steps.</summary>
    public const int MaxHarmonySteps = 14;

    /// <summary>The source rows the detector can hear (Row 1 to Row 6), besides the block input.</summary>
    public const int SourceRows = 6;

    public const int TrackingSmooth = 0;
    public const int TrackingFast = 1;
    public const int TrackingOff = 2;

    public const int WhammyMono = 0;
    public const int WhammyPoly = 1;
    public const int WhammyFree = 2;

    const double CrystalsGrainSeconds = 0.1;
    const double VoicedHoldSeconds = 0.1;
    const double FastHoldSeconds = 0.05;
    const double NoteHysteresis = 0.15;
    const double FastHysteresis = 0.05;
    const float MaxFeedback = 0.95f;
    const double ArpGateSeconds = 0.003;
    const double OnsetReleaseSeconds = 0.03;
    const float OnsetLevel = 0.01f; // -40 dBFS
    const int MajorScale = 0;
    const int MinorScale = 1;

    /// <summary>Arpeggiator step lengths in beats (quarter notes).</summary>
    static readonly double[] ArpDivisions = [1, 0.5, 1 / 3.0, 0.25, 1 / 6.0, 0.125];

    static readonly float[] DefaultArpSteps = [0, 7, 12, 7, 0, 5, 12, 5, 0, 7, 12, 19, 12, 7, 5, 7];

    /// <summary>The Custom Shifter's default tables, in steps of the major scale: a third, a fifth, an octave up and down.</summary>
    static readonly int[] DefaultCustomSteps = [2, 4, 7, -7];

    /// <summary>Each Detune voice's default detune and its LFO offset (in cycles), so the voices sway apart.</summary>
    static readonly (float Cents, double LfoOffset)[] DetuneVoices = [(10f, 0), (-10f, 0.5), (5f, 0.25), (-5f, 0.75)];

    static readonly int[] ShiftParams = [Voice1Shift, Voice2Shift, Voice3Shift, Voice4Shift];
    static readonly int[] HarmonyParams = [Voice1Harmony, Voice2Harmony, Voice3Harmony, Voice4Harmony];
    static readonly int[] LevelParams = [Voice1Level, Voice2Level, Voice3Level, Voice4Level];
    static readonly int[] PanParams = [Voice1Pan, Voice2Pan, Voice3Pan, Voice4Pan];
    static readonly int[] DelayParams = [Voice1Delay, Voice2Delay, Voice3Delay, Voice4Delay];
    static readonly int[] DetuneParams = [Detune, Detune2, Detune3, Detune4];

    readonly PitchTracker tracker = new();
    readonly KeyEstimator keys = new();
    readonly SpliceShifter[] shifters = [new(), new(), new(), new()];
    readonly PsolaShifter psola = new(VoiceCount);
    readonly PsolaShifter psolaRight = new(VoiceCount);
    readonly PhaseVocoderShifter vocoder = new();
    readonly PhaseVocoderShifter vocoderRight = new();
    readonly GrainShifter grains = new();
    readonly AllpassDiffuser diffuser = new();
    readonly FractionalDelayLine[] delays = [new(), new(), new(), new()];
    readonly FractionalDelayLine crystalsLine = new();
    readonly double[] arpSpeeds = new double[MaxArpSteps];
    readonly double[] shifts = new double[VoiceCount];
    readonly double[] harmonies = new double[VoiceCount];
    readonly double[] glidedLog = new double[VoiceCount];
    readonly double[] detuneCents = new double[VoiceCount];
    readonly float[] delayTargets = new float[VoiceCount];
    readonly float[] feedbacks = new float[VoiceCount];
    readonly float[] lastOut = new float[VoiceCount];
    readonly float[] gainsLeft = new float[VoiceCount];
    readonly float[] gainsRight = new float[VoiceCount];
    readonly float[] targetsLeft = new float[VoiceCount];
    readonly float[] targetsRight = new float[VoiceCount];
    readonly float[] stepsLeft = new float[VoiceCount];
    readonly float[] stepsRight = new float[VoiceCount];
    readonly bool[] active = new bool[VoiceCount];
    readonly bool[] skipped = new bool[VoiceCount];
    float[] sideLeft = [];
    float[] sideRight = [];
    int sideFrames;
    OctaveDivider divider;
    OctaveDivider dividerRight;
    SvfCoefficients crystalsLowPass;
    SvfCoefficients crystalsHighPass;
    SvfState crystalsLow;
    SvfState crystalsHigh;
    WetCuts cuts;
    Lfo detuneLfo;
    PitchType activeType;
    int voicedHold;
    int fastHold;
    int sinceVoiced;
    double lastPeriod;
    double hopSeconds;
    int harmonyNote;
    int chordRoot;
    int chordTones;
    double arpPhase;
    int arpStep;
    int arpPasses;
    float arpGate;
    float arpGateRate;
    float onsetEnvelope;
    float onsetRelease;
    bool onsetArmed;
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
            DetuneParam(1),
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
            ParamDescriptor.Choice("arpSteps", "Arp Steps", [..Enumerable.Range(1, MaxArpSteps).Select(Invariant)], 3),
            ..Enumerable.Range(1, MaxArpSteps).Select(ArpStep),
            ParamDescriptor.Choice("capo", "Capo", CapoNames(), 12),
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
            DetuneParam(2),
            DetuneParam(3),
            DetuneParam(4),
            ParamDescriptor.Float("detuneLfoRate", "Detune LFO Rate", 0.05f, 10f, 1f, "Hz", ParamTaper.Log) with {
                TypeLabels = For("Detune LFO Rate", PitchType.Detune)
            },
            ParamDescriptor.Choice("detuneLfoTempo", "Detune LFO Tempo", TempoDivisions.Names) with {
                TypeLabels = For("Detune LFO Tempo", PitchType.Detune)
            },
            ParamDescriptor.Float("detuneLfoDepth", "Detune LFO Depth", 0f, 50f, 0f, "cents") with {
                TypeLabels = For("Detune LFO Depth", PitchType.Detune)
            },
            ParamDescriptor.Float("masterLevel", "Master Level", -24f, 12f, 0f, "dB"),
            ParamDescriptor.Float("masterPan", "Master Pan", -100f, 100f, 100f, "%") with {
                TypeLabels = For("Master Pan", VoiceTypes)
            },
            ParamDescriptor.Float("masterFeedback", "Master Feedback", 0f, 100f, 100f, "%") with {
                TypeLabels = For("Master Feedback", VoiceTypes)
            },
            ParamDescriptor.Float("masterPitch", "Master Pitch", 0f, 100f, 100f, "%") with {
                TypeLabels = For("Master Pitch", PitchType.Detune, PitchType.Chromatic)
            },
            ParamDescriptor.Float("masterDelay", "Master Delay", 0f, 100f, 100f, "%") with {
                TypeLabels = For("Master Delay", VoiceTypes)
            },
            ParamDescriptor.Choice("inputMode", "Input Mode", ["Mono", "Stereo"]),
            ParamDescriptor.Choice("tracking", "Pitch Tracking", ["Smooth", "Fast", "Off"]),
            ParamDescriptor.Float(
                "splice",
                "Splice Length",
                (float)(SpliceShifter.MinSpliceSeconds * 1_000),
                (float)(SpliceShifter.MaxSpliceSeconds * 1_000),
                (float)(SpliceShifter.MinSpliceSeconds * 1_000),
                "ms"
            ) with {
                TypeLabels = For("Splice Length", PitchType.Detune, PitchType.Whammy, PitchType.Arpeggiator, PitchType.VirtualCapo)
            },
            ParamDescriptor.Choice("temperament", "Temperament", ["Equal", "Just"]) with {
                TypeLabels = For("Temperament", PitchType.Diatonic, PitchType.CustomShifter)
            },
            ParamDescriptor.Choice("feedbackMode", "Feedback Mode", ["Delay", "Shifter"]) with {
                TypeLabels = For("Feedback Mode", VoiceTypes)
            },
            ..Enumerable.Range(1, VoiceCount)
                .Select(static v => ParamDescriptor.Float($"feedback{v}", $"Feedback {v}", -90f, 90f, 0f, "%") with {
                        TypeLabels = For($"Feedback {v}", VoiceTypes)
                    }
                ),
            ..Enumerable.Range(1, VoiceCount)
                .Select(static v => ParamDescriptor.Choice($"delayTempo{v}", $"Delay Tempo {v}", TempoDivisions.Names) with {
                        TypeLabels = For($"Delay Tempo {v}", VoiceTypes)
                    }
                ),
            ParamDescriptor.Float("glide", "Glide Time", 0f, 1_000f, 0f, "ms") with {
                TypeLabels = For("Glide Time", PitchType.Diatonic, PitchType.CustomShifter)
            },
            ParamDescriptor.Choice("arpRepeats", "Arp Repeats", ["Infinite", "1", "2", "3", "4", "5", "6", "7", "8"]) with {
                TypeLabels = For("Arp Repeats", PitchType.Arpeggiator)
            },
            ParamDescriptor.Choice("arpAmpShape", "Arp Amp Shape", ["Flat", "Decay", "Swell", "Triangle"]) with {
                TypeLabels = For("Arp Amp Shape", PitchType.Arpeggiator)
            },
            ParamDescriptor.Float("arpAmpAlpha", "Arp Amp Alpha", 0f, 100f, 50f, "%") with {
                TypeLabels = For("Arp Amp Alpha", PitchType.Arpeggiator)
            },
            ParamDescriptor.Choice("arpPanShape", "Arp Pan Shape", ["Center", "Alternate", "Ramp", "Random"]) with {
                TypeLabels = For("Arp Pan Shape", PitchType.Arpeggiator)
            },
            ParamDescriptor.Float("arpPanAlpha", "Arp Pan Width", 0f, 100f, 50f, "%") with {
                TypeLabels = For("Arp Pan Width", PitchType.Arpeggiator)
            },
            ParamDescriptor.Choice("whammyTracking", "Whammy Tracking", ["Mono", "Poly", "Off"]) with {
                TypeLabels = For("Whammy Tracking", PitchType.Whammy)
            },
            ParamDescriptor.Float(
                "crystalsSplice",
                "Crystals Splice",
                20f,
                MaxCrystalsSpliceMs,
                (float)(CrystalsGrainSeconds * 1_000),
                "ms",
                ParamTaper.Log
            ) with {
                TypeLabels = For("Crystals Splice", PitchType.Crystals)
            },
            ParamDescriptor.Choice("crystalsSpliceTempo", "Crystals Splice Tempo", TempoDivisions.Names) with {
                TypeLabels = For("Crystals Splice Tempo", PitchType.Crystals)
            },
            ParamDescriptor.Choice("crystalsDirection", "Crystals Direction", ["Reverse", "Forward"]) with {
                TypeLabels = For("Crystals Direction", PitchType.Crystals)
            },
            ParamDescriptor.Float("crystalsDiffusion", "Crystals Diffusion", 0f, 100f, 0f, "%") with {
                TypeLabels = For("Crystals Diffusion", PitchType.Crystals)
            },
            ParamDescriptor.Choice("crystalsFeedbackType", "Crystals Feedback Type", ["Shifted", "Unshifted"]) with {
                TypeLabels = For("Crystals Feedback Type", PitchType.Crystals)
            },
            ParamDescriptor.Float("capoDetune", "Capo Detune", -50f, 50f, 0f, "cents") with {
                TypeLabels = For("Capo Detune", PitchType.VirtualCapo)
            },
            ParamDescriptor.Float("inputGain", "Input Gain", 0f, 100f, 100f, "%"),
            WetCuts.LowCutParam("lowCut", "Low Cut"),
            WetCuts.HighCutParam("highCut", "High Cut"),
            ParamDescriptor.Choice(
                "detectorSource",
                "Detector Source",
                ["Block Input", ..Enumerable.Range(1, SourceRows).Select(static r => $"Row {r}")]
            ),
            EffectsMix.Param,
            ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions)
        ],
        maxInstances: 2
    );

    /// <summary>The key (pitch class, 0 = C) the harmony voices currently use: Key, or the one from the Key Source.</summary>
    public int HarmonyKey { get; private set; }

    /// <summary>The scale (index into <see cref="Scales.Names" />) the harmony voices currently use.</summary>
    public int HarmonyScale { get; private set; }

    /// <summary>The types with per-voice levels, pans, delays and feedback.</summary>
    static PitchType[] VoiceTypes => [PitchType.Detune, PitchType.Chromatic, PitchType.Diatonic, PitchType.CustomShifter];

    static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Type labels that show a parameter as <paramref name="name" /> on <paramref name="types" /> only.</summary>
    static string[] For(string name, params PitchType[] types) =>
        [..Enum.GetValues<PitchType>().Select(t => types.Contains(t) ? name : "")];

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

    static ParamDescriptor DetuneParam(int voice) =>
        ParamDescriptor.Float($"detune{voice}", $"Detune {voice}", -50f, 50f, DetuneVoices[voice - 1].Cents, "cents") with {
            TypeLabels = For($"Detune {voice}", PitchType.Detune)
        };

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

    static bool HasVoices(PitchType type) => IsHarmony(type) || type == PitchType.Detune;

    /// <summary>The level of an arpeggiator step at <paramref name="position" /> (0..1) through it.</summary>
    public static float ArpAmplitude(int shape, float alpha, double position) {
        var k = Math.Pow(4, (alpha - 50) / 50.0); // 0.25 .. 4
        var p = Math.Clamp(position, 0, 1);
        return shape switch {
            1 => (float)Math.Pow(1 - p, k),
            2 => (float)Math.Pow(p, k),
            3 => (float)Math.Pow(1 - Math.Abs(2 * p - 1), k),
            _ => 1f
        };
    }

    /// <summary>The pan (−1..1) of arpeggiator step <paramref name="step" /> of <paramref name="count" />.</summary>
    public static float ArpPan(int shape, float width, int step, int count) {
        var w = Math.Clamp(width / 100f, 0f, 1f);
        switch (shape) {
            case 1:
                return step % 2 == 0 ? -w : w;
            case 2:
                return count > 1 ? -w + 2 * w * step / (count - 1) : 0f;
            case 3: {
                var x = (uint)step * 0x9E3779B1u;
                x ^= x >> 15;
                x *= 0x85EBCA77u;
                x ^= x >> 13;
                return w * ((x & 0xFFFF) / 32_767.5f - 1);
            }

            default:
                return 0f;
        }
    }

    public void MixInput(int row, in StereoBuffer source, in StereoBuffer destination) {
        var detectorRow = (int)Params.GetTarget(DetectorSource) - 1;
        if (row != detectorRow || source.Length > sideLeft.Length) {
            for (var i = 0; i < source.Length; i++) {
                destination.Left[i] += source.Left[i];
                destination.Right[i] += source.Right[i];
            }

            return;
        }

        if (sideFrames != source.Length) {
            Array.Clear(sideLeft, 0, source.Length);
            Array.Clear(sideRight, 0, source.Length);
            sideFrames = source.Length;
        }

        for (var i = 0; i < source.Length; i++) {
            sideLeft[i] += source.Left[i];
            sideRight[i] += source.Right[i];
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        var rate = spec.SampleRate;
        tracker.Prepare(rate);
        foreach (var shifter in shifters) {
            shifter.Prepare(rate);
        }

        psola.Prepare(rate);
        psolaRight.Prepare(rate);
        vocoder.Prepare(rate);
        vocoderRight.Prepare(rate);
        grains.Prepare(rate, CrystalsGrainSeconds, MaxCrystalsSpliceMs * 0.001);
        diffuser.Prepare(rate);
        var voiceCapacity = (int)(MaxVoiceDelayMs * 0.001 * rate) + 8;
        foreach (var delay in delays) {
            delay.Allocate(voiceCapacity);
        }

        crystalsLine.Allocate((int)(MaxCrystalsTimeMs * 0.001 * rate) + 8);
        divider.Prepare(rate);
        dividerRight.Prepare(rate);
        crystalsLowPass = SvfCoefficients.Design(BiquadType.LowPass, rate, 6_000, 0.7071);
        crystalsHighPass = SvfCoefficients.Design(BiquadType.HighPass, rate, 80, 0.7071);
        voicedHold = (int)(VoicedHoldSeconds * rate);
        fastHold = (int)(FastHoldSeconds * rate);
        hopSeconds = tracker.HopSamples / rate;
        arpGateRate = (float)(1 - Math.Exp(-1 / (ArpGateSeconds * rate)));
        onsetRelease = (float)Math.Exp(-1 / (OnsetReleaseSeconds * rate));
        sideLeft = new float[spec.MaxBlockSize];
        sideRight = new float[spec.MaxBlockSize];
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
        sideFrames = 0;
        detuneLfo.Reset();
        cuts.Reset();
        Array.Clear(harmonies);
        Array.Clear(glidedLog);
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
            foreach (var shifter in shifters) {
                shifter.Dispose();
            }

            psola.Dispose();
            psolaRight.Dispose();
            vocoder.Dispose();
            vocoderRight.Dispose();
            grains.Dispose();
            diffuser.Dispose();
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
                     CrystalsTime,
                     MasterLevel,
                     MasterPan
                 ]) {
            Params.Advance(index, frames);
        }

        var rate = Spec.SampleRate;
        var tempo = context.Tempo > 0 ? context.Tempo : 120;
        var stereo = (int)Params.Current(InputMode) == 1;
        var tracking = (int)Params.Current(Tracking);
        var inputGain = Params.Advance(InputGain, frames) / 100f;
        var mix = MixFor(type, EffectsMix.Apply(Params.Advance(Mix, frames), Params.Current(GlobalMix), context));
        VoiceGains(type);
        var voiced = HasVoices(type);
        var harmony = IsHarmony(type);
        var masterPitch = Params.Advance(MasterPitch, frames) / 100.0;
        var masterDelay = Params.Advance(MasterDelay, frames) / 100f;
        var masterFeedback = Params.Advance(MasterFeedback, frames) / 100f;
        var shifterFeedback = (int)Params.Current(FeedbackMode) == 1;
        for (var v = 0; v < VoiceCount; v++) {
            var delayTempo = (int)Params.Current(DelayTempo1 + v);
            var delayMs = Params.Advance(DelayParams[v], frames);
            if (delayTempo > 0) {
                delayMs = (float)Math.Min(MaxVoiceDelayMs, TempoDivisions.Milliseconds(delayTempo, tempo));
            }

            delayTargets[v] = delayMs * masterDelay * 0.001f * (float)rate;
            shifts[v] = Ratio(Params.Advance(ShiftParams[v], frames) * masterPitch);
            detuneCents[v] = Params.Advance(DetuneParams[v], frames) * masterPitch;
            feedbacks[v] = Math.Clamp(Params.Advance(Feedback1 + v, frames) / 100f * masterFeedback, -MaxFeedback, MaxFeedback);
            stepsLeft[v] = (targetsLeft[v] - gainsLeft[v]) / frames;
            stepsRight[v] = (targetsRight[v] - gainsRight[v]) / frames;

            // A voice that stays silent through the block is skipped; it restarts from a clean state.
            var audible = gainsLeft[v] != 0 || gainsRight[v] != 0 || targetsLeft[v] != 0 || targetsRight[v] != 0;
            active[v] = voiced && audible;
            if (active[v] && skipped[v]) {
                psola.ResetVoice(v);
                psolaRight.ResetVoice(v);
                shifters[v].Reset();
                delays[v].Reset();
                lastOut[v] = 0;
            }

            skipped[v] |= voiced && !audible;
            skipped[v] &= !active[v];
        }

        // Shifter-mode feedback into the shared harmony input never adds up past the limit.
        var feedbackSum = 0f;
        for (var v = 0; v < VoiceCount; v++) {
            feedbackSum += MathF.Abs(feedbacks[v]);
        }

        var sharedFeedbackScale = harmony && shifterFeedback && feedbackSum > MaxFeedback ? MaxFeedback / feedbackSum : 1f;
        var lfoRate = TempoDivisions.Rate(
            (int)Params.Current(DetuneLfoTempo),
            context.Tempo,
            Params.Advance(DetuneLfoRate, frames)
        );
        var lfoDepth = Params.Advance(DetuneLfoDepth, frames);
        var splice = Params.Advance(Splice, frames) * 0.001;
        foreach (var shifter in shifters) {
            shifter.SetSplice(splice);
        }

        FollowNotes(context.Notes);
        UpdateHarmony();
        var glide = Params.Advance(Glide, frames);
        var glideRate = glide > 0 ? 1 - Math.Exp(-1 / (glide * 0.001 * rate)) : 1;
        var whammy = WhammySemitones();
        var whammyStep = (whammy - whammySemitones) / frames;
        var whammyTracking = (int)Params.Current(WhammyTracking);
        var capo = Ratio(Params.Current(Capo) - 12 + Params.Advance(CapoDetune, frames) / 100.0);
        var octaveOne = Params.Advance(Octave1Level, frames) / 100f;
        var octaveTwo = Params.Advance(Octave2Level, frames) / 100f;
        var crystalsSpeed = Ratio(Params.Advance(CrystalsShift, frames))
            * ((int)Params.Current(CrystalsDirection) == 0 ? -1 : 1);
        var crystalsTarget = CrystalsDelaySamples();
        var crystalsStep = (crystalsTarget - crystalsDelay) / frames;
        var feedback = Params.Advance(CrystalsFeedback, frames) / 100f;
        var diffusion = Params.Advance(CrystalsDiffusion, frames) / 100f;
        var unshifted = (int)Params.Current(CrystalsFeedbackType) == 1;
        var spliceTempo = (int)Params.Current(CrystalsSpliceTempo);
        var spliceMs = spliceTempo > 0
            ? Math.Min(MaxCrystalsSpliceMs, TempoDivisions.Milliseconds(spliceTempo, tempo))
            : Params.Advance(CrystalsSplice, frames);
        if (spliceTempo > 0) {
            Params.Advance(CrystalsSplice, frames);
        }

        grains.SetGrain(spliceMs * 0.001);
        cuts.Design(rate, Params.Advance(LowCut, frames), Params.Advance(HighCut, frames));

        var arpCount = (int)Params.Current(ArpSteps) + 1;
        var arpRate = tempo / 60 / rate / ArpDivisions[(int)Params.Current(ArpTempo)];
        var arpRepeats = (int)Params.Current(ArpRepeats);
        var arpAmpShape = (int)Params.Current(ArpAmpShape);
        var arpAmpAlpha = Params.Advance(ArpAmpAlpha, frames);
        var arpPanShape = (int)Params.Current(ArpPanShape);
        var arpPanWidth = Params.Advance(ArpPanAlpha, frames);
        for (var s = 0; s < MaxArpSteps; s++) {
            arpSpeeds[s] = Ratio(Params.Current(ArpStep1 + s));
        }

        var fromRow = (int)Params.Current(DetectorSource) > 0;
        var side = fromRow && sideFrames == frames;
        var ratios = type == PitchType.Chromatic ? shifts : harmonies;
        var mixStep = (mix - this.mix) / frames;
        var inputSelect = (int)Params.Current(InputSelect);
        for (var i = 0; i < frames; i++) {
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var mono = Stereo.Select(inputSelect, dryLeft, dryRight);
            if (tracking != TrackingOff) {
                Track(fromRow ? side ? 0.5f * (sideLeft[i] + sideRight[i]) : 0f : mono, tracking == TrackingFast);
            }

            var hold = tracking == TrackingFast ? fastHold : voicedHold;
            var period = sinceVoiced < hold ? lastPeriod : 0;
            var inLeft = (stereo ? dryLeft : mono) * inputGain;
            var inRight = (stereo ? dryRight : mono) * inputGain;
            for (var v = 0; v < VoiceCount; v++) {
                gainsLeft[v] += stepsLeft[v];
                gainsRight[v] += stepsRight[v];
            }

            float one = 0f, oneRight = 0f, wetLeft = 0f, wetRight = 0f;
            switch (type) {
                case PitchType.Detune:
                    for (var v = 0; v < VoiceCount; v++) {
                        if (!active[v]) {
                            continue;
                        }

                        var cents = detuneCents[v];
                        if (lfoDepth > 0) {
                            cents += lfoDepth * detuneLfo.Value(LfoShape.Sine, DetuneVoices[v].LfoOffset);
                        }

                        var input = (stereo && v % 2 == 1 ? inRight : inLeft) + (shifterFeedback ? feedbacks[v] * lastOut[v] : 0f);
                        var shifted = shifters[v].Process(input, Math.Pow(2, cents / 1_200), period);
                        var voice = Delayed(delays[v], shifted, delayTargets[v], shifterFeedback ? 0f : feedbacks[v]);
                        lastOut[v] = voice;
                        wetLeft += voice * gainsLeft[v];
                        wetRight += voice * gainsRight[v];
                    }

                    detuneLfo.Advance(lfoRate, rate);
                    break;
                case PitchType.Chromatic:
                case PitchType.Diatonic:
                case PitchType.CustomShifter: {
                    float backLeft = 0f, backRight = 0f;
                    if (shifterFeedback) {
                        for (var v = 0; v < VoiceCount; v++) {
                            var back = feedbacks[v] * sharedFeedbackScale * lastOut[v];
                            if (stereo && v % 2 == 1) {
                                backRight += back;
                            } else {
                                backLeft += back;
                            }
                        }
                    }

                    psola.Write(inLeft + backLeft + (stereo ? 0f : backRight));
                    if (stereo) {
                        psolaRight.Write(inRight + backRight);
                    }

                    for (var v = 0; v < VoiceCount; v++) {
                        if (!active[v]) {
                            continue;
                        }

                        var ratio = ratios[v];
                        if (glide > 0 && type != PitchType.Chromatic) {
                            var target = Math.Log2(ratio);
                            glidedLog[v] += (target - glidedLog[v]) * glideRate;
                            ratio = Math.Pow(2, glidedLog[v]);
                        }

                        var engine = stereo && v % 2 == 1 ? psolaRight : psola;
                        var shifted = engine.Read(v, ratio, period);
                        var voice = Delayed(delays[v], shifted, delayTargets[v], shifterFeedback ? 0f : feedbacks[v]);
                        lastOut[v] = voice;
                        wetLeft += voice * gainsLeft[v];
                        wetRight += voice * gainsRight[v];
                    }

                    break;
                }

                case PitchType.Whammy: {
                    whammySemitones += whammyStep;
                    var ratio = Ratio(whammySemitones);
                    if (whammyTracking == WhammyPoly) {
                        one = vocoder.Process(inLeft, ratio);
                        oneRight = stereo ? vocoderRight.Process(inRight, ratio) : one;
                    } else {
                        var hint = whammyTracking == WhammyFree ? 0 : period;
                        one = shifters[0].Process(inLeft, ratio, hint);
                        oneRight = stereo ? shifters[1].Process(inRight, ratio, hint) : one;
                    }

                    break;
                }

                case PitchType.OctaveDivider: {
                    var (down, twoDown) = divider.Process(inLeft);
                    one = octaveOne * down + octaveTwo * twoDown;
                    if (stereo) {
                        var (downRight, twoDownRight) = dividerRight.Process(inRight);
                        oneRight = octaveOne * downRight + octaveTwo * twoDownRight;
                    } else {
                        oneRight = one;
                    }

                    break;
                }

                case PitchType.Crystals: {
                    crystalsDelay += crystalsStep;
                    var repeat = crystalsLine.Read(crystalsDelay);
                    one = grains.Process(repeat, crystalsSpeed);
                    var loop = unshifted ? repeat : one;
                    var fed = (float)crystalsHigh.Tick(crystalsHighPass, crystalsLow.Tick(crystalsLowPass, loop));
                    crystalsLine.Write(0.5f * (inLeft + inRight) + feedback * diffuser.Process(fed, diffusion));
                    oneRight = one;
                    break;
                }

                case PitchType.Arpeggiator: {
                    arpPhase += arpRate;
                    if (arpPhase >= 1) {
                        arpPhase -= 1;
                        arpStep++;
                    }

                    if (arpStep >= arpCount) {
                        arpStep %= arpCount;
                        arpPasses++;
                    }

                    if (arpRepeats > 0) {
                        Onset(mono);
                    }

                    var running = arpRepeats == 0 || arpPasses < arpRepeats;
                    arpGate += ((running ? 1f : 0f) - arpGate) * arpGateRate;
                    var level = arpGate * ArpAmplitude(arpAmpShape, arpAmpAlpha, arpPhase);
                    var shiftedLeft = shifters[0].Process(inLeft, arpSpeeds[arpStep], period) * level;
                    var shiftedRight = stereo ? shifters[1].Process(inRight, arpSpeeds[arpStep], period) * level : shiftedLeft;
                    var pan = ArpPan(arpPanShape, arpPanWidth, arpStep, arpCount);
                    if (pan == 0) {
                        (one, oneRight) = (shiftedLeft, shiftedRight);
                    } else {
                        var (panLeft, panRight) = Pan(1f, 100f * pan);
                        (one, oneRight) = (shiftedLeft * panLeft, shiftedRight * panRight);
                    }

                    break;
                }

                default:
                    one = shifters[0].Process(inLeft, capo, period);
                    oneRight = stereo ? shifters[1].Process(inRight, capo, period) : one;
                    break;
            }

            if (!voiced) {
                wetLeft = one * gainsLeft[0];
                wetRight = oneRight * gainsRight[0];
            }

            cuts.Tick(ref wetLeft, ref wetRight);
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
        if (glide <= 0) {
            for (var v = 0; v < VoiceCount; v++) {
                glidedLog[v] = harmonies[v] > 0 ? Math.Log2(harmonies[v]) : 0;
            }
        }

        for (var v = 0; v < VoiceCount; v++) {
            lastOut[v] = MathF.Abs(lastOut[v]) < 1e-20f ? 0f : lastOut[v];
        }

        divider.FlushDenormals();
        dividerRight.FlushDenormals();
        crystalsLow.FlushDenormals();
        crystalsHigh.FlushDenormals();
        cuts.FlushDenormals();
        if (side) {
            Array.Clear(sideLeft, 0, frames);
            Array.Clear(sideRight, 0, frames);
        }

        sideFrames = 0;
        output.Apply(io, Params.Advance(Level, frames), Params.Advance(Balance, frames));
    }

    static float MixFor(PitchType type, float mix) =>
        type is PitchType.Whammy or PitchType.VirtualCapo ? 1f : mix / 100f;

    /// <summary>The voice's delay line, read before it is written (a delay of N is N samples), with its own feedback.</summary>
    static float Delayed(FractionalDelayLine line, float input, float delay, float feedback) {
        var output = line.Read(delay);
        line.Write(input + feedback * output);
        return output;
    }

    /// <summary>Restarts the arpeggiator on each new note: the playing rising through −40 dBFS after falling 6 dB below.</summary>
    void Onset(float mono) {
        var magnitude = MathF.Abs(mono);
        onsetEnvelope = magnitude > onsetEnvelope ? magnitude : onsetEnvelope * onsetRelease;
        if (onsetArmed && onsetEnvelope > OnsetLevel) {
            onsetArmed = false;
            arpPhase = 0;
            arpStep = 0;
            arpPasses = 0;
        } else if (!onsetArmed && onsetEnvelope < 0.5f * OnsetLevel) {
            onsetArmed = true;
        }
    }

    void Track(float mono, bool fast) {
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
        if (Math.Abs(midi - harmonyNote) > 0.5 + (fast ? FastHysteresis : NoteHysteresis)) {
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

        var just = (int)Params.Current(Temperament) == 1;
        var note = harmonyNote == int.MinValue ? 60 : harmonyNote;
        var degree = Scales.PitchClass(note - key);
        for (var v = 0; v < VoiceCount; v++) {
            var semitones = activeType == PitchType.CustomShifter
                ? Params.Current(CustomTable + 12 * v + degree)
                : Scales.HarmonySemitones(note, key, scale, (int)Params.Current(HarmonyParams[v]) - MaxHarmonySteps, chord);
            harmonies[v] = just ? Scales.JustRatio(note, key, semitones) : Ratio(semitones);
        }
    }

    float WhammySemitones() {
        var start = Params.Current(WhammyStart);
        return start + (Params.Current(WhammyEnd) - start) * Params.Current(Pedal) / 100f;
    }

    float CrystalsDelaySamples() => Params.Current(CrystalsTime) * 0.001f * (float)Spec.SampleRate;

    /// <summary>
    ///     Sets each voice's target gains: four voices for the harmony and detune types, or one centred voice, all at the
    ///     master level and with their pans scaled by the master pan.
    /// </summary>
    void VoiceGains(PitchType type) {
        var master = Decibels.ToGain(Params.Current(MasterLevel));
        var masterPan = Params.Current(MasterPan) / 100f;
        var voices = HasVoices(type) ? VoiceCount : 0;
        for (var v = 0; v < VoiceCount; v++) {
            (targetsLeft[v], targetsRight[v]) = v < voices
                ? Pan(Decibels.ToGain(Params.Current(LevelParams[v])) * master, Params.Current(PanParams[v]) * masterPan)
                : (0f, 0f);
        }

        if (voices == 0) {
            (targetsLeft[0], targetsRight[0]) = (master, master);
        }
    }

    void ResetEngines() {
        foreach (var shifter in shifters) {
            shifter.Reset();
        }

        psola.Reset();
        psolaRight.Reset();
        vocoder.Reset();
        vocoderRight.Reset();
        grains.Reset();
        diffuser.Reset();
        foreach (var delay in delays) {
            delay.Reset();
        }

        crystalsLine.Reset();
        divider.Reset();
        dividerRight.Reset();
        crystalsLow.Reset();
        crystalsHigh.Reset();
        Array.Clear(skipped);
        Array.Clear(lastOut);
        arpPhase = 0;
        arpStep = 0;
        arpPasses = 0;
        arpGate = 1f;
        onsetEnvelope = 0;
        onsetArmed = true;
    }
}
