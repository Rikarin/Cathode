using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

public static partial class DriveCircuits {
    // No static fields here: the catalogue is built by a static initialiser in the other part of this class, and the
    // order of initialisers across the parts of a partial class is not defined. Everything below is built in methods.

    /// <summary>
    ///     Drive round 4, group D2: distortions and amp-in-a-box pedals (docs/08 §6.5, docs/09 stream D2), appended
    ///     after group D1's overdrives. Values are from each pedal's published schematic where one exists; the docs
    ///     list what was guessed.
    /// </summary>
    static DriveCircuit[] Round4Distortions() => [
        new(
            "M-Zone Distortion",
            "Boss MT-2 Metal Zone: two cascaded gain stages (rails, then silicon across the feedback), silicon to ground, "
            + "three-band active EQ with a sweepable mid"
        ) {
            // The transistor pre-gain stage ahead of the first op-amp, as a fixed gain.
            MinGainDb = 14,
            MaxGainDb = 14,
            InputHighPassHz = 30,
            Stages = [
                new(StageKind.OpAmp) {
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 100e-9, // 720 Hz: the mid push into the clipping
                    GroundResistance2 = 22e3,
                    GroundCapacitance2 = 1e-6,
                    Feedback = 10e3,
                    FeedbackPot = 220e3, // Dist, first gang
                    FeedbackCapacitance = 100e-12
                },
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 47e3,
                    FeedbackPot = 220e3, // Dist, second gang
                    FeedbackCapacitance = 220e-12,
                    GainBandwidth = 3e6,
                    Diodes = DiodeStacks.Pair(Si)
                },
                new(StageKind.Clipper) { Series = 10e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.ActiveEq) {
                Source = 1e3,
                Resistor1 = 10e3,
                Pot = 50e3,
                BandResistance = 2.2e3, // about ±14 dB
                LowHz = 100,
                MidHz = 1_000,
                MidSweep = true,
                BandQ = 1.2,
                HighHz = 4_000,
                Load = 100e3
            },
            OutputDb = -7.5
        },
        new("Fat Rat", "Pro Co Fat Rat: the RAT gain stage with its Fat bass boost on, MOSFET clipping to ground") {
            Oversampling = 2,
            Stages = [
                // Fat: the gain-setting leg's capacitor raised from 2.2 µF, so the bass gets the full gain too.
                RatStage() with { GroundCapacitance = 10e-6 },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(DiodeModel.Mosfet) }
            ],
            Tone = RatFilter,
            OutputDb = -17.1
        },
        RiotCircuit("Suhr Riot Ge", "germanium", DiodeStacks.Pair(Ge), -1.3),
        RiotCircuit("Suhr Riot LED", "red LEDs", DiodeStacks.Pair(DiodeModel.Led), -16.4),
        RiotCircuit("Suhr Riot LED/Si", "an LED one way and silicon the other", new(DiodeModel.Led, 1, Si, 1), -13),
        new(
            "MCMLXXXI Drive",
            "1981 Inventions DRV: a RAT-family op-amp gain stage on an 18 V charge pump, silicon to ground, active treble"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    Supply = 18,
                    GroundResistance = 560,
                    GroundCapacitance = 4.7e-6,
                    GroundResistance2 = 4.7e3,
                    GroundCapacitance2 = 47e-9,
                    Feedback = 1e3,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 100e-12
                },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 4.7e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = TrebleTone with { Capacitor1 = 6.8e-9, Capacitor2 = 6.8e-9, Pot = 25e3 },
            OutputDb = -9.3
        },
        new(
            "Box O' Crunch",
            "MI Audio Crunch Box: one hot op-amp gain stage voiced for the mids, silicon to ground, passive treble cut"
        ) {
            InputHighPassHz = 50,
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 3.3e3,
                    GroundCapacitance = 47e-9, // about 1 kHz: the Marshall-style mid push
                    GroundResistance2 = 33e3,
                    GroundCapacitance2 = 1e-6,
                    Feedback = 4.7e3,
                    FeedbackPot = 1e6,
                    FeedbackCapacitance = 47e-12
                },
                new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 2.2e3,
                Resistor1 = 1e3,
                Pot = 25e3,
                AudioTaper = true,
                Capacitor1 = 22e-9
            },
            OutputDb = -7
        },
        new(
            "Blackglass 7K",
            "Darkglass Microtubes B7K: two CMOS inverter gain stages that soft-clip on their rails, clean blend, "
            + "four-band active EQ, Grunt and Attack switches"
        ) {
            GruntAndAttack = true,
            BlendKnob = true,
            InputHighPassHz = 40,
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 10e3,
                    GroundCapacitance = 220e-9,
                    Feedback = 22e3,
                    FeedbackPot = 1e6, // Drive
                    FeedbackCapacitance = 100e-12,
                    GainBandwidth = 1e6,
                    Inverting = true,
                    Rail = 3
                },
                new(StageKind.Gain) {
                    GroundResistance = 22e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 220e3,
                    FeedbackCapacitance = 47e-12,
                    GainBandwidth = 1e6,
                    Inverting = true,
                    Rail = 3.5
                }
            ],
            CleanMix = 0.5, // the clean path's level with the Blend knob at noon
            Tone = new(ToneKind.ActiveEq) {
                Source = 1e3,
                Resistor1 = 10e3,
                Pot = 50e3,
                BandResistance = 3.3e3, // ±12 dB
                LowHz = 100,
                MidHz = 500, // Lo Mids (the Middle Pot)
                Mid2Hz = 1_500, // Hi Mids
                HighHz = 5_000,
                BandQ = 0.9,
                Load = 100e3
            },
            OutputDb = -23.7
        },
        new(
            "Esoteric ACB",
            "Xotic AC Booster: an op-amp gain stage with silicon across its feedback (soft compression), bass and treble"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 220e-9,
                    Feedback = 10e3,
                    FeedbackPot = 250e3,
                    FeedbackCapacitance = 47e-12,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            Tone = BoosterEq(120, 3_000),
            OutputDb = -5.7
        },
        new(
            "Esoteric Bass RCB",
            "Xotic Bass RC Booster: a full-range clean op-amp boost that only softens on its rails, bass and treble, "
            + "internal DIP switches for the bass and treble frequencies"
        ) {
            // The DIP switches move each band up an octave (guessed: Xotic publish no frequencies for them).
            DipLowHz = 120,
            DipHighHz = 5_000,
            MaxGainDb = 12, // as the Esoteric Boost: the booster's gain range reaches its rails on a hot pickup
            InputHighPassHz = 10,
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 10e-6,
                    Feedback = 2.2e3,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 100e-12
                }
            ],
            Tone = BoosterEq(60, 2_500),
            OutputDb = -12
        },
        new(
            "FAS LED-Drive",
            "Fractal's own LED-clipped design (no schematic): a TL072 with red LEDs across its feedback, passive treble cut"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 4.7e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 47e-12,
                    Diodes = DiodeStacks.Pair(DiodeModel.Led)
                }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 50e3,
                AudioTaper = true,
                Capacitor1 = 4.7e-9
            },
            OutputDb = -16
        },
        new(
            "Ruckus",
            "Suhr Riot as Fractal first modelled it (the original Riot): the Riot gain stage, silicon to ground, LP/HP tone"
        ) {
            Stages = [
                RiotStage(), new(StageKind.Clipper) { Series = 2.2e3, Shunt = 4.7e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.Blend) {
                Source = 2.2e3,
                Resistor1 = 22e3,
                Capacitor1 = 10e-9,
                Capacitor2 = 10e-9,
                Resistor2 = 22e3,
                Pot = 100e3,
                Load = 100e3
            },
            OutputDb = 0.7
        },
        new(
            "Shimmer Drive",
            "Fractal's own boost for amps without a master volume (no schematic): a bright TL072 boost that softens on its "
            + "rails, active treble"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 15e-9, // 2.3 kHz: the treble gets about 4 dB more gain
                    GroundResistance2 = 3.3e3,
                    GroundCapacitance2 = 1e-6,
                    Feedback = 3.3e3,
                    FeedbackPot = 250e3,
                    FeedbackCapacitance = 22e-12
                }
            ],
            Tone = TrebleTone,
            OutputDb = -12
        },
        TubeDriver(
            "Tube Drive 3-Knob",
            "Chandler Tube Driver (3 knobs): op-amp drive stage into a starved-plate 12AX7, passive treble cut",
            new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 100e3,
                AudioTaper = true,
                Capacitor1 = 3.3e-9
            }
        ),
        TubeDriver(
            "Tube Drive 4-Knob",
            "B.K. Butler Tube Driver (4 knobs): op-amp drive stage into a starved-plate 12AX7, active bass and treble",
            BoosterEq(100, 3_000) with { BandResistance = 2.2e3 }
        )
    ];

    /// <summary>
    ///     The Suhr Riot's gain stage: a TL072 with the Distortion pot in its feedback and two legs to ground (the bass
    ///     cut and a mid push), driving the diodes to ground into its rails.
    /// </summary>
    static DriveStage RiotStage() =>
        new(StageKind.OpAmp) {
            Amplifier = OpAmpModel.TL072,
            GroundResistance = 1e3,
            GroundCapacitance = 1e-6,
            GroundResistance2 = 4.7e3,
            GroundCapacitance2 = 22e-9,
            Feedback = 1e3,
            FeedbackPot = 500e3,
            FeedbackCapacitance = 100e-12
        };

    /// <summary>
    ///     One voicing of the Suhr Riot: its gain stage into the voicing switch's diodes, passive treble cut.
    /// </summary>
    static DriveCircuit RiotCircuit(string name, string diodes, DiodeStacks stacks, double outputDb) =>
        new(name, $"Suhr Riot: TL072 gain stage into {diodes} to ground (the voicing switch), passive treble cut") {
            Stages = [RiotStage(), new(StageKind.Clipper) { Series = 2.2e3, Shunt = 4.7e-9, Diodes = stacks }],
            Tone = new(ToneKind.HighCut) {
                Source = 2.2e3,
                Resistor1 = 1e3,
                Pot = 100e3,
                AudioTaper = true,
                Capacitor1 = 6.8e-9
            },
            OutputDb = outputDb
        };

    /// <summary>The Xotic boosters' bass and treble: an active EQ with a low and a treble band, ±15 dB.</summary>
    static ToneNetwork BoosterEq(double lowHz, double highHz) =>
        new(ToneKind.ActiveEq) {
            Source = 100,
            Resistor1 = 10e3,
            Pot = 50e3,
            BandResistance = 1.8e3,
            LowHz = lowHz,
            HighHz = highHz,
            Load = 100e3
        };

    /// <summary>
    ///     The Tube Driver: a TL072 drive stage (the Drive pot in its feedback) into one 12AX7 on a 40 V plate supply,
    ///     whose grid conduction and cut-off do the clipping, then the version's tone control.
    /// </summary>
    static DriveCircuit TubeDriver(string name, string modelledAfter, ToneNetwork tone) =>
        new(name, modelledAfter) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 10e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 1e3,
                    FeedbackPot = 470e3,
                    FeedbackCapacitance = 47e-12
                },
                new(StageKind.Triode) {
                    Tube = TriodeModel.Ecc83,
                    Supply = 40,
                    SourceResistance = 10e3, // the op-amp's output and the grid stopper
                    InputCapacitance = 22e-9,
                    GridLeak = 1e6,
                    Plate = 100e3,
                    Cathode = 1.5e3,
                    CathodeCapacitance = 22e-6,
                    OutputCapacitance = 100e-9,
                    Load = 470e3
                }
            ],
            Tone = tone,
            OutputDb = -30.5
        };
}
