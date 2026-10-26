using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

public static partial class DriveCircuits {
    /// <summary>
    ///     Drive round 4, group D1: overdrives after the Fractal Blocks Guide's list (pp. 37–38), appended after every
    ///     earlier circuit (presets store the type by index). Values come from the pedals' published schematics and traces;
    ///     where none was found the circuit follows the pedal's published description, and docs/09 (stream D1) lists what
    ///     was guessed.
    /// </summary>
    static DriveCircuit[] Round4Overdrives() => [
        FullDrive(),
        new(
            "OD-One Overdrive",
            "Boss OD-1: op-amp with asymmetric 1 + 2 silicon diodes across its feedback, fixed output low-pass (no tone "
            + "knob; Tone 10 is the pedal)"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    InputBias = 470e3,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 10e3,
                    FeedbackPot = 1e6,
                    FeedbackCapacitance = 47e-12,
                    Diodes = new(Si, 1, Si, 2)
                }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 10e3,
                Pot = 100e3,
                AudioTaper = true,
                Capacitor1 = 4.7e-9
            },
            OutputDb = -7.2
        },
        new(
            "OD 250",
            "DOD 250 (yellow): LM741 with the gain pot in its ground leg, silicon diodes to ground (no tone knob)"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.LM741,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 100e-9,
                    GroundPot = 500e3,
                    Feedback = 1e6,
                    FeedbackCapacitance = 0
                },
                new(StageKind.Clipper) { Series = 10e3, Shunt = 1e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 10e3,
                Resistor1 = 1e3,
                Pot = 100e3,
                AudioTaper = true,
                Capacitor1 = 1e-9
            },
            OutputDb = -6.8
        },
        new(
            "Sonic Drive",
            "Ibanez SD9M / Maxon SD-9: a TS gain stage driven into its rails, then silicon diodes to ground, TS tone stage"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    InputBias = 510e3,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 47e-9,
                    Feedback = 51e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 51e-12
                },
                new(StageKind.Clipper) { Series = 1e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = ScreamerTone with { Load = 100e3 },
            OutputDb = -6.5
        },
        Eternity(),
        new(
            "Jam Ray",
            "Vemuram Jan Ray: Timmy-style op-amp stage with silicon diodes across its feedback, full bass, active treble "
            + "recovery stage"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 10e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 47e-12,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            Tone = TrebleTone,
            OutputDb = -5.4
        },
        new(
            "Horizon Precision Drive",
            "Horizon Devices Precision Drive: tight pre-cut (the Attack knob), high-gain TL072 into its rails, silicon "
            + "diodes to ground, passive treble cut in place of the Bright switch, noise gate"
        ) {
            InputHighPassHz = 160, // Attack at noon
            AttackKnob = true,
            Gate = true,
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    GroundResistance = 1e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 1e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 100e-12
                },
                new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 50e3,
                AudioTaper = true,
                Capacitor1 = 4.7e-9
            },
            OutputDb = -8
        },
        KingOfTone(),
        new(
            "Hoodoo Drive",
            "Voodoo Lab Overdrive: a TS808-derived clipping amp with more bass into the diodes, TS tone stage"
        ) {
            Stages = [ScreamerStage(DiodeStacks.Pair(Si), 100e-9)],
            Tone = ScreamerTone with { Load = 100e3 },
            OutputDb = -3.5
        },
        new(
            "Valve Screamer VS9",
            "The guide's TS9 Tube Screamer: the TS9 clipping amp and tone stage with a TL072 in place of the JRC4558"
        ) {
            Stages = [ScreamerStage(DiodeStacks.Pair(Si)) with { Amplifier = OpAmpModel.TL072 }],
            Tone = ScreamerTone with { Load = 100e3 },
            OutputDb = -2.8
        }
    ];

    /// <summary>
    ///     The Full-Drive 2: JRC4558 with silicon diodes across its feedback, passive treble cut. Its three-way switch:
    ///     Flat-Mids (the circuit as listed: 2.2k + 220 nF to ground, a 330 Hz bass corner), Comp-Cut (two diodes in
    ///     series each way: less compression, more output) and Vintage (the TS's 4.7k + 47 nF, 720 Hz: the mid hump). The
    ///     Boost footswitch adds up to 20 dB of clean boost ahead of the clipping amp. The switch positions' values and the
    ///     boost's range are guessed from Fulltone's descriptions.
    /// </summary>
    static DriveCircuit FullDrive() {
        var stage = new DriveStage(StageKind.OpAmp) {
            InputBias = 510e3,
            GroundResistance = 2.2e3,
            GroundCapacitance = 220e-9,
            Feedback = 47e3,
            FeedbackPot = 500e3,
            FeedbackCapacitance = 47e-12,
            Diodes = DiodeStacks.Pair(Si)
        };
        var flat = new DriveCircuit(
            "Full OD",
            "Fulltone Full-Drive 2: JRC4558 with silicon diodes across its feedback, a lower bass corner than a TS "
            + "(Flat-Mids), passive treble cut; Comp-Cut and Vintage modes, Boost footswitch"
        ) {
            Switch = PedalSwitch.FullDriveMode,
            BoostDb = 20,
            Stages = [stage],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 25e3,
                AudioTaper = true,
                Capacitor1 = 10e-9
            },
            OutputDb = -6
        };
        return flat with {
            Modes = [
                flat with { Stages = [stage with { Diodes = DiodeStacks.Pair(Si, 2) }] },
                flat with { Stages = [stage with { GroundResistance = 4.7e3, GroundCapacitance = 47e-9 }] }
            ]
        };
    }

    /// <summary>
    ///     The Eternity: a TS-style gain stage with a fuller bass corner into silicon diodes to ground, passive treble cut
    ///     (Glass). Glow adds a silicon pair across the gain stage's feedback, for the smoother, more compressed voice
    ///     Lovepedal describes (the Glow network is guessed: no verified schematic of it).
    /// </summary>
    static DriveCircuit Eternity() {
        var stage = new DriveStage(StageKind.OpAmp) {
            InputBias = 510e3,
            GroundResistance = 4.7e3,
            GroundCapacitance = 100e-9,
            Feedback = 51e3,
            FeedbackPot = 500e3,
            FeedbackCapacitance = 51e-12
        };
        var clipper = new DriveStage(StageKind.Clipper) {
            Series = 4.7e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si)
        };
        var glass = new DriveCircuit(
            "Eternal Love",
            "Lovepedal Eternity: TS-style gain stage with a fuller bass corner into silicon diodes to ground, passive "
            + "treble cut (Glass); Glow adds diodes across the gain stage's feedback"
        ) {
            Switch = PedalSwitch.Glow,
            Stages = [stage, clipper],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 25e3,
                AudioTaper = true,
                Capacitor1 = 10e-9
            },
            OutputDb = -7.4
        };
        return glass with { Modes = [glass with { Stages = [stage with { Diodes = DiodeStacks.Pair(Si) }, clipper] }] };
    }

    /// <summary>
    ///     The King of Tone: a Bluesbreaker-style op-amp stage into a second, low-gain diode-feedback stage, passive
    ///     treble cut. Its mode DIP switches: OD (the first stage's diodes across its feedback, as listed), Distortion
    ///     (those diodes to ground after it instead, through 1k: harder clipping, more gain) and Boost (no diodes: the
    ///     op-amps clip on their rails, much louder). The Distortion clipper's resistor and the Boost stage's 4 V swing
    ///     are guessed.
    /// </summary>
    static DriveCircuit KingOfTone() {
        var first = new DriveStage(StageKind.OpAmp) {
            Amplifier = OpAmpModel.TL072,
            GroundResistance = 6.8e3,
            GroundCapacitance = 1e-6,
            Feedback = 10e3,
            FeedbackPot = 100e3,
            FeedbackCapacitance = 1e-9,
            Diodes = DiodeStacks.Pair(Si)
        };
        var second = new DriveStage(StageKind.Gain) {
            GroundResistance = 4.7e3,
            GroundCapacitance = 220e-9,
            Feedback = 10e3,
            FeedbackCapacitance = 1e-9,
            Diodes = DiodeStacks.Pair(Si)
        };
        var overdrive = new DriveCircuit(
            "Tone of Kings",
            "Analog Man King of Tone: a Bluesbreaker-style op-amp stage with silicon diodes across its feedback (OD; "
            + "to ground in Distortion, none in Boost) into a second, low-gain diode-feedback stage, passive treble cut"
        ) {
            Switch = PedalSwitch.KingMode,
            Stages = [first, second],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 1e3,
                Pot = 25e3,
                AudioTaper = true,
                Capacitor1 = 10e-9
            },
            OutputDb = -4
        };
        var open = first with { Diodes = default };
        return overdrive with {
            Modes = [
                overdrive with {
                    Stages = [
                        open, new(StageKind.Clipper) { Series = 1e3, Shunt = 10e-9, Diodes = DiodeStacks.Pair(Si) },
                        second
                    ]
                },
                overdrive with { Stages = [open, second with { Diodes = default, Rail = 4 }] }
            ]
        };
    }
}
