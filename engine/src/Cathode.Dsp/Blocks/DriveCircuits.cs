using System.Runtime.CompilerServices;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>What one stage of a <see cref="DriveCircuit" /> does.</summary>
public enum StageKind {
    /// <summary>
    ///     An op-amp (or transistor) gain stage: the stage input drives two series RC legs to ground (or to a virtual
    ///     ground when inverting), and their current flows through a feedback network of a resistor, a capacitor and
    ///     optional clipping diodes. Non-inverting output is the input plus the feedback voltage.
    /// </summary>
    Gain,

    /// <summary>A series resistor into diodes and a capacitor to ground (hard clipping to ground).</summary>
    Clipper,

    /// <summary>A smooth full-wave rectifier (octave-up), blended with its input.</summary>
    Rectifier
}

/// <summary>How the single Tone knob filters the output, at the base sample rate.</summary>
public enum ToneKind {
    /// <summary>A first-order low-pass swept from <see cref="ToneControl.LowHz" /> to <see cref="ToneControl.HighHz" />.</summary>
    HighCut,

    /// <summary>
    ///     A crossfade from a low-pass at <see cref="ToneControl.LowHz" /> to a high-pass at
    ///     <see cref="ToneControl.HighHz" /> (the Big Muff / DS-1 tone stack, with its mid scoop at noon).
    /// </summary>
    Blend,

    /// <summary>A first-order tilt around <see cref="ToneControl.LowHz" />, ±<see cref="ToneControl.RangeDb" /> / 2.</summary>
    Tilt
}

/// <summary>A circuit's tone control; the frequencies' meaning depends on <see cref="ToneKind" />.</summary>
public readonly record struct ToneControl(ToneKind Kind, double LowHz, double HighHz, double RangeDb = 0);

/// <summary>Two antiparallel diode stacks; see <see cref="DiodePair" />.</summary>
public readonly record struct DiodeStacks(DiodeModel Forward, int ForwardCount, DiodeModel Reverse, int ReverseCount) {
    public bool IsSymmetric => ForwardCount == ReverseCount && (ForwardCount == 0 || Forward == Reverse);

    public static DiodeStacks Pair(DiodeModel model, int count = 1) => new(model, count, model, count);
}

/// <summary>Component values of one stage. Resistances in ohms, capacitances in farads.</summary>
public sealed record DriveStage(StageKind Kind) {
    /// <summary>Gain: first ground leg.</summary>
    public double GroundResistance { get; init; } = 4.7e3;

    public double GroundCapacitance { get; init; } = 47e-9;

    /// <summary>Gain: optional second ground leg (0 = none).</summary>
    public double GroundResistance2 { get; init; }

    public double GroundCapacitance2 { get; init; }

    /// <summary>Gain: fixed feedback resistance.</summary>
    public double Feedback { get; init; } = 10e3;

    /// <summary>Gain: the drive pot in series with <see cref="Feedback" /> (audio taper; 0 = fixed gain).</summary>
    public double FeedbackPot { get; init; }

    public double FeedbackCapacitance { get; init; } = 100e-12;

    /// <summary>Gain: gain-bandwidth product, which caps the closed-loop bandwidth at GBW / gain.</summary>
    public double GainBandwidth { get; init; } = 3e6;

    public bool Inverting { get; init; }

    /// <summary>
    ///     Gain: output swing before the supply rails soften it (0 = unlimited). Set it only where nothing clips after
    ///     the stage: rail clipping ahead of a diode clipper is barely audible, and the doubled hard clipping aliases.
    /// </summary>
    public double Rail { get; init; }

    /// <summary>Clipper: series resistance.</summary>
    public double Series { get; init; } = 1e3;

    /// <summary>Clipper: shunt capacitance across the diodes.</summary>
    public double Shunt { get; init; } = 10e-9;

    /// <summary>Rectifier: knee voltage (smoothness at the zero crossing).</summary>
    public double Knee { get; init; } = 0.02;

    /// <summary>Rectifier: proportion of rectified signal.</summary>
    public double Blend { get; init; } = 1;

    public DiodeStacks Diodes { get; init; }

    public bool IsSymmetric => Kind != StageKind.Rectifier && Diodes.IsSymmetric;
}

/// <summary>
///     One drive circuit: input coupling, a gain set by Drive, an operating-point bias, up to
///     <see cref="DriveCircuits.MaxStages" /> stages at the oversampled rate, an optional clean blend, a DC blocker, then
///     the tone control and output trim at the base rate.
/// </summary>
public sealed record DriveCircuit(string Name, string ModelledAfter) {
    /// <summary>Input coupling capacitor corner.</summary>
    public double InputHighPassHz { get; init; } = 20;

    /// <summary>Gain ahead of the first stage, swept by Drive (for circuits whose drive is not a feedback pot).</summary>
    public double MinGainDb { get; init; }

    public double MaxGainDb { get; init; }

    /// <summary>DC offset added ahead of the first stage, in volts: shifts the operating point of asymmetric clippers.</summary>
    public double Bias { get; init; }

    public IReadOnlyList<DriveStage> Stages { get; init; } = [];

    /// <summary>Level of the clean path summed after the stages at zero drive; it falls to a quarter at full drive.</summary>
    public double CleanMix { get; init; }

    public ToneControl Tone { get; init; } = new(ToneKind.HighCut, 1_000, 12_000);

    public double OutputDb { get; init; }

    /// <summary>True when the transfer curve is odd-symmetric, so the circuit makes (almost) only odd harmonics.</summary>
    public bool IsSymmetric => Bias == 0 && Stages.All(static s => s.IsSymmetric);
}

/// <summary>The circuit catalogue appended after the three original <see cref="DriveBlock" /> voicings.</summary>
public static class DriveCircuits {
    public const int MaxStages = 4;

    static readonly DiodeModel Si = DiodeModel.Silicon;
    static readonly DiodeModel Ge = DiodeModel.Germanium;

    public static IReadOnlyList<DriveCircuit> All { get; } = [
        new("TS808 OD", "Ibanez TS808: silicon pair in the op-amp feedback, 720 Hz bass cut, mid hump") {
            Stages = [ScreamerStage(DiodeStacks.Pair(Si))],
            Tone = new(ToneKind.HighCut, 700, 6_000),
            OutputDb = -4.5
        },
        new("TS9 OD", "Ibanez TS9: TS808 clipping with the brighter output stage") {
            Stages = [ScreamerStage(DiodeStacks.Pair(Si))],
            Tone = new(ToneKind.HighCut, 900, 9_000),
            OutputDb = -4
        },
        new("TS Mod OD", "Modded TS: 1 + 2 asymmetric diodes, more bass into the clipper, more gain") {
            Stages = [ScreamerStage(new(Si, 1, Si, 2), 100e-9, 1e6)],
            Tone = new(ToneKind.HighCut, 900, 9_000),
            OutputDb = -8.5
        },
        new("Super OD", "Boss SD-1: asymmetric 1 + 2 silicon diodes in the feedback") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 33e3,
                    FeedbackPot = 1e6,
                    FeedbackCapacitance = 47e-12,
                    Diodes = new(Si, 1, Si, 2)
                }
            ],
            Tone = new(ToneKind.HighCut, 800, 7_000),
            OutputDb = -7.5
        },
        new("Zen OD", "Hermida Zendrive: TS-type feedback clipping with a lower bass corner and doubled diodes") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 3.3e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 22e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 100e-12,
                    Diodes = DiodeStacks.Pair(Si, 2)
                }
            ],
            Tone = new(ToneKind.Tilt, 1_000, 1_000, 12),
            OutputDb = -11
        },
        new("Chiron OD", "Klon Centaur: germanium clipping to ground, summed with a clean path that fades as gain rises") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 2e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 1e3,
                    FeedbackPot = 100e3,
                    Rail = 9 // charge-pumped 18 V supply
                },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Ge) }
            ],
            CleanMix = 1,
            Tone = new(ToneKind.Tilt, 1_500, 1_500, 18),
            OutputDb = -2.5
        },
        new("Transparent OD", "Paul Cochrane Timmy: low-gain feedback clipping, bass cut ahead, treble cut after") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 10e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 10e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 47e-12,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            InputHighPassHz = 80,
            Tone = new(ToneKind.HighCut, 1_500, 14_000),
            OutputDb = -4.5
        },
        new("Blues OD", "Marshall Bluesbreaker: full-range feedback clipping at low gain") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 6.8e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 10e3,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 1e-9,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            Tone = new(ToneKind.Tilt, 800, 800, 12),
            OutputDb = -5
        },
        new("Governor Dist", "Marshall Guv'nor: op-amp gain into silicon diodes to ground, tilt EQ") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 1e3,
                    GroundCapacitance = 10e-6,
                    Feedback = 1e3,
                    FeedbackPot = 220e3,
                    FeedbackCapacitance = 100e-12
                },
                new(StageKind.Clipper) { Series = 4.7e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.Tilt, 700, 700, 16),
            OutputDb = -9
        },
        new("Obsessive OD", "Fulltone OCD: hot gain stage into soft rails, then asymmetric germanium/silicon to ground") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 3.3e3,
                    GroundCapacitance = 220e-9,
                    Feedback = 2.2e3,
                    FeedbackPot = 470e3,
                    FeedbackCapacitance = 100e-12,
                    Rail = 3.5
                },
                new(StageKind.Clipper) { Series = 10e3, Shunt = 4.7e-9, Diodes = new(Ge, 1, Si, 1) }
            ],
            Tone = new(ToneKind.HighCut, 1_200, 10_000),
            OutputDb = -4
        },
        new("Rodent Dist", "ProCo RAT: slow LM308 gain stage, silicon pair to ground, RC filter") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 47,
                    GroundCapacitance = 2.2e-6,
                    GroundResistance2 = 560,
                    GroundCapacitance2 = 4.7e-6,
                    Feedback = 100,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 100e-12,
                    GainBandwidth = 1e6
                },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 4.7e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.HighCut, 475, 20_000),
            OutputDb = -10
        },
        new("Rodent LED", "Turbo RAT: the RAT gain stage into red LEDs") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 47,
                    GroundCapacitance = 2.2e-6,
                    GroundResistance2 = 560,
                    GroundCapacitance2 = 4.7e-6,
                    Feedback = 100,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 100e-12,
                    GainBandwidth = 1e6
                },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 3.3e-9, Diodes = DiodeStacks.Pair(DiodeModel.Led) }
            ],
            Tone = new(ToneKind.HighCut, 475, 20_000),
            OutputDb = -18
        },
        new("DS1 Distortion", "Boss DS-1: transistor boost, op-amp gain, silicon to ground, LP/HP tone blend") {
            MinGainDb = 20,
            MaxGainDb = 20,
            InputHighPassHz = 30,
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 100,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 250e-12
                },
                new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.Blend, 700, 1_600),
            OutputDb = -1
        },
        new("Plus Distortion", "MXR Distortion+: 741 gain stage into germanium to ground") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 1e3,
                    FeedbackPot = 1e6,
                    FeedbackCapacitance = 10e-12,
                    GainBandwidth = 1e6
                },
                new(StageKind.Clipper) { Series = 10e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(Ge) }
            ],
            Tone = new(ToneKind.HighCut, 2_000, 14_000),
            OutputDb = -1.5
        },
        new("Pi Fuzz", "Electro-Harmonix Big Muff Pi: sustain stage, two diode-feedback clipping stages, mid-scoop tone") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 10e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 1e3,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 100e-12
                },
                MuffStage(),
                MuffStage()
            ],
            Tone = new(ToneKind.Blend, 450, 1_100),
            OutputDb = 4
        },
        new("Face Fuzz", "Dallas Fuzz Face (germanium): biased, asymmetric saturation and cut-off") {
            MinGainDb = 14,
            MaxGainDb = 44,
            Bias = 0.1,
            Stages = [new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = new(Ge, 1, Ge, 3) }],
            Tone = new(ToneKind.HighCut, 1_500, 12_000),
            OutputDb = -9
        },
        new("Si Face Fuzz", "Fuzz Face with BC108 silicon transistors: more gain, harder edges") {
            MinGainDb = 20,
            MaxGainDb = 50,
            Bias = 0.15,
            Stages = [new(StageKind.Clipper) { Series = 2.2e3, Shunt = 4.7e-9, Diodes = new(Si, 1, Si, 2) }],
            Tone = new(ToneKind.HighCut, 1_500, 14_000),
            OutputDb = -13
        },
        new("Bender Fuzz", "Sola Sound Tone Bender MkII: three germanium stages, two of them clipping") {
            MinGainDb = 12,
            MaxGainDb = 36,
            Bias = 0.05,
            Stages = [
                new(StageKind.Clipper) { Series = 1e3, Shunt = 22e-9, Diodes = new(Ge, 1, Ge, 2) },
                new(StageKind.Gain) {
                    GroundResistance = 1e3,
                    GroundCapacitance = 10e-6,
                    Feedback = 10e3,
                    FeedbackCapacitance = 1e-9,
                    GainBandwidth = 10e6
                },
                new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = new(Ge, 1, Ge, 3) }
            ],
            Tone = new(ToneKind.HighCut, 1_500, 12_000),
            OutputDb = -8.5
        },
        new("Treble Booster", "Dallas Rangemaster: small input cap, one germanium transistor, mild asymmetric clipping") {
            InputHighPassHz = 800,
            MinGainDb = 6,
            MaxGainDb = 26,
            Bias = 0.05,
            Stages = [new(StageKind.Clipper) { Series = 4.7e3, Shunt = 2.2e-9, Diodes = new(Ge, 1, Ge, 4) }],
            Tone = new(ToneKind.HighCut, 3_000, 16_000),
            OutputDb = 1
        },
        new("FET Boost", "JFET clean boost: wide band, soft asymmetric compression when pushed") {
            MinGainDb = 0,
            MaxGainDb = 20,
            Bias = 0.3,
            Stages = [
                new(StageKind.Clipper) {
                    Series = 100e3,
                    Shunt = 47e-12,
                    Diodes = new(DiodeModel.Mosfet, 1, DiodeModel.Mosfet, 3)
                }
            ],
            Tone = new(ToneKind.Tilt, 1_000, 1_000, 12),
            OutputDb = -2
        },
        new("Octave Fuzz", "Tycobrahe Octavia: diode-feedback fuzz stage into a soft full-wave rectifier (octave up)") {
            MaxGainDb = 12,
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 10e3,
                    FeedbackPot = 500e3,
                    Diodes = DiodeStacks.Pair(Si)
                },
                new(StageKind.Rectifier) { Knee = 0.3, Blend = 0.85 }
            ],
            Tone = new(ToneKind.HighCut, 1_500, 12_000),
            OutputDb = 2
        },
        new("Shred Distortion", "Modern high-gain pedal: tight pre-cut, two op-amp stages with diodes in feedback, tilt EQ") {
            InputHighPassHz = 150,
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 1e3,
                    FeedbackPot = 1e6,
                    FeedbackCapacitance = 47e-12,
                    GainBandwidth = 10e6,
                    Diodes = DiodeStacks.Pair(Si)
                },
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 100e3,
                    FeedbackCapacitance = 100e-12,
                    GainBandwidth = 10e6,
                    Diodes = DiodeStacks.Pair(Si, 2)
                }
            ],
            Tone = new(ToneKind.Tilt, 800, 800, 16),
            OutputDb = -14
        },
        new("Esoteric Boost", "Xotic RC Booster: clean op-amp boost that only softens on the rails") {
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 4.7e-6,
                    Feedback = 2.2e3,
                    FeedbackPot = 47e3,
                    FeedbackCapacitance = 100e-12,
                    Rail = 4
                }
            ],
            Tone = new(ToneKind.Tilt, 1_000, 1_000, 12),
            OutputDb = -6
        }
    ];

    /// <summary>The TS clipping amp: 4.7k + 47n to ground (720 Hz), 51k + Drive in feedback with 51p and the diodes.</summary>
    static DriveStage ScreamerStage(DiodeStacks diodes, double groundCapacitance = 47e-9, double pot = 500e3) =>
        new(StageKind.Gain) {
            GroundResistance = 4.7e3,
            GroundCapacitance = groundCapacitance,
            Feedback = 51e3,
            FeedbackPot = pot,
            FeedbackCapacitance = 51e-12,
            Diodes = diodes
        };

    /// <summary>A Big Muff clipping stage: inverting transistor amp with 100k and 470p in feedback across 1N914s.</summary>
    static DriveStage MuffStage() =>
        new(StageKind.Gain) {
            GroundResistance = 10e3,
            GroundCapacitance = 100e-9,
            Feedback = 100e3,
            FeedbackCapacitance = 470e-12,
            GainBandwidth = 10e6,
            Inverting = true,
            Diodes = DiodeStacks.Pair(Si)
        };

    /// <summary>Audio-taper pot: about 12 % of the resistance at half rotation.</summary>
    public static double AudioTaper(double position) => (Math.Exp(4 * position) - 1) / (Math.Exp(4) - 1);
}

/// <summary>The wave digital filters of one <see cref="DriveStage" />, for one channel.</summary>
struct CircuitStage {
    const double Absent = 1e12; // an unused ground leg: 1 TΩ in series with 1 pF

    StageKind kind;
    bool inverting;
    double rail;
    double knee;
    double blend;
    bool open; // no diodes in the feedback: the stage is linear up to the rails
    bool antialias;
    double previous; // antiderivative antialiasing state of the rails or the rectifier
    double previousIntegral;

    // Gain: the input voltage across the two RC legs; their current is forced through the feedback network.
    WdfCircuit<Parallel<Series<Resistor, Capacitor>, Series<Resistor, Capacitor>>, IdealVoltageSource> legs;
    WdfCircuit<Parallel<ResistiveCurrentSource, Capacitor>, DiodePair> feedback;

    // Clipper: a resistive source across the shunt capacitor and the diodes.
    WdfCircuit<Parallel<ResistiveVoltageSource, Capacitor>, DiodePair> clipper;

    public CircuitStage() {
        legs = new(new(new(new(1e3), new(1e-9, 48_000)), new(new(Absent), new(1e-12, 48_000))), default);
        feedback = new(new(new(10e3), new(1e-9, 48_000)), default);
        clipper = new(new(new(1e3), new(1e-9, 48_000)), default);
    }

    /// <summary>Sets the component values for drive pot position <paramref name="drive" /> (0..1).</summary>
    public void Configure(DriveStage stage, double drive, double sampleRate, bool antialias) {
        kind = stage.Kind;
        inverting = stage.Inverting;
        rail = stage.Rail;
        knee = stage.Knee;
        blend = stage.Blend;
        this.antialias = antialias;
        previousIntegral = Integral(previous);
        var d = stage.Diodes;
        switch (kind) {
            case StageKind.Gain: {
                var second = stage.GroundResistance2 > 0;
                legs.Tree.A.A.Value = stage.GroundResistance;
                legs.Tree.A.B.Configure(stage.GroundCapacitance, sampleRate);
                legs.Tree.B.A.Value = second ? stage.GroundResistance2 : Absent;
                legs.Tree.B.B.Configure(second ? stage.GroundCapacitance2 : 1e-12, sampleRate);
                legs.Propagate();

                // A finite gain-bandwidth product limits the closed-loop bandwidth to GBW / gain: model it as the
                // smallest feedback capacitance that places the feedback pole there.
                var rf = stage.Feedback + stage.FeedbackPot * DriveCircuits.AudioTaper(drive);
                var ground = second ? Math.Min(stage.GroundResistance, stage.GroundResistance2) : stage.GroundResistance;
                var cf = Math.Max(stage.FeedbackCapacitance, (1 + rf / ground) / (2 * Math.PI * stage.GainBandwidth * rf));
                feedback.Tree.A.Value = rf;
                feedback.Tree.B.Configure(cf, sampleRate);
                feedback.Root.Configure(d.Forward, d.ForwardCount, d.Reverse, d.ReverseCount);
                feedback.Root.Antialiased = antialias;
                feedback.Propagate();
                open = feedback.Root.IsOpen;
                break;
            }

            case StageKind.Clipper:
                clipper.Tree.A.Value = stage.Series;
                clipper.Tree.B.Configure(stage.Shunt, sampleRate);
                clipper.Root.Configure(d.Forward, d.ForwardCount, d.Reverse, d.ReverseCount);
                clipper.Root.Antialiased = antialias;
                clipper.Propagate();
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        switch (kind) {
            case StageKind.Gain: {
                legs.Root.Volts = x;
                legs.Tick();
                feedback.Tree.A.Amps = legs.Tree.Current;
                var vf = open ? LinearFeedback() : feedback.Tick();
                var y = inverting ? -vf : x + vf;
                return rail > 0 ? Antialiased(y) : y;
            }

            case StageKind.Clipper:
                clipper.Tree.A.Volts = x;
                return clipper.Tick();

            default:
                return Antialiased(x);
        }
    }

    /// <summary>
    ///     The memoryless part of the stage (soft rails, or the rectifier) with first-order antiderivative
    ///     antialiasing, like <see cref="AdaaClipper" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Antialiased(double x) {
        if (!antialias) {
            return Function(x);
        }

        var integral = Integral(x);
        var delta = x - previous;
        var y = Math.Abs(delta) > 1e-6 ? (integral - previousIntegral) / delta : Function(0.5 * (x + previous));
        previous = x;
        previousIntegral = integral;
        return y;
    }

    /// <summary>Gain: soft rails x / sqrt(1 + x² / r²). Rectifier: x blended towards sqrt(x² + k²) - k.</summary>
    readonly double Function(double x) {
        if (kind != StageKind.Rectifier) {
            return x / Math.Sqrt(1 + x * x / (rail * rail));
        }

        return x + blend * (Math.Sqrt(x * x + knee * knee) - knee - x);
    }

    readonly double Integral(double x) {
        if (kind != StageKind.Rectifier) {
            return rail * Math.Sqrt(rail * rail + x * x);
        }

        var rectified = 0.5 * (x * Math.Sqrt(x * x + knee * knee) + knee * knee * Math.Asinh(x / knee)) - knee * x;
        return 0.5 * x * x + blend * (rectified - 0.5 * x * x);
    }

    /// <summary>An open root is b = a; skipping the diode solve saves its logarithms.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double LinearFeedback() {
        var a = feedback.Tree.Reflect();
        feedback.Tree.Incident(a);
        return a;
    }

    public void Reset() {
        legs.Reset();
        feedback.Reset();
        clipper.Reset();
        previous = 0;
        previousIntegral = Integral(0);
    }

    /// <summary>Clears capacitor charge that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        Flush(ref legs.Tree.A.B);
        Flush(ref legs.Tree.B.B);
        Flush(ref feedback.Tree.B);
        Flush(ref clipper.Tree.B);
    }

    static void Flush(ref Capacitor capacitor) {
        if (Math.Abs(capacitor.Reflected) < 1e-30) {
            capacitor.Reset();
        }
    }
}

/// <summary>Per-channel state of the circuit voicings: everything except the oversampler, which the block shares.</summary>
sealed class CircuitChannel {
    readonly CircuitStage[] stages = new CircuitStage[DriveCircuits.MaxStages];
    RcLowPass input = new();
    RcLowPass toneLow = new();
    RcLowPass toneHigh = new();
    SvfState dc;
    DriveCircuit? circuit;
    int count;
    double bias;
    double clean;
    double toneLowGain;
    double toneHighGain;
    double trim;

    public CircuitChannel() {
        for (var i = 0; i < stages.Length; i++) {
            stages[i] = new();
        }
    }

    /// <summary>Forgets the configured circuit, so the next <see cref="Configure" /> starts discharged.</summary>
    public void Release() => circuit = null;

    /// <summary>Per block: component values for <paramref name="drive" /> and <paramref name="tone" /> (both 0..10).</summary>
    public void Configure(DriveCircuit circuit, double drive, double tone, double innerRate, double rate, bool antialias) {
        if (!ReferenceEquals(circuit, this.circuit)) {
            this.circuit = circuit; // a new circuit starts discharged
            Reset();
        }

        var position = drive / 10;
        count = circuit.Stages.Count;
        bias = circuit.Bias;
        for (var i = 0; i < count; i++) {
            stages[i].Configure(circuit.Stages[i], position, innerRate, antialias);
        }

        input.SetCutoff(circuit.InputHighPassHz, innerRate);
        clean = circuit.CleanMix * (1 - 0.75 * position);
        trim = Decibels.ToGain(circuit.OutputDb);

        var t = circuit.Tone;
        var amount = tone / 10;
        switch (t.Kind) {
            case ToneKind.HighCut:
                toneLow.SetCutoff(t.LowHz * Math.Pow(t.HighHz / t.LowHz, amount), rate);
                break;
            case ToneKind.Blend:
                toneLow.SetCutoff(t.LowHz, rate);
                toneHigh.SetCutoff(t.HighHz, rate);
                toneLowGain = 1 - amount;
                toneHighGain = amount;
                break;
            default: {
                var db = (amount - 0.5) * t.RangeDb;
                toneLow.SetCutoff(t.LowHz, rate);
                toneLowGain = Math.Pow(10, -db / 40);
                toneHighGain = Math.Pow(10, db / 40);
                break;
            }
        }
    }

    /// <summary>
    ///     The oversampled part, for both channels in one loop: coupling, gain ramp, bias, stages, clean blend and DC
    ///     blocker.
    /// </summary>
    public static void ProcessInner(
        CircuitChannel left,
        CircuitChannel right,
        Span<float> leftSamples,
        Span<float> rightSamples,
        float fromGain,
        float toGain,
        in SvfCoefficients dcBlock
    ) {
        var step = (toGain - fromGain) / leftSamples.Length;
        for (var i = 0; i < leftSamples.Length; i++) {
            var gain = fromGain + step * (i + 1);
            leftSamples[i] = left.Tick(leftSamples[i], gain, dcBlock);
            rightSamples[i] = right.Tick(rightSamples[i], gain, dcBlock);
        }

        left.FlushDenormals();
        right.FlushDenormals();
    }

    /// <summary>The base-rate part: tone control and output trim.</summary>
    public void ProcessOuter(Span<float> samples) {
        var kind = circuit!.Tone.Kind;
        for (var i = 0; i < samples.Length; i++) {
            double x = samples[i];
            var y = kind switch {
                ToneKind.HighCut => toneLow.Tick(x),
                ToneKind.Blend => toneLowGain * toneLow.Tick(x) + toneHighGain * (x - toneHigh.Tick(x)),
                _ => Tilt(x)
            } * trim;
            samples[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y; // a decaying high-pass can cancel down to subnormals
        }

        toneLow.FlushDenormals();
        toneHigh.FlushDenormals();
    }

    public void Reset() {
        foreach (ref var stage in stages.AsSpan()) {
            stage.Reset();
        }

        input.Reset();
        toneLow.Reset();
        toneHigh.Reset();
        dc.Reset();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    float Tick(float sample, float gain, in SvfCoefficients dcBlock) {
        var x = sample - input.Tick(sample);
        var v = x * gain + bias;
        var stages = this.stages.AsSpan(0, count);
        for (var s = 0; s < stages.Length; s++) {
            v = stages[s].Tick(v);
        }

        return (float)dc.Tick(dcBlock, v + clean * x);
    }

    void FlushDenormals() {
        input.FlushDenormals();
        dc.FlushDenormals();
        foreach (ref var stage in stages.AsSpan(0, count)) {
            stage.FlushDenormals();
        }
    }

    double Tilt(double x) {
        var low = toneLow.Tick(x);
        return toneLowGain * low + toneHighGain * (x - low);
    }
}
