using Cathode.Dsp.Cabinets;
using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Blocks;

public static partial class DriveCircuits {
    /// <summary>
    ///     RCA's CA3130 (the MXR Micro Amp's): a CMOS-output op-amp that swings close to its rails (0.2 V here, loaded);
    ///     110 dB, 15 MHz, 10 V/µs, typical datasheet values.
    /// </summary>
    static OpAmpModel CA3130 => new(320e3, 15e6, 10e6, 1.5e12, 200, 0.2);

    /// <summary>
    ///     Round 4, group D3: fuzzes, boosts, preamps and the two digital/tape types, after the Blocks Guide's Drive type
    ///     list (pp. 37–39). Values come from the published schematics where there is one; docs/09 (stream D3) lists what
    ///     was guessed for each.
    /// </summary>
    static DriveCircuit[] Round4Fuzz() => [
        new(
            "Master Fuzz",
            "Maestro FZ-1A Fuzz-Tone: starved germanium PNPs on 3 V, a gain stage into a direct-coupled pair"
        ) {
            Stages = [
                new(StageKind.Transistor) {
                    Transistor = BjtModel.AC128,
                    Supply = 3,
                    InputCapacitance = 1e-6,
                    Feedback = 470e3,
                    Collector = 10e3,
                    Emitter = 1,
                    EmitterCapacitance = 1e-6,
                    OutputCapacitance = 1e-6,
                    Load = 1e6
                },
                FaceStage(BjtModel.AC128) with {
                    Supply = 3,
                    InputCapacitance = 100e-9,
                    SourceResistance = 10e3,
                    Collector = 22e3,
                    CollectorSupply = 4.7e3,
                    OutputCapacitance = 100e-9
                }
            ],
            MinGainDb = -18, // the Attack pot: how much of the guitar reaches the first stage
            MaxGainDb = 0,
            InputHighPassHz = 0,
            InvertOutput = true,
            Oversampling = 2,
            Tone = FuzzTone,
            OutputDb = -14
        },
        SuperFuzz(),
        new(
            "Hard Fuzz",
            "Hard-clipping 60s-style fuzz (a design): transistor gain slamming its rails, 1 + 2 silicon to ground"
        ) {
            Bias = 0.15,
            MinGainDb = 0,
            MaxGainDb = 30,
            Stages = [
                new(StageKind.Gain) {
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 2.2e-6,
                    Feedback = 22e3,
                    FeedbackCapacitance = 100e-12,
                    Rail = 4
                },
                new(StageKind.Clipper) { Series = 2.2e3, Shunt = 10e-9, Diodes = new(Si, 1, Si, 2) }
            ],
            Tone = FuzzTone,
            OutputDb = -12
        },
        new(
            "Tape Distortion",
            "Reel-to-reel deck driven hot: 50 µs record pre-emphasis, tape saturation, matching playback de-emphasis"
        ) {
            MinGainDb = -6,
            MaxGainDb = 24,
            InputHighPassHz = 30,
            Stages = [new(StageKind.Tape) { TapeType = MicPreampTypes.Tape50, Rail = 0.5 }],
            Tone = new(ToneKind.HighCut) {
                Source = 1e3,
                Resistor1 = 470,
                Pot = 25e3,
                AudioTaper = true,
                Capacitor1 = 10e-9
            },
            OutputDb = -6
        },
        new("Bit Crusher", "Digital lo-fi (a design): Drive takes 16 bits down to 3 and the rate down to 1.5 kHz") {
            MinGainDb = 12,
            MaxGainDb = 12,
            Stages = [new(StageKind.Crusher) { Bits = 3, HoldRate = 1_500 }],
            Tone = new(ToneKind.HighCut) {
                Source = 100,
                Resistor1 = 220,
                Pot = 47e3,
                AudioTaper = true,
                Capacitor1 = 4.7e-9
            },
            OutputDb = -12
        },
        new("SDD Preamp", "Korg SDD-3000 input preamp: a 4558 on ±15 V with treble emphasis, clipping on its rails") {
            Stages = [
                new(StageKind.OpAmp) {
                    Supply = 30,
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 1e-6,
                    GroundResistance2 = 2.2e3,
                    GroundCapacitance2 = 22e-9,
                    Feedback = 10e3,
                    FeedbackPot = 470e3,
                    FeedbackCapacitance = 22e-12
                }
            ],
            Tone = TrebleTone,
            OutputDb = -31
        },
        new(
            "FET Preamp",
            "Two-JFET preamp (a design): a square-law first stage into a second running out of headroom, passive stack"
        ) {
            MinGainDb = 0,
            MaxGainDb = 24,
            Bias = 0.4,
            Stages = [
                new(StageKind.Clipper) {
                    Series = 47e3, Shunt = 100e-12, Diodes = new(DiodeModel.Mosfet, 1, DiodeModel.Mosfet, 2)
                },
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 4.7e-6,
                    Feedback = 47e3,
                    FeedbackCapacitance = 100e-12,
                    Rail = 3
                }
            ],
            Tone = new(ToneKind.Stack) {
                Source = 4.7e3,
                Resistor1 = 47e3,
                Pot = 250e3,
                Resistor2 = 1e6,
                Resistor3 = 25e3,
                Capacitor1 = 250e-12,
                Capacitor2 = 22e-9,
                Capacitor3 = 22e-9
            },
            OutputDb = -10
        },
        new(
            "Integral Pre",
            "Clean preamp and EQ (a design): a TL072 on 18 V, modest gain, passive treble/bass/middle stack"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    Supply = 18,
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 4.7e-6,
                    Feedback = 4.7e3,
                    FeedbackPot = 47e3,
                    FeedbackCapacitance = 100e-12
                }
            ],
            Tone = new(ToneKind.Stack) {
                Source = 4.7e3, // the op-amp's build-out resistor
                Resistor1 = 33e3,
                Pot = 250e3,
                Resistor2 = 1e6,
                Resistor3 = 25e3,
                Capacitor1 = 250e-12,
                Capacitor2 = 22e-9,
                Capacitor3 = 22e-9
            },
            OutputDb = 0
        },
        new("Micro Boost", "MXR Micro Amp: CA3130 non-inverting, the Gain pot in its ground leg, up to about 26 dB") {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = CA3130,
                    InputBias = 1e6,
                    GroundResistance = 2.7e3,
                    GroundCapacitance = 4.7e-6,
                    GroundPot = 500e3,
                    Feedback = 56e3,
                    FeedbackCapacitance = 0
                }
            ],
            Tone = new(ToneKind.HighCut) {
                Source = 100,
                Resistor1 = 100,
                Pot = 50e3,
                AudioTaper = true,
                Capacitor1 = 2.2e-9
            },
            OutputDb = -4
        },
        new(
            "Mid Boost",
            "Mid-boost overdrive (a design): bass cut at 720 Hz and treble cut in the feedback around silicon diodes"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 100e-9,
                    Feedback = 22e3,
                    FeedbackPot = 500e3,
                    FeedbackCapacitance = 220e-12,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            Tone = ScreamerTone,
            OutputDb = -3.5
        },
        new(
            "Bosom Boost",
            "Friedman Buxom Boost: clean op-amp boost to about 27 dB on 18 V, active bass and treble, Tight switch"
        ) {
            // Tight raises the input coupling corner; Bass is a ±15 dB shelf at 100 Hz after the treble (both guessed).
            TightHz = 150,
            BassShelfHz = 100,
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    Supply = 18,
                    GroundResistance = 1e3,
                    GroundCapacitance = 10e-6,
                    Feedback = 0,
                    FeedbackPot = 22e3,
                    FeedbackCapacitance = 220e-12
                }
            ],
            Tone = TrebleTone with { Capacitor1 = 6.8e-9, Capacitor2 = 6.8e-9, Pot = 100e3 },
            OutputDb = -12
        },
        new(
            "FAS Boost",
            "Fractal's own boost (a design): flat TL072 boost to 21 dB on 18 V with a gentle presence lift"
        ) {
            Stages = [
                new(StageKind.OpAmp) {
                    Amplifier = OpAmpModel.TL072,
                    Supply = 18,
                    GroundResistance = 2.2e3,
                    GroundCapacitance = 10e-6,
                    GroundResistance2 = 4.7e3,
                    GroundCapacitance2 = 4.7e-9,
                    Feedback = 0,
                    FeedbackPot = 22e3,
                    FeedbackCapacitance = 47e-12
                }
            ],
            Tone = TrebleTone,
            OutputDb = -10
        }
    ];

    /// <summary>
    ///     The Super-Fuzz: a silicon gain stage, a phase splitter and two diodes rectifying (octave up), a clipped output
    ///     stage. Its tone switch: Scoop (the passive low-pass/high-pass network at noon: the mid scoop) or Flat (the
    ///     filter switched out: the output stage straight into the 100k Balance pot, a little stray capacitance). The
    ///     networks' values are guessed (39k + 10 nF, 3.9 nF + 22k; 100 pF).
    /// </summary>
    static DriveCircuit SuperFuzz() {
        var scoop = new DriveCircuit(
            "Super Fuzz",
            "Univox Super-Fuzz: a silicon gain stage, a phase splitter and two diodes rectifying (octave up), clipped "
            + "output stage; the tone switch scoops the mids or switches the filter out"
        ) {
            Switch = PedalSwitch.ToneSwitch,
            ToneKnob = false,
            Stages = [
                new(StageKind.Transistor) {
                    Transistor = BjtModel.BC108,
                    Supply = 9,
                    InputCapacitance = 100e-9,
                    Feedback = 470e3,
                    Collector = 10e3,
                    Emitter = 1e3,
                    EmitterCapacitance = 1e-9, // unbypassed: the first stage's gain is about Rc / Re
                    OutputCapacitance = 100e-9,
                    Load = 100e3
                },
                new(StageKind.Rectifier) { Knee = 0.05, Blend = 0.9 },
                new(StageKind.Gain) {
                    GroundResistance = 4.7e3,
                    GroundCapacitance = 1e-6,
                    Feedback = 4.7e3,
                    FeedbackPot = 100e3,
                    FeedbackCapacitance = 1e-9,
                    Diodes = DiodeStacks.Pair(Si)
                }
            ],
            InputHighPassHz = 0,
            Oversampling = 2,
            Tone = new(ToneKind.Blend) {
                Source = 10e3,
                Resistor1 = 39e3,
                Capacitor1 = 10e-9,
                Capacitor2 = 3.9e-9,
                Resistor2 = 22e3,
                Pot = 100e3,
                Load = 100e3
            },
            OutputDb = 10
        };
        var flat = new ToneNetwork(ToneKind.HighCut) {
            Source = 10e3, Resistor1 = 100, Pot = 1, Capacitor1 = 100e-12, Load = 100e3
        };
        return scoop with { Modes = [scoop with { Tone = flat }] };
    }
}
