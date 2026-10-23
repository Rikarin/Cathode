using Cathode.Dsp.Wdf;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The output transformer, referred to the whole primary (plate to plate): its nominal load
///     <paramref name="PlateToPlate" /> (the speaker's nominal 8 Ω times the impedance ratio), the primary (magnetising)
///     inductance, the leakage inductance, the copper resistance of both windings, and across the primary its
///     self-capacitance and core loss. Linear: no core saturation or hysteresis yet.
/// </summary>
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
    double CoreLoss = 200e3
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
public sealed record SpeakerDesign(
    double Resistance = 6.4,
    double Inductance = 1.2e-3,
    double EddyResistance = 25,
    double Resonance = 90,
    double PeakImpedance = 40,
    double Q = 3
) {
    public const double Nominal = 8;

    /// <summary>The motional resistance, inductance and capacitance of the resonance.</summary>
    public (double Resistance, double Inductance, double Capacitance) Motional {
        get {
            var r = PeakImpedance - Resistance;
            var omega = 2 * Math.PI * Resonance;
            var l = r / (omega * Q);
            return (r, l, 1 / (omega * omega * l));
        }
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
public sealed record FeedbackDesign(
    double TapGain,
    double FeedbackResistor,
    double ShuntResistor,
    Pot Presence,
    double PresenceCap,
    double DepthFrequency = 90
) {
    /// <summary>The feedback fraction at mid frequencies, with presence off.</summary>
    public double Beta => ShuntResistor / (FeedbackResistor + ShuntResistor);
}

/// <summary>
///     The power supply: a rectifier and transformer of <paramref name="SourceResistance" /> charging the reservoir cap
///     (the plates' centre tap), a choke or resistor to the screen node, and a dropping resistor to the preamp node,
///     which draws <paramref name="PreampCurrent" />. Sag is the reservoir falling under the tubes' current draw.
/// </summary>
public sealed record PowerSupplyDesign(
    double SourceResistance,
    double Reservoir,
    double ChokeResistance,
    double ScreenCap,
    double PreampResistance,
    double PreampCap,
    double PreampCurrent = 4e-3
);

/// <summary>
///     A push-pull pentode power amp: phase inverter, coupling caps and grid networks, the output pair (or quad), the
///     output transformer and speaker load, global negative feedback and the power supply.
/// </summary>
/// <param name="Name">A label, e.g. "2 x EL34".</param>
/// <param name="Tube">The output tube.</param>
/// <param name="TubesPerSide">1 for a pair, 2 for a quad (paralleled).</param>
/// <param name="Supply">B+ at the transformer centre tap at idle, in volts.</param>
/// <param name="ScreenResistor">Per tube, from the screen node.</param>
/// <param name="IdleCurrent">Plate current per tube at idle with the bias knob at noon, in amperes.</param>
/// <param name="CathodeBias">Cathode-biased (a shared, bypassed cathode resistor) rather than fixed bias.</param>
/// <param name="CathodeBypass">The shared cathode resistor's bypass cap (cathode bias).</param>
/// <param name="RatedPower">Output power at the onset of clipping, in watts (scales the block's output level).</param>
/// <param name="PhaseInverter">The long-tail pair driving the output tubes.</param>
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
    LongTailPairDesign PhaseInverter,
    double CouplingCap,
    double GridLeak,
    double GridStopper,
    double CutCap,
    Pot CutPot,
    FeedbackDesign Feedback,
    bool HasFeedback,
    OutputTransformerDesign Transformer,
    SpeakerDesign Speaker,
    PowerSupplyDesign PowerSupply
);
