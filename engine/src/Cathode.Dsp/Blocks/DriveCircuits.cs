using System.Runtime.CompilerServices;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

/// <summary>What one stage of a <see cref="DriveCircuit" /> does.</summary>
public enum StageKind {
    /// <summary>
    ///     A simplified gain stage: the stage input drives two series RC legs to ground (or to a virtual ground when
    ///     inverting), and their current flows through a feedback network of a resistor, a capacitor and optional clipping
    ///     diodes. Non-inverting output is the input plus the feedback voltage. Used where the pedal's gain stage is a
    ///     transistor amplifier this catalogue does not model at component level (the Big Muff's).
    /// </summary>
    Gain,

    /// <summary>A series resistor into diodes and a capacitor to ground (hard clipping to ground).</summary>
    Clipper,

    /// <summary>A smooth full-wave rectifier (octave-up), blended with its input.</summary>
    Rectifier,

    /// <summary>An op-amp gain stage at component level; see <see cref="OpAmpStage" />.</summary>
    OpAmp,

    /// <summary>One transistor in common emitter (a treble booster); see <see cref="TransistorStage" />.</summary>
    Transistor,

    /// <summary>The Fuzz Face's direct-coupled transistor pair; see <see cref="FuzzPairStage" />.</summary>
    FuzzPair,

    /// <summary>
    ///     A tape machine: the record amplifier's treble pre-emphasis (<see cref="DriveStage.TapeType" />), the tape's
    ///     soft saturation <c>r tanh(x / r)</c> at <see cref="DriveStage.Rail" /> volts (antiderivative antialiased), and
    ///     playback de-emphasis, the exact inverse of the record curve.
    /// </summary>
    Tape,

    /// <summary>
    ///     A digital bit crusher: sample-and-hold at a reduced rate, then quantisation to a reduced word length, both swept
    ///     by Drive from transparent (16 bits, every sample) to <see cref="DriveStage.Bits" /> bits held at
    ///     <see cref="DriveStage.HoldRate" /> Hz.
    /// </summary>
    Crusher
}

/// <summary>The topology of a pedal's tone network; see <see cref="ToneNetwork" /> for the component roles.</summary>
public enum ToneKind {
    /// <summary>
    ///     Passive treble cut: <see cref="ToneNetwork.Resistor1" /> and the Tone pot in series from the source into
    ///     <see cref="ToneNetwork.Capacitor1" /> to ground (the RAT filter). Tone 10 turns the pot to zero.
    /// </summary>
    HighCut,

    /// <summary>
    ///     Low-pass and high-pass side by side with the Tone pot across their outputs, its wiper the output (Big Muff,
    ///     DS-1): <see cref="ToneNetwork.Resistor1" /> into <see cref="ToneNetwork.Capacitor1" /> to ground, and
    ///     <see cref="ToneNetwork.Capacitor2" /> into <see cref="ToneNetwork.Resistor2" /> to ground. Mid scoop at noon.
    /// </summary>
    Blend,

    /// <summary>
    ///     The Tube Screamer's active tone stage: <see cref="ToneNetwork.Resistor1" /> into the op-amp's non-inverting
    ///     input with <see cref="ToneNetwork.Capacitor1" /> to ground; the Tone pot from there to the inverting input, its
    ///     wiper through <see cref="ToneNetwork.Resistor2" /> and <see cref="ToneNetwork.Capacitor2" /> to ground;
    ///     <see cref="ToneNetwork.Resistor3" /> from the output back to the inverting input. The shunt cuts treble on the
    ///     input side of the pot and boosts it on the feedback side.
    /// </summary>
    Screamer,

    /// <summary>
    ///     A Baxandall treble control around an inverting op-amp: <see cref="ToneNetwork.Resistor1" /> in and
    ///     <see cref="ToneNetwork.Resistor2" /> in feedback (flat gain), <see cref="ToneNetwork.Capacitor1" /> from the
    ///     input and <see cref="ToneNetwork.Capacitor2" /> from the output to the Tone pot's ends, its wiper through
    ///     <see cref="ToneNetwork.Resistor3" /> to the inverting input. The output is inverted back.
    /// </summary>
    Treble,

    /// <summary>
    ///     The passive treble/bass/middle stack of a Marshall: <see cref="ToneNetwork.Capacitor1" /> from the source to the
    ///     treble pot (the Tone pot, its wiper the output), the slope resistor <see cref="ToneNetwork.Resistor1" /> to the
    ///     junction of <see cref="ToneNetwork.Capacitor2" /> (to the treble pot's bottom and the bass pot
    ///     <see cref="ToneNetwork.Resistor2" />) and <see cref="ToneNetwork.Capacitor3" /> (to the middle pot
    ///     <see cref="ToneNetwork.Resistor3" />). Bass and middle stay at noon.
    /// </summary>
    Stack,

    /// <summary>
    ///     No network: Tone sweeps the first stage's input capacitor from <see cref="ToneNetwork.Capacitor1" /> (Tone 0,
    ///     full range) to <see cref="ToneNetwork.Capacitor2" /> (Tone 10), logarithmically: a treble booster's range.
    /// </summary>
    Range
}

/// <summary>
///     A pedal's tone network, solved as a linear R-type wave digital filter (<see cref="ToneStage" />) at the base rate
///     with the Tone pot inside the adaptor. Resistances in ohms, capacitances in farads; what each component does depends
///     on <see cref="ToneKind" />.
/// </summary>
public sealed record ToneNetwork(ToneKind Kind) {
    /// <summary>Resistance of whatever drives the network (the previous stage's output).</summary>
    public double Source { get; init; } = 100;

    /// <summary>The Tone pot.</summary>
    public double Pot { get; init; } = 100e3;

    /// <summary>An audio-taper Tone pot (about 12 % of the resistance at noon) instead of a linear one.</summary>
    public bool AudioTaper { get; init; }

    public double Resistor1 { get; init; } = 1e3;

    public double Resistor2 { get; init; } = 1e3;

    public double Resistor3 { get; init; } = 1e3;

    public double Capacitor1 { get; init; } = 10e-9;

    public double Capacitor2 { get; init; } = 10e-9;

    public double Capacitor3 { get; init; } = 10e-9;

    /// <summary>What the network drives (the next stage's input).</summary>
    public double Load { get; init; } = 1e6;
}

/// <summary>Two antiparallel diode stacks; see <see cref="DiodePair" />.</summary>
public readonly record struct DiodeStacks(DiodeModel Forward, int ForwardCount, DiodeModel Reverse, int ReverseCount) {
    public bool IsSymmetric => ForwardCount == ReverseCount && (ForwardCount == 0 || Forward == Reverse);

    public static DiodeStacks Pair(DiodeModel model, int count = 1) => new(model, count, model, count);
}

/// <summary>Component values of one stage. Resistances in ohms, capacitances in farads.</summary>
public sealed record DriveStage(StageKind Kind) {
    /// <summary>Gain, OpAmp: first ground leg.</summary>
    public double GroundResistance { get; init; } = 4.7e3;

    public double GroundCapacitance { get; init; } = 47e-9;

    /// <summary>Gain, OpAmp: optional second ground leg (0 = none).</summary>
    public double GroundResistance2 { get; init; }

    public double GroundCapacitance2 { get; init; }

    /// <summary>
    ///     Gain, OpAmp: fixed feedback resistance. FuzzPair: the DC feedback from the second collector's divider to the
    ///     first base. Transistor: collector-to-base feedback (0 = none).
    /// </summary>
    public double Feedback { get; init; } = 10e3;

    /// <summary>Gain, OpAmp: the drive pot in series with <see cref="Feedback" /> (audio taper; 0 = fixed gain).</summary>
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

    /// <summary>Gain, OpAmp: diodes across the feedback. Clipper: diodes to ground.</summary>
    public DiodeStacks Diodes { get; init; }

    /// <summary>OpAmp: the op-amp.</summary>
    public OpAmpModel Amplifier { get; init; } = OpAmpModel.JRC4558;

    /// <summary>
    ///     OpAmp: the supply, whose half the rails sit at either side of the bias reference. Transistor, FuzzPair: the
    ///     supply (negative for PNP parts, as in a positive-ground pedal).
    /// </summary>
    public double Supply { get; init; } = 9;

    /// <summary>
    ///     OpAmp without feedback diodes: solve the full <see cref="OpAmp" /> device (slew rate and smooth rails) at a
    ///     Newton root instead of the linear op-amp with clamped rails, for the pedals whose op-amp's slew is their
    ///     character (see <see cref="OpAmpSolve" />).
    /// </summary>
    public bool SlewLimited { get; init; }

    /// <summary>OpAmp: resistance from the non-inverting input to the bias reference.</summary>
    public double InputBias { get; init; } = 1e6;

    /// <summary>OpAmp: a pot in series with the first ground leg, turned down as Drive rises (audio taper; 0 = none).</summary>
    public double GroundPot { get; init; }

    /// <summary>OpAmp, Transistor, FuzzPair: what the stage's output drives.</summary>
    public double Load { get; init; } = 10e3;

    /// <summary>
    ///     OpAmp, Transistor, FuzzPair: the resistance driving the stage when an earlier stage does (the first stage sees
    ///     the guitar instead, see <see cref="DriveCircuits.Guitar" />).
    /// </summary>
    public double SourceResistance { get; init; } = 1e3;

    /// <summary>Transistor, FuzzPair: the transistors.</summary>
    public BjtModel Transistor { get; init; } = BjtModel.AC128;

    /// <summary>Transistor, FuzzPair: the coupling capacitor from the input to the (first) base.</summary>
    public double InputCapacitance { get; init; } = 2.2e-6;

    /// <summary>Transistor, FuzzPair: the coupling capacitor from the (last) collector to the output.</summary>
    public double OutputCapacitance { get; init; } = 10e-9;

    /// <summary>Transistor: the collector load. FuzzPair: the first transistor's collector load.</summary>
    public double Collector { get; init; } = 33e3;

    /// <summary>FuzzPair: the second collector's resistor, then the one from there to the supply.</summary>
    public double SecondCollector { get; init; } = 470;

    public double CollectorSupply { get; init; } = 8.2e3;

    /// <summary>
    ///     Transistor: the emitter resistor. FuzzPair: the Fuzz pot from the second emitter to ground, whose wiper Drive
    ///     moves; <see cref="EmitterCapacitance" /> bypasses the wiper.
    /// </summary>
    public double Emitter { get; init; } = 1e3;

    public double EmitterCapacitance { get; init; } = 20e-6;

    /// <summary>Transistor: base bias divider (0 = none).</summary>
    public double BaseToSupply { get; init; }

    public double BaseToGround { get; init; }

    /// <summary>Tape: the record/playback equalisation, a tape type of <see cref="Cathode.Dsp.Cabinets.MicPreampTypes" />.</summary>
    public int TapeType { get; init; } = Cabinets.MicPreampTypes.Tape50;

    /// <summary>
    ///     Crusher: the word length at full Drive (16 bits at zero, swept geometrically), in bits; one volt is full scale.
    /// </summary>
    public double Bits { get; init; } = 4;

    /// <summary>Crusher: the sample-and-hold rate at full Drive, in hertz (every sample at zero).</summary>
    public double HoldRate { get; init; } = 2_000;

    /// <summary>True when the stage's transfer curve is odd-symmetric.</summary>
    public bool IsSymmetric =>
        Kind switch {
            StageKind.Rectifier or StageKind.Transistor or StageKind.FuzzPair => false,
            _ => Diodes.IsSymmetric
        };
}

/// <summary>
///     One drive circuit: input coupling, a gain set by Drive, an operating-point bias, up to
///     <see cref="DriveCircuits.MaxStages" /> stages at the oversampled rate, an optional clean blend, a DC blocker, then
///     the tone network and output trim at the base rate.
/// </summary>
public sealed record DriveCircuit(string Name, string ModelledAfter) {
    /// <summary>Input coupling capacitor corner (0 = none: the first stage's netlist has its own).</summary>
    public double InputHighPassHz { get; init; } = 20;

    /// <summary>Gain ahead of the first stage, swept by Drive (for circuits whose drive is not a feedback pot).</summary>
    public double MinGainDb { get; init; }

    public double MaxGainDb { get; init; }

    /// <summary>DC offset added ahead of the first stage, in volts: shifts the operating point of asymmetric clippers.</summary>
    public double Bias { get; init; }

    /// <summary>
    ///     Input resistance of the first stage when it is not modelled at component level, which loads the guitar (see
    ///     <see cref="DriveCircuits.Guitar" />).
    /// </summary>
    public double InputResistance { get; init; } = 1e6;

    public IReadOnlyList<DriveStage> Stages { get; init; } = [];

    /// <summary>Level of the clean path summed after the stages at zero drive; it falls to a quarter at full drive.</summary>
    public double CleanMix { get; init; }

    public ToneNetwork Tone { get; init; } = new(ToneKind.HighCut);

    public double OutputDb { get; init; }

    /// <summary>Inverts the output: the circuit has an odd number of inverting stages, and the pedal keeps polarity.</summary>
    public bool InvertOutput { get; init; }

    /// <summary>
    ///     Oversampling factor of the stages (2 or 4): the circuits that need a Newton solve run at 2x, the ones solved in
    ///     closed form at 4x.
    /// </summary>
    public int Oversampling { get; init; } = 4;

    /// <summary>True when the transfer curve is odd-symmetric, so the circuit makes (almost) only odd harmonics.</summary>
    public bool IsSymmetric => Bias == 0 && Stages.All(static s => s.IsSymmetric);
}

/// <summary>The guitar as a source for its volume knob; see <see cref="DriveCircuits.Guitar" />.</summary>
/// <param name="Series">The pickup's resistance plus the top of the volume pot, in series with the pickup's inductance.</param>
/// <param name="Shunt">The bottom of the volume pot, across the output.</param>
/// <param name="Resistance">The resistive Thévenin equivalent of the two, ignoring the inductance.</param>
/// <param name="Level">The open-circuit output level relative to full volume.</param>
/// <param name="Emf">
///     The pickup voltage per unit of the full-volume, open-circuit output (the signal a high-impedance DI records).
/// </param>
public readonly record struct GuitarSource(double Series, double Shunt, double Resistance, double Level, double Emf);

/// <summary>The circuit catalogue appended after the three original <see cref="DriveBlock" /> voicings.</summary>
public static partial class DriveCircuits {
    public const int MaxStages = 4;

    /// <summary>The guitar's pickup resistance and volume pot, which form the source the first stage sees.</summary>
    public const double PickupResistance = 8e3;

    public const double VolumePot = 250e3;

    /// <summary>A single-coil pickup's inductance, in henries.</summary>
    public const double PickupInductance = 2.5;

    static readonly DiodeModel Si = DiodeModel.Silicon;
    static readonly DiodeModel Ge = DiodeModel.Germanium;
    static readonly Dictionary<DriveStage, double[]> OperatingPoints = new(ReferenceEqualityComparer.Instance);

    static readonly DiodeModel[] DiodeModels = [
        DiodeModel.Silicon, DiodeModel.Germanium, DiodeModel.Schottky, DiodeModel.Led, DiodeModel.Mosfet
    ];

    /// <summary>
    ///     Every circuit with every diode override, built once when the catalogue loads so that choosing one on the audio
    ///     thread only looks it up: <c>[circuit][type * DiodeCounts.Count + count]</c>. A choice that changes nothing (a
    ///     circuit without clipping diodes, or both Stock) is the circuit itself.
    /// </summary>
    static readonly DriveCircuit[][] DiodeVariants;

    public static IReadOnlyList<DriveCircuit> All { get; } = Catalogue();

    /// <summary>The Drive block's Diode Type choices: the circuit's own, then a model for every clipping diode.</summary>
    public static IReadOnlyList<string> DiodeTypes { get; } =
        ["Stock", "Silicon", "Germanium", "Schottky", "LED", "MOSFET"];

    /// <summary>The Drive block's Diode Count choices: the circuit's own, then 1 to 4 in series on each side.</summary>
    public static IReadOnlyList<string> DiodeCounts { get; } = ["Stock", "1", "2", "3", "4"];

    // In the static constructor, which runs after every initializer above whatever order they are in: the variants read
    // the catalogue and the diode lists.
    static DriveCircuits() {
        DiodeVariants = [..All.Select(BuildVariants)];
    }

    /// <summary>The TS808 tone stage: 1k into 220 nF, 20k Tone pot, 220 Ω + 220 nF from its wiper, 1k feedback.</summary>
    static ToneNetwork ScreamerTone =>
        new(ToneKind.Screamer) {
            Source = 75,
            Resistor1 = 1e3,
            Capacitor1 = 220e-9,
            Pot = 20e3,
            Resistor2 = 220,
            Capacitor2 = 220e-9,
            Resistor3 = 1e3,
            Load = 10e3
        };

    /// <summary>A Baxandall treble control (Klon-style): 10k in and out, 10 nF to a 10k pot, 1.8k from its wiper.</summary>
    static ToneNetwork TrebleTone =>
        new(ToneKind.Treble) {
            Source = 100,
            Resistor1 = 10e3,
            Resistor2 = 10e3,
            Capacitor1 = 10e-9,
            Capacitor2 = 10e-9,
            Pot = 10e3,
            Resistor3 = 1.8e3,
            Load = 100e3
        };

    /// <summary>The RAT's filter: 1.5k and the 100k Filter pot into 3.3 nF (Tone 10 is the Filter knob fully down).</summary>
    static ToneNetwork RatFilter =>
        new(ToneKind.HighCut) {
            Source = 1e3,
            Resistor1 = 1.5e3,
            Pot = 100e3,
            AudioTaper = true,
            Capacitor1 = 3.3e-9
        };

    /// <summary>A passive treble cut after a fuzz with no tone control of its own (the common tone mod).</summary>
    static ToneNetwork FuzzTone =>
        new(ToneKind.HighCut) {
            Source = 1e3,
            Resistor1 = 1e3,
            Pot = 100e3,
            AudioTaper = true,
            Capacitor1 = 1e-9
        };

    /// <summary>True when <paramref name="circuit" /> has clipping diodes the Diode Type and Count can replace.</summary>
    public static bool HasDiodes(DriveCircuit circuit) => circuit.Stages.Any(HasDiodes);

    /// <summary>
    ///     Circuit <paramref name="index" /> of <see cref="All" /> with its clipping diodes replaced: Diode Type
    ///     <paramref name="type" /> and Count <paramref name="count" /> (0 = Stock for either). Allocation-free.
    /// </summary>
    public static DriveCircuit Variant(int index, int type, int count) {
        var variants = DiodeVariants[index];
        var at = Math.Clamp(type, 0, DiodeTypes.Count - 1) * DiodeCounts.Count
            + Math.Clamp(count, 0, DiodeCounts.Count - 1);
        return variants[at];
    }

    /// <summary>The DC operating point of a transistor stage of the catalogue, computed once when it loads.</summary>
    public static bool TryGetOperatingPoint(DriveStage stage, out double[] point) =>
        OperatingPoints.TryGetValue(stage, out point!);

    /// <summary>
    ///     The guitar as a source, for its volume knob at <paramref name="volume" /> (0..10): the pickup (its resistance and
    ///     inductance) in series with the top of an audio-taper volume pot, the bottom of the pot across the output. Rolling
    ///     the volume back raises the source resistance, so a low-impedance input (a Fuzz Face) loses gain much faster than
    ///     the level alone would say, and cleans up; the inductance makes such an input darker, as on the real pedal.
    /// </summary>
    public static GuitarSource Guitar(double volume) {
        var bottom = VolumePot * AudioTaper(Math.Clamp(volume / 10, 0, 1));
        var series = PickupResistance + VolumePot - bottom;
        var full = VolumePot / (PickupResistance + VolumePot);
        return new(
            series,
            Math.Max(bottom, 1e-3),
            Math.Max(series * bottom / (series + bottom), 1),
            bottom / (series + bottom) / full,
            1 / full
        );
    }

    /// <summary>Audio-taper pot: about 12 % of the resistance at half rotation.</summary>
    public static double AudioTaper(double position) => (Math.Exp(4 * position) - 1) / (Math.Exp(4) - 1);

    static bool HasDiodes(DriveStage stage) =>
        stage.Kind is StageKind.Gain or StageKind.OpAmp or StageKind.Clipper
        && stage.Diodes.ForwardCount + stage.Diodes.ReverseCount > 0;

    static DriveCircuit[] BuildVariants(DriveCircuit circuit) {
        var variants = new DriveCircuit[DiodeTypes.Count * DiodeCounts.Count];
        for (var type = 0; type < DiodeTypes.Count; type++) {
            for (var count = 0; count < DiodeCounts.Count; count++) {
                var at = type * DiodeCounts.Count + count;
                if ((type == 0 && count == 0) || !HasDiodes(circuit)) {
                    variants[at] = circuit;
                    continue;
                }

                variants[at] = circuit with {
                    Stages = [
                        ..circuit.Stages.Select(s => HasDiodes(s)
                                ? s with { Diodes = Override(s.Diodes, type, count) }
                                : s
                        )
                    ]
                };
            }
        }

        return variants;
    }

    static DiodeStacks Override(DiodeStacks stacks, int type, int count) {
        var forward = type > 0 ? DiodeModels[type - 1] : stacks.Forward;
        var reverse = type > 0 ? DiodeModels[type - 1] : stacks.Reverse;
        var forwardCount = count > 0 && stacks.ForwardCount > 0 ? count : stacks.ForwardCount;
        var reverseCount = count > 0 && stacks.ReverseCount > 0 ? count : stacks.ReverseCount;
        return new(forward, forwardCount, reverse, reverseCount);
    }

    static DriveCircuit[] Catalogue() {
        DriveCircuit[] all = [
            new(
                "TS808 OD",
                "Ibanez TS808: JRC4558 with silicon diodes across its feedback, 720 Hz bass cut, active tone stage"
            ) { Stages = [ScreamerStage(DiodeStacks.Pair(Si))], Tone = ScreamerTone, OutputDb = -2.8 },
            new("TS9 OD", "Ibanez TS9: the TS808 circuit with the TS9's output section") {
                Stages = [ScreamerStage(DiodeStacks.Pair(Si))],
                Tone = ScreamerTone with { Load = 100e3 },
                OutputDb = -2.8
            },
            new("TS Mod OD", "Modded TS: 1 + 2 asymmetric diodes, more bass into the clipper, more gain") {
                Stages = [ScreamerStage(new(Si, 1, Si, 2), 100e-9, 1e6)], Tone = ScreamerTone, OutputDb = -6.6
            },
            new(
                "Super OD",
                "Boss SD-1: asymmetric 1 + 2 silicon diodes across the op-amp's feedback, TS-type tone stage"
            ) {
                Stages = [
                    new(StageKind.OpAmp) {
                        GroundResistance = 4.7e3,
                        GroundCapacitance = 47e-9,
                        Feedback = 33e3,
                        FeedbackPot = 1e6,
                        FeedbackCapacitance = 47e-12,
                        Diodes = new(Si, 1, Si, 2)
                    }
                ],
                Tone = ScreamerTone with { Capacitor1 = 100e-9, Capacitor2 = 100e-9 },
                OutputDb = -7.3
            },
            new("Zen OD", "Hermida Zendrive: TL072 with doubled silicon diodes across its feedback, Baxandall treble") {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        GroundResistance = 3.3e3,
                        GroundCapacitance = 100e-9,
                        Feedback = 22e3,
                        FeedbackPot = 500e3,
                        FeedbackCapacitance = 100e-12,
                        Diodes = DiodeStacks.Pair(Si, 2)
                    }
                ],
                Tone = TrebleTone with { Capacitor1 = 4.7e-9, Capacitor2 = 4.7e-9, Pot = 50e3, Resistor3 = 3.3e3 },
                OutputDb = -10.9
            },
            new(
                "Chiron OD",
                "Klon Centaur: TL072 gain stage on 18 V, germanium diodes to ground, clean blend, active treble"
            ) {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        Supply = 18, // charge pump
                        GroundResistance = 2e3,
                        GroundCapacitance = 100e-9,
                        Feedback = 1e3,
                        FeedbackPot = 100e3,
                        FeedbackCapacitance = 100e-12
                    },
                    new(StageKind.Clipper) { Series = 1e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Ge) }
                ],
                CleanMix = 1,
                Tone = TrebleTone,
                OutputDb = -2.4
            },
            new(
                "Transparent OD",
                "Paul Cochrane Timmy: TL072 with diodes across its feedback, bass cut ahead, passive treble cut"
            ) {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        GroundResistance = 10e3,
                        GroundCapacitance = 47e-9,
                        Feedback = 10e3,
                        FeedbackPot = 500e3,
                        FeedbackCapacitance = 47e-12,
                        Diodes = DiodeStacks.Pair(Si)
                    }
                ],
                InputHighPassHz = 80,
                Tone = new(ToneKind.HighCut) {
                    Source = 1e3,
                    Resistor1 = 100,
                    Pot = 22e3,
                    AudioTaper = true,
                    Capacitor1 = 4.7e-9
                },
                OutputDb = -4.7
            },
            new(
                "Blues OD",
                "Marshall Bluesbreaker: full-range op-amp stage with diodes across its feedback, passive treble cut"
            ) {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        GroundResistance = 6.8e3,
                        GroundCapacitance = 1e-6,
                        Feedback = 10e3,
                        FeedbackPot = 100e3,
                        FeedbackCapacitance = 1e-9,
                        Diodes = DiodeStacks.Pair(Si)
                    }
                ],
                Tone = new(ToneKind.HighCut) {
                    Source = 1e3,
                    Resistor1 = 1e3,
                    Pot = 100e3,
                    AudioTaper = true,
                    Capacitor1 = 2.2e-9
                },
                OutputDb = -4.8
            },
            new("Governor Dist", "Marshall Guv'nor: op-amp gain into silicon diodes to ground, Marshall tone stack") {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        GroundResistance = 1e3,
                        GroundCapacitance = 10e-6,
                        Feedback = 1e3,
                        FeedbackPot = 220e3,
                        FeedbackCapacitance = 100e-12
                    },
                    new(StageKind.Clipper) { Series = 4.7e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
                ],
                Tone = new(ToneKind.Stack) {
                    Source = 4.7e3,
                    Resistor1 = 33e3,
                    Pot = 220e3,
                    Resistor2 = 1e6,
                    Resistor3 = 22e3,
                    Capacitor1 = 470e-12,
                    Capacitor2 = 22e-9,
                    Capacitor3 = 22e-9
                },
                OutputDb = 1.5
            },
            new(
                "Obsessive OD",
                "Fulltone OCD: hot op-amp stage into its rails, then asymmetric germanium/silicon to ground"
            ) {
                Stages = [
                    new(StageKind.OpAmp) {
                        GroundResistance = 3.3e3,
                        GroundCapacitance = 220e-9,
                        Feedback = 2.2e3,
                        FeedbackPot = 470e3,
                        FeedbackCapacitance = 100e-12
                    },
                    new(StageKind.Clipper) { Series = 10e3, Shunt = 4.7e-9, Diodes = new(Ge, 1, Si, 1) }
                ],
                Tone = new(ToneKind.HighCut) {
                    Source = 1e3,
                    Resistor1 = 1e3,
                    Pot = 47e3,
                    AudioTaper = true,
                    Capacitor1 = 4.7e-9
                },
                OutputDb = -4.3
            },
            new("Rodent Dist", "ProCo RAT: slew-limited LM308 gain stage, silicon pair to ground, RC filter") {
                Oversampling = 2,
                Stages = [
                    RatStage(), new(StageKind.Clipper) { Series = 1e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(Si) }
                ],
                Tone = RatFilter,
                OutputDb = -8.4
            },
            new("Rodent LED", "Turbo RAT: the RAT gain stage into red LEDs") {
                Oversampling = 2,
                Stages = [
                    RatStage(),
                    new(StageKind.Clipper) { Series = 1e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(DiodeModel.Led) }
                ],
                Tone = RatFilter,
                OutputDb = -17.1
            },
            new("DS1 Distortion", "Boss DS-1: transistor boost, op-amp gain, silicon to ground, LP/HP tone blend") {
                MinGainDb = 20,
                MaxGainDb = 20,
                InputHighPassHz = 30,
                Stages = [
                    new(StageKind.OpAmp) {
                        GroundResistance = 4.7e3,
                        GroundCapacitance = 1e-6,
                        Feedback = 100,
                        FeedbackPot = 100e3,
                        FeedbackCapacitance = 250e-12
                    },
                    new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
                ],
                Tone = new(ToneKind.Blend) {
                    Source = 2.2e3,
                    Resistor1 = 6.8e3,
                    Capacitor1 = 100e-9,
                    Capacitor2 = 22e-9,
                    Resistor2 = 6.8e3,
                    Pot = 20e3,
                    Load = 100e3
                },
                OutputDb = 7.5
            },
            new("Plus Distortion", "MXR Distortion+: LM741 with the gain pot in its ground leg, germanium to ground") {
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.LM741,
                        GroundResistance = 4.7e3,
                        GroundCapacitance = 47e-9,
                        GroundPot = 1e6,
                        Feedback = 1e6,
                        FeedbackCapacitance = 0
                    },
                    new(StageKind.Clipper) { Series = 10e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(Ge) }
                ],
                Tone = new(ToneKind.HighCut) {
                    Source = 10e3,
                    Resistor1 = 1e3,
                    Pot = 100e3,
                    AudioTaper = true,
                    Capacitor1 = 1e-9
                },
                OutputDb = 0
            },
            new(
                "Pi Fuzz",
                "Electro-Harmonix Big Muff Pi: sustain stage, two diode-feedback clipping stages, mid-scoop tone stack"
            ) {
                InputResistance = 100e3,
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
                Tone = new(ToneKind.Blend) {
                    Source = 15e3,
                    Resistor1 = 39e3,
                    Capacitor1 = 10e-9,
                    Capacitor2 = 4e-9,
                    Resistor2 = 22e3,
                    Pot = 100e3,
                    Load = 100e3
                },
                OutputDb = 10
            },
            new("Face Fuzz", "Dallas Fuzz Face: two AC128 germanium PNPs with DC feedback, Fuzz pot in the emitter") {
                Stages = [FaceStage(BjtModel.AC128)],
                InputHighPassHz = 0,
                Oversampling = 2,
                Tone = FuzzTone,
                OutputDb = -25
            },
            new("Si Face Fuzz", "Fuzz Face with BC108 silicon NPNs: more gain, harder edges") {
                Stages = [FaceStage(BjtModel.BC108)],
                InputHighPassHz = 0,
                Oversampling = 2,
                Tone = FuzzTone,
                OutputDb = -24.3
            },
            new("Bender Fuzz", "Sola Sound Tone Bender MkII: an AC128 gain stage into a Fuzz Face-style AC128 pair") {
                Stages = [
                    new(StageKind.Transistor) {
                        Transistor = BjtModel.AC128,
                        InputCapacitance = 4.7e-6,
                        Feedback = 100e3,
                        Collector = 4.7e3,
                        Emitter = 1,
                        EmitterCapacitance = 1e-6,
                        OutputCapacitance = 10e-6,
                        Load = 1e6
                    },
                    FaceStage(BjtModel.AC128) with { InputCapacitance = 100e-9, SourceResistance = 4.5e3 }
                ],
                InputHighPassHz = 0,
                InvertOutput = true,
                Oversampling = 2,
                Tone = FuzzTone,
                OutputDb = -24
            },
            new(
                "Treble Booster",
                "Dallas Rangemaster: one germanium PNP, 5 nF input; Tone sweeps the input capacitor"
            ) {
                Oversampling = 2,
                MinGainDb = 0,
                MaxGainDb = 20,
                InputHighPassHz = 0,
                Stages = [
                    new(StageKind.Transistor) {
                        Transistor = BjtModel.NKT275,
                        InputCapacitance = 5e-9,
                        BaseToSupply = 470e3,
                        BaseToGround = 68e3,
                        Feedback = 0,
                        Emitter = 3.9e3,
                        EmitterCapacitance = 47e-6,
                        Collector = 10e3,
                        OutputCapacitance = 10e-9,
                        Load = 1e6
                    }
                ],
                InvertOutput = true,
                Tone = new(ToneKind.Range) { Capacitor1 = 100e-9, Capacitor2 = 5e-9 },
                OutputDb = -24
            },
            new(
                "FET Boost",
                "JFET clean boost: wide band, soft asymmetric compression when pushed, passive treble cut"
            ) {
                MinGainDb = 0,
                MaxGainDb = 20,
                Bias = 0.3,
                Stages = [
                    new(StageKind.Clipper) {
                        Series = 100e3, Shunt = 47e-12, Diodes = new(DiodeModel.Mosfet, 1, DiodeModel.Mosfet, 3)
                    }
                ],
                Tone = new(ToneKind.HighCut) {
                    Source = 1e3,
                    Resistor1 = 100,
                    Pot = 50e3,
                    AudioTaper = true,
                    Capacitor1 = 2.2e-9
                },
                OutputDb = -9
            },
            new(
                "Octave Fuzz",
                "Tycobrahe Octavia: diode-feedback fuzz stage into a soft full-wave rectifier (octave up)"
            ) {
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
                Tone = FuzzTone,
                OutputDb = 2
            },
            new(
                "Shred Distortion",
                "Modern high-gain pedal: tight pre-cut, two diode-feedback gain stages, active treble"
            ) {
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
                Tone = TrebleTone,
                OutputDb = -14
            },
            new("Esoteric Boost", "Xotic RC Booster: clean TL072 boost that only softens on its rails, active treble") {
                MaxGainDb = 12,
                Stages = [
                    new(StageKind.OpAmp) {
                        Amplifier = OpAmpModel.TL072,
                        GroundResistance = 2.2e3,
                        GroundCapacitance = 4.7e-6,
                        Feedback = 2.2e3,
                        FeedbackPot = 100e3,
                        FeedbackCapacitance = 100e-12
                    }
                ],
                Tone = TrebleTone,
                OutputDb = -12
            },
            new("NKT Face Fuzz", "Fuzz Face with NKT275 germanium PNPs: the early, darker and leakier germanium Face") {
                Stages = [FaceStage(BjtModel.NKT275)],
                InputHighPassHz = 0,
                Oversampling = 2,
                Tone = FuzzTone,
                OutputDb = -25
            },
            ..Round4Fuzz()
        ];

        foreach (var circuit in all) {
            foreach (var stage in circuit.Stages) {
                if (stage.Kind == StageKind.Transistor) {
                    OperatingPoints[stage] = TransistorStage.OperatingPoint(stage);
                } else if (stage.Kind == StageKind.FuzzPair) {
                    OperatingPoints[stage] = FuzzPairStage.OperatingPoint(stage);
                }
            }
        }

        return all;
    }

    /// <summary>
    ///     The TS clipping amp: JRC4558 on 9 V, 510k input bias, 4.7k + 47n to ground (720 Hz), 51k + Drive in feedback
    ///     with 51p and the diodes.
    /// </summary>
    static DriveStage ScreamerStage(DiodeStacks diodes, double groundCapacitance = 47e-9, double pot = 500e3) =>
        new(StageKind.OpAmp) {
            InputBias = 510e3,
            GroundResistance = 4.7e3,
            GroundCapacitance = groundCapacitance,
            Feedback = 51e3,
            FeedbackPot = pot,
            FeedbackCapacitance = 51e-12,
            Diodes = diodes
        };

    /// <summary>The RAT gain stage: LM308, Distortion pot in feedback with 100p, 47 Ω + 2.2 µF and 560 Ω + 4.7 µF legs.</summary>
    static DriveStage RatStage() =>
        new(StageKind.OpAmp) {
            Amplifier = OpAmpModel.LM308,
            SlewLimited = true,
            GroundResistance = 47,
            GroundCapacitance = 2.2e-6,
            GroundResistance2 = 560,
            GroundCapacitance2 = 4.7e-6,
            Feedback = 0,
            FeedbackPot = 100e3,
            FeedbackCapacitance = 100e-12,
            Load = 1e3
        };

    /// <summary>
    ///     The Fuzz Face: 2.2 µF in, 33k first collector, 470 Ω + 8.2k second collector, 100k feedback, 1k Fuzz pot with
    ///     20 µF from its wiper, 10 nF out into the 500k volume pot.
    /// </summary>
    static DriveStage FaceStage(BjtModel transistor) =>
        new(StageKind.FuzzPair) {
            Transistor = transistor,
            InputCapacitance = 2.2e-6,
            Collector = 33e3,
            SecondCollector = 470,
            CollectorSupply = 8.2e3,
            Feedback = 100e3,
            Emitter = 1e3,
            EmitterCapacitance = 20e-6,
            OutputCapacitance = 10e-9,
            Load = 500e3
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

    // Tape: the record emphasis and playback de-emphasis, first-order sections (transposed direct form II).
    double emphasisB0, emphasisB1, emphasisA1;
    double emphasisState, deemphasisState;

    // Crusher: quantisation levels per volt (0 = none), hold step per sample, its phase and the held sample.
    double levels;
    double holdStep;
    double holdPhase;
    double held;

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
                var ground =
                    second ? Math.Min(stage.GroundResistance, stage.GroundResistance2) : stage.GroundResistance;
                var cf = Math.Max(
                    stage.FeedbackCapacitance,
                    (1 + rf / ground) / (2 * Math.PI * stage.GainBandwidth * rf)
                );
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

            case StageKind.Tape: {
                // The analog shelf (b1 s + b0) / (a1 s + a0) through the bilinear transform; de-emphasis is its inverse.
                Cabinets.MicPreampTypes.Emphasis(stage.TapeType, out var b1, out var b0, out var a1, out var a0);
                var k = 2 * sampleRate;
                var norm = a1 * k + a0;
                emphasisB0 = (b1 * k + b0) / norm;
                emphasisB1 = (b0 - b1 * k) / norm;
                emphasisA1 = (a0 - a1 * k) / norm;
                break;
            }

            case StageKind.Crusher: {
                // Geometric sweeps, so that the middle of the knob is already audibly crushed.
                var bits = 16 * Math.Pow(stage.Bits / 16, drive);
                levels = Math.Pow(2, bits - 1);
                holdStep = Math.Pow(Math.Min(stage.HoldRate / sampleRate, 1), drive);
                break;
            }
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

            case StageKind.Tape: {
                var recorded = emphasisB0 * x + emphasisState;
                emphasisState = emphasisB1 * x - emphasisA1 * recorded;
                var saturated = Antialiased(recorded);
                var played = (saturated + deemphasisState) / emphasisB0;
                deemphasisState = emphasisA1 * saturated - emphasisB1 * played;
                return played;
            }

            case StageKind.Crusher:
                holdPhase += holdStep;
                if (holdPhase >= 1) {
                    holdPhase -= Math.Floor(holdPhase);
                    held = Math.Round(x * levels, MidpointRounding.AwayFromZero) / levels;
                }

                return held;

            default:
                return Antialiased(x);
        }
    }

    public void Reset() {
        legs.Reset();
        feedback.Reset();
        clipper.Reset();
        previous = 0;
        previousIntegral = Integral(0);
        emphasisState = 0;
        deemphasisState = 0;
        holdPhase = 1; // the first sample is taken at once
        held = 0;
    }

    /// <summary>Clears capacitor charge that has decayed into the subnormal range.</summary>
    public void FlushDenormals() {
        Flush(ref legs.Tree.A.B);
        Flush(ref legs.Tree.B.B);
        Flush(ref feedback.Tree.B);
        Flush(ref clipper.Tree.B);
        if (Math.Abs(emphasisState) < 1e-30) {
            emphasisState = 0;
        }

        if (Math.Abs(deemphasisState) < 1e-30) {
            deemphasisState = 0;
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

    /// <summary>
    ///     Gain: soft rails x / sqrt(1 + x² / r²). Rectifier: x blended towards sqrt(x² + k²) - k. Tape: r tanh(x / r).
    /// </summary>
    readonly double Function(double x) {
        if (kind == StageKind.Tape) {
            return rail * Math.Tanh(x / rail);
        }

        if (kind != StageKind.Rectifier) {
            return x / Math.Sqrt(1 + x * x / (rail * rail));
        }

        return x + blend * (Math.Sqrt(x * x + knee * knee) - knee - x);
    }

    readonly double Integral(double x) {
        if (kind == StageKind.Tape) {
            // r² ln cosh(x / r), written so that it cannot overflow.
            var u = Math.Abs(x / rail);
            return rail * rail * (u + Math.Log(1 + Math.Exp(-2 * u)) - Math.Log(2));
        }

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

    static void Flush(ref Capacitor capacitor) {
        if (Math.Abs(capacitor.Reflected) < 1e-30) {
            capacitor.Reset();
        }
    }
}

/// <summary>
///     The Drive block's Advanced page, applied on top of a circuit's own values; <see cref="Stock" /> changes nothing.
/// </summary>
/// <param name="BassScale">
///     Scales the input coupling corner (1 stock); a circuit coupled in its first stage's netlist gets an extra
///     first-order high-pass at 10 Hz times it instead.
/// </param>
/// <param name="Bias">Volts added to the circuit's own bias ahead of the first stage.</param>
/// <param name="DryLevel">Clean signal summed after the stages, on top of the circuit's own clean blend (linear).</param>
/// <param name="Slew">
///     Largest change per oversampled sample, in volts (0 = no limit): an op-amp circuit's op-amp slews at it (each
///     op-amp stage, <see cref="OpAmpStage" />), any other circuit is limited after its stages.
/// </param>
/// <param name="StackBass">A tone stack's bass pot (<see cref="ToneKind.Stack" />), 0..1; noon as built.</param>
/// <param name="StackMiddle">A tone stack's middle pot.</param>
readonly record struct CircuitTweaks(
    double BassScale = 1,
    double Bias = 0,
    double DryLevel = 0,
    double Slew = 0,
    double StackBass = 0.5,
    double StackMiddle = 0.5
) {
    public static CircuitTweaks Stock => new(1);
}

/// <summary>Per-channel state of the circuit voicings: everything except the oversampler, which the block shares.</summary>
sealed class CircuitChannel {
    readonly CircuitStage[] stages = new CircuitStage[DriveCircuits.MaxStages];
    readonly StageKind[] kinds = new StageKind[DriveCircuits.MaxStages];
    readonly OpAmpStage opAmp = new();
    readonly TransistorStage transistor = new();
    readonly FuzzPairStage fuzz = new();
    readonly ToneStage tone = new();
    RcLowPass input = new();
    SvfState dc;
    DriveCircuit? circuit;
    int count;
    bool highPass;
    double bias;
    double clean;
    double level; // the guitar's volume, and its loading of an input that is not modelled at component level
    double trim;
    double slew;
    double slewed;

    public CircuitChannel() {
        for (var i = 0; i < stages.Length; i++) {
            stages[i] = new();
        }
    }

    /// <summary>Forgets the configured circuit, so the next <see cref="Configure" /> starts discharged.</summary>
    public void Release() => circuit = null;

    /// <summary>
    ///     Per sub-block: component values for <paramref name="drive" />, <paramref name="tone" /> and the guitar's
    ///     <paramref name="volume" /> (all 0..10). Each stage recomputes only what changed.
    /// </summary>
    public void Configure(
        DriveCircuit circuit,
        double drive,
        double tone,
        double volume,
        double innerRate,
        double rate,
        bool antialias,
        in CircuitTweaks tweaks = default
    ) {
        var fresh = !ReferenceEquals(circuit, this.circuit);
        this.circuit = circuit;
        var position = drive / 10;
        var guitar = DriveCircuits.Guitar(volume);
        count = circuit.Stages.Count;
        bias = circuit.Bias + tweaks.Bias;
        var opAmps = false;
        for (var i = 0; i < count; i++) {
            opAmps |= circuit.Stages[i].Kind == StageKind.OpAmp; // a loop: no enumerator on the audio thread
        }

        slew = opAmps ? 0 : tweaks.Slew;
        for (var i = 0; i < count; i++) {
            var stage = circuit.Stages[i];
            var resistance = i == 0 ? guitar.Resistance : stage.SourceResistance;
            var driven = i == 0
                ? new(guitar.Series, DriveCircuits.PickupInductance, guitar.Shunt)
                : StageSource.Driven(stage.SourceResistance);
            kinds[i] = stage.Kind;
            switch (stage.Kind) {
                case StageKind.OpAmp:
                    opAmp.Configure(stage, position, resistance, innerRate, antialias, tweaks.Slew * innerRate);
                    break;
                case StageKind.Transistor: {
                    var range = circuit.Tone;
                    var capacitance = i == 0 && range.Kind == ToneKind.Range
                        ? range.Capacitor1 * Math.Pow(range.Capacitor2 / range.Capacitor1, tone / 10)
                        : stage.InputCapacitance;
                    transistor.Configure(stage, capacitance, driven, innerRate);
                    break;
                }

                case StageKind.FuzzPair:
                    fuzz.Configure(stage, position, driven, innerRate);
                    break;
                default:
                    stages[i].Configure(stage, position, innerRate, antialias);
                    break;
            }
        }

        // A transistor stage takes the pickup's own voltage behind the whole guitar network; an op-amp stage the guitar's
        // Thévenin equivalent (its input is too high to care for the inductance); anything else that equivalent's level,
        // loaded by a nominal input resistance.
        level = circuit.Stages[0].Kind switch {
            StageKind.Transistor or StageKind.FuzzPair => guitar.Emf,
            StageKind.OpAmp => guitar.Level,
            _ => guitar.Level * circuit.InputResistance / (circuit.InputResistance + guitar.Resistance)
        };
        var bassScale = tweaks.BassScale == 0 ? 1 : tweaks.BassScale;
        var corner = circuit.InputHighPassHz > 0 ? circuit.InputHighPassHz * bassScale
            : bassScale != 1 ? 10 * bassScale
            : 0;
        highPass = corner > 0;
        if (highPass) {
            input.SetCutoff(corner, innerRate);
        }

        clean = circuit.CleanMix * (1 - 0.75 * position) + tweaks.DryLevel;
        trim = Decibels.ToGain(circuit.OutputDb) * (circuit.InvertOutput ? -1 : 1);
        this.tone.Configure(circuit.Tone, tone / 10, rate, tweaks.StackBass, tweaks.StackMiddle);
        if (fresh) {
            Reset(); // a new circuit starts discharged, or at its operating point
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

    /// <summary>
    ///     <see
    ///         cref="ProcessInner(CircuitChannel, CircuitChannel, Span{float}, Span{float}, float, float, in SvfCoefficients)" />
    ///     for one channel: the Circuit Amp's input boost, a mono pedal ahead of a mono amp.
    /// </summary>
    public void ProcessInner(Span<float> samples, float fromGain, float toGain, in SvfCoefficients dcBlock) {
        var step = (toGain - fromGain) / samples.Length;
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = Tick(samples[i], fromGain + step * (i + 1), dcBlock);
        }

        FlushDenormals();
    }

    /// <summary>The base-rate part: tone network and output trim.</summary>
    public void ProcessOuter(Span<float> samples) {
        for (var i = 0; i < samples.Length; i++) {
            var y = tone.Tick(samples[i]) * trim;
            samples[i] = Math.Abs(y) < 1e-30 ? 0f : (float)y; // a decaying high-pass can cancel down to subnormals
        }

        tone.FlushDenormals();
    }

    public void Reset() {
        foreach (ref var stage in stages.AsSpan()) {
            stage.Reset();
        }

        opAmp.Reset();
        transistor.Reset();
        fuzz.Reset();
        tone.Reset();
        input.Reset();
        dc.Reset();
        slewed = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    float Tick(float sample, float gain, in SvfCoefficients dcBlock) {
        double x = sample;
        if (highPass) {
            x -= input.Tick(x);
        }

        var v = x * gain * level + bias;
        var stages = this.stages.AsSpan(0, count);
        for (var s = 0; s < stages.Length; s++) {
            v = kinds[s] switch {
                StageKind.OpAmp => opAmp.Tick(v),
                StageKind.Transistor => transistor.Tick(v),
                StageKind.FuzzPair => fuzz.Tick(v),
                _ => stages[s].Tick(v)
            };
        }

        if (slew > 0) {
            v = slewed + Math.Clamp(v - slewed, -slew, slew);
            slewed = v;
        }

        return (float)dc.Tick(dcBlock, v + clean * x * level);
    }

    void FlushDenormals() {
        input.FlushDenormals();
        dc.FlushDenormals();
        opAmp.FlushDenormals();
        foreach (ref var stage in stages.AsSpan(0, count)) {
            stage.FlushDenormals();
        }
    }
}
