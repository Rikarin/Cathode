using Cathode.Dsp.Modulation;
using Cathode.Dsp.Parameters;
using System.Collections.Immutable;

namespace Cathode.Engine.Rig;

/// <summary>
///     The rig-wide parameters (IPC block id <c>rig</c>): scene level, tempo, metronome, the modifier sources a person
///     or a pedal moves (Manual A-E, scene controllers 1-4, external 1-16) and the settings of the controllers
///     (LFOs, ADSRs, envelope follower, sequencer). They live with the engine, not the graph, so changing one never
///     recompiles and controllers keep their phase across preset changes.
/// </summary>
public static class RigControls {
    public const string BlockId = "rig";
    public const int SequencerStages = 32;

    /// <summary>Manual A-E.</summary>
    public const int ManualCount = 5;

    /// <summary>Scene Controller 1-4.</summary>
    public const int SceneControllerCount = 4;

    /// <summary>External 1-16.</summary>
    public const int ExternalCount = 16;

    // Values of the Output Mode choices.
    public const int Stereo = 0;
    public const int SumLeftRight = 1;
    public const int CopyLeftToRight = 2;

    // Values of the Tuner Output choice.
    public const int TunerMute = 0;
    public const int TunerBypass = 1;
    public const int TunerPassThrough = 2;

    public static readonly ImmutableArray<string> LfoShapes = Dsp.Modulation.Lfo.ShapeNames;

    /// <summary>Output Mode choices (<c>out1Mode</c>..<c>out4Mode</c>).</summary>
    public static readonly ImmutableArray<string> OutputModes = ["Stereo", "Sum L+R", "Copy L>R"];

    /// <summary>
    ///     Tuner Output choices (<c>tunerOut</c>): what every output plays while the tuner is on. Mute fades them out,
    ///     Bypass plays the input dry (no blocks), Pass Through keeps the preset sounding.
    /// </summary>
    public static readonly ImmutableArray<string> TunerOutputs = ["Mute", "Bypass", "Pass Through"];

    /// <summary>Tempo divisions for synced controllers; "Off" uses the rate in Hz.</summary>
    public static readonly ImmutableArray<string> Divisions = TempoDivisions.Names;

    /// <summary>Length of each <see cref="Divisions" /> entry in quarter notes (index 0 unused).</summary>
    public static ReadOnlySpan<double> DivisionBeats => TempoDivisions.Beats;

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
            ..Names("manual", Enumerable.Range(0, ManualCount).Select(static i => ((char)('A' + i)).ToString()), "Manual "),
            ..Names("scene", Numbers(SceneControllerCount), "Scene Controller "),
            ..Names("ext", Numbers(ExternalCount), "External "),
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
                .Select(static i => ParamDescriptor.Float($"seq{i}", $"Stage {i}", 0f, 1f, i % 2 == 0 ? 0f : 1f)),
            // Global: Cab and IR Player blocks pass audio through (BlockContext.CabsOff), for a real cab.
            ParamDescriptor.Switch("cabsOff", "Cab Modeling Off"),
            // Live only (never saved): silences every output, after the metronome and the looper.
            ParamDescriptor.Switch("mute", "Mute"),
            // Global: what the outputs play while the tuner is on (TunerOutputs).
            ParamDescriptor.Choice("tunerOut", "Tuner Output", TunerOutputs),
            // Global: the tuner's A4 reference, read by the host's pitch detector.
            new("tunerRef", "Tuner Reference", ParamKind.Float, 430f, 450f, 440f, "Hz", Smoothing: ParamSmoothing.Step),
            // Live only, set by the host while its tuner runs: applies Tuner Output.
            ParamDescriptor.Switch("tuner", "Tuner")
        ],
        maxInstances: 1,
        channels: 1
    );

    static IEnumerable<ParamDescriptor> Output(int k) => [
        ParamDescriptor.Choice($"out{k}Mode", $"Output {k} Mode", OutputModes),
        ParamDescriptor.Float($"out{k}Level", $"Output {k} Boost/Pad", -18f, 18f, 0f, "dB")
    ];

    static IEnumerable<string> Numbers(int count) =>
        Enumerable.Range(1, count).Select(static i => i.ToString(System.Globalization.CultureInfo.InvariantCulture));

    static IEnumerable<ParamDescriptor> Names(string prefix, IEnumerable<string> suffixes, string name) =>
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
    ManualE,
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
    Ext8,
    Ext9,
    Ext10,
    Ext11,
    Ext12,
    Ext13,
    Ext14,
    Ext15,
    Ext16
}

/// <summary>The shape a source value (0..1) is bent through before it is scaled into the parameter's range.</summary>
public enum ModCurve {
    Linear,
    Exponential,
    Logarithmic,
    SCurve
}

/// <summary>
///     A modifier's response to its source (0..1 in, 0..1 out), applied before the <see cref="ModCurve" />. Slope bends
///     the source around its centre (0.5 is straight; above it the response is steeper through the middle and flatter
///     at the ends, below it the other way round). The bent source then runs along two straight lines, from
///     <see cref="Start" /> at 0 through <see cref="Mid" /> at 0.5 to <see cref="End" /> at 1. That output is
///     multiplied by <see cref="Scale" />, <see cref="Offset" /> is added, and the result is held within 0..1. So
///     Start 1, End 0 inverts a pedal, Scale 2 reaches the top at half travel, and Offset lifts the floor.
/// </summary>
public readonly record struct ModShape(float Start, float Mid, float End, float Slope, float Scale, float Offset) {
    public const float MaxScale = 10f;

    /// <summary>The response that changes nothing: what a modifier without these settings has.</summary>
    public static ModShape Identity { get; } = new(0f, 0.5f, 1f, 0.5f, 1f, 0f);

    public bool IsIdentity => this == Identity;

    /// <summary>The settings a modifier stores (null = default), each held within its range.</summary>
    public static ModShape From(float? start, float? mid, float? end, float? slope, float? scale, float? offset) =>
        new(
            Unit(start, 0f),
            Unit(mid, 0.5f),
            Unit(end, 1f),
            Unit(slope, 0.5f),
            Finite(scale, 1f, 0f, MaxScale),
            Finite(offset, 0f, -1f, 1f)
        );

    /// <summary>The response to <paramref name="source" />, 0..1.</summary>
    public float Apply(float source) {
        var x = Math.Clamp(source, 0f, 1f);

        // Slope: a sigmoid through (0, 0), (0.5, 0.5) and (1, 1), a straight line at 0.5.
        var k = (0.5f - Slope) * 1.8f;
        var u = 2f * x - 1f;
        x = 0.5f + 0.5f * (u - k * u) / (k - 2f * k * Math.Abs(u) + 1f);

        var y = x < 0.5f ? Start + (Mid - Start) * 2f * x : Mid + (End - Mid) * (2f * x - 1f);
        return Math.Clamp(y * Scale + Offset, 0f, 1f);
    }

    static float Unit(float? value, float fallback) => Finite(value, fallback, 0f, 1f);

    static float Finite(float? value, float fallback, float min, float max) =>
        value is { } v && float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}

/// <summary>What engages a block from its engage modifier (<see cref="ModNames.EngageParam" />).</summary>
public enum EngageMode {
    /// <summary>The modified value: engaged at or above the threshold, bypassed below it.</summary>
    Position,

    /// <summary>
    ///     Movement: engaged as soon as the value moves away from where it rested, bypassed once it is back below the
    ///     threshold.
    /// </summary>
    Moving
}

public static class ModNames {
    /// <summary>
    ///     The pseudo-parameter a modifier can drive to engage and bypass a block (docs/08-blocks-parity.md §3): by
    ///     default engaged while the modified value (0..1) is at least 0.5, bypassed below; its mode, threshold and
    ///     off delay change that (Cathode.Engine.Graph.EngageSwitch). While one drives a block, it wins over the
    ///     scene's bypass state.
    /// </summary>
    public const string EngageParam = "engage";

    public static readonly ImmutableArray<string> Sources = [
        "lfo1", "lfo2", "adsr1", "adsr2", "seq", "env", "pitch", "manualA", "manualB", "manualC", "manualD", "manualE",
        "scene1", "scene2", "scene3", "scene4", "ext1", "ext2", "ext3", "ext4", "ext5", "ext6", "ext7", "ext8", "ext9",
        "ext10", "ext11", "ext12", "ext13", "ext14", "ext15", "ext16"
    ];

    public static readonly ImmutableArray<string> Curves = ["linear", "exp", "log", "scurve"];

    /// <summary>
    ///     How a modifier's second source joins its first (<see cref="Modulation.Combine" />); the first entry is what
    ///     a modifier that names none uses.
    /// </summary>
    public static readonly ImmutableArray<string> Operations = ["add", "multiply"];

    /// <summary>The <see cref="EngageMode" /> names, in order.</summary>
    public static readonly ImmutableArray<string> EngageModes = ["position", "moving"];

    public static bool TryParseSource(string name, out ModSource source) {
        var index = Sources.IndexOf(name);
        source = (ModSource)Math.Max(index, 0);
        return index >= 0;
    }

    public static bool TryParseCurve(string name, out ModCurve curve) {
        var index = Curves.IndexOf(name);
        curve = (ModCurve)Math.Max(index, 0);
        return index >= 0;
    }

    /// <summary>An operation by name; none (null) is <c>add</c>.</summary>
    public static bool TryParseOperation(string? name, out bool multiply) {
        var index = name is null ? 0 : Operations.IndexOf(name);
        multiply = index == 1;
        return index >= 0;
    }

    /// <summary>An engage mode by name; none (null) is <see cref="EngageMode.Position" />.</summary>
    public static bool TryParseEngageMode(string? name, out EngageMode mode) {
        var index = name is null ? 0 : EngageModes.IndexOf(name);
        mode = (EngageMode)Math.Max(index, 0);
        return index >= 0;
    }
}
