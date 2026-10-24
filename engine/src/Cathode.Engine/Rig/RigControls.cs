using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;

namespace Cathode.Engine.Rig;

/// <summary>
///     The rig-wide parameters (IPC block id <c>rig</c>): scene level, tempo, metronome, the modifier sources a person
///     or a pedal moves (Manual A-D, scene controllers 1-4, external 1-8) and the settings of the controllers
///     (LFOs, ADSRs, envelope follower, sequencer). They live with the engine, not the graph, so changing one never
///     recompiles and controllers keep their phase across preset changes.
/// </summary>
public static class RigControls {
    public const string BlockId = "rig";
    public const int SequencerStages = 16;

    // Values of the Output Mode choices.
    public const int Stereo = 0;
    public const int SumLeftRight = 1;
    public const int CopyLeftToRight = 2;
    public const int DualMono = 3;

    public static readonly string[] LfoShapes = Dsp.Modulation.Lfo.ShapeNames;

    /// <summary>Output Mode choices (<c>out1Mode</c>..<c>out4Mode</c>).</summary>
    public static readonly string[] OutputModes = ["Stereo", "Sum L+R", "Copy L>R", "Dual Mono"];

    /// <summary>Tempo divisions for synced controllers; "Off" uses the rate in Hz.</summary>
    public static readonly string[] Divisions = TempoDivisions.Names;

    /// <summary>Length of each <see cref="Divisions" /> entry in quarter notes (index 0 unused).</summary>
    public static readonly double[] DivisionBeats = TempoDivisions.Beats;

    public static BlockDescriptor Descriptor { get; } = new(
        BlockId,
        "Rig",
        "System",
        [
            ParamDescriptor.Float("level", "Scene Level", -60f, 12f, 0f, "dB"),
            new("tempo", "Tempo", ParamKind.Float, 30f, 300f, 120f, "BPM", Smoothing: ParamSmoothing.Step),
            ParamDescriptor.Switch("metronome", "Metronome"),
            ParamDescriptor.Float("metroLevel", "Metronome Level", -60f, 0f, -12f, "dB"),
            // Global settings the blocks read through BlockContext: Global Mix and the Input gate's offset.
            ParamDescriptor.Float("effectsMix", "Effects Mix", -50f, 50f, 0f, "%"),
            ParamDescriptor.Float("gateOffset", "Noise Gate Offset", -20f, 20f, 0f, "dB"),
            // Audio I/O (docs/08-blocks-parity.md §2): Input 1's trim and calibration, and the outputs' settings.
            ParamDescriptor.Float("in1Gain", "Input 1 Gain", -20f, 20f, 0f, "dB"),
            ParamDescriptor.Float("in1Cal", "Input 1 Full Scale", -30f, 40f, 0f, "dBV"),
            ParamDescriptor.Switch("copyOut2", "Copy Output 1 to Output 2"),
            ..Enumerable.Range(1, 4).SelectMany(static k => Output(k)),
            // Preset-change spillover: on, the outgoing preset's tails ring out; off, it fades out in the swap fade.
            ParamDescriptor.Switch("spillover", "Preset Spillover", true),
            ..Names("manual", ["A", "B", "C", "D"], "Manual "),
            ..Names("scene", ["1", "2", "3", "4"], "Scene Controller "),
            ..Names("ext", ["1", "2", "3", "4", "5", "6", "7", "8"], "External "),
            // Written by the host's pitch detector (normalised between pitchLow and pitchHigh), not by people.
            ParamDescriptor.Float("pitch", "Pitch", 0f, 1f, 0f),
            ParamDescriptor.Float("pitchLow", "Pitch Low", 40f, 2_000f, 82f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("pitchHigh", "Pitch High", 80f, 4_000f, 1_320f, "Hz", ParamTaper.Log),
            ..Lfo(1),
            ..Lfo(2),
            ..Adsr(1),
            ..Adsr(2),
            ParamDescriptor.Float("envAttack", "Envelope Attack", 0.1f, 500f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("envRelease", "Envelope Release", 1f, 5_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("envGain", "Envelope Sensitivity", 0f, 40f, 12f, "dB"),
            new(
                "seqStages",
                "Sequencer Stages",
                ParamKind.Float,
                2f,
                SequencerStages,
                8f,
                Smoothing: ParamSmoothing.Step
            ),
            ParamDescriptor.Float("seqRate", "Sequencer Rate", 0.1f, 20f, 2f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("seqTempo", "Sequencer Tempo", Divisions),
            ..Enumerable.Range(1, SequencerStages)
                .Select(static i => ParamDescriptor.Float($"seq{i}", $"Stage {i}", 0f, 1f, i % 2 == 0 ? 0f : 1f))
        ],
        maxInstances: 1,
        channels: 1
    );

    static IEnumerable<ParamDescriptor> Output(int k) => [
        ParamDescriptor.Choice($"out{k}Mode", $"Output {k} Mode", OutputModes),
        ParamDescriptor.Float($"out{k}Level", $"Output {k} Boost/Pad", -18f, 18f, 0f, "dB")
    ];

    static IEnumerable<ParamDescriptor> Names(string prefix, string[] suffixes, string name) =>
        suffixes.Select(s => ParamDescriptor.Float(prefix + s, name + s, 0f, 1f, 0f));

    static IEnumerable<ParamDescriptor> Lfo(int n) => [
        ParamDescriptor.Float($"lfo{n}Rate", $"LFO {n} Rate", 0.05f, 20f, 1f, "Hz", ParamTaper.Log),
        ParamDescriptor.Choice($"lfo{n}Shape", $"LFO {n} Shape", LfoShapes),
        ParamDescriptor.Choice($"lfo{n}Tempo", $"LFO {n} Tempo", Divisions)
    ];

    static IEnumerable<ParamDescriptor> Adsr(int n) => [
        ParamDescriptor.Float($"adsr{n}Attack", $"ADSR {n} Attack", 1f, 5_000f, 10f, "ms", ParamTaper.Log),
        ParamDescriptor.Float($"adsr{n}Decay", $"ADSR {n} Decay", 1f, 5_000f, 200f, "ms", ParamTaper.Log),
        ParamDescriptor.Float($"adsr{n}Sustain", $"ADSR {n} Sustain", 0f, 1f, 0.5f),
        ParamDescriptor.Float($"adsr{n}Release", $"ADSR {n} Release", 1f, 10_000f, 500f, "ms", ParamTaper.Log),
        ParamDescriptor.Float($"adsr{n}Threshold", $"ADSR {n} Threshold", -80f, 0f, -40f, "dB")
    ];
}

/// <summary>Modifier sources (docs/02-architecture.md §5.4). The names are what presets and the API use.</summary>
public enum ModSource {
    Lfo1,
    Lfo2,
    Adsr1,
    Adsr2,
    Sequencer,
    Envelope,
    Pitch,
    ManualA,
    ManualB,
    ManualC,
    ManualD,
    Scene1,
    Scene2,
    Scene3,
    Scene4,
    Ext1,
    Ext2,
    Ext3,
    Ext4,
    Ext5,
    Ext6,
    Ext7,
    Ext8
}

/// <summary>The shape a source value (0..1) is bent through before it is scaled into the parameter's range.</summary>
public enum ModCurve {
    Linear,
    Exponential,
    Logarithmic,
    SCurve
}

public static class ModNames {
    /// <summary>
    ///     The pseudo-parameter a modifier can drive to engage and bypass a block (docs/08-blocks-parity.md §3): engaged
    ///     while the modified value (0..1) is at least 0.5, bypassed below. While one drives a block, it wins over the
    ///     scene's bypass state.
    /// </summary>
    public const string EngageParam = "engage";

    public static readonly string[] Sources = [
        "lfo1", "lfo2", "adsr1", "adsr2", "seq", "env", "pitch", "manualA", "manualB", "manualC", "manualD", "scene1",
        "scene2", "scene3", "scene4", "ext1", "ext2", "ext3", "ext4", "ext5", "ext6", "ext7", "ext8"
    ];

    public static readonly string[] Curves = ["linear", "exp", "log", "scurve"];

    public static bool TryParseSource(string name, out ModSource source) {
        var index = Array.IndexOf(Sources, name);
        source = (ModSource)Math.Max(index, 0);
        return index >= 0;
    }

    public static bool TryParseCurve(string name, out ModCurve curve) {
        var index = Array.IndexOf(Curves, name);
        curve = (ModCurve)Math.Max(index, 0);
        return index >= 0;
    }
}
