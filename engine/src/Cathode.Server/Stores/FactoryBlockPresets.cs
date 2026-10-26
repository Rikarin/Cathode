using Cathode.Contracts;

namespace Cathode.Server.Stores;

/// <summary>
///     The factory Block Library (docs/08-blocks-parity.md §8, item 7): for every block whose guide "types" are mostly
///     settings of one algorithm, a curated set of starting points with the parameters that make the difference.
///     Values are Cathode's own, not copied from any manual; the rest of each block's parameters stay at their
///     defaults. A test checks every id and value against the block catalogue.
/// </summary>
public static class FactoryBlockPresets {
    public static IReadOnlyList<BlockPreset> All { get; } = [
        // Chorus: one algorithm (1-4 voices); the classic flavours are rates, depths, delays, voices and tone.
        P("chorus", "Digital Mono", ("rate", 0.6f), ("depth", 40), ("delay", 8), ("spread", 0)),
        P("chorus", "Digital Stereo", ("rate", 0.6f), ("depth", 40), ("delay", 8), ("spread", 90)),
        P(
            "chorus",
            "Analog Mono",
            ("rate", 0.8f),
            ("depth", 50),
            ("delay", 10),
            ("spread", 0),
            ("lfoType", 1),
            ("highCut", 6_000)
        ),
        P(
            "chorus",
            "Analog Stereo",
            ("rate", 0.8f),
            ("depth", 50),
            ("delay", 10),
            ("spread", 90),
            ("lfoType", 1),
            ("highCut", 6_000)
        ),
        P(
            "chorus",
            "Compact Pedal",
            ("rate", 1.1f),
            ("depth", 60),
            ("delay", 7),
            ("spread", 0),
            ("lfoType", 1),
            ("highCut", 5_000)
        ),
        P(
            "chorus",
            "Warm Stereo",
            ("rate", 0.5f),
            ("depth", 45),
            ("delay", 12),
            ("mix", 40),
            ("spread", 120),
            ("lowCut", 100),
            ("highCut", 4_500)
        ),
        P(
            "chorus",
            "Eighties Wide",
            ("rate", 0.9f),
            ("depth", 55),
            ("delay", 6),
            ("spread", 180),
            ("voices", 1),
            ("stereoSpread", 150)
        ),
        P("chorus", "Triangle", ("rate", 0.7f), ("depth", 50), ("delay", 8), ("lfoType", 1)),
        P("chorus", "Tri-Chorus", ("rate", 0.5f), ("depth", 40), ("delay", 12), ("spread", 120), ("voices", 2)),
        P("chorus", "Four-Voice Ensemble", ("rate", 0.4f), ("depth", 35), ("delay", 14), ("voices", 3)),
        P("chorus", "Vintage Tape", ("rate", 0.3f), ("depth", 25), ("delay", 15), ("mix", 40), ("highCut", 7_000)),
        P("chorus", "Lush Wash", ("rate", 0.35f), ("depth", 60), ("delay", 18), ("voices", 3), ("stereoSpread", 150)),

        // Flanger.
        P("flanger", "Digital Mono", ("rate", 0.3f), ("depth", 70), ("delay", 1), ("feedback", 50), ("spread", 0)),
        P("flanger", "Digital Stereo", ("rate", 0.3f), ("depth", 70), ("delay", 1), ("feedback", 50), ("spread", 90)),
        P(
            "flanger",
            "Analog Mono",
            ("rate", 0.3f),
            ("depth", 70),
            ("delay", 1.5f),
            ("feedback", 60),
            ("spread", 0),
            ("highCut", 7_000)
        ),
        P(
            "flanger",
            "Analog Stereo",
            ("rate", 0.3f),
            ("depth", 70),
            ("delay", 1.5f),
            ("feedback", 60),
            ("highCut", 7_000)
        ),
        P("flanger", "Jet", ("rate", 0.1f), ("depth", 90), ("delay", 0.5f), ("feedback", 80)),
        P("flanger", "Hollow (Negative)", ("rate", 0.25f), ("depth", 70), ("delay", 1), ("feedback", -70)),
        P("flanger", "Slow Sweep", ("rate", 0.05f), ("depth", 100), ("delay", 2), ("feedback", 40), ("lfoReset", 1)),
        P("flanger", "Mild Chorus", ("rate", 0.8f), ("depth", 30), ("delay", 4), ("feedback", 10), ("mix", 40)),
        P("flanger", "Metallic", ("rate", 0.2f), ("depth", 50), ("delay", 3), ("feedback", 90)),
        P("flanger", "Eighth-Note Sweep", ("tempo", 6), ("depth", 80), ("delay", 1), ("feedback", 60), ("lfoReset", 1)),

        // Phaser.
        P("phaser", "Four-Stage Pedal", ("stages", 1), ("rate", 0.5f), ("depth", 80), ("center", 700)),
        P(
            "phaser",
            "Four-Stage Feedback",
            ("stages", 1),
            ("rate", 0.5f),
            ("depth", 80),
            ("center", 700),
            ("feedback", 50)
        ),
        P("phaser", "Two-Stage Subtle", ("stages", 0), ("rate", 0.4f), ("depth", 60), ("center", 900)),
        P("phaser", "Six-Stage", ("stages", 2), ("rate", 0.6f), ("depth", 80), ("feedback", 30)),
        P(
            "phaser",
            "Eight-Stage Stereo",
            ("stages", 3),
            ("rate", 0.4f),
            ("depth", 90),
            ("feedback", 40),
            ("spread", 90)
        ),
        P(
            "phaser",
            "Twelve-Stage Deep",
            ("stages", 5),
            ("rate", 0.3f),
            ("depth", 100),
            ("center", 1_000),
            ("feedback", 60)
        ),
        P(
            "phaser",
            "Slow Swirl",
            ("stages", 3),
            ("rate", 0.1f),
            ("depth", 100),
            ("feedback", 70),
            ("spread", 180),
            ("lfoReset", 1)
        ),
        P("phaser", "Fast Warble", ("stages", 1), ("rate", 4), ("depth", 40), ("mix", 40)),

        // Delay.
        P("delay", "Digital Mono", ("type", 0), ("time", 375), ("feedback", 30), ("spread", 0)),
        P("delay", "Digital Stereo", ("type", 0), ("time", 375), ("feedback", 30), ("ratio", 75)),
        P(
            "delay",
            "Analog Mono",
            ("type", 1),
            ("feedback", 40),
            ("highcut", 3_500),
            ("lfo1Rate", 0.4f),
            ("lfo1Depth", 0.5f),
            ("spread", 0)
        ),
        P(
            "delay",
            "Analog Stereo",
            ("type", 1),
            ("feedback", 40),
            ("highcut", 3_500),
            ("lfo1Rate", 0.4f),
            ("lfo1Depth", 0.5f),
            ("ratio", 80)
        ),
        P(
            "delay",
            "Tape Echo",
            ("type", 2),
            ("time", 330),
            ("feedback", 35),
            ("lowcut", 120),
            ("highcut", 4_000),
            ("lfo1Rate", 0.6f),
            ("lfo1Depth", 1),
            ("drive", 3)
        ),
        P("delay", "Ping-Pong", ("type", 3), ("time", 375), ("feedback", 40), ("mix", 30)),
        P("delay", "Vintage Digital", ("type", 4), ("bitReduction", 8), ("highcut", 8_000)),
        P("delay", "Slapback", ("type", 1), ("time", 110), ("feedback", 5), ("mix", 30), ("highcut", 5_000)),
        P("delay", "Dotted Eighth", ("type", 0), ("tempo", 7), ("feedback", 35), ("mix", 30)),
        P("delay", "Dual", ("type", 5), ("time", 375), ("feedback", 30)),
        P("delay", "Mono Tape", ("type", 6), ("time", 330), ("feedback", 35), ("highcut", 4_000), ("drive", 3)),
        P("delay", "Reverse", ("type", 7), ("time", 500), ("feedback", 30), ("mix", 40)),
        P(
            "delay",
            "Sweep Echo",
            ("type", 8),
            ("sweepRate", 0.3f),
            ("sweepStart", 300),
            ("sweepStop", 3_000),
            ("sweepResonance", 4)
        ),
        P("delay", "Ducking Echo", ("type", 0), ("feedback", 35), ("mix", 35), ("duckAttenuation", 15)),
        P("delay", "Lo-Fi", ("type", 4), ("bitReduction", 12), ("lowcut", 300), ("highcut", 3_000), ("drive", 5)),
        P(
            "delay",
            "Ambient Long",
            ("type", 0),
            ("time", 800),
            ("feedback", 60),
            ("mix", 30),
            ("highcut", 4_000),
            ("lfo1Depth", 1.5f)
        ),

        // Multi-Delay.
        P("multidelay", "Quad Rhythm", ("type", 0), ("time", 500)),
        P(
            "multidelay",
            "Quad Dotted",
            ("type", 0),
            ("time", 600),
            ("tap1Time", 25),
            ("tap2Time", 37.5f),
            ("tap3Time", 75),
            ("tap4Time", 100)
        ),
        P("multidelay", "Quad Parallel", ("type", 1), ("time", 600), ("tap4Feedback", 30)),
        P("multidelay", "Quad Series Cascade", ("type", 2), ("time", 500)),
        P("multidelay", "Quad Diffusor", ("type", 3), ("time", 700), ("diffusionMix", 60), ("mix", 30)),
        P("multidelay", "Quad Tape", ("type", 4), ("time", 450), ("highCut", 4_500), ("drive", 3)),

        // Ten-Tap Delay.
        P("tentap", "Ten-Tap Even", ("type", 0), ("time", 800)),
        P("tentap", "Ten-Tap Diffuse", ("type", 0), ("time", 800), ("shuffle", 20), ("decay", 70)),
        P("tentap", "Ten-Tap Shuffle", ("type", 0), ("time", 600), ("shuffle", 50), ("decay", 30)),
        P("tentap", "Rhythm Tap", ("type", 1), ("time", 1_000), ("feedback", 20)),

        // Plex Delay.
        P("plex", "Plex Delay", ("type", 0), ("decay", 3), ("diffusion", 50)),
        P("plex", "Plex Detune", ("type", 1), ("decay", 3), ("mix", 35)),
        P("plex", "Plex Shifter", ("type", 2), ("decay", 4), ("mix", 30)),
        P("plex", "Plex Verb", ("type", 3), ("decay", 4), ("diffusion", 80), ("inputDiffusion", 50)),
        P("plex", "Shimmer Verb", ("type", 4), ("decay", 8), ("mix", 35), ("lowCut", 200)),
        P("plex", "Ducked Plex", ("type", 0), ("decay", 3), ("duckAttenuation", 15), ("mix", 35)),

        // Megatap.
        P("megatap", "Fade-Out Taps", ("levelShape", 1), ("panShape", 1)),
        P("megatap", "Fade-In Taps", ("levelShape", 2), ("panShape", 1)),
        P("megatap", "Swell", ("levelShape", 3), ("taps", 24), ("time", 1_500)),
        P("megatap", "Bouncing Ball", ("timeAlpha", -70), ("levelShape", 4), ("taps", 12), ("panShape", 0)),
        P("megatap", "Accelerando", ("timeAlpha", 70), ("levelShape", 2), ("taps", 12)),
        P("megatap", "Left to Right", ("panShape", 2), ("taps", 16)),
        P("megatap", "Rain", ("taps", 40), ("time", 2_000), ("levelShape", 0), ("highCut", 6_000), ("mix", 30)),

        // Reverb.
        P("reverb", "Small Room", ("type", 0), ("decay", 0.6f), ("size", 25), ("predelay", 2), ("damping", 7_000)),
        P("reverb", "Medium Room", ("type", 0), ("decay", 1), ("size", 50), ("predelay", 5)),
        P("reverb", "Large Room", ("type", 0), ("decay", 1.6f), ("size", 80), ("predelay", 10)),
        P("reverb", "Studio", ("type", 0), ("decay", 0.8f), ("size", 40), ("damping", 9_000), ("lowcut", 120)),
        P("reverb", "Chamber", ("type", 5), ("decay", 1.6f), ("size", 35), ("predelay", 8)),
        P("reverb", "Small Hall", ("type", 1), ("decay", 1.8f), ("size", 50), ("predelay", 15)),
        P("reverb", "Large Hall", ("type", 1), ("decay", 3.2f), ("size", 85), ("predelay", 25)),
        P(
            "reverb",
            "Cathedral",
            ("type", 6),
            ("decay", 7),
            ("size", 100),
            ("predelay", 40),
            ("damping", 4_500),
            ("mix", 30)
        ),
        P("reverb", "Plate", ("type", 2), ("decay", 2), ("predelay", 10), ("damping", 10_000)),
        P("reverb", "Bright Plate", ("type", 3), ("decay", 2.5f), ("damping", 12_000)),
        P("reverb", "Vintage Spring", ("type", 4), ("decay", 2), ("damping", 5_000), ("mix", 25)),
        P("reverb", "Long Spring", ("type", 4), ("decay", 3.5f), ("size", 80), ("mix", 25)),
        // The classic digital types (docs/08 §6.26): settings of the ring (7, 8), the combs (9) and the nested room (10).
        P(
            "reverb",
            "Classic Concert Hall",
            ("type", 7),
            ("decay", 2.8f),
            ("size", 70),
            ("predelay", 20),
            ("damping", 7_000),
            ("lowTime", 120)
        ),
        P(
            "reverb",
            "Classic Large Hall",
            ("type", 7),
            ("decay", 4.5f),
            ("size", 95),
            ("predelay", 30),
            ("damping", 5_500),
            ("lowTime", 130)
        ),
        P(
            "reverb",
            "Random Hall",
            ("type", 7),
            ("decay", 2.5f),
            ("size", 60),
            ("predelay", 15),
            ("modDepth", 200),
            ("modRate", 0.5f)
        ),
        P("reverb", "Rich Plate", ("type", 8), ("decay", 2.2f), ("size", 50), ("predelay", 8), ("damping", 9_000)),
        P("reverb", "Small Plate", ("type", 8), ("decay", 1.2f), ("size", 25), ("damping", 11_000)),
        P(
            "reverb",
            "Vocal Plate",
            ("type", 8),
            ("decay", 1.8f),
            ("size", 40),
            ("predelay", 25),
            ("damping", 8_000),
            ("lowcut", 180)
        ),
        P("reverb", "Early Digital Hall", ("type", 9), ("decay", 2.5f), ("size", 70), ("damping", 6_000)),
        P("reverb", "Early Digital Room", ("type", 9), ("decay", 0.9f), ("size", 25), ("damping", 5_000)),
        P(
            "reverb",
            "Ambience",
            ("type", 10),
            ("decay", 0.5f),
            ("size", 40),
            ("damping", 8_000),
            ("earlyLevel", 50),
            ("mix", 25)
        ),
        P(
            "reverb",
            "Small Ambience",
            ("type", 10),
            ("decay", 0.3f),
            ("size", 10),
            ("damping", 9_000),
            ("earlyLevel", 60),
            ("mix", 25)
        ),
        P(
            "reverb",
            "Recording Studio",
            ("type", 10),
            ("decay", 0.7f),
            ("size", 55),
            ("damping", 7_000),
            ("lowcut", 120)
        ),
        P("reverb", "Bright Room", ("type", 10), ("decay", 1), ("size", 70), ("damping", 12_000)),
        P(
            "reverb",
            "Ambient Wash",
            ("type", 1),
            ("decay", 10),
            ("size", 100),
            ("predelay", 60),
            ("damping", 3_500),
            ("lowcut", 200),
            ("mix", 35)
        ),

        // Compressor.
        P(
            "compressor",
            "Pedal Squash",
            ("type", 0),
            ("threshold", -35),
            ("ratio", 8),
            ("attack", 2),
            ("release", 200),
            ("knee", 3),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Pedal Light",
            ("type", 0),
            ("threshold", -20),
            ("ratio", 3),
            ("attack", 10),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Studio",
            ("type", 1),
            ("threshold", -18),
            ("ratio", 4),
            ("attack", 5),
            ("release", 120),
            ("autoMakeup", 1)
        ),
        P("compressor", "Studio Sustainer", ("type", 3), ("threshold", -40), ("ratio", 4), ("release", 300)),
        P(
            "compressor",
            "JFET Compressor",
            ("type", 6),
            ("threshold", -24),
            ("ratio", 8),
            ("attack", 0.5f),
            ("release", 100),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Tube Compressor",
            ("type", 7),
            ("threshold", -24),
            ("ratio", 3),
            ("attack", 20),
            ("release", 400),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Compander",
            ("type", 8),
            ("threshold", -24),
            ("ratio", 4),
            ("expThreshold", -55),
            ("expRatio", 2),
            ("autoMakeup", 1)
        ),
        P("compressor", "Pedal 1", ("type", 0), ("controls", 1), ("compression", 60), ("attack", 5), ("autoMakeup", 1)),
        P(
            "compressor",
            "Optical",
            ("type", 2),
            ("threshold", -20),
            ("ratio", 4),
            ("attack", 10),
            ("release", 300),
            ("knee", 12),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Optical Sustainer",
            ("type", 2),
            ("threshold", -35),
            ("ratio", 10),
            ("attack", 10),
            ("release", 500),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Parallel",
            ("type", 1),
            ("threshold", -30),
            ("ratio", 10),
            ("attack", 1),
            ("release", 100),
            ("mix", 50),
            ("autoMakeup", 1)
        ),
        P(
            "compressor",
            "Limiter",
            ("type", 0),
            ("threshold", -6),
            ("ratio", 20),
            ("attack", 0.1f),
            ("release", 80),
            ("knee", 0),
            ("lookAhead", 3)
        ),
        P(
            "compressor",
            "Bass",
            ("type", 1),
            ("threshold", -25),
            ("ratio", 5),
            ("attack", 15),
            ("release", 200),
            ("autoMakeup", 1)
        ),

        // Gate/Expander.
        P("gate", "Classic Expander", ("type", 0), ("threshold", -60), ("ratio", 2)),
        P("gate", "Classic Gate", ("type", 1), ("threshold", -55), ("attack", 0.2f), ("hold", 30), ("release", 80)),
        P("gate", "Smooth Expander", ("type", 0), ("detector", 1), ("threshold", -55), ("ratio", 3), ("release", 200)),
        P(
            "gate",
            "Tight High-Gain Gate",
            ("type", 1),
            ("threshold", -45),
            ("attenuation", 80),
            ("attack", 0.1f),
            ("hold", 10),
            ("release", 30),
            ("scLowCut", 150)
        ),
        P("gate", "Gate on the Input", ("type", 1), ("threshold", -50), ("scSource", 1)),

        // Filter.
        P("filter", "Telephone", ("type", 2), ("freq", 1_000), ("q", 0.7f), ("lowCut", 300), ("highCut", 3_400)),
        P("filter", "Brighter (Tilt)", ("type", 9), ("gain", 4)),
        P("filter", "Darker (Tilt)", ("type", 9), ("gain", -4)),
        P(
            "filter",
            "Auto Low-Pass",
            ("type", 0),
            ("freq", 1_000),
            ("q", 4),
            ("lfoOn", 1),
            ("lfoRate", 0.5f),
            ("modFreq", 4_000)
        ),
        P("filter", "Resonant High-Pass", ("type", 1), ("freq", 200), ("q", 3)),
        P("filter", "Mains Hum Notch", ("type", 3), ("freq", 60), ("q", 10)),
        P("filter", "Comb Tone", ("type", 11), ("combTime", 3), ("depth", 60)),
        P("filter", "Mid Hump", ("type", 4), ("freq", 800), ("q", 1), ("gain", 6)),

        // Graphic EQ.
        P("geq", "Mid Scoop", ("g125", 3), ("g250", 1), ("g500", -3), ("g1k", -4), ("g2k", -2), ("g4k", 2), ("g8k", 3)),
        P("geq", "Mid Push", ("g500", 2), ("g1k", 4), ("g2k", 2)),
        P("geq", "Tighten Lows", ("g31", -12), ("g63", -8), ("g125", -3)),
        P("geq", "Presence Lift", ("g4k", 3), ("g8k", 4), ("g16k", 2)),
        P("geq", "Warm", ("g125", 2), ("g250", 2), ("g4k", -2), ("g8k", -4), ("g16k", -6)),

        // Wah.
        P("wah", "Classic Pedal", ("type", 0)),
        P("wah", "British", ("type", 1)),
        P("wah", "Wide Sweep", ("type", 2)),
        P("wah", "Vocal", ("type", 3), ("resonance", 2), ("qTracking", 50)),
        P("wah", "Funk Quack", ("type", 4), ("resonance", 2.5f), ("lowCut", 150), ("taper", 1)),
        P("wah", "Fixed Mid Boost", ("type", 0), ("position", 60), ("resonance", 1.5f)),

        // Pitch.
        P(
            "pitch",
            "Detune Thickener",
            ("type", 0),
            ("detune1", 8),
            ("detune2", -8),
            ("voice1Delay", 12),
            ("voice2Delay", 18),
            ("mix", 40)
        ),
        P("pitch", "Octave Up and Down", ("type", 1), ("voice1Shift", 12), ("voice2Shift", -12), ("mix", 40)),
        P("pitch", "Fifth Up", ("type", 1), ("voice1Shift", 7), ("voice2Level", -80), ("voice1Pan", 0), ("mix", 40)),
        P(
            "pitch",
            "Harmony Thirds",
            ("type", 2),
            ("voice1Harmony", 16),
            ("voice2Level", -80),
            ("voice1Pan", 0),
            ("mix", 50)
        ),
        P("pitch", "Harmony Thirds and Fifths", ("type", 2), ("voice1Harmony", 16), ("voice2Harmony", 18), ("mix", 50)),
        P("pitch", "Whammy Octave", ("type", 3), ("whammyStart", 0), ("whammyEnd", 12), ("mix", 100)),
        P("pitch", "Dive Bomb", ("type", 3), ("whammyStart", 0), ("whammyEnd", -24), ("mix", 100)),
        P("pitch", "Octave Down", ("type", 4), ("octave1Level", 100), ("octave2Level", 0), ("mix", 50)),
        P(
            "pitch",
            "Crystals",
            ("type", 5),
            ("crystalsShift", 12),
            ("crystalsTime", 300),
            ("crystalsFeedback", 40),
            ("mix", 30)
        ),
        P("pitch", "Whole Step Down", ("type", 7), ("capo", 10), ("mix", 100)),

        // Tremolo/Panner.
        P("tremolo", "Vintage Tremolo", ("type", 0), ("rate", 5), ("depth", 50)),
        P("tremolo", "Square Chop", ("type", 0), ("rate", 6), ("depth", 100), ("shape", 2), ("startPhase", 90)),
        P("tremolo", "Bias Pulse", ("type", 2), ("rate", 4), ("depth", 60)),
        P("tremolo", "Harmonic Vibrato", ("type", 3), ("rate", 3), ("depth", 70)),
        P("tremolo", "Optical Throb", ("type", 4), ("rate", 4.5f), ("depth", 70)),
        P("tremolo", "Slow Auto-Pan", ("type", 1), ("rate", 0.3f), ("depth", 100)),

        // Ring Modulator.
        P("ringmod", "Bell", ("frequency", 1_000), ("mix", 50), ("highCut", 8_000)),
        P("ringmod", "Robot", ("frequency", 150)),
        P("ringmod", "Sub-Audio Tremolo", ("frequency", 12)),
        P("ringmod", "Clang", ("frequency", 2_500), ("mix", 60)),

        // Dynamic Distortion.
        P("dyndist", "Touch-Sensitive Soft", ("type", 0), ("drive", 5), ("dynamics", 70)),
        P("dyndist", "Hard Clip Bloom", ("type", 1), ("drive", 6), ("dynamics", 50)),
        P("dyndist", "Asymmetric Crunch", ("type", 2), ("drive", 4), ("dynamics", 30)),
        P("dyndist", "Inverse (Softer Picks Dirtier)", ("type", 0), ("drive", 5), ("dynamics", -60)),
        // The guide's types are settings of the frequency-shaped filter ahead of the clipper (paraphrased, not named).
        P(
            "dyndist",
            "Treble Edge",
            ("type", 0),
            ("drive", 4),
            ("filterType", 2),
            ("filterFreq", 2_500),
            ("filterGain", 12)
        ),
        P(
            "dyndist",
            "Bass Bloom",
            ("type", 5),
            ("drive", 4),
            ("filterType", 1),
            ("filterFreq", 200),
            ("filterGain", 12)
        ),
        P(
            "dyndist",
            "Mid Honk",
            ("type", 7),
            ("drive", 5),
            ("filterType", 3),
            ("filterFreq", 900),
            ("filterQ", 1.5f),
            ("filterGain", 15)
        ),
        P("dyndist", "Fuzzy Octave", ("type", 8), ("drive", 7), ("dynamics", 40), ("bias", 20)),
        P(
            "dyndist",
            "Hot Plate",
            ("type", 11),
            ("drive", 6),
            ("filterType", 2),
            ("filterFreq", 1_500),
            ("filterGain", 6)
        ),
        P(
            "dyndist",
            "Silicon Bite",
            ("type", 12),
            ("drive", 6),
            ("filterType", 3),
            ("filterFreq", 1_800),
            ("filterGain", 9)
        ),
        P(
            "dyndist",
            "Screamer Push",
            ("type", 13),
            ("drive", 5),
            ("filterType", 3),
            ("filterFreq", 720),
            ("filterGain", 12)
        ),

        // Drive: the circuits are types already; these are settings of them.
        P("drive", "Clean Boost", ("type", 22), ("drive", 2), ("tone", 5), ("level", 6)),
        P("drive", "Classic Screamer", ("type", 3), ("drive", 4), ("tone", 6)),
        P("drive", "Tight Boost for High Gain", ("type", 3), ("drive", 0), ("tone", 7), ("level", 6)),
        P("drive", "Transparent Push", ("type", 9), ("drive", 3), ("tone", 5)),
        P("drive", "Rodent Distortion", ("type", 13), ("drive", 7), ("tone", 4)),
        P("drive", "Vintage Fuzz", ("type", 18), ("drive", 8), ("tone", 5)),
        P("drive", "Treble Booster", ("type", 21), ("drive", 7), ("tone", 5))
    ];

    static BlockPreset P(string type, string name, params (string Id, float Value)[] values) =>
        new() {
            Id = $"factory-{type}-{Slug(name)}",
            Type = type,
            Name = name,
            Factory = true,
            Values = values.ToDictionary(static v => v.Id, static v => v.Value)
        };

    static string Slug(string name) {
        var slug = string.Concat(name.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
        while (slug.Contains("--", StringComparison.Ordinal)) {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
