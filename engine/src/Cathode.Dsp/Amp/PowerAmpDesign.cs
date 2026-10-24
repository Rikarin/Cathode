using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The output transformer, referred to the whole primary (plate to plate): its nominal load
///     <paramref name="PlateToPlate" /> (the speaker's nominal 8 Ω times the impedance ratio), the primary (magnetising)
///     inductance, the leakage inductance, the copper resistance of both windings, and across the primary its
///     self-capacitance and core loss; and the core's saturation.
/// </summary>
/// <param name="PlateToPlate">The nominal primary load, plate to plate (a single-ended amp: plate to B+).</param>
/// <param name="PrimaryInductance">The magnetising inductance below saturation.</param>
/// <param name="LeakageInductance">Primary-referred.</param>
/// <param name="WindingResistance">Both windings' copper, primary-referred.</param>
/// <param name="PrimaryCapacitance">Across the primary.</param>
/// <param name="CoreLoss">Across the primary.</param>
/// <param name="SaturationFlux">
///     The flux linkage (volt-seconds, primary) at the core's knee; 0 for a linear core. Beyond it the magnetising
///     inductance falls (<see cref="PowerAmp" />), so low notes at full power saturate the core and distort.
/// </param>
/// <remarks>
///     The capacitance and core loss matter: pentodes are current sources, and when both cut off (blocking, deep class
///     B) the leakage inductance's current needs somewhere to go; without them the flyback would be unbounded.
/// </remarks>
public sealed record OutputTransformerDesign(
    double PlateToPlate,
    double PrimaryInductance = 20,
    double LeakageInductance = 10e-3,
    double WindingResistance = 150,
    double PrimaryCapacitance = 1e-9,
    double CoreLoss = 200e3,
    double SaturationFlux = 0
) {
    /// <summary>The turns ratio to a nominal 8 Ω speaker.</summary>
    public double TurnsRatio => Math.Sqrt(PlateToPlate / SpeakerDesign.Nominal);
}

/// <summary>
///     A guitar speaker's electrical impedance (Thiele-Small): voice-coil resistance, and the voice coil's inductance
///     made lossy by eddy currents (in parallel with <paramref name="EddyResistance" />, a first-order stand-in for its
///     semi-inductance: about 11 Ω at 1 kHz and 26 Ω at 5 kHz), in series with the motional parallel resonance at
///     <paramref name="Resonance" /> hertz, peaking at <paramref name="PeakImpedance" /> ohms with quality factor
///     <paramref name="Q" />. Nominally 8 Ω.
/// </summary>
/// <remarks>
///     The presets are parametric curves of the usual cabinet types normalised to 8 Ω (a 4 × 10 at 2 Ω on a 2 Ω tap
///     looks the same to the output tubes as an 8 Ω load on an 8 Ω tap), with typical values for their kind, not
///     measurements of particular cabinets.
/// </remarks>
public sealed record SpeakerDesign(
    double Resistance = 6.4,
    double Inductance = 1.2e-3,
    double EddyResistance = 25,
    double Resonance = 90,
    double PeakImpedance = 40,
    double Q = 3
) {
    public const double Nominal = 8;

    /// <summary>An open-back 1 × 12 combo: a high, sharp resonance (little air load).</summary>
    public static SpeakerDesign OpenBack1x12 { get; } = new(6.4, 1.0e-3, 25, 100, 40, 3);

    /// <summary>An open-back 2 × 12 combo: two cones share the air load, a slightly lower, broader peak.</summary>
    public static SpeakerDesign OpenBack2x12 { get; } = new(6.4, 1.1e-3, 25, 90, 32, 2.5);

    /// <summary>
    ///     An open-back 4 × 10 (Super Reverb, Bassman): ten-inch cones resonate higher, with less voice-coil
    ///     inductance.
    /// </summary>
    public static SpeakerDesign OpenBack4x10 { get; } = new(6.2, 0.7e-3, 30, 115, 30, 2.5);

    /// <summary>A closed-back 4 × 12: the box's air spring damps and broadens the peak; a larger HF rise (Vintage 30-like).</summary>
    public static SpeakerDesign Closed4x12 { get; } = new(6.4, 1.4e-3, 22, 105, 26, 2);

    /// <summary>A closed 8 × 10 bass cabinet (SVT): low, broad resonance.</summary>
    public static SpeakerDesign Closed8x10 { get; } = new(6.2, 1.0e-3, 28, 60, 24, 1.8);

    /// <summary>The presets, in the order of the Circuit Amp's Speaker choice (after Stock).</summary>
    public static IReadOnlyList<SpeakerDesign> Presets { get; } = [OpenBack1x12, OpenBack2x12, OpenBack4x10, Closed4x12, Closed8x10];

    /// <summary>The motional resistance, inductance and capacitance of the resonance.</summary>
    public (double Resistance, double Inductance, double Capacitance) Motional =>
        MotionalOf(Resonance, PeakImpedance - Resistance, Q);

    /// <summary>
    ///     The motional parallel RLC of a resonance at <paramref name="resonance" /> hertz peaking
    ///     <paramref name="excess" /> ohms above the voice coil's resistance, with quality factor <paramref name="q" />.
    /// </summary>
    public static (double Resistance, double Inductance, double Capacitance) MotionalOf(
        double resonance,
        double excess,
        double q
    ) {
        var r = Math.Max(excess, 0.1);
        var omega = 2 * Math.PI * resonance;
        var l = r / (omega * q);
        return (r, l, 1 / (omega * omega * l));
    }
}

/// <summary>
///     Global negative feedback from a speaker tap to the bottom of the phase inverter's tail: the feedback resistor into
///     a shunt resistor, with the presence control (a pot in series with a cap) across the shunt, which lowers the
///     feedback, and so lifts the response, at high frequencies. Depth lowers it below
///     <paramref name="DepthFrequency" />, lifting the speaker resonance.
/// </summary>
/// <param name="FeedbackResistor">From the tap to the shunt resistor.</param>
/// <param name="ShuntResistor">From the bottom of the phase inverter tail to ground.</param>
/// <param name="Presence">The presence pot, in series with the presence cap across the shunt.</param>
/// <param name="PresenceCap">The presence cap.</param>
/// <param name="DepthFrequency">Below it, the depth control lowers the feedback.</param>
/// <param name="TapGain">The tap's voltage relative to an 8 Ω secondary (√2 for a 16 Ω tap).</param>
/// <param name="LowFeedback">
///     The network's own feedback below the depth frequency, as a fraction of the mid band's, with the depth control at
///     0: a cap across part of the feedback resistor (the Friedman BE-100's 220k with 4.7 nF, in series with 47k) is a
///     fixed low shelf with its pole at <paramref name="DepthFrequency" /> and its zero at this fraction of it; 1 for a
///     plain resistor. The depth control lowers it further.
/// </param>
public sealed record FeedbackDesign(
    double TapGain,
    double FeedbackResistor,
    double ShuntResistor,
    Pot Presence,
    double PresenceCap,
    double DepthFrequency = 90,
    double LowFeedback = 1
) {
    /// <summary>The feedback fraction at mid frequencies, with presence off.</summary>
    public double Beta => ShuntResistor / (FeedbackResistor + ShuntResistor);
}

/// <summary>
///     A rectifier: silicon (no drop to speak of) or a vacuum diode, whose drop follows Child's law,
///     V = (I / <paramref name="Perveance" />)^(2/3), fitted to a datasheet drop at a rated current.
/// </summary>
/// <param name="Name">The tube, e.g. "GZ34".</param>
/// <param name="Perveance">I = k V^1.5 in A/V^1.5; 0 for silicon.</param>
public sealed record RectifierModel(string Name, double Perveance) {
    /// <summary>A silicon bridge or full-wave pair: about a volt, neglected.</summary>
    public static RectifierModel Silicon { get; } = new("Silicon", 0);

    /// <summary>GZ34 / 5AR4: about 17 V at 250 mA, the stiffest tube rectifier.</summary>
    public static RectifierModel GZ34 { get; } = new("GZ34", 0.25 / Math.Pow(17, 1.5));

    /// <summary>5U4GB: about 44 V at 275 mA.</summary>
    public static RectifierModel FiveU4 { get; } = new("5U4", 0.275 / Math.Pow(44, 1.5));

    /// <summary>5Y3GT: about 60 V at 125 mA, the softest (tweed Deluxe).</summary>
    public static RectifierModel FiveY3 { get; } = new("5Y3", 0.125 / Math.Pow(60, 1.5));

    /// <summary>EZ81 / 6CA4: about 25 V at 150 mA (approximate).</summary>
    public static RectifierModel EZ81 { get; } = new("EZ81", 0.15 / Math.Pow(25, 1.5));

    /// <summary>The rectifiers, in the order of the Circuit Amp's Rectifier choice (after Stock).</summary>
    public static IReadOnlyList<RectifierModel> All { get; } = [Silicon, GZ34, FiveU4, FiveY3, EZ81];

    /// <summary>The drop at <paramref name="current" /> amperes.</summary>
    public double Drop(double current) => Perveance > 0 && current > 0 ? Math.Pow(current / Perveance, 2.0 / 3) : 0;
}

/// <summary>
///     The power supply: a rectifier and transformer of <paramref name="SourceResistance" /> charging the reservoir cap
///     (the plates' centre tap), a choke or resistor to the screen node, and a dropping resistor to the preamp node,
///     which draws <paramref name="PreampCurrent" />. Sag is the reservoir falling under the tubes' current draw.
/// </summary>
/// <param name="SourceResistance">The transformer and rectifier as one resistance (the rectifier's own drop besides).</param>
/// <param name="Reservoir">The first filter cap.</param>
/// <param name="ChokeResistance">The choke (or resistor) to the screen node.</param>
/// <param name="ScreenCap">The screen node's cap.</param>
/// <param name="PreampResistance">The dropping resistor to the preamp node.</param>
/// <param name="PreampCap">The preamp node's cap.</param>
/// <param name="PreampCurrent">What the preamp draws.</param>
/// <param name="Rectifier">
///     A tube rectifier's drop on top of <paramref name="SourceResistance" />; null where the source resistance already
///     stands for the whole rectifier (the first models).
/// </param>
/// <param name="LineFrequency">
///     The mains frequency, for an AC supply that charges the reservoir on the peaks of the rectified mains (ripple at
///     twice it); 0 for a DC source (no ripple).
/// </param>
public sealed record PowerSupplyDesign(
    double SourceResistance,
    double Reservoir,
    double ChokeResistance,
    double ScreenCap,
    double PreampResistance,
    double PreampCap,
    double PreampCurrent = 4e-3,
    RectifierModel? Rectifier = null,
    double LineFrequency = 0
);

/// <summary>
///     The phase inverter's input network: the coupling cap from the preamp's last stage into the driven grid, and the
///     grid leak to the bias junction (which follows the input: the leak is bootstrapped). With it, the driven grid draws
///     grid current (Dempwolf's law, at the grid-cathode voltage of the inverter's tables, which include the plates' load
///     through the power tubes' grid leaks and the cathode's rise from the grid current itself) through the previous
///     stage's output resistance and charges the cap: the inverter's own blocking under hard drive. The feedback's share
///     of the cathode's movement enters a sample late. Without it (the first models) the inverter's input is the previous
///     stage's voltage, unloaded.
/// </summary>
public sealed record InverterInputDesign(double CouplingCap, double GridLeak);

/// <summary>
///     A front-panel switch on the power supply, which while it is on puts <paramref name="Rectifier" /> in place of the
///     design's rectifier (Mesa's tube/silicon rectifier switch) and scales the mains by <paramref name="Variac" />
///     (Mesa's Bold/Spongy, a lower primary tap). The Rectifier and Variac expert parameters apply on top: a rectifier
///     chosen there wins, and the variacs multiply.
/// </summary>
/// <param name="Role">The switch.</param>
/// <param name="Rectifier">The rectifier while it is on; null to keep the design's.</param>
/// <param name="Variac">The mains while it is on, as a fraction of nominal.</param>
public sealed record SupplySwitchDesign(SwitchRole Role, RectifierModel? Rectifier = null, double Variac = 1);

/// <summary>How the output tubes drive the transformer.</summary>
public enum PowerTopology {
    /// <summary>Two sides in push-pull across a centre-tapped primary.</summary>
    PushPull,

    /// <summary>
    ///     One side (class A, a gapped core carrying the idle current): no phase inverter, the last preamp stage drives
    ///     the grid through the coupling cap.
    /// </summary>
    SingleEnded
}

/// <summary>
///     A push-pull (or single-ended) pentode power amp: phase inverter, coupling caps and grid networks, the output
///     tubes, the output transformer and speaker load, global negative feedback and the power supply.
/// </summary>
/// <param name="Name">A label, e.g. "2 x EL34".</param>
/// <param name="Tube">The output tube.</param>
/// <param name="TubesPerSide">1 for a pair, 2 for a quad (paralleled).</param>
/// <param name="Supply">B+ at the transformer centre tap at idle, in volts.</param>
/// <param name="ScreenResistor">Per tube, from the screen node.</param>
/// <param name="IdleCurrent">Plate current per tube at idle with the bias knob at noon, in amperes.</param>
/// <param name="CathodeBias">Cathode-biased (a bypassed cathode resistor) rather than fixed bias.</param>
/// <param name="CathodeBypass">The cathode resistor's bypass cap (cathode bias; per side with separate cathodes).</param>
/// <param name="RatedPower">Output power at the onset of clipping, in watts (scales the block's output level).</param>
/// <param name="PhaseInverter">The phase inverter driving the output tubes (ignored when single-ended).</param>
/// <param name="CouplingCap">Phase inverter plates to the power-tube grids.</param>
/// <param name="GridLeak">Power-tube grid leaks (bias feed).</param>
/// <param name="GridStopper">Series resistors at the power-tube grids.</param>
/// <param name="CutCap">
///     Vox "cut": a cap in series with <paramref name="CutPot" /> between the phase inverter's plates; 0 for none.
/// </param>
/// <param name="CutPot">The cut pot.</param>
/// <param name="Feedback">The feedback network.</param>
/// <param name="HasFeedback">
///     False for an amp without global feedback (the AC30), where <paramref name="Feedback" /> only applies when the
///     feedback knob is turned past noon.
/// </param>
/// <param name="Transformer">The output transformer.</param>
/// <param name="Speaker">The speaker load.</param>
/// <param name="PowerSupply">The supply and its sag.</param>
/// <param name="Topology">Push-pull or single-ended.</param>
/// <param name="UltralinearTap">
///     Ultralinear: the screens hang on taps at this fraction of each half-primary (0.43 is common); 0 for pentode
///     operation (screens on the screen node).
/// </param>
/// <param name="Strapped">
///     A second tube type wired as a triode (screen to plate) beside the main tubes on each side: Mesa's Simul-Class,
///     a pentode pair plus a triode-strapped pair; null for none.
/// </param>
/// <param name="StrappedPerSide">How many <paramref name="Strapped" /> tubes per side.</param>
/// <param name="TriodeSwitch">
///     A front-panel switch that straps the main tubes as triodes (a Hi/Low or Pentode/Triode switch); null for none.
/// </param>
/// <param name="SeparateCathodes">
///     Cathode bias with a resistor and bypass cap per side (per tube, with the tubes of a side in parallel) instead of
///     one shared by all.
/// </param>
/// <param name="InverterInput">The phase inverter's input network with grid current; null for none (see the type).</param>
/// <param name="ShuntMaster">
///     A master volume wired as a rheostat across the phase inverter's outputs (Matchless DC-30), turned by the Master
///     knob: its full track at 10, a short at 0; beside the cut control if there is one. Null for none.
/// </param>
/// <param name="CathodeResistor">
///     Cathode bias: the schematic's cathode resistor (all tubes'; with separate cathodes, half of each side's), which
///     then sets the idle current in place of <paramref name="IdleCurrent" /> (a replacement tube keeps it, as it would
///     in the amp); 0 to derive it from <paramref name="IdleCurrent" />.
/// </param>
/// <param name="SupplySwitches">Front-panel switches on the supply (a rectifier switch, Bold/Spongy); null for none.</param>
public sealed record PowerAmpDesign(
    string Name,
    PentodeModel Tube,
    int TubesPerSide,
    double Supply,
    double ScreenResistor,
    double IdleCurrent,
    bool CathodeBias,
    double CathodeBypass,
    double RatedPower,
    PhaseInverterDesign PhaseInverter,
    double CouplingCap,
    double GridLeak,
    double GridStopper,
    double CutCap,
    Pot CutPot,
    FeedbackDesign Feedback,
    bool HasFeedback,
    OutputTransformerDesign Transformer,
    SpeakerDesign Speaker,
    PowerSupplyDesign PowerSupply,
    PowerTopology Topology = PowerTopology.PushPull,
    double UltralinearTap = 0,
    PentodeModel? Strapped = null,
    int StrappedPerSide = 0,
    SwitchRole? TriodeSwitch = null,
    bool SeparateCathodes = false,
    InverterInputDesign? InverterInput = null,
    Pot? ShuntMaster = null,
    double CathodeResistor = 0,
    IReadOnlyList<SupplySwitchDesign>? SupplySwitches = null
) {
    /// <summary>1 when single-ended, else 2.</summary>
    public int Sides => Topology == PowerTopology.SingleEnded ? 1 : 2;
}
