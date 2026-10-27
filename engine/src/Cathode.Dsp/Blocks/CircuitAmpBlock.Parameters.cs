using Cathode.Dsp.Amp;
using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

// The Amp block's parameters: the descriptor, with each model's panel names, and its pages. KeptOnTypeChange comes
// first: Info reads it as it is built, and static fields start in the order they are written.
public sealed partial class CircuitAmpBlock {
    /// <summary>
    ///     The parameters a type change leaves alone: the block's own routing and output. Every other one belongs to a
    ///     model and goes back to its default, so the new model starts as it ships
    ///     (<see cref="BlockDescriptor.TypeResets" />).
    /// </summary>
    /// <summary>The choice that leaves a model's own part (tube, speaker, supply, ...) in place.</summary>
    const string Stock = "Stock";

    static readonly string[] KeptOnTypeChange = ["type", "level", "balance", "inputsel", "oversampling"];

    public static BlockDescriptor Info { get; } = Describe(
        Labelled(
            [
                ParamDescriptor.Choice("type", "Type", [..AmpDesign.All.Select(static a => a.Name)]),
                ParamDescriptor.Float("gain", "Gain", 0f, 10f, 5f),
                ParamDescriptor.Float("bass", "Bass", 0f, 10f, 5f),
                ParamDescriptor.Float("middle", "Middle", 0f, 10f, 5f),
                ParamDescriptor.Float("treble", "Treble", 0f, 10f, 5f),
                ParamDescriptor.Float("presence", "Presence", 0f, 10f, 5f),
                ParamDescriptor.Float("depth", "Depth", 0f, 10f, 0f),
                ParamDescriptor.Float("cut", "Cut", 0f, 10f, 0f),
                ParamDescriptor.Float("master", "Master", 0f, 10f, 5f),
                ParamDescriptor.Float("sag", "Sag", 0f, 10f, 5f),
                ParamDescriptor.Float("bias", "Bias", 0f, 100f, 50f, "%"),
                ParamDescriptor.Float("feedback", "Feedback", 0f, 10f, 5f),
                ParamDescriptor.Switch("bright", "Bright"),
                BlockParams.Level,
                ParamDescriptor.Switch("switch1", "Switch 1"),
                ParamDescriptor.Switch("switch2", "Switch 2"),
                ParamDescriptor.Switch("switch3", "Switch 3"),
                ParamDescriptor.Float("control1", "Control 1", 0f, 10f, 5f),
                ParamDescriptor.Float("control2", "Control 2", 0f, 10f, 5f),
                ParamDescriptor.Float("eq1", "EQ 1", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("eq2", "EQ 2", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("eq3", "EQ 3", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("eq4", "EQ 4", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("eq5", "EQ 5", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("intrim", "Input Trim", -20f, 20f, 0f, "dB"),
                ParamDescriptor.Choice("inputsel", "Input Select", ["Left", "Right", "Left + Right"]),
                ParamDescriptor.Float("lowcut", "Low Cut", 0f, 500f, 0f, "Hz"),
                ParamDescriptor.Choice("pretube", "Preamp Tube", [Stock, "12AX7", "12AY7", "5751", "12AT7", "12AU7"]),
                Expert(Scale("brightcap", "Bright Cap")),
                Expert(ParamDescriptor.Float("prebias", "Preamp Bias", -100f, 100f, 0f, "%")),
                ParamDescriptor.Float("presag", "Preamp Sag", 0f, 200f, 100f, "%"),
                Expert(Scale("stackfreq", "Stack Freq", 50f, 200f)),
                ParamDescriptor.Float("mvtrim", "Master Trim", 10f, 200f, 100f, "%"),
                ParamDescriptor.Switch("poweramp", "Power Amp", true),
                ParamDescriptor.Choice(
                    "powertube",
                    "Power Tube",
                    [Stock, "6L6GC", "EL34", "EL84", "6V6GT", "KT66", "KT88", "6550"]
                ),
                Expert(Scale("xfmrmatch", "Xfmr Match", 50f, 200f)),
                Expert(Scale("xfmrlf", "Xfmr Low Freq")),
                Expert(Scale("xfmrhf", "Xfmr High Freq")),
                Expert(ParamDescriptor.Float("xfmrdrive", "Xfmr Drive", 0f, 10f, 0f)),
                ParamDescriptor.Choice(
                    "speaker",
                    "Speaker load",
                    [Stock, "1x12 Open", "2x12 Open", "4x10 Open", "4x12 Closed", "8x10 Closed", "Follow Cab"],
                    SpeakerAuto
                ),
                Expert(Scale("spkres", "Load resonance")),
                Expert(Scale("spkq", "Load Q")),
                Expert(Scale("spkpeak", "Load peak")),
                Expert(Scale("spkhf", "Load HF")),
                Expert(Scale("spkimp", "Load impedance", 50f, 200f)),
                Scale("presfreq", "Presence Freq"),
                Scale("depthfreq", "Depth Freq"),
                ParamDescriptor.Float("variac", "Variac", 50f, 130f, 100f, "%"),
                Expert(Scale("btc", "B+ Time Const")),
                ParamDescriptor.Choice("supply", "Supply Type", [Stock, "DC", "AC 60 Hz", "AC 50 Hz"]),
                ParamDescriptor.Choice("rectifier", "Rectifier", [Stock, "Silicon", "GZ34", "5U4", "5Y3", "EZ81"]),
                Expert(Scale("katc", "Cathode Time Const")),
                ParamDescriptor.Choice("boost", "Boost Type", AmpBoost.Types),
                ParamDescriptor.Float("boostdrive", "Boost Drive", 0f, 10f, 3f),
                ParamDescriptor.Float("boosttone", "Boost Tone", 0f, 10f, 5f),
                ParamDescriptor.Float("boostlevel", "Boost Level", -20f, 20f, 0f, "dB"),
                ParamDescriptor.Switch("saturation", "Saturation"),
                ParamDescriptor.Float("satdrive", "Saturation Drive", 0f, 10f, 5f),
                ParamDescriptor.Choice("infilter", "Input Filter", AmpInputFilter.Types),
                ParamDescriptor.Float("inffreq", "Input Filter Freq", 20f, 20_000f, 1_000f, "Hz", ParamTaper.Log),
                ParamDescriptor.Float("infq", "Input Filter Q", 0.1f, 10f, 0.7071f, string.Empty, ParamTaper.Log),
                ParamDescriptor.Float("infgain", "Input Filter Gain", -12f, 12f, 0f, "dB"),
                ParamDescriptor.Float("definition", "Definition", -100f, 100f, 0f, "%"),
                ParamDescriptor.Choice("outeq", "Output EQ", ["Off", ..GraphicEqualizer.LayoutNames]),
                ParamDescriptor.Choice("outeqloc", "Output EQ Location", ["Output", "Pre Power Amp", "Input"]),
                ..Enumerable.Range(1, GraphicEqualizer.MaxBands)
                    .Select(static b => ParamDescriptor.Float($"outeq{b}", $"Output EQ {b}", -12f, 12f, 0f, "dB")),
                ParamDescriptor.Float("indyn", "Input Dynamics", -100f, 100f, 0f, "%"),
                ParamDescriptor.Choice("comptype", "Output Comp", AmpCompressor.Types),
                ParamDescriptor.Float("compamount", "Output Comp Amount", 0f, 10f, 5f),
                ParamDescriptor.Float("compthresh", "Output Comp Threshold", -60f, 0f, -20f, "dB"),
                ParamDescriptor.Float("compclarity", "Output Comp Clarity", 0f, 10f, 0f),
                ParamDescriptor.Float("spkdrive", "Speaker Drive", 0f, 10f, 0f),
                ParamDescriptor.Float("thump", "Speaker Thump", 0f, 10f, 0f),
                ParamDescriptor.Float("cabres", "Cab Resonance", 50f, 250f, 100f, "Hz", ParamTaper.Log),
                ParamDescriptor.Float("spkcomp", "Speaker Compression", 0f, 10f, 0f),
                ParamDescriptor.Float("spkcomptc", "Speaker Comp Time", 10f, 2_000f, 300f, "ms", ParamTaper.Log),
                ParamDescriptor.Float("tremrate", "Bias Trem Rate", 0.5f, 15f, 4f, "Hz", ParamTaper.Log),
                ParamDescriptor.Float("tremdepth", "Bias Trem Depth", 0f, 100f, 0f, "%"),
                BlockParams.Balance,
                Expert(ParamDescriptor.Float("pthard", "Power Tube Hardness", 0f, 10f, 5f)),
                Expert(ParamDescriptor.Float("prehard", "Preamp Hardness", 0f, 10f, 5f)),
                ParamDescriptor.Switch("presshift", "Presence Shift"),
                ParamDescriptor.Switch("fatsw", "Fat"),
                ParamDescriptor.Switch("cutsw", "Input Cut"),
                ParamDescriptor.Float("hitreble", "High Treble", 0f, 10f, 5f),
                ParamDescriptor.Float("overdrive", "Overdrive", 0f, 10f, 0f),
                ParamDescriptor.Choice("stacktype", "Tonestack Type", AmpStacks.Names),
                ParamDescriptor.Choice("stackloc", "Tonestack Location", [Stock, "Pre", "Mid", "Post"]),
                Expert(Scale("t2plate", "Triode 2 Plate Freq")),
                Expert(ParamDescriptor.Float("cfcomp", "CF Compression", 0f, 400f, 100f, "%")),
                Expert(Scale("cfharm", "CF Harmonics", 50f, 200f)),
                Expert(ParamDescriptor.Float("prebiasx", "Preamp Bias Excursion", 0f, 400f, 100f, "%")),
                Expert(ParamDescriptor.Float("pibiasx", "PI Bias Excursion", 0f, 400f, 100f, "%")),
                Expert(ParamDescriptor.Float("ptbiasx", "Power Tube Bias Excursion", 0f, 400f, 100f, "%")),
                Expert(Scale("ptbiastime", "Excursion Time")),
                Expert(Scale("ptrecovery", "Recovery Time")),
                Expert(ParamDescriptor.Float("masterbiasx", "Master Bias Excursion", 0f, 200f, 100f, "%")),
                ParamDescriptor.Switch("suppressor", "Plate Suppressor Diodes"),
                ParamDescriptor.Choice("mvloc", "Master Location", [Stock, "Post PI"]),
                ParamDescriptor.Choice("outmode", "Output Mode", ["FRFR", "SS Power Amp + Cab"]),
                Expert(Scale("screenfreq", "Screen Freq")),
                Expert(ParamDescriptor.Float("screenq", "Screen Q", 0f, 5f, 0f)),
                ParamDescriptor.Choice("oversampling", "Oversampling", ["Standard", "High"]) with { Recompile = true }
            ]
        )
    );

    static BlockDescriptor Describe(IReadOnlyList<ParamDescriptor> parameters) =>
        new(
            "circuitamp",
            "Amp",
            "Amp",
            parameters,
            maxInstances: 2,
            typeResets: [..parameters.Select(static p => p.Id).Where(static id => !KeptOnTypeChange.Contains(id))],
            pages: [
                new(
                    "Basic",
                    [
                        "gain", "bass", "middle", "treble", "presence", "depth", "cut", "master", "bright", "switch1",
                        "switch2", "switch3", "control1", "control2", "eq1", "eq2", "eq3", "eq4", "eq5", "hitreble",
                        "overdrive", "presshift", "fatsw", "cutsw"
                    ]
                ),
                new(
                    "Preamp",
                    [
                        "intrim", "inputsel", "lowcut", "pretube", "presag", "stacktype", "stackloc", "definition",
                        "saturation", "satdrive", "boost", "boostdrive", "boosttone", "boostlevel"
                    ]
                ),
                new(
                    "Power Amp",
                    [
                        "sag", "bias", "feedback", "mvtrim", "poweramp", "powertube", "presfreq", "depthfreq", "variac",
                        "supply", "rectifier", "mvloc", "suppressor", "tremrate", "tremdepth"
                    ]
                ),
                new(
                    "Speaker",
                    [
                        "speaker", "spkres", "spkq", "spkpeak", "spkhf", "spkimp", "spkdrive", "thump", "cabres",
                        "spkcomp", "spkcomptc", "outmode"
                    ]
                ),
                new(
                    "EQ",
                    [
                        "infilter", "inffreq", "infq", "infgain", "outeq", "outeqloc", "outeq1", "outeq2", "outeq3",
                        "outeq4", "outeq5", "outeq6", "outeq7", "outeq8", "outeq9", "outeq10"
                    ]
                ),
                new("Dynamics", ["indyn", "comptype", "compamount", "compthresh", "compclarity"]),
                new(
                    "Advanced",
                    [
                        "brightcap", "prebias", "prehard", "stackfreq", "t2plate", "cfcomp", "cfharm", "prebiasx",
                        "pibiasx", "pthard", "ptbiasx", "ptbiastime", "ptrecovery", "masterbiasx",
                        "screenfreq", "screenq", "xfmrmatch", "xfmrlf", "xfmrhf", "xfmrdrive", "btc", "katc",
                        "oversampling"
                    ]
                )
            ]
        );

    /// <summary>
    ///     The descriptor's parameters with each model's panel names (<see cref="ParamDescriptor.TypeLabels" />) and
    ///     its own defaults (<see cref="ParamDescriptor.TypeDefaults" />, from <see cref="AmpPanel.Defaults" />).
    /// </summary>
    static IReadOnlyList<ParamDescriptor> Labelled(IReadOnlyList<ParamDescriptor> parameters) => [
        ..parameters.Select(static p => {
                string[] labels = [
                    ..AmpDesign.All.Select(a => AmpFeatures.Has(a, p.Id)
                            ? a.Panel.Label(p.Id, p.Name) ?? string.Empty
                            : string.Empty
                    )
                ];
                float[] defaults = [..AmpDesign.All.Select(a => a.Panel.Default(p.Id, p.Default))];
                return p with {
                    TypeLabels = labels.All(l => l == p.Name) ? null : labels,
                    TypeDefaults = defaults.All(d => d == p.Default) ? null : defaults
                };
            }
        )
    ];

    /// <summary>A component scale in percent of the model's value, 100 % as built, on a log taper.</summary>
    static ParamDescriptor Scale(string id, string name, float min = 25f, float max = 400f) =>
        ParamDescriptor.Float(id, name, min, max, 100f, "%", ParamTaper.Log);

    /// <summary>A component value: applied as a step (it may recompute a circuit), not ramped.</summary>
    static ParamDescriptor Expert(ParamDescriptor descriptor) => descriptor with { Smoothing = ParamSmoothing.Step };
}
