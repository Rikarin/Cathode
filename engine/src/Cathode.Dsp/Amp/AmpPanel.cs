namespace Cathode.Dsp.Amp;

/// <summary>
///     An amp model's front panel: what each of the Circuit Amp block's generic controls is called on this model, or
///     null where the model has no such control (the editor hides it, and the circuit ignores it). The standard knobs
///     default to their usual names; the model-specific controls (<see cref="Switch1" /> to <see cref="Switch3" />,
///     <see cref="Control1" />, <see cref="Control2" /> and the <see cref="Equalizer" /> bands) default to absent, and
///     a model that wires one into its circuit (a <see cref="PotRole" /> or <see cref="SwitchRole" /> on a stage) names
///     it here: "Fat", "Lead Drive", "Jumper", "Bright Input" and so on.
/// </summary>
public sealed record AmpPanel {
    /// <summary>
    ///     The Circuit Amp parameters that only mean something on a tube amp: its power tubes, transformer, speaker
    ///     load (which a solid-state amp's feedback holds flat), bias and bias tremolo, feedback knob, rectifier,
    ///     preamp tubes and bright caps.
    /// </summary>
    static readonly string[] TubeOnly = [
        "bias", "feedback", "brightcap", "presag", "powertube", "xfmrmatch", "xfmrlf", "xfmrhf", "xfmrdrive", "speaker",
        "spkres",
        "spkq", "spkpeak", "spkhf", "spkimp", "presfreq", "depthfreq", "supply", "rectifier", "katc", "tremrate",
        "tremdepth", "pthard", "t2plate", "cfcomp", "cfharm", "pibiasx", "ptbiasx", "ptbiastime", "ptrecovery",
        "masterbiasx", "suppressor", "mvloc", "screenfreq", "screenq", "stacktype"
    ];

    /// <summary>
    ///     The Circuit Amp parameters that act on the preamp's triodes themselves (their type, bias, hardness and grid
    ///     current): absent where the preamp has none (<see cref="HasPreampTubes" />). Preamp Sag stays in
    ///     <see cref="TubeOnly" />: a solid-state amp's preamp rails are regulated.
    /// </summary>
    static readonly string[] PreampTubeOnly = ["pretube", "prebias", "prehard", "prebiasx"];

    /// <summary>Every standard knob under its usual name, no model-specific controls.</summary>
    public static AmpPanel Standard { get; } = new();

    public string? Gain { get; init; } = "Gain";

    public string? Bass { get; init; } = "Bass";

    public string? Middle { get; init; } = "Middle";

    public string? Treble { get; init; } = "Treble";

    public string? Presence { get; init; } = "Presence";

    public string? Depth { get; init; } = "Depth";

    public string? Cut { get; init; } = "Cut";

    public string? Master { get; init; } = "Master";

    public string? Bright { get; init; } = "Bright";

    /// <summary>
    ///     The name of the Circuit Amp's tremolo rate on a model with a tremolo of its own ("Speed"); null keeps the
    ///     generic bias tremolo's name.
    /// </summary>
    public string? TremoloRate { get; init; }

    /// <summary>The tremolo depth's name on a model with a tremolo of its own ("Intensity", "Depth").</summary>
    public string? TremoloDepth { get; init; }

    /// <summary>A model-specific switch (<see cref="SwitchRole.Switch1" />).</summary>
    public string? Switch1 { get; init; }

    public string? Switch2 { get; init; }

    public string? Switch3 { get; init; }

    /// <summary>
    ///     A model-specific pot (<see cref="PotRole.Control1" />), e.g. a lead drive or a second channel's volume.
    /// </summary>
    public string? Control1 { get; init; }

    public string? Control2 { get; init; }

    /// <summary>
    ///     The band labels of a graphic equaliser the model places in its circuit
    ///     (<see cref="EqualizerStageDesign" />), up to five; null for none.
    /// </summary>
    public IReadOnlyList<string>? Equalizer { get; init; }

    /// <summary>
    ///     False for a solid-state amp (<see cref="AmpDesign.SolidState" />): the tube-only parameters are then absent
    ///     from its panel (and do nothing).
    /// </summary>
    public bool HasTubes { get; init; } = true;

    /// <summary>
    ///     False where the preamp has no triode: the preamp-tube parameters (tube type, bias, hardness, grid current)
    ///     are then absent from its panel. It follows <see cref="HasTubes" /> unless set, so a solid-state
    ///     amp whose preamp runs a real triode (the Valvestate's ECC83) keeps them.
    /// </summary>
    public bool HasPreampTubes {
        get => hasPreampTubes ?? HasTubes;
        init => hasPreampTubes = value;
    }

    readonly bool? hasPreampTubes;

    /// <summary>
    ///     The model's own defaults for Circuit Amp parameters it ships at another setting than the block's default, by
    ///     parameter id: the Quick Rod's Solo Boost at 0, the boost off
    ///     (<see cref="Parameters.ParamDescriptor.TypeDefaults" />).
    /// </summary>
    public IReadOnlyDictionary<string, float> Defaults { get; init; } = new Dictionary<string, float>();

    /// <summary>
    ///     The default of Circuit Amp parameter <paramref name="id" /> on this model, or <paramref name="fallback" />.
    /// </summary>
    public float Default(string id, float fallback) => Defaults.GetValueOrDefault(id, fallback);

    /// <summary>Whether one of the model's own switches is called <paramref name="name" />.</summary>
    public bool HasSwitch(string name) => Switch1 == name || Switch2 == name || Switch3 == name;

    /// <summary>
    ///     The label of the Circuit Amp parameter <paramref name="id" /> on this panel: its name, null where absent, or
    ///     <paramref name="fallback" /> for a parameter the panel does not cover (the expert parameters).
    /// </summary>
    public string? Label(string id, string fallback) {
        if ((!HasTubes && TubeOnly.Contains(id)) || (!HasPreampTubes && PreampTubeOnly.Contains(id))) {
            return null;
        }

        return id switch {
            "gain" => Gain,
            "bass" => Bass,
            "middle" => Middle,
            "treble" => Treble,
            "presence" => Presence,
            "depth" => Depth,
            "cut" => Cut,
            "master" => Master,
            "bright" => Bright,
            "switch1" => Switch1,
            "switch2" => Switch2,
            "switch3" => Switch3,
            "control1" => Control1,
            "control2" => Control2,
            "tremrate" => TremoloRate ?? fallback,
            "tremdepth" => TremoloDepth ?? fallback,
            ['e', 'q', var band] when band is >= '1' and <= '5' => Equalizer is { } bands
                && band - '1' < bands.Count
                    ? bands[band - '1']
                    : null,
            _ => fallback
        };
    }
}
